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
#   .\export_xcode_project.ps1 [-Version "2.2.15"] [-Upload] [-ChunksSource <dir>]
#
# 參數:
#   -Version      — 版本標籤(預設:2.2.15,對應 IOS_VERSION)
#   -Upload       — 若設定,自動用 gh CLI 上傳至 GitHub Release(需 gh auth login)
#   -ChunksSource — IPA 分塊來源目錄(預設:v2.2.14 2MB IPA 分塊)。
#                   設定 IPA_CHUNKS_SOURCE_DIR 環境變數,BuildIOS.cs 會於匯出前
#                   複製至 StreamingAssets/Chunks(覆寫本地高解析度分塊)。
#                   不設定時沿用 StreamingAssets/Chunks 現有內容(向後相容)。
param(
    [string]$Version = "2.2.15",
    [switch]$Upload,
    [string]$ChunksSource = "C:\Users\ccmak\AppData\Local\Temp\opencode\csdi\11-NW-15B_chunks_ipa_2mb_v2214"
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

# v2.2.15: IPA 分塊來源 — BuildIOS.cs 會於匯出前複製至 StreamingAssets/Chunks,
# 覆寫本地高解析度分塊(98MB Unity 版)。未設定時沿用現有內容(向後相容)。
if ($ChunksSource -and (Test-Path $ChunksSource)) {
    $env:IPA_CHUNKS_SOURCE_DIR = $ChunksSource
    $ipaGlbCount = (Get-ChildItem "$ChunksSource\*.glb" -ErrorAction SilentlyContinue).Count
    $ipaTotalMB = [math]::Round((Get-ChildItem "$ChunksSource\*.glb" -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    Write-Output "  IPA 分塊來源   : $ChunksSource ($ipaGlbCount glbs, $ipaTotalMB MB)"
} elseif ($ChunksSource) {
    Write-Warning "⚠ 指定的 -ChunksSource 不存在:$ChunksSource"
    Write-Warning "  將沿用 StreamingAssets/Chunks 現有內容(可能為高解析度 Unity 版)"
    $env:IPA_CHUNKS_SOURCE_DIR = $null
} else {
    $env:IPA_CHUNKS_SOURCE_DIR = $null
}

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
# v2.2.15: 若設定 IPA_CHUNKS_SOURCE_DIR,BuildIOS.cs 會於匯出前注入分塊,
# 故不需預先檢查 StreamingAssets/Chunks(可能含高解析度 Unity 版或為空)。
$ChunksDir = "$ProjectPath\Assets\StreamingAssets\Chunks"
if ($env:IPA_CHUNKS_SOURCE_DIR) {
    Write-Output "分塊資料:將由 IPA_CHUNKS_SOURCE_DIR 注入(BuildIOS.cs 匯出前複製)"
    Write-Output "  來源:$($env:IPA_CHUNKS_SOURCE_DIR)"
} elseif (-not (Test-Path "$ChunksDir\manifest.json")) {
    Write-Error "找不到分塊資料:$ChunksDir\manifest.json"
    Write-Error "請用 -ChunksSource 指定 IPA 分塊來源,或先執行 swap_chunks.ps1 注入分塊。"
    exit 1
} else {
    $GlbCount = (Get-ChildItem "$ChunksDir\*.glb" -ErrorAction SilentlyContinue).Count
    Write-Output "分塊資料(現有 StreamingAssets/Chunks):$GlbCount 個 .glb + manifest.json"
    Write-Warning "⚠ 未設定 -ChunksSource,將沿用 StreamingAssets/Chunks 現有內容。"
    Write-Warning "  若含高解析度 Unity 版(98MB),IPA 會過大且 iPhone 可能 OOM。"
}

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
    "-projectPath", "`"$ProjectPath`"",
    "-executeMethod", "BuildIOS.Export",
    "-logFile", "`"$UnityLog`"",
    "-quit"
)

Write-Output "命令:Tuanjie.exe $($argList -join ' ')"
& $TuanjieEditor $argList
$exitCode = $LASTEXITCODE
Write-Output "Tuanjie 啟動器結束代碼:$exitCode(注意:stderr 雜訊為正常,以產物存在為準)"

# v2.2.16: Tuanjie.exe 為啟動器,可能立即返回但 Editor 仍在背景執行建置。
# 等待 Editor 進程結束(最多 15 分鐘),再驗證產物。
Write-Output "等待 Tuanjie Editor 完成建置(最多 15 分鐘)..."
$maxWaitSeconds = 900
$waited = 0
$editorSeen = $false
while ($waited -lt $maxWaitSeconds) {
    $editorProcs = Get-Process -Name "Tuanjie" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "*Editor*Tuanjie.exe" }
    if ($editorProcs) {
        $editorSeen = $true
    } elseif ($editorSeen) {
        # Editor 進程曾存在但已結束 → 建置完成
        Start-Sleep -Seconds 3  # 等待檔案系統刷新
        Write-Output "✅ Tuanjie Editor 已結束(等待 $waited 秒)"
        break
    }
    Start-Sleep -Seconds 5
    $waited += 5
    if ($waited % 60 -eq 0) {
        $xcodeprojExists = Test-Path "$XcodeExportDir\*.xcodeproj"
        Write-Output "  等待中... ${waited}s (Editor running: $([bool]$editorProcs), xcodeproj: $xcodeprojExists)"
    }
}
if ($waited -ge $maxWaitSeconds) {
    Write-Warning "⚠ 等待逾時($maxWaitSeconds 秒),繼續檢查產物"
}
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
