#!/usr/bin/env bash
# codebase/scripts/build_unsigned_ipa.sh
#
# 確定性建置並打包「未簽署 .ipa」,供免費 Apple ID 側載使用。
# 不需程式碼式碼簽署、憑證、描述檔。僅限 macOS(需 Xcode + xcodebuild)。
#
# 輸出(在 GitHub Actions 中亦寫入 $GITHUB_OUTPUT):
#   build/ipa/<App>-<Version>.ipa     未簽署 IPA(Payload/<App>.app 在 zip 根目錄)
#   build/dsym/<App>-<Version>.dSYM.zip
#   build/ipa/manifest.json           資產來源中繼資料,供 AltStore 饋流使用
#
# 透過環境變數覆寫探索結果:SCHEME、PROJECT、WORKSPACE。
# 透過環境變數覆寫檔名:IPA_NAME、DSYM_NAME、CONFIGURATION、ARCHIVE_DIR。
set -euo pipefail

# ---- 設定(可由環境變數覆寫)----
CONFIGURATION="${CONFIGURATION:-Release}"
ARCHIVE_DIR="${ARCHIVE_DIR:-build}"
ARCHIVE_PATH="$ARCHIVE_DIR/App.xcarchive"
IPA_DIR="${IPA_DIR:-build/ipa}"
DSYM_DIR="${DSYM_DIR:-build/dsym}"

# 未簽署旗標 — 每次 xcodebuild 呼叫都必須帶上。
UNSIGN_FLAGS=(
  CODE_SIGNING_ALLOWED=NO
  CODE_SIGNING_REQUIRED=NO
  CODE_SIGN_IDENTITY=""
  CODE_SIGN_ENTITLEMENTS=""
)

log() { printf '\033[1;34m[建置]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[建置:錯誤]\033[0m %s\n' "$*" >&2; exit 1; }

[[ "$(uname -s)" == "Darwin" ]] || die "必須在 macOS 上執行(目前為 $(uname -s))。請使用 macos-* GitHub runner。"
command -v xcodebuild >/dev/null || die "找不到 xcodebuild,請安裝 Xcode。"

# ---- JSON 字串逸出(純 bash,無相依)----
json_escape() {
  local s; s=$(cat)
  s="${s//\\/\\\\}"
  s="${s//\"/\\\"}"
  s="${s//$'\n'/\\n}"
  s="${s//$'\r'/\\r}"
  s="${s//$'\t'/\\t}"
  printf '"%s"' "$s"
}

# ---- 探索 project / workspace(找到多個則立即失敗)----
discover() {
  # $1 = glob 模式。每行印一個路徑,排除 build/vendor 目錄。
  local pattern="$1"
  find . -maxdepth 4 -name "$pattern" \
    -not -path "*/Pods/*" \
    -not -path "*/.build/*" \
    -not -path "*/DerivedData/*" \
    -not -path "*/build/*" \
    -not -path "*/.git/*" 2>/dev/null || true
}

WORKSPACE=""
PROJECT=""

# workspace(排除 .xcodeproj 內自動產生的 project.xcworkspace)。
ws_list=()
while IFS= read -r w; do
  [[ -z "$w" ]] && continue
  [[ "$w" == *".xcodeproj/project.xcworkspace" ]] && continue
  ws_list+=("$w")
done < <(discover "*.xcworkspace")

pr_list=()
while IFS= read -r p; do
  [[ -n "$p" ]] && pr_list+=("$p")
done < <(discover "*.xcodeproj")

if [[ ${#ws_list[@]} -gt 1 ]]; then die "找到多個 workspace:${ws_list[*]},請設定 WORKSPACE 以明確指定。"; fi
if [[ ${#pr_list[@]} -gt 1 ]]; then die "找到多個 project:${pr_list[*]},請設定 PROJECT 以明確指定。"; fi

if [[ -n "${WORKSPACE:-}" ]]; then
  log "使用 workspace(環境變數):$WORKSPACE"
elif [[ -n "${PROJECT:-}" ]]; then
  log "使用 project(環境變數):$PROJECT"
elif [[ ${#ws_list[@]} -eq 1 ]]; then
  WORKSPACE="${ws_list[0]}"; log "使用 workspace:$WORKSPACE"
elif [[ ${#pr_list[@]} -eq 1 ]]; then
  PROJECT="${pr_list[0]}"; log "使用 project:$PROJECT"
else
  die "找不到 .xcodeproj/.xcworkspace,請加入 Xcode 專案,或設定 PROJECT/WORKSPACE。"
fi

# ---- 組裝 -workspace/-project 引數----
xcode_args=()
if [[ -n "$WORKSPACE" ]]; then xcode_args+=(-workspace "$WORKSPACE"); else xcode_args+=(-project "$PROJECT"); fi

# ---- 探索 scheme----
scheme="${SCHEME:-}"
if [[ -z "$scheme" ]]; then
  schemes_out=$(xcodebuild -list "${xcode_args[@]}" 2>/dev/null || true)
  scheme=$(printf '%s\n' "$schemes_out" \
    | awk '/Schemes:/{f=1;next} f&&/^$/{f=0} f{print}' \
    | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' \
    | sed '/^$/d' \
    | head -n1 || true)
fi
[[ -n "$scheme" ]] || die "找不到 scheme,請明確設定 SCHEME。"
log "使用 scheme:$scheme"

xcode_args+=(-scheme "$scheme" -configuration "$CONFIGURATION")

# ---- 清除並封存(未簽署)----
rm -rf "$ARCHIVE_DIR" "$IPA_DIR" "$DSYM_DIR"
mkdir -p "$ARCHIVE_DIR" "$IPA_DIR" "$DSYM_DIR"

log "封存中(未簽署,$CONFIGURATION)…"
# `set -o pipefail` + `|| handle`,讓建置失敗即使透過 tee 也能以非零狀態退出。
if ! xcodebuild archive "${xcode_args[@]}" \
      -archivePath "$ARCHIVE_PATH" \
      -destination "generic/platform=iOS" \
      "${UNSIGN_FLAGS[@]}" \
      | tee "$ARCHIVE_DIR/archive.log"; then
  die "xcodebuild archive 失敗,請參閱 $ARCHIVE_DIR/archive.log"
fi

[[ -d "$ARCHIVE_PATH/Products/Applications" ]] \
  || die "封存完成,但 $ARCHIVE_PATH 下找不到 Products/Applications。"

# ---- 定位 .app 並讀取 Info.plist----
app_path=""
# 使用 process substitution(非管線),避免 pipefail/SIGPIPE 因 `head` 中止腳本。
while IFS= read -r line; do app_path="$line"; break; done \
  < <(find "$ARCHIVE_PATH/Products/Applications" -maxdepth 1 -name "*.app" 2>/dev/null || true)
[[ -n "$app_path" ]] || die "在封存的 Products/Applications 中找不到 .app 套件。"
app_basename=$(basename "$app_path")

app_name=$(plutil -extract CFBundleName raw -o - "$app_path/Info.plist" 2>/dev/null || echo "")
app_version=$(plutil -extract CFBundleShortVersionString raw -o - "$app_path/Info.plist" 2>/dev/null || echo "")
bundle_id=$(plutil -extract CFBundleIdentifier raw -o - "$app_path/Info.plist" 2>/dev/null || echo "")
[[ -n "$app_version" ]] || die "無法從 $app_path/Info.plist 讀取 CFBundleShortVersionString。"

# 檔名安全的 App 名稱( fallback 為去除 .app 的 bundle 名稱)。
file_name="${app_name:-${app_basename%.app}}"
file_name="${file_name// /_}"
ipa_name="${IPA_NAME:-${file_name}-${app_version}.ipa}"
dsym_name="${DSYM_NAME:-${file_name}-${app_version}.dSYM.zip}"

log "App:$app_basename | 版本:$app_version | BundleID:${bundle_id:-<未知>}"

# ---- 打包 IPA:Payload/<App>.app 必須在 zip 根目錄----
staging="$ARCHIVE_DIR/ipa-staging"
rm -rf "$staging"; mkdir -p "$staging/Payload"
# ditto 保留 bundle 內的符號連結與中繼資料(如 framework "Current" 連結)。
ditto "$app_path" "$staging/Payload/$app_basename"

ipa_abs="$IPA_DIR/$ipa_name"
( cd "$staging" && zip -r -q "$ipa_abs" Payload )
[[ -f "$ipa_abs" ]] || die "IPA 壓縮失敗:$ipa_abs"

# 驗證版面 — AltStore/Sideloadly 會拒絕 Payload/ 不在根目錄的 IPA。
log "驗證 IPA 版面…"
if ! unzip -l "$ipa_abs" | grep -q "Payload/$app_basename/"; then
  die "IPA 版面檢查失敗:'Payload/$app_basename/' 不在 zip 根目錄。檢查:unzip -l \"$ipa_abs\""
fi
log "IPA 完成:$ipa_abs ($(du -h "$ipa_abs" | cut -f1))"

# ---- 打包 dSYM----
dsym_abs="$DSYM_DIR/$dsym_name"
if [[ -d "$ARCHIVE_PATH/dSYMs" ]] && find "$ARCHIVE_PATH/dSYMs" -mindepth 1 -maxdepth 1 | grep -q .; then
  ( cd "$ARCHIVE_PATH/dSYMs" && zip -r -q "$dsym_abs" . )
  log "dSYM 完成:$dsym_abs"
else
  log "封存中無 dSYM(符號已剝離?),略過 dSYM。"
fi

# ---- 寫入 manifest.json(資產來源中繼資料,供 AltStore 饋流使用)----
manifest="$IPA_DIR/manifest.json"
{
  printf '{\n'
  printf '  "appName": %s,\n'          "$(printf '%s' "$app_name"   | json_escape)"
  printf '  "bundleIdentifier": %s,\n' "$(printf '%s' "$bundle_id"  | json_escape)"
  printf '  "version": %s,\n'          "$(printf '%s' "$app_version"| json_escape)"
  printf '  "ipaFileName": %s,\n'      "$(printf '%s' "$ipa_name"   | json_escape)"
  printf '  "dsymFileName": %s\n'       "$(printf '%s' "$dsym_name" | json_escape)"
  printf '}\n'
} > "$manifest"
log "Manifest 完成:$manifest"

# ---- GitHub Actions 輸出----
if [[ -n "${GITHUB_ACTIONS:-}" && -n "${GITHUB_OUTPUT:-}" ]]; then
  {
    echo "ipa-path=$ipa_abs"
    echo "dsym-path=$dsym_abs"
    echo "manifest-path=$manifest"
    echo "app-name=$app_name"
    echo "app-version=$app_version"
    echo "bundle-id=$bundle_id"
    echo "ipa-file-name=$ipa_name"
    echo "dsym-file-name=$dsym_name"
  } >> "$GITHUB_OUTPUT"
fi

log "完成。"
