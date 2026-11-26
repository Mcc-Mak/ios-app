#!/usr/bin/env bash
# codebase/scripts/generate_apps_json.sh
#
# 從最新 GitHub Release 產生 AltStore/SideStore 相容的 apps.json 來源饋流。
# 資產來源欄位(version、versionDate、size、downloadURL、bundleIdentifier、
# appName)來自發行版本及其 manifest.json 資產;饋流層級中繼資料(來源名稱、
# 副標題、描述、網站、圖示)來自 app.config.json。即使尚無發行版本也能輸出
# 合法的 JSON。
#
# 用法:generate_apps_json.sh [output_path] [config_path]
#   output_path  預設:apps.json
#   config_path  預設:app.config.json(環境變數:APP_CONFIG)
#
# 相依:gh(以 GITHUB_TOKEN 驗證)、jq。兩者皆隨附於 GitHub runner。
set -euo pipefail

OUTPUT="${1:-apps.json}"
CONFIG="${2:-${APP_CONFIG:-app.config.json}}"

log() { printf '\033[1;34m[apps.json]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[apps.json:錯誤]\033[0m %s\n' "$*" >&2; exit 1; }

command -v gh >/dev/null || die "找不到 gh CLI,請在 GitHub runner 上執行或安裝 GitHub CLI。"
command -v jq >/dev/null || die "找不到 jq。"
[[ -f "$CONFIG" ]] || die "找不到設定檔:$CONFIG"

REPO="${GITHUB_REPOSITORY:-}"
[[ -n "$REPO" ]] || die "未設定 GITHUB_REPOSITORY(預期為 owner/repo)。"

# ---- 來自設定檔的饋流層級中繼資料----
feed_name=$(jq -r '.name // "App Source"'          "$CONFIG")
feed_sub=$(jq -r '.subtitle // ""'                 "$CONFIG")
feed_desc=$(jq -r '.description // ""'             "$CONFIG")
feed_web=$(jq -r '.website // ""'                  "$CONFIG")
feed_icon=$(jq -r '.iconURL // ""'                 "$CONFIG")
cfg_app=$(jq -r '.appName // ""'                   "$CONFIG")
cfg_bundle=$(jq -r '.bundleIdentifier // ""'       "$CONFIG")
cfg_dev=$(jq -r '.developerName // ""'             "$CONFIG")

build_empty() {
  jq -n \
    --arg name "$feed_name" \
    --arg sub  "$feed_sub"  \
    --arg desc "$feed_desc" \
    --arg web  "$feed_web"  \
    --arg icon "$feed_icon" \
    '{name:$name, subtitle:$sub, description:$desc, website:$web, iconURL:$icon, apps:[]}'
}

# ---- 最新發行版本(若存在)----
# 僅考慮 v* 標籤的發行版本(實際 App 發行)。
# 儲存庫中亦有 xcode-export-*(Xcode 專案 tarball)與 data-*(分塊資料)
# 發行版本,這些不含 .ipa,不應出現在饋流中。
release_json=""
# 先取值再判斷;`gh ... | grep -q` 會因 SIGPIPE + pipefail 偶發誤判。
# --exclude-drafts 確保只看已發行版本;--json tagName 取標籤再過濾 v*。
release_list=$(gh release list --repo "$REPO" --exclude-drafts --json tagName,createdAt \
  --jq '[.[] | select(.tagName | startswith("v"))] | sort_by(.createdAt) | reverse | .[0].tagName // empty' 2>/dev/null || true)
if [[ -n "$release_list" ]]; then
  release_json=$(gh release view "$release_list" --repo "$REPO" --json tagName,name,publishedAt,body,assets 2>/dev/null || echo "")
fi

if [[ -z "$release_json" ]]; then
  log "找不到發行版本,寫入空饋流。"
  build_empty > "$OUTPUT"
  exit 0
fi

tag=$(printf '%s' "$release_json"      | jq -r '.tagName')
published=$(printf '%s' "$release_json" | jq -r '.publishedAt // ""')
body=$(printf '%s' "$release_json"     | jq -r '.body // ""')

log "選取最新 v* 發行版本: $tag"

ipa_asset=$(printf '%s' "$release_json" | jq -r '.assets[] | select(.name | endswith(".ipa")) | .name' | head -n1 || true)
if [[ -z "$ipa_asset" ]]; then
  log "發行版本 $tag 沒有 .ipa 檔案,寫入空饋流。"
  build_empty > "$OUTPUT"
  exit 0
fi

ipa_size=$(printf '%s' "$release_json" | jq -r --arg n "$ipa_asset" '.assets[] | select(.name==$n) | .size')

# 直接瀏覽器下載 URL(公開儲存庫免驗證即可使用)。
ipa_url="https://github.com/$REPO/releases/download/$tag/$ipa_asset"

# ---- 從 manifest.json 資產取得 version / bundleID / appName(資產來源)----
version=""
bundle_id=""
app_name=""
manifest_asset=$(printf '%s' "$release_json" | jq -r '.assets[] | select(.name=="manifest.json") | .name' | head -n1 || true)
if [[ -n "$manifest_asset" ]]; then
  tmp=$(mktemp -d)
  if gh release download "$tag" --repo "$REPO" --pattern "manifest.json" --dir "$tmp" --clobber >/dev/null 2>&1 && [[ -f "$tmp/manifest.json" ]]; then
    version=$(jq -r '.version // ""'           "$tmp/manifest.json")
    bundle_id=$(jq -r '.bundleIdentifier // ""' "$tmp/manifest.json")
    app_name=$(jq -r '.appName // ""'           "$tmp/manifest.json")
  fi
  rm -rf "$tmp"
fi
[[ -n "$version" ]]   || version="${tag#v}"      # fallback:去除前導 'v' 的標籤
[[ -n "$bundle_id" ]] || bundle_id="$cfg_bundle"
[[ -n "$app_name" ]]  || app_name="$cfg_app"
[[ -n "$app_name" ]]  || app_name="$feed_name"

version_date="$published"
[[ -n "$version_date" ]] || version_date=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

log "發行版本 $tag → $app_name v$version($ipa_size bytes)"

jq -n \
  --arg name  "$feed_name" \
  --arg sub   "$feed_sub"  \
  --arg desc  "$feed_desc" \
  --arg web   "$feed_web"  \
  --arg icon  "$feed_icon" \
  --arg app   "$app_name"  \
  --arg bid   "$bundle_id" \
  --arg dev   "$cfg_dev"   \
  --arg ver   "$version"   \
  --arg vdate "$version_date" \
  --arg vdesc "$body"      \
  --arg url   "$ipa_url"   \
  --argjson size "$ipa_size" \
  '{
    name:$name, subtitle:$sub, description:$desc, website:$web, iconURL:$icon,
    apps:[{
      name:$app,
      bundleIdentifier:$bid,
      developerName:$dev,
      version:$ver,
      versionDate:$vdate,
      versionDescription:$vdesc,
      downloadURL:$url,
      size:$size,
      iconURL:$icon
    }]
  }' > "$OUTPUT"

log "已寫入 $OUTPUT"
