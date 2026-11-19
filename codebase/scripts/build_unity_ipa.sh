#!/usr/bin/env bash
# build_unity_ipa.sh — 完整 iOS 建置管線(Unity 匯出 → xcodebuild 封存 → .ipa)
#
# 階段:
#   0. fetch_chunks.sh — 注入分塊資料至 StreamingAssets/Chunks/
#   1. Tuanjie.exe -batchmode -executeMethod BuildIOS.Export — 匯出 Xcode 專案
#   2. xcodebuild archive(未簽署,4 旗標)— 封存
#   3. 從 .xcarchive 提取 .app → Payload/<App>.app → .ipa
#   4. 打包 dSYMs → <App>-<Ver>.dSYM.zip
#   5. 產生 manifest.json + GITHUB_OUTPUT
#
# 三種模式(互斥):
#   預設(全管線)        — 階段 0-5:fetch + Unity 匯出 + xcodebuild + 打包(需 macOS)
#   UNITY_EXPORT_ONLY=1  — 階段 0-1:fetch + Unity 匯出(Windows 本機用,產出 Xcode 專案)
#   XCODEBUILD_ONLY=1    — 階段 2-5:僅 xcodebuild + 打包(CI macOS 用,使用預匯出 Xcode 專案)
#                          需搭配 XCODE_PROJECT_DIR 指向已匯出的 Xcode 專案目錄。
#
# 平台限制:
#   階段 0-1 可在 Windows(Tuanjie)或 macOS 執行。
#   階段 2-4 僅 macOS(xcodebuild 僅存在於 macOS)。
#   在 Windows 上執行時,設定 UNITY_EXPORT_ONLY=1 只跑階段 0-1。
#
# 零 Apple 機密:所有 xcodebuild 皆未簽署:
#   CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO
#   CODE_SIGN_IDENTITY="" CODE_SIGN_ENTITLEMENTS=""
#
# 用法:
#   ./build_unity_ipa.sh
#
# 選項(環境變數):
#   UNITY_EDITOR        — Tuanjie/Unity Editor 路徑(預設:自動偵測)
#   PROJECT_PATH        — Unity 專案路徑(預設:codebase/unity-project)
#   CHUNKS_RELEASE_TAG  — 分塊資料 release 標籤(預設:data-11-NW-15B-l16-v1)
#   IOS_BUNDLE_ID       — Bundle ID(預設:com.mccmak.hkgridvoxel)
#   IOS_VERSION         — 版本(預設:1.0)
#   IOS_PRODUCT_NAME    — 產品名稱(預設:HKGridVoxel)
#   UNITY_EXPORT_ONLY   — 設為 1 時只匯出 Xcode 專案(跳過 xcodebuild,Windows 用)
#   XCODEBUILD_ONLY     — 設為 1 時只跑 xcodebuild + 打包(CI macOS 用)
#   XCODE_PROJECT_DIR   — [XCODEBUILD_ONLY=1 必填] 預匯出 Xcode 專案目錄
#   ARCHIVE_DIR         — 封存目錄(預設:build)
#   OUTPUT_DIR          — 最終產物目錄(預設:build/output)
set -euo pipefail

# ─── 路徑與設定 ───────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PROJECT_PATH="${PROJECT_PATH:-$REPO_ROOT/codebase/unity-project}"
CHUNKS_RELEASE_TAG="${CHUNKS_RELEASE_TAG:-data-11-NW-15B-l16-v1}"
IOS_BUNDLE_ID="${IOS_BUNDLE_ID:-com.mccmak.hkgridvoxel}"
IOS_VERSION="${IOS_VERSION:-1.0}"
IOS_PRODUCT_NAME="${IOS_PRODUCT_NAME:-HKGridVoxel}"
IOS_MIN_VERSION="${IOS_MIN_VERSION:-12.0}"
IOS_BUILD_NUMBER="${IOS_BUILD_NUMBER:-1}"
ARCHIVE_DIR="${ARCHIVE_DIR:-$REPO_ROOT/build}"
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/build/output}"
UNITY_EXPORT_ONLY="${UNITY_EXPORT_ONLY:-0}"
XCODEBUILD_ONLY="${XCODEBUILD_ONLY:-0}"
XCODE_PROJECT_DIR="${XCODE_PROJECT_DIR:-}"

XCODE_EXPORT_DIR="$ARCHIVE_DIR/ios-xcode"
ARCHIVE_PATH="$ARCHIVE_DIR/App.xcarchive"
IPA_DIR="$OUTPUT_DIR"

LOG_DIR="$ARCHIVE_DIR/logs"
UNITY_LOG="$LOG_DIR/unity_export.log"
XCODE_LOG="$LOG_DIR/xcodebuild.log"

mkdir -p "$ARCHIVE_DIR" "$OUTPUT_DIR" "$LOG_DIR"

# 模式互斥檢查
if [[ "$UNITY_EXPORT_ONLY" == "1" && "$XCODEBUILD_ONLY" == "1" ]]; then
  echo "::error::UNITY_EXPORT_ONLY 與 XCODEBUILD_ONLY 不可同時為 1" >&2
  exit 1
fi

echo "═══════════════════════════════════════════════════════════════"
echo " build_unity_ipa.sh — iOS 建置管線"
if [[ "$XCODEBUILD_ONLY" == "1" ]]; then
  echo " 模式:XCODEBUILD_ONLY(僅 xcodebuild + 打包,CI macOS 用)"
elif [[ "$UNITY_EXPORT_ONLY" == "1" ]]; then
  echo " 模式:UNITY_EXPORT_ONLY(僅 Unity 匯出,Windows 本機用)"
else
  echo " 模式:全管線(fetch + Unity 匯出 + xcodebuild + 打包)"
fi
echo "═══════════════════════════════════════════════════════════════"
echo "  Bundle ID        : $IOS_BUNDLE_ID"
echo "  Version          : $IOS_VERSION (build $IOS_BUILD_NUMBER)"
echo "  Product Name     : $IOS_PRODUCT_NAME"
echo "  Min iOS Version  : $IOS_MIN_VERSION"
echo "  Archive Dir      : $ARCHIVE_DIR"
echo "  Output Dir       : $OUTPUT_DIR"
echo "═══════════════════════════════════════════════════════════════"

# ═══════════════════════════════════════════════════════════════
# 階段 0-1:Unity 匯出(UNITY_EXPORT_ONLY 或全管線模式)
# ═══════════════════════════════════════════════════════════════
if [[ "$XCODEBUILD_ONLY" != "1" ]]; then

  # ─── 階段 0:注入分塊資料 ──────────────────────────────────
  echo ""
  echo "─── 階段 0:注入分塊資料 ───"
  CHUNKS_DIR="$PROJECT_PATH/Assets/StreamingAssets/Chunks"
  bash "$SCRIPT_DIR/fetch_chunks.sh" "$CHUNKS_RELEASE_TAG" "$CHUNKS_DIR"

  # ─── 偵測 Unity/Tuanjie Editor 路徑 ───────────────────────
  UNITY_EDITOR="${UNITY_EDITOR:-}"
  if [[ -z "$UNITY_EDITOR" ]]; then
    for candidate in \
      "/c/Program Files/Tuanjie 2022.3.61t15/Editor/Tuanjie.exe" \
      "/mnt/c/Program Files/Tuanjie 2022.3.61t15/Editor/Tuanjie.exe" \
      "$(command -v Tuanjie 2>/dev/null || true)" \
      "$(command -v Unity 2>/dev/null || true)"; do
      if [[ -n "$candidate" && -f "$candidate" ]]; then
        UNITY_EDITOR="$candidate"
        break
      fi
    done
  fi

  if [[ -z "$UNITY_EDITOR" || ! -f "$UNITY_EDITOR" ]]; then
    echo "::error::找不到 Unity/Tuanjie Editor。請設定 UNITY_EDITOR 環境變數。" >&2
    exit 1
  fi
  echo "Unity Editor: $UNITY_EDITOR"

  # ─── 階段 1:匯出 Xcode 專案 ──────────────────────────────
  echo ""
  echo "─── 階段 1:匯出 Xcode 專案(BuildIOS.Export)───"

  export IOS_BUNDLE_ID IOS_VERSION IOS_BUILD_NUMBER IOS_MIN_VERSION IOS_PRODUCT_NAME
  export IOS_OUTPUT_DIR="$XCODE_EXPORT_DIR"

  rm -rf "$XCODE_EXPORT_DIR"

  "$UNITY_EDITOR" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -executeMethod BuildIOS.Export \
    -logFile "$UNITY_LOG" \
    -quit

  if [[ ! -d "$XCODE_EXPORT_DIR" ]]; then
    echo "::error::Xcode 專案匯出失敗 — 目錄不存在: $XCODE_EXPORT_DIR" >&2
    echo "::error::參見 Unity log: $UNITY_LOG" >&2
    tail -50 "$UNITY_LOG" 2>/dev/null || true
    exit 1
  fi

  XCODEPROJ=$(find "$XCODE_EXPORT_DIR" -maxdepth 1 -name "*.xcodeproj" | head -n1)
  if [[ -z "$XCODEPROJ" ]]; then
    echo "::error::匯出目錄中找不到 .xcodeproj" >&2
    exit 1
  fi
  echo "✅ Xcode 專案匯出成功: $XCODEPROJ"

  # 若僅匯出模式(Windows),在此停止
  if [[ "$UNITY_EXPORT_ONLY" == "1" ]]; then
    echo ""
    echo "─── UNITY_EXPORT_ONLY=1:僅匯出 Xcode 專案,跳過 xcodebuild ───"
    echo ""
    echo "Xcode 專案位於: $XCODE_EXPORT_DIR"
    echo "下一步:將此目錄打包上傳至 GitHub Release,由 CI macOS 執行 xcodebuild。"
    # 輸出 GITHUB_OUTPUT 供 CI 使用
    if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
      echo "xcode-export-dir=$XCODE_EXPORT_DIR" >> "$GITHUB_OUTPUT"
    fi
    exit 0
  fi

else  # XCODEBUILD_ONLY=1

  # 使用預匯出的 Xcode 專案
  if [[ -z "$XCODE_PROJECT_DIR" || ! -d "$XCODE_PROJECT_DIR" ]]; then
    echo "::error::XCODEBUILD_ONLY=1 需指定有效的 XCODE_PROJECT_DIR" >&2
    exit 1
  fi
  XCODE_EXPORT_DIR="$XCODE_PROJECT_DIR"
  XCODEPROJ=$(find "$XCODE_EXPORT_DIR" -maxdepth 1 -name "*.xcodeproj" | head -n1)
  if [[ -z "$XCODEPROJ" ]]; then
    echo "::error::在 $XCODE_EXPORT_DIR 中找不到 .xcodeproj" >&2
    exit 1
  fi
  echo "使用預匯出 Xcode 專案: $XCODEPROJ"

fi

# ═══════════════════════════════════════════════════════════════
# 階段 2-5:xcodebuild + 打包(僅 macOS)
# ═══════════════════════════════════════════════════════════════
if ! command -v xcodebuild >/dev/null 2>&1; then
  echo "::error::xcodebuild 不存在(僅 macOS 有)。" >&2
  echo "::error::在 Windows 上請設定 UNITY_EXPORT_ONLY=1;在 CI 上請使用 macOS runner。" >&2
  exit 1
fi

# ─── 階段 2:xcodebuild archive(未簽署)──────────────────────
echo ""
echo "─── 階段 2:xcodebuild archive(未簽署)───"

# 偵測 scheme
SCHEMES=$(xcodebuild -list -project "$XCODEPROJ" 2>/dev/null | grep -A20 "Schemes:" | grep -E "^\s+\S" | head -n1 | tr -d ' ')
if [[ -z "$SCHEMES" ]]; then
  SCHEMES="Unity-iPhone"  # Unity 預設 scheme 名稱
fi
echo "Scheme: $SCHEMES"

# 未簽署封存的 4 個旗標(AGENTS.md 硬性要求)
UNSIGNED_FLAGS=(
  CODE_SIGNING_ALLOWED=NO
  CODE_SIGNING_REQUIRED=NO
  CODE_SIGN_IDENTITY=""
  CODE_SIGN_ENTITLEMENTS=""
)

rm -rf "$ARCHIVE_PATH"
xcodebuild archive \
  -project "$XCODEPROJ" \
  -scheme "$SCHEMES" \
  -configuration Release \
  -archivePath "$ARCHIVE_PATH" \
  -destination "generic/platform=iOS" \
  "${UNSIGNED_FLAGS[@]}" \
  2>&1 | tee "$XCODE_LOG"

if [[ ! -d "$ARCHIVE_PATH" ]]; then
  echo "::error::封存失敗 — .xcarchive 不存在: $ARCHIVE_PATH" >&2
  tail -30 "$XCODE_LOG"
  exit 1
fi
echo "✅ 封存成功: $ARCHIVE_PATH"

# ─── 階段 3:打包 .ipa(Payload/<App>.app)────────────────────
echo ""
echo "─── 階段 3:打包 .ipa ───"

APP_DIR="$ARCHIVE_PATH/Products/Applications"
APP_NAME=$(find "$APP_DIR" -maxdepth 1 -name "*.app" | head -n1)
if [[ -z "$APP_NAME" ]]; then
  echo "::error::在 $APP_DIR 中找不到 .app" >&2
  exit 1
fi
APP_BASENAME=$(basename "$APP_NAME")  # 如 HKGridVoxel.app

# 從 Info.plist 讀取版本(避免硬編碼)
APP_VERSION=$(/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP_NAME/Info.plist" 2>/dev/null || echo "$IOS_VERSION")
APP_BUNDLE_ID=$(/usr/libexec/PlistBuddy -c "Print :CFBundleIdentifier" "$APP_NAME/Info.plist" 2>/dev/null || echo "$IOS_BUNDLE_ID")

IPA_NAME="${IOS_PRODUCT_NAME}-${APP_VERSION}.ipa"
DSYM_NAME="${IOS_PRODUCT_NAME}-${APP_VERSION}.dSYM.zip"
IPA_PATH="$IPA_DIR/$IPA_NAME"
DSYM_PATH="$IPA_DIR/$DSYM_NAME"

echo "App: $APP_BASENAME (version=$APP_VERSION, bundleId=$APP_BUNDLE_ID)"
echo "IPA: $IPA_PATH"
echo "dSYM: $DSYM_PATH"

# 建立 Payload/<App>.app 結構
rm -rf "$ARCHIVE_DIR/Payload"
mkdir -p "$ARCHIVE_DIR/Payload"
# 使用 ditto 保留符號連結與元資料(AGENTS.md 要求)
ditto "$APP_NAME" "$ARCHIVE_DIR/Payload/$APP_BASENAME"

# 從包含 Payload/ 的目錄打包(ipa 根 = Payload/)
(cd "$ARCHIVE_DIR" && zip -r -y "$IPA_PATH" Payload)

# 驗證 .ipa 結構
echo ""
echo "─── 驗證 .ipa 結構(unzip -l 前 10 行)───"
unzip -l "$IPA_PATH" | head -10

if ! unzip -l "$IPA_PATH" | grep -q "^.*Payload/${APP_BASENAME}/$"; then
  echo "::error::.ipa 結構錯誤:根目錄未含 Payload/${APP_BASENAME}/" >&2
  exit 1
fi
echo "✅ .ipa 結構正確(Payload/${APP_BASENAME}/ 在根)"

# ─── 階段 4:打包 dSYMs ────────────────────────────────────────
echo ""
echo "─── 階段 4:打包 dSYMs ───"

DSYM_SRC="$ARCHIVE_PATH/dSYMs"
if [[ -d "$DSYM_SRC" ]]; then
  (cd "$DSYM_SRC" && zip -r -y "$DSYM_PATH" *.dSYM)
  echo "✅ dSYM 打包完成: $DSYM_PATH"
else
  echo "⚠ 無 dSYMs 目錄(可能未產生除錯符號),跳過"
  DSYM_NAME=""
fi

# ─── 階段 5:manifest.json + GITHUB_OUTPUT ─────────────────────
echo ""
echo "─── 階段 5:產生 manifest.json ───"

IPA_SIZE=$(stat -f%z "$IPA_PATH" 2>/dev/null || stat -c%s "$IPA_PATH" 2>/dev/null || echo 0)

MANIFEST_PATH="$IPA_DIR/manifest.json"
cat > "$MANIFEST_PATH" <<EOF
{
  "appName": "$IOS_PRODUCT_NAME",
  "bundleIdentifier": "$APP_BUNDLE_ID",
  "version": "$APP_VERSION",
  "ipaFileName": "$IPA_NAME",
  "dsymFileName": "${DSYM_NAME:-}",
  "ipaSizeBytes": $IPA_SIZE,
  "minIOSVersion": "$IOS_MIN_VERSION"
}
EOF
cat "$MANIFEST_PATH"

# GITHUB_OUTPUT(供 GitHub Actions 使用)
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "ipa-path=$IPA_PATH"           >> "$GITHUB_OUTPUT"
  echo "dsym-path=$DSYM_PATH"         >> "$GITHUB_OUTPUT"
  echo "manifest-path=$MANIFEST_PATH" >> "$GITHUB_OUTPUT"
  echo "app-name=$IOS_PRODUCT_NAME"   >> "$GITHUB_OUTPUT"
  echo "app-version=$APP_VERSION"     >> "$GITHUB_OUTPUT"
  echo "bundle-id=$APP_BUNDLE_ID"     >> "$GITHUB_OUTPUT"
  echo "ipa-size=$IPA_SIZE"           >> "$GITHUB_OUTPUT"
  echo "ipa-file-name=$IPA_NAME"      >> "$GITHUB_OUTPUT"
  echo "dsym-file-name=${DSYM_NAME:-}" >> "$GITHUB_OUTPUT"
fi

echo ""
echo "═══════════════════════════════════════════════════════════════"
echo " ✅ 建置完成"
echo "═══════════════════════════════════════════════════════════════"
echo "  IPA      : $IPA_PATH ($((IPA_SIZE / 1024 / 1024)) MB)"
echo "  dSYM     : ${DSYM_PATH:-N/A}"
echo "  manifest : $MANIFEST_PATH"
echo "═══════════════════════════════════════════════════════════════"
