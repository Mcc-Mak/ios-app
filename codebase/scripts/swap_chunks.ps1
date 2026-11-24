# swap_chunks.ps1 — 切換 StreamingAssets/Chunks 在本地高解析度與 IPA 低解析度之間
#
# 用法:
#   .\codebase\scripts\swap_chunks.ps1 local   # 切換至本地高解析度(15B L18 nofloat,204.6MB,1.69M 面)
#   .\codebase\scripts\swap_chunks.ps1 ipa     # 切換至 IPA 低解析度(15B flat,1.29MB,33K 面)
#   .\codebase\scripts\swap_chunks.ps1 status  # 顯示目前分塊版本與大小(不切換)
#
# 注意:來源目錄為機器專屬暫存路徑(C:\Users\ccmak\...\Temp\opencode\csdi\...),
#       僅適用本機。StreamingAssets/Chunks 已 gitignore,不會被提交。

param(
    [Parameter(Position = 0)]
    [string]$Mode = "status"
)

# ── 路徑設定(機器專屬)─────────────────────────────────────────────
$LocalSrc = "C:\Users\ccmak\AppData\Local\Temp\opencode\csdi\11-NW-15B_chunks_unity_800mb"
$IpaSrc   = "C:\Users\ccmak\AppData\Local\Temp\opencode\csdi\11-NW-15B_chunks_ipa_2mb"
$Dst      = "C:\Users\ccmak\Desktop\OpenCode Workplace\ios-app\codebase\unity-project\Assets\StreamingAssets\Chunks"

function Copy-Chunks {
    param([string]$Src, [string]$Label)
    if (-not (Test-Path $Src)) {
        Write-Error "來源目錄不存在: $Src"
        exit 1
    }
    if (-not (Test-Path $Dst)) {
        New-Item -ItemType Directory -Path $Dst -Force | Out-Null
    }
    # 清空目的地舊檔(避免殘留另一版本的 .glb)
    Get-ChildItem $Dst -File | Remove-Item -Force
    Copy-Item "$Src\*" -Destination $Dst -Force
    $count = (Get-ChildItem $Dst -Filter *.glb).Count
    $sizeMB = [math]::Round((Get-ChildItem $Dst -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Output "✅ 已切換至 $Label"
    Write-Output "   分塊數: $count, 總計 $sizeMB MB"
    Write-Output "   來源:   $Src"
    Write-Output "   目的:   $Dst"
}

function Show-Status {
    if (-not (Test-Path "$Dst\manifest.json")) {
        Write-Output "StreamingAssets/Chunks 無 manifest.json(可能為空)"
        return
    }
    $m = Get-Content "$Dst\manifest.json" -Raw | ConvertFrom-Json
    $total = ($m.chunks | Measure-Object -Property bytes -Sum).Sum
    $tris  = ($m.chunks | Measure-Object -Property triangles -Sum).Sum
    $sizeMB = [math]::Round($total / 1MB, 1)
    $ver = if ($sizeMB -lt 25) { "IPA 極低解析度(flat)" }
           elseif ($sizeMB -lt 60) { "本地高解析度(L17)" }
           elseif ($sizeMB -lt 100) { "L18 raw(中解析度)" }
           else { "本地高解析度(L18 nofloat)" }
    Write-Output "目前 StreamingAssets/Chunks: $ver"
    Write-Output "   分塊數: $($m.chunks.Count), 總計 $sizeMB MB, $tris 面"
    Write-Output "   has positionOffset: $($null -ne $m.chunks[0].positionOffset)"
}

switch ($Mode.ToLower()) {
    "local"  { Copy-Chunks -Src $LocalSrc -Label "本地高解析度(L18 nofloat)" }
    "ipa"    { Copy-Chunks -Src $IpaSrc   -Label "IPA 極低解析度(flat)" }
    "status" { Show-Status }
    default  { Write-Output "未知模式: $Mode(可用:local / ipa / status)" }
}
