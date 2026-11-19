# export_xcode_project.ps1 — 本機 Windows 匯出 iOS Xcode 專案(供 CI macOS 執行 xcodebuild)
#
# 用途:Tuanjie 授權為機器綁定,僅本機可用;Windows 無 xcodebuild。
#       此腳本在本機用 Tuanjie 匯出 Xcode 專案,打包為 tar.gz,
#       供使用者上傳至 GitHub Release 後,由 CI macOS runner 執行 xcodebuild。
#
# 前置條件:
#   1. Tuanjie iOS Build Support 模組已安裝(經 Tuanjie Hub)
#   2. StreamingAssets/Chunks/ 已有分塊資料(.glb + manifest.json)
#   3. Tuanjie Editor 未在執行中(批次模式需獨佔)
#
# 用法:
#   .\export_xcode_project.ps1 [-Version "2.1.0"] [-Upload]
#
# 參數:
#   -Version  — 版本標籤(預設:1.0,對應 IOS_VERSION)
#   -Upload   — 若設定,自動用 gh CLI 上傳至 GitHub Release(需 gh auth login)
#
param(
    [string]$Version = "1.0",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"

# ─── 路徑設定 ───────────────────────────────────────────────
$RepoRoot = Resolve-Path "$PSScriptRoot\..\.."
$ProjectPath = "$RepoRoot\codebase\unity-project"
$TuanjieEditor = "C:\Program Files\Tuanjie 2022.3.61t15\Editor\Tuanjie.exe"
$BuildDir = "$RepoRoot\build"
$XcodeExportDir = "$BuildDir\ios-xcode"
$LogDir = "$BuildDir\logs"
$UnityLog = "$LogDir\unity_export.log"
$TarballName = "xcode-export-v$Version.tar.gz"
$TarballPath = "$BuildDir\$TarballName"

# 環境變數傳遞給 BuildIOS.cs
$env:IOS_BUNDLE_ID = "com.mccmak.hkgridvoxel"
$env:IOS_VERSION = $Version
$env:IOS_BUILD_NUMBER = "1"
$env:IOS_MIN_VERSION = "12.0"
$env:IOS_PRODUCT_NAME = "HKGridVoxel"
$env:IOS_OUTPUT_DIR = $XcodeExportDir

Write-Output "═══════════════════════════════════════════════════════════════"
Write-Output " export_xcode_project.ps1 — 本機匯出 iOS Xcode 專案"
Write-Output "═══════════════════════════════════════════════════════════════"
Write-Output "  Tuanjie Editor  : $TuanjieEditor"
Write-Output "  Project         : $ProjectPath"
Write-Output "  Version         : $Version"
Write-Output "  Bundle ID       : $($env:IOS_BUNDLE_ID)"
Write-Output "  Xcode 匯出目錄  : $XcodeExportDir"
Write-Output "  Tarball         : $TarballPath"
Write-Output "═══════════════════════════════════════════════════════════════"

# ─── 前置檢查 ───────────────────────────────────────────────
if (-not (Test-Path $TuanjieEditor)) {
    Write-Error "找不到 Tuanjie Editor: $TuanjieEditor"
    exit 1
}

# 檢查 iOS Build Support 模組
$IosModule = "C:\Program Files\Tuanjie 2022.3.61t15\Editor\Data\PlaybackEngines\iossupport"
if (-not (Test-Path $IosModule)) {
    Write-Warning "⚠ 找不到 iOS Build Support 模組($IosModule)"
    Write-Warning "  Tuanjie Hub 可能無法偵測此 Editor 安裝(掃描路徑不同)。"
    Write-Warning "  直接下載模組安裝器並以 /S /D=<Editor 根目錄> 靜默安裝:"
    Write-Warning "    https://download.tuanjie.cn/download_tuanjie/08f812df7f0b/TargetSupportInstaller/TuanjieSetup-iOS-Support-for-Editor-2022.3.61t15.exe"
    Write-Warning "  安裝指令(管理員 PowerShell):"
    Write-Warning "    & 'TuanjieSetup-iOS-Support-for-Editor-2022.3.61t15.exe' /S /D='C:\Program Files\Tuanjie 2022.3.61t15'"
    $confirm = Read-Host "是否仍要繼續?(y/N)"
    if ($confirm -ne "y") { exit 1 }
}

# 檢查分塊資料
$ChunksDir = "$ProjectPath\Assets\StreamingAssets\Chunks"
if (-not (Test-Path "$ChunksDir\manifest.json")) {
    Write-Error "找不到分塊資料:$ChunksDir\manifest.json"
    Write-Error "請先執行 fetch_chunks.sh 注入分塊資料,或確認 Stage A 轉換已完成。"
    exit 1
}
$GlbCount = (Get-ChildItem "$ChunksDir\*.glb" -ErrorAction SilentlyContinue).Count
Write-Output "分塊資料:$GlbCount 個 .glb + manifest.json"

# 檢查 Tuanjie 是否正在執行
$tuanjieProcs = Get-Process -Name "Tuanjie" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "*Editor*Tuanjie.exe" }
if ($tuanjieProcs) {
    Write-Warning "⚠ Tuanjie Editor 正在執行中(PID: $($tuanjieProcs.Id -join ', '))"
    Write-Warning "  批次模式需獨佔專案,建議先關閉 Editor。"
    $confirm = Read-Host "是否自動關閉?(y/N)"
    if ($confirm -eq "y") {
        $tuanjieProcs | Stop-Process -Force
        Start-Sleep -Seconds 3
        Write-Output "已關閉 Tuanjie Editor。"
    } else {
        Write-Error "請先關閉 Tuanjie Editor 再執行此腳本。"
        exit 1
    }
}

# ─── 清理舊產物 ─────────────────────────────────────────────
Write-Output ""
Write-Output "─── 清理舊產物 ───"
if (Test-Path $XcodeExportDir) { Remove-Item $XcodeExportDir -Recurse -Force }
if (Test-Path $TarballPath) { Remove-Item $TarballPath -Force }
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

# ─── 執行 Tuanjie 批次匯出 ──────────────────────────────────
Write-Output ""
Write-Output "─── 執行 Tuanjie 批次匯出(BuildIOS.Export)───"
Write-Output "開始時間:$(Get-Date -Format 'HH:mm:ss')"

$argList = @(
    "-batchmode",
    "-nographics",
    "-projectPath", "`"$ProjectPath`"",
    "-executeMethod", "BuildIOS.Export",
    "-logFile", "`"$UnityLog`"",
    "-quit"
)

Write-Output "命令:Tuanjie.exe $($argList -join ' ')"
& $TuanjieEditor $argList
$exitCode = $LASTEXITCODE
Write-Output "Tuanjie 結束代碼:$exitCode(注意:stderr 雜訊為正常,以產物存在為準)"
Write-Output "結束時間:$(Get-Date -Format 'HH:mm:ss')"

# ─── 驗證匯出結果 ───────────────────────────────────────────
if (-not (Test-Path $XcodeExportDir)) {
    Write-Error "匯出失敗 — 目錄不存在:$XcodeExportDir"
    Write-Output ""
    Write-Output "=== Unity log 最後 50 行 ==="
    if (Test-Path $UnityLog) {
        Get-Content $UnityLog -Encoding UTF8 -Tail 50
    }
    exit 1
}

$Xcodeproj = Get-ChildItem $XcodeExportDir -Filter "*.xcodeproj" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $Xcodeproj) {
    Write-Error "匯出目錄中找不到 .xcodeproj:$XcodeExportDir"
    exit 1
}

Write-Output ""
Write-Output "✅ Xcode 專案匯出成功:$($Xcodeproj.FullName)"

# 顯示匯出目錄大小
$exportSize = (Get-ChildItem $XcodeExportDir -Recurse -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Output "   匯出目錄大小:$([math]::Round($exportSize / 1MB, 1)) MB"

# ─── 打包為 tar.gz ──────────────────────────────────────────
Write-Output ""
Write-Output "─── 打包為 $TarballName ───"
# tar -czf <output> -C <dir> .(打包目錄內容,不含目錄名)
& tar -czf $TarballPath -C $XcodeExportDir .
if ($LASTEXITCODE -ne 0) {
    Write-Error "打包失敗(tar exit $LASTEXITCODE)"
    exit 1
}

$tarballSize = (Get-Item $TarballPath).Length
Write-Output "✅ 打包完成:$TarballPath($([math]::Round($tarballSize / 1MB, 1)) MB)"

# ─── 上傳至 GitHub Release(可選)────────────────────────────
if ($Upload) {
    Write-Output ""
    Write-Output "─── 上傳至 GitHub Release ───"
    $releaseTag = "xcode-export-v$Version"

    # 檢查 gh 是否已認證
    $ghAuth = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "gh CLI 未認證。請先執行:gh auth login"
        Write-Output "之後手動上傳:"
        Write-Output "  gh release create $releaseTag `"$TarballPath`" --title "Xcode 匯出 v$Version" --notes "本機 Tuanjie 匯出的 iOS Xcode 專案""
        exit 1
    }

    # 建立 release 並上傳
    gh release create $releaseTag "$TarballPath" --title "Xcode 匯出 v$Version" --notes "本機 Tuanjie 匯出的 iOS Xcode 專案,供 CI macOS 執行 xcodebuild。"
    if ($LASTEXITCODE -eq 0) {
        Write-Output "✅ 已上傳至 release $releaseTag"
        Write-Output ""
        Write-Output "下一步:至 GitHub Actions 頁面手動觸發 workflow:"
        Write-Output "  xcode_export_tag = $releaseTag"
        Write-Output "  release_tag      = v$Version"
    } else {
        Write-Error "上傳失敗。請手動上傳至 GitHub Release。"
        exit 1
    }
} else {
    Write-Output ""
Write-Output "═══════════════════════════════════════════════════════════════"
Write-Output " ✅ 匯出完成"
Write-Output "═══════════════════════════════════════════════════════════════"
Write-Output "  Tarball:$TarballPath"
Write-Output ""
Write-Output "下一步:"
Write-Output "  1. 上傳 $TarballName 至 GitHub Release(標籤 xcode-export-v$Version):"
Write-Output "     gh release create xcode-export-v$Version `"$TarballPath`" --title "Xcode 匯出 v$Version" --notes "...""
Write-Output "     或透過 GitHub 網頁 UI 上傳。"
Write-Output "  2. 至 GitHub Actions 頁面手動觸發 workflow_dispatch:"
Write-Output "     xcode_export_tag = xcode-export-v$Version"
Write-Output "     release_tag      = v$Version"
Write-Output "═══════════════════════════════════════════════════════════════"
}
