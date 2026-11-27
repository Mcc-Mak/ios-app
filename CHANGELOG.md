# 變更記錄 (CHANGELOG)

本專案所有顯著變更皆記錄於此檔案。版本遵循[語意化版本](https://semver.org/lang/zh-TW/)
(`vX.X.X`)。每次依提示進行變更時,依變更性質升版並於此新增條目:

- **主版本 (X.0.0):** 不相容的破壞性變更
- **次版本 (1.X.0):** 向後相容的新功能
- **修訂版 (1.1.X):** 向後相容的修正

格式參考 [Keep a Changelog](https://keepachangelog.com/zh-TW/)。

## [v2.6.7] - 2026-11-27

### 變更(五圖幅 LOD 升級 l18→l19hybrid + 修正 spawn 藍屏根因)

**A. LOD 升級:l18 → l19hybrid(l19 為主,l18 回填缺塊)**

- v2.6.6/v2.6.7 初版五圖幅(22A + 17C + 21B + 21A + 16D)使用 l18。本版將全部五圖幅升級至 l19,以提升模型細節。
- **問題**:CSDI OSGB 源資料 l19 覆蓋不完整——部分子圖幅無 l19 檔案(22A 僅 7/20、21B 16/20、21A 18/20 有 l19),純 l19 轉換會跳過缺塊子圖幅 → 地面大洞。
- **解法:轉換器新增 `--lod-fallback` 選項**(向下相容新功能)。當主 `--lod` 在某子圖幅無檔案時,自動回退至指定 LOD。本版使用 `--lod l19 --lod-fallback l18`,確保所有 100 個子圖幅都有資料(l19 主體 + l18 回填缺塊),無地面空洞。`SubTile.lod_used` 欄位記錄各子圖幅實際使用的 LOD。
- **每圖幅 l19/l18 混合比例**(經 `--lod-base` 覆蓋率檢查後最終結果):

  | 圖幅 | l19 子圖幅 | l18 回填 | 說明 |
  | --- | ---: | ---: | --- |
  | 6-SE-16D | 20/20 | 0 | 純 l19(覆蓋率全數 ≥ 80%) |
  | 6-SE-17C | 18/20 | 2 | 2 子圖幅 l19 覆蓋率 < 80%(最差 15%)→ l18 |
  | 6-SE-21A | 18/20 | 2 | 2 子圖幅 l19 覆蓋率 < 80% → l18 |
  | 6-SE-21B | 15/20 | 5 | 5 子圖幅 l19 覆蓋率 < 80%(最差 2%)→ l18 |
  | 6-SE-22A | 5/20 | 15 | 22A 為 SE 邊緣,l19 源資料覆蓋最少(最差 5%) |

- **轉換參數**(沿用 l18 IPA-friendly 食譜,僅改 LOD):`--lod l19 --lod-fallback l18 --lod-base l18 --no-floating-removal --max-texture-px 512 --max-chunk-mb 15 --max-total-mb 800 --decimate-threshold 5000`。
- **l18 vs l19hybrid 大小對照**:

  | 圖幅 | l18 MB | l18 三角面 | l19hybrid MB | l19hybrid 三角面 | size 倍率 | tris 倍率 |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: |
  | 6-SE-16D | 69.8 | 727,290 | 140.1 | 1,264,170 | 2.01x | 1.74x |
  | 6-SE-17C | 149.3 | 1,676,835 | 234.7 | 2,513,311 | 1.57x | 1.50x |
  | 6-SE-21A | 112.2 | 1,279,072 | 178.3 | 1,907,054 | 1.59x | 1.49x |
  | 6-SE-21B | 85.0 | 856,047 | 142.1 | 1,358,055 | 1.67x | 1.59x |
  | 6-SE-22A | 62.2 | 716,549 | 65.3 | 787,828 | 1.05x | 1.10x |
  | **總計** | **478.5** | **5,255,793** | **760.5** | **7,830,418** | **1.59x** | **1.49x** |

  - 16D 全數 l19,細節提升最顯著。22A 因 l19 源資料覆蓋率普遍不足(15/20 子圖幅回退 l18),大小接近純 l18。總計 760.5 MB 低於 800 MB 預算。

**B. 五圖幅無縫接合(E-W 反轉公式,l19hybrid 校正值)**

- 五圖幅布局:22A(東南/基準)+ 17C(北)+ 21B(西南)+ 21A(最西,21B 西側)+ 16D(西北,21B 北側,與 17C 同列)。
- **E-W 反轉通用公式**(以 22A 為基準 R,圖幅 T 目標格偏移 (dx, dy)):
  - `dx>0 = 北、dx<0 = 南`(南北軸 `positionOffset[1]` 未反轉);`dy>0 = 西、dy<0 = 東`(東西軸 `positionOffset[0]` 在 Unity 中反轉,值越大越西)。
  - `T.corrected[0] = R[0] + dy × TILE_W`;`T.corrected[1] = R[1] + dx × TILE_H`;`T.corrected[2] = T.original[2]`(高度不變)。
  - 常數(l19hybrid):`TILE_W = 749.810`;`TILE_H = 598.908`(l18 為 598.727,差異來自各 LOD 之子圖幅原點)。
- **五圖幅修正後 positionOffset(l19hybrid)**:

  | 圖幅 | 目標 (dx,dy) | 修正後 positionOffset |
  | --- | --- | --- |
  | 6-SE-22A | (0, 0) | [750.07, −596.34, 8.55] |
  | 6-SE-17C | (+1, 0) | [750.07, 2.57, 8.50] |
  | 6-SE-21B | (0, +1) | [1499.88, −596.34, −13.80] |
  | 6-SE-21A | (0, +2) | [2249.69, −596.34, −21.50] |
  | 6-SE-16D | (+1, +1) | [1499.88, 2.57, 0.00] |

- **無縫鄰接驗證(六項全數通過)**:21A↔21B、21B↔22A、16D↔21B、16D↔17C 東西/南北差距皆為整數倍 TILE_W/TILE_H ✓。

**C. 修正 spawn 藍屏根因(無縫 manifest 的 centroid/bbox 未同步位移)**

- **症狀**:Unity Play / IPA 啟動後畫面全藍(藍屏),玩家落入虛空。Console 顯示 `OnSpawned: 地面未找到`。此為 v2.6.6/v2.6.7 初版即存在的 bug(發生於 l18 資料 + 舊無縫 manifest)。
- **根因**:無縫校正腳本(`build_*_seamless.py` / `apply_seamless_ew`)對各圖幅 `positionOffset` 套用 E-W 反轉位移(`shift`),但 **`centroid` 與 `bbox` 未同步位移**,停留在原始(未位移)座標系。後果:
  - `ChunkStreamer` 渲染:容器節點於 `positionOffset`(已位移)+ GLB 頂點(圍繞本地 centroid)→ 網格渲染於 `positionOffset_shifted + local_centroid` = `centroid_original + shift`(位移後位置)。
  - `ChunkStreamBootstrapper` spawn:累加 `c.CentroidVector`(manifest centroid,未位移)→ spawn 落於 `centroid_original`(未位移座標系),距最近渲染分塊約 **1985 m**(21A 實測,shift ≈ 2999 m)→ 地面跟隨射線(250/500 m)打不到任何網格 → 永墜 → 藍屏。
  - 經 GLB 頂點實測確認:GLB 頂點已由轉換器 recenter 至圖幅 centroid(頂點圍繞本地 centroid,非全域座標);`merge_tiles.py` 將 `positionOffset` 設為圖幅偏移、`centroid` 設為全域座標(= 本地 centroid + 圖幅偏移)。無縫位移僅改 `positionOffset` → `centroid` 與渲染位置分歧 `shift` 量。
- **修正**:無縫 manifest 產生時,`positionOffset`、`centroid`、`bbox` 三者皆套用相同 `shift`(X/Y 軸;Z 軸 shift = 0 不變)。修正後 `centroid` = `positionOffset_shifted + local_centroid` = 渲染網格中心,spawn(`avg centroid`)落於 21A 渲染中心,距最近分塊僅 **77 m**(實測)。`ChunkStreamBootstrapper.cs` spawn 邏輯維持原樣(`c.CentroidVector`,無需改動)——根因在 manifest 資料,非程式碼。
- 修正腳本:`fix_seamless_centroid.py`(事後修補既有 manifest)+ `convert_5tiles_l19hybrid.py` 的 `apply_seamless_ew`(未來重跑時同步位移 centroid/bbox)。
- `SetupScene.cs`(建置權威)與 `ChunkStreamConfig.asset`(Unity Play)的 `spawnTileCode` 均為 `6-SE-21A`。

**D. 其他**

- `export_xcode_project.ps1` 預設 `-ChunksSource` 改為 `5-tiles-seamless_l19hybrid_nofloat`;註解同步更新。
- Unity 專案 `Assets/StreamingAssets/Chunks/`(gitignored)已回填 100 個 l19hybrid GLB + 新 `manifest.json`(760.5 MB,76 塊 l19 + 24 塊 l18),供 Unity Play 顯示正確資料;IPA 匯出時由 `BuildIOS.cs` 自 `-ChunksSource` 覆寫。`.meta` 由 Unity 匯入時自動重新生成(檔名與 l18 一致,沿用舊 `.meta`)。
- 執行時影響:`spawnTileCode=6-SE-21A`(玩家出生於 21A 最西側,可向東依序進入 21B→22A,向北進入 16D/17C);`preloadAllChunks=true` 預載全部 100 分塊;`addChunkColliders=true`(真實網格為地面);`groundFillEnabled=false`(GroundFill 停用)。
- 小地圖(M 鍵)顯示五個圖幅矩形:17C 與 16D 並列於北(16D 在 17C 西側),21A、21B、22A 並列於南(由西至東:21A—21B—22A)。
- **已知**:17C/16D 北列與 22A/21B/21A 南列之間仍有約 599 m 南北空隙(自 v2.6.5 即存在,使用者未反映問題,暫不處理)。

**E. l19 覆蓋率修正(轉換器新增 `--lod-base` 選項;v2.6.9)**

- **問題**:CSDI l19 OSGB 源資料的部分子圖幅覆蓋率不足——l19 四元樹僅涵蓋部分分支,導致某些分塊的 XY 覆蓋面積僅為 l18 的 2%–80%。`--lod-fallback` 僅在子圖幅**完全無** l19 檔案時才回退,無法處理「有檔但覆蓋不足」→ 地面大洞。經實測 5 圖幅共 7 個分塊覆蓋不足:

  | 圖幅 | 覆蓋不足分塊 | 最差覆蓋率 |
  | --- | ---: | ---: |
  | 6-SE-16D | 1 | 55% |
  | 6-SE-17C | 3 | 15% |
  | 6-SE-21A | 0 | 全數 ≥ 80% |
  | 6-SE-21B | 1 | 2% |
  | 6-SE-22A | 2 | 5% |

- **解法**:轉換器新增 `--lod-base` 選項。指定基底 LOD(如 `--lod-base l18`),預掃描階段同時轉換主 LOD(l19)與基底 LOD(l18)的 OSGB → OBJ,比較 XY 覆蓋面積。若主 LOD 覆蓋率 < 基底 LOD 的 80%,自動改用基底 LOD(確保完整覆蓋)。所有分塊共用同一圖幅 centroid(無 recenter 不一致問題)。使用者命名為「l18 為底 + l19 增強解析」。
- **與 `--lod-fallback` 的差異**:`--lod-fallback` 處理「完全無檔」(子圖幅無 l19 檔案 → 回退 l18);`--lod-base` 處理「有檔但覆蓋不足」(子圖幅有 l19 檔案但覆蓋率 < 80% → 改用 l18)。兩者可同時使用。
- `SubTile.base_osgbs` 欄位儲存基底 LOD 檔案;`ChunkRecord.lod_used` 欄位記錄各分塊實際使用的 LOD(l19 / l18),序列化至 manifest.json(同時修正 v2.6.8 `lod_used` 未寫入 manifest 的問題)。
- 轉換參數:`--lod l19 --lod-fallback l18 --lod-base l18 --no-floating-removal --max-texture-px 512 --max-chunk-mb 15 --max-total-mb 800 --decimate-threshold 5000`。
- **重新轉換結果**:100 分塊,76 塊 l19 + 24 塊 l18,760.5 MB / 7,830,418 三角面。所有 6 項無縫驗證通過。`lod_used` 欄位已正確寫入 manifest.json。

**F. 停用跳躍冷卻 + 確保 IPA 與 Unity 使用相同 3D 模型**

- **停用跳躍冷卻**:`TouchPlayerController.jumpCooldown` 由 `0.5f` 改為 `0f`(著地即可再跳,無需等待 0.5 秒)。三處同步更新:`TouchPlayerController.cs` 預設值、`Main.unity` 序列化值、`SetupScene.cs` 建置權威覆寫。跳躍仍受 `!_isJumping`(必須著地)與 `_isGrounded`(必須在地面)雙重門控,避免空中連跳。
- **IPA = Unity 一致性保證**:`export_xcode_project.ps1` 新增一致性檢查——比對 `-ChunksSource`(IPA 分塊來源)與 `StreamingAssets/Chunks`(Unity Play 來源)的 GLB 數量。相符時印出 `✓ IPA 與 Unity 一致`;不符時警告。實際資料流:匯出時 `BuildIOS.CopyIpaChunksIfNeeded()` 自 `-ChunksSource`(預設 `5-tiles-seamless_l19hybrid_nofloat`)複製 100 GLB 至 `StreamingAssets/Chunks` → 烘焙進 Xcode 專案 → CI `xcodebuild` 打包進 IPA;Unity Play 直接讀 `StreamingAssets/Chunks`(同一份 100 GLB)。兩者資料來源相同,確保 IPA 與 Unity 顯示相同的 l19hybrid 3D 模型。
- 未建置/未發布(依指示待命)。

## [v2.6.6] - 2026-11-27

### 變更(重新加入 6-SE-21B,東緣與 6-SE-22A 西緣無縫接合)

**A. 重新加入 6-SE-21B,三圖幅無縫接合(17C 北 + 22A 東南 + 21B 西南)**

- v2.6.5 移除了 6-SE-21B(其擺放始終無法正確呈現)。本版重新加入 21B,並修正其擺放:將 21B 的 `positionOffset[0]` 位移 **+1499.62 m**,使其在 Unity 中東緣與 6-SE-22A 西緣無縫接合。
- **問題根因**:CSDI 原始資料中 21B 與 22A 的 `positionOffset[0]` 差約 749.81 m(一個圖幅寬),但在 Unity 中 21B 原本出現在 22A 的**東側**(而非預期的西側),兩者背對背相連但方向錯誤。
- **位移計算修正歷程**:
  - **第一次嘗試(+746.566 m,以 manifest `bbox_min`/`bbox_max` 推導)**:假設 bbox 代表 Unity 中實際網格範圍,計算 21B 東緣與 22A 西緣的差距為 746.6 m,據此位移。**結果:21B 與 22A 完全重疊**——因為 bbox 並非 Unity 中的實際網格範圍(GLB 已 recenter,`positionOffset` 才是實際定位),位移一個圖幅寬後 21B 正好疊在 22A 上。
  - **使用者修正邏輯**:位移 +750(約一個圖幅寬)造成重疊,表示 21B 從「錯誤側(東側)」移到了「與 22A 重疊」;要移到「正確側(西側)無縫接合」,需再移一個圖幅寬,合計 **+750 × 2 = +1500**。
  - **精確值** = 2 × (22A.positionOffset[0] − 21B.positionOffset[0]) = 2 × (750.094 − 0.284) = **+1499.620 m**。
  - 位移後:21B positionOffset[0] = 1499.90,22A positionOffset[0] = 750.09,差距 = 749.81(一個圖幅寬)→ 21B 在 Unity 中位於 22A 西側,東緣緊貼 22A 西緣。
  - 南北對齊:21B 與 22A 的 positionOffset[1] 均約 −596.8 → 同一南北列,水平接合無高低錯位。
- 合併後共 60 分塊(17C: 20 分塊 149.3 MB / 1,676,835 三角形;22A: 20 分塊 62.2 MB / 716,549 三角形;21B: 20 分塊 85.0 MB / 856,047 三角形),合計 296.5 MB / 3,249,431 三角形。
- `manifest.json` 重建:`tiles=["6-SE-17C","6-SE-22A","6-SE-21B"]`、`chunkCount=60`、`source="6-SE-17C+22A+21B-seamless-l18-nofloat"`;21B 所有分塊 `positionOffset[0]` 已 +1499.620(原始 0.284 → 1499.904)。
- 建立無縫三圖幅分塊來源目錄 `…\csdi\17c-22a-21b_seamless_l18_nofloat`(60 `.glb` + `manifest.json`),供 `export_xcode_project.ps1 -ChunksSource` 注入 IPA。
- `export_xcode_project.ps1` 預設 `-ChunksSource` 改為 `17c-22a-21b_seamless_l18_nofloat`。
- Unity 專案 `Assets/StreamingAssets/Chunks/` 已回填 21B 的 20 個 `.glb`(17C+22A 的 40 個已存在),共 60 個。`.meta` 由 Unity 匯入時自動重新生成。
- 執行時影響:`spawnTileCode=6-SE-17C` 不變(玩家出生於 17C 北側,可向南巡航進入 22A,再向西進入 21B);`preloadAllChunks=true` 預載全部 60 分塊;`addChunkColliders=true`(真實網格為地面);`groundFillEnabled=false`(GroundFill 停用)。
- 小地圖(M 鍵)將顯示三個圖幅矩形:17C(北)、22A(東南)、21B(西南),其中 21B 東緣緊貼 22A 西緣。
- **已知**:17C 與 22A 之間仍有 594 m 南北空隙(自 v2.6.5 即存在,非本版引入);使用者未反映問題,暫不處理。
- 未建置/未發布(依指示待命)。

## [v2.6.5] - 2026-11-27

### 變更(3D 模型回復為 6-SE-17C + 6-SE-22A 兩圖幅 + 預設隱藏診斷覆蓋層 + L 鍵羅盤方位與北朝上小地圖 + M 鍵小地圖切換 + SonarCloud 徽章)

**A. 移除 6-SE-21B,回復為 6-SE-17C(北)+ 6-SE-22A(南)兩圖幅**

- 先前嘗試於 6-SE-22A 西側加入 6-SE-21B 構成 L 型三圖幅(60 分塊),但其擺放始終無法正確呈現,故本版移除 21B,回復為 17C + 22A 兩圖幅(40 分塊)。
- 合併後共 40 分塊(17C: 20 分塊 149.3 MB / 1,676,835 三角形;22A: 20 分塊 62.2 MB / 716,549 三角形),合計 211.5 MB / 2,393,384 三角形。
- `manifest.json` 確認為兩圖幅版本:`tiles=["6-SE-17C","6-SE-22A"]`、`chunkCount=40`、`source="6-SE-17C+22A-l18-nofloat"`。
- Unity 專案 `Assets/StreamingAssets/Chunks/` 已清空 21B 的 20 個 `.glb`,僅保留 17C+22A 共 40 個(來源目錄 `…\csdi\17c-22a_l18_nofloat`)。
- `export_xcode_project.ps1` 預設 `-ChunksSource` 回復為 `17c-22a_l18_nofloat`;註解同步更新為「6-SE-17C + 6-SE-22A 兩圖幅」。
- 執行時影響:`spawnTileCode=6-SE-17C` 不變(玩家出生於 17C,可向南巡航進入 22A);`preloadAllChunks=true` 預載全部 40 分塊;`addChunkColliders=true`(真實網格為地面);`groundFillEnabled=false`(GroundFill 停用)。
- 未建置/未發布(依指示待命)。

**B. 預設隱藏診斷覆蓋層(IPA 與 Unity 皆隱藏)— 修正「仍預設顯示」問題**

- 根因:`SetupScene.Build()` 僅由 `BuildIOS.Export()` 於匯出時呼叫,**不會在 Unity Editor Play 模式執行**;而 `DebugOverlay` 欄位預設值不會覆寫既有場景已序列化的值。因此 `Main.unity` 中殘留的序列化 `show=true` 在 Editor Play 時生效 → 覆蓋層仍預設顯示。
- 修正(四層保證):
  1. `DebugOverlay.cs` 新增 `Awake()`,執行時強制 `show = false`(涵蓋 IPA 與 Unity Play;Awake 僅實體化時執行一次,不影響後續齒輪按鈕/Q 鍵切換)。
  2. `Main.unity` 序列化值 `show: 1` → `show: 0`(Inspector 與 Editor Play 反映正確預設)。
  3. `DebugOverlay.cs` 欄位預設 `show = true` → `show = false`(新建元件實例預設隱藏)。
  4. `SetupScene.cs`(建置權威來源)掛載 `DebugOverlay` 後強制 `dbg.show = false`(匯出時保證)。
- 畫面右上角齒輪按鈕(⚙)仍永遠顯示,點擊可切換覆蓋層顯示/隱藏;桌面 Unity 亦可按 Q 鍵切換。使用者可隨時開啟診斷資訊,但預設畫面乾淨。

**C. L 鍵新增羅盤方位 + 北朝上小地圖;M 鍵切換持續小地圖**

- `TouchPlayerController.cs` 的 L 鍵(原僅印座標/yaw/pitch 至 Console)新增羅盤方位:由 `yaw` 計算 8 方位(北/東北/東/…/西北)+ 角度,座標亦標註「東+」「南+」方便判讀。
- `DebugOverlay.cs` 新增 L 鍵處理:計算攝影機 `transform.forward` 投影至水平面後的羅盤方位,並於畫面上方中央暫時顯示 5 秒(即使覆蓋層隱藏也可見)。顯示內容含:面向方位 + 角度、位置(X=東+, Z=南+)、以及「面南時:螢幕左=東, 右=西」等左右方向提示。
- `DebugOverlay.cs` 新增 **M 鍵切換持續小地圖**:按 M 鍵顯示/隱藏北朝上小地圖(即使覆蓋層隱藏也可見)。小地圖內容:各圖幅矩形(藍框 + 代碼)、圖幅間連接線、玩家位置(黃點)+ 面向箭頭、N/S/E/W 方向標。圖幅資料由 Bootstrapper 於 manifest 載入時注入一次(`SetMiniMapTiles`),玩家位置/面向每幀自行更新。
- `ChunkStreamBootstrapper.cs` 於 manifest 載入後計算各圖幅世界 bbox(中心 + 尺寸)注入 `DebugOverlay`;L 鍵另於 Console 印出各圖幅相對玩家的距離與方向。修正:圖幅世界中心 = `TransformPoint(positionOffset + 平均 centroid)`(舊版漏加 `positionOffset` → 所有圖幅映射至原點,小地圖失效)。
- 動機:玩家出生時 `startYaw=0` → 面向南方(+Z=南);面南時螢幕左=東、右=西,與北朝上地圖的「右=東」直覺相反。L 鍵的羅盤方位 + M 鍵的持續北朝上小地圖可即時確認面向方向與各圖幅實際地理位置及連接關係。

**D. README 新增 SonarCloud 品質閘門徽章**

- 於 `README.md` 最上方新增 `[![Quality gate](…)](…)` 徽章,連至 SonarCloud 專案摘要頁。

## [v2.6.4] - 2026-11-27

### 修正(建置腳本覆寫地面設定 → IPA 仍啟用 GroundFill)

**根因:`SetupScene.cs` 硬編 `groundFillEnabled=true`,每次匯出 Xcode 專案時覆寫磁碟 `.asset`**

- v2.6.3 將 `ChunkStreamConfig.asset` 的 `groundFillEnabled` 改為 `0`(停用 GroundFill),但 `SetupScene.Build()`(由 `BuildIOS.Export()` 於每次匯出呼叫)第 139 行硬編 `config.groundFillEnabled = true`,經 `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` 寫回磁碟 → 覆寫回 `1`。
- 結果:v2.6.3 IPA 仍啟用 GroundFill(可見橄欖綠偽地面 + 建物頂點污染 + 邊界空隙),與磁碟 `.asset` 設定不符。使用者報告「Unity 和 IPA 未遵循磁碟設定」。
- **此為 v2.6.3 IPA 地面問題與『17C 重複出現』視覺瑕疵的根因**:GroundFill 為每個分塊建立可見橄欖綠平面,17C 的 20 個分塊 = 20 個 GroundFill 平面 + 20 個真實網格,視覺上如同「17C 重複出現(扁平綠色鏡像)」。

**修正內容**

1. `SetupScene.cs` 第 139 行:`groundFillEnabled = true` → `false`。
2. `SetupScene.cs` 新增強制設定 `spawnTileCode="6-SE-17C"`、`preloadAllChunks=true`、`keepAllChunksAfterPreload=true`、`preloadDeferBudget=0.5`(建置權威來源,避免舊 `.asset` 殘留空值)。
3. `SetupScene.cs` 新增 `Debug.Log` 記錄強制設定的 config 值(匯出日誌可驗證)。
4. `ChunkStreamBootstrapper.Awake()` 新增啟動時 `Debug.Log` 記錄套用的 config 值(裝置日誌可驗證 IPA 是否套用正確設定)。

**5 圖幅對齊調查結論(資料已驗證正確)**

- manifest 100 分塊、每圖幅 20 分塊、無重複 ID、無重複 positionOffset。
- 17C 恰 20 分塊,全部 positionOffset=(750, 2.4, 8.5),無重複。
- 旋轉比對:22A/21A/21B 的網格資料與 17C 在任何 180° 旋轉下比對,吻合率 <0.1%(非旋轉複製)。
- 網格資料完全不同(17C: 234 萬頂點/274m 高程範圍;22A: 78 萬頂點/204m)。
- 結論:「17C 重複出現」為 GroundFill 可見平面所致(本版本修正),非資料重複或旋轉錯誤。

## [v2.6.3] - 2026-11-26

### 變更(真實網格碰撞器取代 GroundFill + 修正 SonarCloud S2083/S3776)

**A. 地面系統改用真實攝影測量網格碰撞器(停用 GroundFill)**

- v2.6.2 以前使用 `ChunkStreamBootstrapper.CreateChunkFill` 產生的 8×8 高度場網格(GroundFill)作為地面,引發三個已確認問題:
  1. **邊界空隙**:`(i+0.5)/res` 頂點置位使 GroundFill 內縮 ~9m/邊,相鄰分塊之間有 ~18m 空隙,玩家在分塊邊界與 spawn 點墜落。
  2. **建物頂點污染**:逐格取 min-Y 會把屋頂高度當地面(`GROUND_BAND=40m` 濾除高樓但保留 ≤40m 建物),產生錯誤高度的「偽地面」。
  3. **可見偽地面**:GroundFill 的 `MeshRenderer`(橄欖綠 Standard 材質)穿模顯示。
- **修正**:`ChunkStreamConfig` 改為 `addChunkColliders=1` + `groundFillEnabled=0`。地面跟隨射線直接命中真實攝影測量網格的 `MeshCollider`(道路、地形、建物底部);`CharacterController` 同時提供建物水平碰撞。
- GroundFill 程式碼保留(停用時為死碼)以利回退。

**B. GroundFill 頂點 extent 修正(保留供回退使用)**

- `CreateChunkFill` 頂點置位由 `(i+0.5)/res` 改為 `i/(res-1)`,涵蓋完整 0%..100% extent,相鄰 GroundFill 相接。新增 `GROUND_BAND=40m` 過濾建物頂點。雖然 v2.6.3 已停用 GroundFill,此修正保留以備未來重新啟用。

**C. 修正 SonarCloud S2083(BLOCKER 路徑穿越)**

- `convert_osgb_to_chunks._rewrite_mtl_to_basenames()` 的白名單正規表達式由 `^[A-Za-z0-9._-]+$` 改為 `^[A-Za-z0-9._+\-]+$`(接受 CSDI 材質檔名的 `+` 字元,如 `Tile_+155_+171_L18_0000_t_0.jpg`)。
- 舊正規表達式錯誤拒絕所有 CSDI 材質檔名 → 材質被丟棄 → GLB 無貼圖。
- 新增 `_sanitize_texture_basename()` 為獨立淨化函式(SonarCloud 污染分析以函式呼叫邊界為淨化點)。

**D. 修正 SonarCloud S3776(認知複雜度)**

- `discover_subtiles` 重構為四個輔助函式:`_resolve_osgb_root`、`_select_lod_files`、`_parse_tile_coords`、`_collect_subtiles`。主函式認知複雜度 23 → ~6。

## [v2.6.2] - 2026-11-26

### 變更(跳躍改右側 + 修正跨圖幅移動 + IPA 改名 HK_FPS + SonarCloud S2083)

**A. 行動跳躍:由左側雙擊改為右側雙擊**

- v2.6.0 的行動跳躍為左半屏(移動搖桿側)雙擊;v2.6.2 改為右半屏(視角搖桿側)雙擊。
- `TouchPlayerController.HandleTouchInput`:雙擊偵測由左側 `_moveFingerId` 的 `Ended` 移至右側 `_lookFingerId` 的 `Ended`;`_touchStartPos`/`_touchStartTime` 改於右側 `Began` 記錄。
- 左側恢復為純移動搖桿,右側同時處理視角拖曳與雙擊跳躍。

**B. 修正跨圖幅移動:玩家無法走出初始圖幅**

- **根因**:CSDI 攝影測量 OSGB 圖幅邊緣有垂直「裙牆」(curtain wall)——掃描區邊界的網格向下垂直延伸封閉。這些垂直面經 `addChunkColliders` 產生 `MeshCollider`,成為隱形牆壁,阻擋玩家從一個圖幅走向相鄰圖幅。
- **網格排列已驗證正確**:manifest 的 `positionOffset` 確認為 5 圖幅 3×2 矩陣(16D/17C 上列、21A/21B/22A 下列),排列無誤;「無法走出」純為碰撞器遮擋,非排列問題。
- **修正**:`ChunkStreamConfig.addChunkColliders` 設為 `0`(停用分塊 `MeshCollider`)。玩家改由逐塊地面填補(`GroundFill_*`)的 `MeshCollider` 提供碰撞與地面跟隨。地面填補為連續高度場,跨越所有圖幅邊界,玩家可自由行走於全部 5 個圖幅之間。
- **取捨**:停用分塊碰撞器後,玩家可穿越建物(無建物碰撞),但可自由跨圖幅移動。如需恢復建物碰撞,需於轉換器加入裙牆移除功能(需重新轉換全部圖幅)。

**C. IPA 檔名改為 `HK_FPS-X.X.X.ipa`**

- v2.6.0–v2.6.1 的 `PRODUCT_NAME` 為「香港 FPS 小遊戲」,xcodebuild 在 macOS runner 上將 CJK 自產品名稱剔除,產生 `FPS.app` / `FPS.-2.6.X.ipa`(檔名錯誤且混淆)。
- v2.6.2 將 `PRODUCT_NAME` 改為 ASCII `HK_FPS`(`ProjectSettings.asset`、`BuildIOS.cs`、`export_xcode_project.ps1`、`build_unity_ipa.sh`),xcodebuild 產生 `HK_FPS.app` → `HK_FPS-2.6.2.ipa`。
- 家畫面顯示名稱「香港 FPS 小遊戲」由 `export_xcode_project.ps1` 於匯出後寫入 `Info.plist` 的 `CFBundleDisplayName`(與 `PRODUCT_NAME` 解耦)。
- `build_unity_ipa.sh` 新增 `IOS_DISPLAY_NAME` 變數,manifest.json 的 `appName` 改用此變數(顯示名稱),IPA 檔名仍用 `IOS_PRODUCT_NAME`(ASCII)。
- Bundle ID `com.mccmak.hkgridvoxel` 不變,側載更新不受影響。

**D. 修復 SonarCloud S2083(BLOCKER 安全漏洞)**

- `pythonsecurity:S2083`(BLOCKER):`convert_osgb_to_chunks._rewrite_mtl_to_basenames()` 的路徑穿越防護由子字串檢查(`".." in basename`)升級為嚴格白名單正規表達式(`SAFE_BASENAME_RE = ^[A-Za-z0-9._-]+$`),並改用 `os.path.basename()` 正規化路徑。
- `shell:S7684`(×2, MAJOR):`il2cpp_pty_wrapper.sh` 的 `COMPlus_ReadyToRun` / `DOTNET_ReadyToRun`(.NET 執行階段官方環境變數,不可改名)加 `NOSONAR` 抑制。

### 已知問題

- `python:S3776` 認知複雜度(6 項)仍存在於 `convert_osgb_to_chunks.py` / `decimate_glb.py` / `validate_chunks.py` 的大型函式,需深度重構,暫緩處理。

## [v2.6.1] - 2026-11-26

### 修正(移除 6-SW-25B + 安全修復 + 停用 Pages)

**A. 修正網格排列:移除 6-SW-25B,改為 5 圖幅 3×2 矩陣**

- v2.6.0 誤將 6-SW-25B 納入(不屬於此 3D 場景)。
- v2.6.1 改為 5 圖幅,正確的 3 欄 × 2 列排列(左至右、上至下):
  - 上列:6-SE-16D、6-SE-17C
  - 下列:6-SE-21A、6-SE-21B、6-SE-22A
  - 6-SE-16C 位置(左上角)不需要,正確留空。
- 重新合併:**100 分塊,478.45 MB,5,255,793 三角面**(v2.6.0 為 120 分塊 / 560.94 MB)。
- 個別圖幅轉換成果沿用(快取),僅重新執行合併步驟。

**B. 修復 SonarCloud 安全問題(13 項)**

- `githubactions:S7630`(×5, BLOCKER):workflow `inputs.xcode_export_tag` / `inputs.release_tag` 改為透過環境變數傳入 run 區塊,防範腳本注入。
- `pythonsecurity:S2083`(×1, BLOCKER):`convert_osgb_to_chunks.py` MTL 改寫時拒絕含 `..` 或路徑分隔符的 basename,防範路徑穿越。
- `githubactions:S7637`(×1, HIGH):`maxim-lobanov/setup-xcode` 釘選至 commit SHA。
- `pythonsecurity:S8707`(×6, HIGH):`decimate_glb.py` / `merge_tiles.py` / `validate_chunks.py` 新增 `_safe_resolve()` 驗證 CLI 路徑引數,防範路徑穿越。

**C. 修復 SonarCloud 程式碼異味(17 項)**

- `python:S6729`(×2):`np.where(cond)` → `np.nonzero(cond)`。
- `python:S1192`(×1):`.osgb` 字面值抽取為 `OSGB_SUFFIX` 常數。
- `python:S8572`(×1):`log.error("…%s", e)` → `log.exception(…)`。
- `python:S7494`(×2):`set(...)` → 集合推導式。
- `python:S3457`(×1):無佔位符的 f-string 改為普通字串。
- `Web:S7930`(×1):`index.html` 重複的 `id="download"` → `id="download-btn"`。
- `javascript:S7761`(×1):`getAttribute('data-copy')` → `.dataset.copy`。
- `javascript:S7762`(×1):`parentNode.removeChild(child)` → `childNode.remove()`。
- `javascript:S2486`(×1):空 catch 加註解說明。
- `css:S9440`(×1):新增 `prefers-reduced-motion` 媒體查詢。
- `css:S4666`(×1):合併重複的 `.copyrow code` 選擇器。
- `shell:S1066`(×1):`generate_apps_json.sh` 合併巢狀 if。
- `shell:S7684`(×2):`il2cpp_pty_wrapper.sh` 環境變數加註解(.NET 執行階段官方名稱,不可改名)。
- `shell:S1192`(×1):`build_unity_ipa.sh` 分隔線字面值抽取為 `SEP` 變數。
- `python:S3776`(×1, merge_tiles):`main()` 拆出 `_parse_tile_specs()` / `_load_manifests()` 降低認知複雜度。

**D. 停用 GitHub Pages 部署**

- 儲存庫已由公開改為私有,GitHub Pages 需付費方案(GitHub Pro/Team/Enterprise)。
- `deploy:docs` job 設為 `if: false`(保留定義以便日後重新啟用)。
- 入口網站(`codebase/site/`)與 `apps.json` 饋流不再部署。IPA 直接由 GitHub Releases 下載。

### 已知問題

- `python:S3776` 認知複雜度(6 項)仍存在於 `convert_osgb_to_chunks.py` / `decimate_glb.py` / `validate_chunks.py` 的大型函式,需深度重構,暫緩處理以避免破壞運作中的轉換管線。

## [v2.6.0] - 2026-11-26

### 變更(改善模型品質 + 雙隱形搖桿 + 保留全部分塊 + 改名)

**A. 改善模型品質(重新轉換 6 圖幅 l18-nofloat)**

- v2.5.0 轉換參數過度簡化(256px 貼圖 + decimate-threshold 2000),導致建物形狀不真實。
- v2.6.0 重新轉換:`--max-texture-px 512`(提升一倍)+ `--decimate-threshold 5000`(減少簡化)+ `--max-chunk-mb 15`(放寬分塊上限)。
- 結果:**560.94 MB,6,180,046 三角面**(v2.5.0 為 418 MB / 5.84M 三角面,+34% 三角面)。
- 貼圖解析度 256px → 512px,建物外觀更真實。

**B. 雙隱形搖桿控制方案 — `TouchPlayerController.cs`**

- **左半屏隱形搖桿 → 移動**(觸控起點=搖桿原點,拖曳方向=移動方向,隱形不顯示)。
- **右半屏隱形搖桿 → pan/tilt 視角**(類比:推越遠轉越快,隱形不顯示)。
- **左半屏雙擊 → 跳躍**(兩次輕擊間隔 < 0.3s)。
- 取代 v2.5.0 方案(單指拖曳=移動 + 雙指拖曳=視角+縮放 + 單指雙擊=跳躍)。
- `showJoystickUI` 預設 `false`(隱形);桌面控制不變(WASD + 滑鼠右鍵 + 滾輪 + SPACE)。

**C. 保留全部分塊駐留(`keepAllChunksAfterPreload=true`)**

- v2.5.0 設為 `false`(串流卸載遠處分塊),但使用者無法走出原始圖幅(分塊卸載後邊界消失)。
- v2.6.0 改回 `true`:預載入完成後保留全部 120 分塊,可自由跨圖幅移動。
- 風險:120 分塊全部駐留記憶體(~560 MB GLB),OOM 風險較高;若發生需降低 `--max-texture-px` 或減少圖幅。

**D. App 改名為「香港 FPS 小遊戲」**

- `ProjectSettings.asset` `productName`:`HKGridVoxel` → `香港 FPS 小遊戲`(iOS 主畫面顯示名稱)。
- `app.config.json` `appName`:`HKGridVoxel` → `香港 FPS 小遊戲`(AltStore/SideStore 饋流顯示名稱)。
- `BuildIOS.cs` `DefaultProductName` 同步更新。
- `export_xcode_project.ps1` + `build_unity_ipa.sh` `IOS_PRODUCT_NAME` 預設值同步更新。
- IPA/dSYM 檔名移除空白(避免下載 URL 編碼問題):`香港FPS小遊戲-2.6.0.ipa`。
- Bundle ID 不變:`com.mccmak.hkgridvoxel`(避免破壞側載更新)。

### 已知問題

- 560 MB 分塊集 + `keepAllChunksAfterPreload=true` 全部駐留,iPhone OOM 風險高於 v2.5.0(串流卸載)。若預載入階段 OOM 需降低 `--max-texture-px` 或減少圖幅數。

## [v2.5.0] - 2026-11-26

### 新增(6 圖幅 6-SW/6-SE l18 + 齒輪按鈕 + 新觸控方案)

**A. 6 圖幅 l18-nofloat 分塊集(`6-SW-SE-merged-l18-nofloat`)**

- 6-SE-16D + 6-SE-17C + 6-SE-21A + 6-SE-21B + 6-SE-22A + 6-SW-25B 全部以 l18-nofloat 轉換並合併。
- **120 分塊,418.34 MB,5,835,872 三角面**(6 圖幅 × 20 分塊)。
- `--max-texture-px 256`(iPhone 螢幕足夠)+ `--no-floating-removal`(l18+ 地面無洞規則)。
- 起始位置:6-SE-17C 圖幅中心(新增 `spawnTileCode` 設定)。
- `keepAllChunksAfterPreload=false`:預載入完成後恢復半徑串流(遠處卸載),降低 120 分塊同時駐留的 OOM 風險。
- 最大分塊 18.82 MB(遠低於 v2.3.0 l19 的 43 MB);每分塊平均 ~3.5 MB。

**B. 齒輪按鈕切換診斷覆蓋層 — `DebugOverlay.cs`**

- 螢幕右上角齒輪按鈕(`⚙`),永遠顯示(即使覆蓋層隱藏)。
- 點擊切換診斷覆蓋層顯示/隱藏(iOS 無實體鍵盤,Q 鍵無法使用)。
- `GearButtonRect` 靜態欄位供 `TouchPlayerController` 排除該區域觸控(避免移動搖桿誤觸齒輪)。
- 半透明深色圓鈕,不遮擋視線。

**C. 新觸控方案 — `TouchPlayerController.cs`**

- **單指拖曳 → 移動**(浮動搖桿:觸控起點=搖桿原點,拖曳方向=移動方向,放開隱藏)。
- **雙指拖曳 → pan/tilt 視角**(兩指平均位移)+ **捏合縮放**(兩指距離變化)。同時進行。
- **單指雙擊 → 跳躍**(兩次輕擊間隔 < 0.3s,位移 < 20px 視為點擊)。
- 取代舊方案(v2.2.4–v2.2.18:左下角固定搖桿=視角、右半屏拖曳=視角、右側雙擊=跳躍、無移動)。
- 搖桿 UI 改為浮動(觸控時出現於觸控點,放開隱藏),`showJoystickUI` 預設 `true`。
- 移除 `joystickMarginX/Y`、`_lookFingerId`、`PositionJoystickAtBottomLeft`、`HandlePinch`、`UpdateLook`(舊方案欄位/方法)。
- 桌面控制不變:WASD + 滑鼠右鍵 + 滾輪 + SPACE。

### 已知問題

- 6 圖幅 l18 分塊集 418 MB,IPA 預估 ~350-400 MB(大於 v2.4.0 的 190 MB,但小於 v2.3.0 的 405 MB)。
- `keepAllChunksAfterPreload=false`:預載入完成後恢復半徑串流,遠處分塊卸載(可能有遠處彈出 pop-in);若 OOM 仍發生可進一步降低 `--max-texture-px`。
- 預載入 120 分塊仍可能在載入頁階段 OOM(雖然每塊僅 ~3.5 MB);若發生需降低 `--max-texture-px` 或減少圖幅數。

## [v2.4.0] - 2026-11-25

### 新增(4 圖幅 inter-tile 移動 + 載入頁)

**A. 4 圖幅合併分塊集(`11-NW-15-merged-l19-nofloat`)**

- 15A + 15B + 15C + 15D 全部以 l19-nofloat 轉換並合併為單一分塊集:**80 分塊,2231.33 MB,25,835,393 三角面**。
- 使用者可從 15B 步行至 15A(西)/ 15C(北)/ 15D(東北),實現 inter-tile 移動(v2.3.0 僅含 15B,無法跨圖幅)。
- 4 圖幅以 2×2 四象限排列,邊緣相接(15A=西南, 15B=東南, 15C=西北, 15D=東北),幾何已驗證正確。

**B. 載入頁(預載入模式)— `ChunkStreamBootstrapper.cs` + `ChunkStreamer.cs` + `ChunkStreamConfig.cs`**

- 新增 `preloadAllChunks` 設定(預設 `true`):場景啟動時顯示全螢幕載入頁,載入**所有分塊**完成後才顯示場景。
- 修正「玩家在分塊載入前墜落穿模」問題(分塊非同步載入期間玩家已開始施重力 → 落入地面下方)。
- `ChunkStreamer` 新增 `PreloadMode`:忽略半徑載入全部分塊、停用卸載;`IsPreloadComplete` 判定(含失敗分塊容錯)。
- `preloadDeferBudget`(預設 0.5):預載入期間提高 glTFast 每幀載入預算至 50%(載入頁可接受幀率下降),完成後恢復 `gltfFrameBudget`(0.15)。
- `keepAllChunksAfterPreload`(預設 `true`):預載入完成後保持所有分塊載入(停用半徑卸載,無遠處彈出);設為 `false` 則恢復半徑串流(記憶體扁平但遠處卸載)。
- 載入頁 UI:全螢幕深色背景 +「載入中」標題 +「X / 80 分塊 (NN%)」進度文字 + 進度條(OnGUI 實作,無需場景變更)。
- 逾時安全網:預載入超過 300 秒強制完成(避免單一分塊失敗導致永久卡在載入頁)。

**C. 載入頁期間停用玩家控制器**

- 預載入期間 `TouchPlayerController.enabled = false`(不施重力、不接受輸入),避免攝影機在分塊載入前墜落。
- 預載入完成後重新啟用並呼叫 `OnSpawned()`(地面已存在,射線命中)。

**D. 雜項**

- `DebugOverlay.cs`:`Q` 鍵切換覆蓋層顯示/隱藏(桌面 Unity Editor 用)。
- `ChunkStreamConfig.asset`:補入 `preloadAllChunks`、`keepAllChunksAfterPreload`、`preloadDeferBudget` 欄位。

### 已知問題

- 4 圖幅 l19-nofloat 分塊集 2231 MB,IPA 預估 ~1.7-1.8 GB(超出原 800 MB 目標;800 MB 目標原為 15B-only)。
- `keepAllChunksAfterPreload=true` 時 80 分塊同時駐留記憶體 ~2+ GB,iPhone 可能 OOM;若發生需降級部分圖幅至 l18。
- 11-NW-15B 東北角部分分塊缺失(intra-grid 問題,暫不修正)。

## [v2.3.0] - 2026-11-25

### 新增(l19 高解析度攝影測量 + 地面無洞修正)

**A. Stage A 高解析度分塊(`11-NW-15B_chunks_l19_nofloat`)**

- 1.8 GB OSGB → **508.19 MB GLB**(20 分塊,全部保留),三角面 **6,079,966**(v2.2.18 的 3.6×)。
- 採用 **l19 LOD**(最深可完整容納的層級),**無簡化**(decimate-threshold 上限)、**完整紋理**(max-texture-px 99999 不降解析)。
- **`--no-floating-removal`**:修正 l18+ 地面破洞根因——浮空地面移除在高密度 LOD 過度移除合法表面(平台、低屋頂、高架面)造成地面大洞;停用後地面連續無洞,對齊 v2.2.18 l18 參考集的平滑地面品質。
- 最大分塊 43.18 MB,遠低於 800 MB 預算。

**B. 文件(`AGENTS.md`)**

- 補登 **l18 地面品質參考集**(`11-NW-15B_chunks_unity_800mb`,204.64 MB / 1,686,450 三角面)為九龍城區「平滑地面、無洞」規範,含完整重現指令。
- 補登 **floating-removal 規則**:l18 及更深 LOD 一律 `--no-floating-removal`;以及 Stage B「雙平面」症狀根因(破洞區 `overallMinY` 回退導致填充層 Z 不對)。

**C. 雜項**

- `.gitignore` 補 `/6-SE-*.zip`、`/6-SW-*.zip`(新地區圖幅資料,不入庫)。
- `Main.unity` 補 v2.2.18 場景參數(`showJoystickUI: 0`、`mobileJumpDoubleTapTime: 0.3`)。

## [v2.2.18] - 2026-11-25

### 新增(iOS 搖桿隱藏 + iOS 跳躍 + 20MB 保留紋理分塊)

**A. 搖桿視覺隱藏(`TouchPlayerController.cs`)**

新增 `showJoystickUI` 欄位(預設 `false`)。行動裝置上搖桿底盤與拇指的 Image 顏色設為
完全透明(alpha 0),輸入仍正常運作(`Input.touches` 由程式碼處理,不依賴 GraphicRaycaster)。
iOS App 畫面不再顯示搖桿圓圈,但左側拖曳 pan/tilt 功能完整保留。

**B. iOS 跳躍(`TouchPlayerController.cs`)**

新增 `mobileJumpDoubleTapTime` 欄位(預設 `0.3` 秒)。行動裝置上**右側雙擊**(兩次點擊間隔
≤ `mobileJumpDoubleTapTime`)即排隊跳躍請求(`_mobileJumpQueued`),於下一幀 `Update` 消費。
跳躍邏輯(地面狀態 + 冷卻 + 單跳)與桌面 SPACE 鍵共用。雙擊偵測後立即重設計時器以防三擊
誤觸。`0` 停用行動跳躍。

**C. 20MB 保留紋理分塊(`decimate_glb.py` v5 `--textured` 模式)**

新增 `--textured` 模式,取代舊有平面色簡化。流程:

1. `trimesh` 載入 GLB(保留子網格 + UV + 紋理)。
2. 每個子網格:`fast_simplification` 簡化至目標面數。
3. `scipy.spatial.cKDTree` 最近鄰(逆距離加權 k=3)將原始 UV 重投影至簡化後頂點
   (`fast_simplification` 不保留 UV)。
4. 紋理降解析度至 `--texture-size`(預設 128px)。
5. 合併子網格 → 匯出 GLB。

**不需 pymeshlab** —— 先前嘗試以 pymeshlab 的 `meshing_decimation_quadric_edge_collapse_with_textures`
保留 UV,但:(a) 該 filter 需 per-vertex UV,而 CSDI 網格為 per-wedge UV(濾鏡不存在);
(b) pymeshlab 無法匯出 `.glb`(「Unknown format for save: glb」);(c) pymeshlab 亦無法
匯出含紋理的 `.obj`(image I/O plugin 缺失)。trimesh 純路徑完全規避這些限制。

轉換結果:`11-NW-15B_chunks_unity_100mb_v2214`(102.7 MB,1,581,960 面,20 分塊)→
`11-NW-15B_chunks_ipa_20mb_v2218`(22.5 MB,261,948 面,**20 分塊全保留**)。每塊含
UV + 128×128 紋理,遠優於 v2.2.14 的 2MB 平面色版本。

**符合硬性約束:** 1.8 GB OSGB 轉換保留所有分塊,僅進行簡化,不丟棄任何分塊。

**D. 移除死碼與過時檔案**

清除已不存在的 Swift/SceneKit 架構遺留:

- **刪除 `codebase/scripts/build_unsigned_ipa.sh`** —— 原 Swift App 的未簽署建置腳本。
  Unity 專案改用 `build_unity_ipa.sh`(`XCODEBUILD_ONLY=1` 模式)。`cicd.yml` 的 `test:sast`
  job(唯一呼叫者,已 `if: false`)整個移除。
- **刪除 `codebase/scripts/fetch_chunks.sh`**(0 位元組空檔)—— 分塊資料改由
  `export_xcode_project.ps1 -ChunksSource` 於本機匯出時注入,不再從 Release 下載。
  `build_unity_ipa.sh` 的階段 0(呼叫 fetch_chunks)移除。
- **刪除 `codebase/.swiftlint.yml`** —— Swift 專案已不存在,SwiftLint 設定無用。
  `dependabot.yml` 的 `swift` ecosystem 項目一併移除。
- **刪除 `codebase/unity-project/Assets/Scripts/FlyCameraController.cs`(+ .meta)** ——
  舊桌面飛行攝影機控制器,已被 `TouchPlayerController.cs` 完全取代。GUID 在任何場景/預製物/資產
  中均未被引用。
- **`cicd.yml`**:`test:sast` job(含 Swift 模擬器測試 + CodeQL Swift 分析,90+ 行)刪除,
  改為註解說明待 Stage C GameCI 重建。標頭註解更新為 v2.2.18 狀態。
- **`build_unity_ipa.sh`**:移除階段 0(fetch_chunks 呼叫)、`CHUNKS_RELEASE_TAG` 環境變數、
  模式顯示中的 "fetch" 字樣。
- **`export_xcode_project.ps1`**:錯誤訊息移除 `fetch_chunks.sh` 引用。
- **`codebase/converter/requirements.txt`**:新增 `fast_simplification`、`scipy`
  (`decimate_glb.py --textured` 的相依)。
- **`AGENTS.md`**:更新工作流程狀態(v2.2.18)、build & package 章節(改指向
  `build_unity_ipa.sh`)、App 章節(移除 FlyCameraController、更新腳本清單、更新狀態)、
  Testing 章節(標明無活躍測試 job)、Conventions 章節(移除 SwiftLint、build_unsigned_ipa)、
  權限說明、mermaid 圖。
- **`README.md`**:完整改寫,移除 SceneKit/Minecraft/HKMinecraftVoxel 引用,改為
  Tuanjie + Unity + glTFast 攝影測量飛覽架構。

## [v2.2.17] - 2026-11-24

### 修正(iOS 地面填補 ArgumentNullException——Standard 著色器被 stripping 移除)

**問題:** v2.2.16 IPA 安裝後,3D 分塊可渲染(glTFast 著色器修正成功),但
DebugOverlay 顯示:

```
[ChunkStreamer] 載入 x249_y141 例外: System.ArgumentNullException...
Parameter Name: shader at HKGrid.Streaming...
```

根因:`ChunkStreamBootstrapper.cs` 地面填補(GroundFill)材質使用
`new Material(Shader.Find("Standard"))`。`Standard` 著色器經動態查找,Unity 建置
管線視為無靜態引用 → engine code stripping 移除 → `Shader.Find("Standard")` 回傳
null → `new Material(null)` 擲回 `ArgumentNullException: shader`。v2.2.16 的
`EnsureGltfFastShadersIncluded()` 僅加入 glTFast 著色器,未涵蓋 `Standard`。

**修正:**

- **`BuildIOS.cs`:** `EnsureGltfFastShadersIncluded()` 著色器清單新增 `Standard`。
- **`ChunkStreamBootstrapper.cs`:** 防禦性著色器查找——若 `Standard` 仍被移除,
  回退至 `UI/Default` → `Hidden/InternalErrorShader`,避免 `ArgumentNullException`。
  並以 `HasProperty("_Cull")` 保護 `SetInt` 呼叫(回退著色器可能無此屬性)。

## [v2.2.16] - 2026-11-24

### 修正(iOS 藍畫面根因——glTFast 著色器被 stripping 移除)

**問題:** v2.2.15 IPA 安裝至 iPhone 後,DebugOverlay 顯示:

```
ShaderMissing:glTF/PbrMetallicRoughness
System.NullReferenceException
GLTFast.Materials.BuiltInMaterialGenerator.GenerateDefaultMaterial
```

根因:glTFast 著色器(`glTF/PbrMetallicRoughness` 等)經 `Shader.Find()` 動態查詢,
Unity 建置管線視為無靜態引用 → 由 engine code stripping 移除。`link.xml` 僅保留
managed 組件(C# 程式碼),**不涵蓋著色器資產**。著色器缺失 → `GenerateDefaultMaterial`
擲回 `NullReferenceException` → 分塊無法建立材質 → 不渲染 → 藍畫面。

**修正:**

- **`BuildIOS.cs` 新增 `EnsureGltfFastShadersIncluded()`**:建置前將 glTFast 著色器
  (`glTF/PbrMetallicRoughness`、`glTF/PbrSpecularGlossiness`、`glTF/Unlit`、`glTF/Points`、
  `glTF/Point`)加入 GraphicsSettings 的 Always Included Shaders 清單。以 reflection 呼叫
  內部方法 `UnityEditor.ShaderUtil.AddAlwaysIncludedShader`(公開 API 無此功能)。
- **`export_xcode_project.ps1` 修正 Tuanjie 等待問題**:Tuanjie.exe 為啟動器,`& $TuanjieEditor`
  立即返回但 Editor 仍在背景執行。新增等待迴圈:偵測 Editor 進程啟動後等待其結束
  (最多 15 分鐘),再驗證產物。修正 v2.2.15 匯出時誤報「匯出失敗」(實際建置成功但腳本
  過早檢查)。

## [v2.2.15] - 2026-11-24

### 修正(iOS 藍畫面診斷 + IL2CPP stripping 防護 + IPA 分塊注入 + Pages 部署間歇失敗)

**問題 A(IPA 藍畫面):** IPA 安裝至 iPhone 後可開啟(Tuanjie 動畫正常)、搖桿可用,
但畫面全藍(僅天空)——3D 分塊未載入。歷次嘗試(~200MB IPA 含 98MB 分塊→卡頓崩潰;
~60MB IPA→藍畫面)皆失敗。根因:從未設定 `IPA_CHUNKS_SOURCE_DIR`,且 iOS 上 glTFast
可能因 IL2CPP stripping 靜默失敗,無主控台可見錯誤。

**問題 B(Pages 部署間歇失敗):** `deploy:docs` 的 `cp -r codebase/site/. _site/`
間歇性只複製 `apps.json`(漏掉 `index.html` + `style.css`)→ upload-pages-artifact
僅含 1 檔 → deploy-pages 找不到 `index.html` → "Deployment failed / No artifacts"。
v2.2.14 及 v2.2.15 皆觸發此問題(v2.2.13 成功為僥倖)。

**A. 螢幕診斷覆蓋層(`DebugOverlay.cs`)**

- 新增 `DebugOverlay` MonoBehaviour:捕捉 `Application.logMessageReceived`,
  在螢幕左上角顯示最近 N 筆 Debug.Log/Warning/Error + manifest 狀態 + 分塊計數
  + 攝影機位置。iOS 無 Xcode 主控台時,這是唯一的執行時診斷管道。
- `SetupScene.cs` 自動掛載至攝影機;`ChunkStreamBootstrapper` 報告 manifest
  載入狀態;`ChunkStreamer` 詳細記錄每塊載入路徑與結果。
- 錯誤訊息紅色、警告黃色、一般白色;半透明黑底不遮擋視線。

**B. IL2CPP stripping 防護(`Assets/link.xml`)**

- 新增 `link.xml`:`preserve="all"` 保留 glTFast 組件(GLTFast、GLTFast.Editor)、
  System.Reflection、HKGrid.* 專案腳本、UnityWebRequest、JSONSerialize 模組。
  即使 `iPhoneStrippingLevel=0`,engine code stripping(`stripEngineCode=1`)
  仍可能移除 glTFast reflection 程式碼 → `GltfImport.Load()` 靜默失敗 → 藍畫面。

**C. 詳細載入診斷(`ChunkStreamBootstrapper.cs` + `ChunkStreamer.cs`)**

- Bootstrapper 記錄 `streamingAssetsPath`、manifest 完整路徑/URL、`File.Exists`
  結果、Chunks 目錄檔案數(診斷空目錄/路徑錯誤)。
- ChunkStreamer 記錄每塊的完整 `file://` 路徑、`gltf.Load()` 結果、
  `InstantiateMainSceneAsync()` 結果(成功時印 `✅ 分塊就緒`)。

**D. IPA 匯出腳本修正(`codebase/scripts/export_xcode_project.ps1`)**

- 新增 `-ChunksSource` 參數(預設:v2.2.14 2MB IPA 分塊),自動設定
  `IPA_CHUNKS_SOURCE_DIR` 環境變數。BuildIOS.cs 於匯出前複製指定分塊至
  StreamingAssets/Chunks,覆寫本地高解析度 Unity 版(98MB)。
- 舊版未設定此環境變數 → IPA 含錯誤分塊(98MB → OOM,或無分塊 → 藍畫面)。
- 預檢邏輯調整:設定 `-ChunksSource` 時跳過 StreamingAssets/Chunks 檢查
  (BuildIOS.cs 會注入);未設定時警告可能含高解析度分塊。

**E. 2MB IPA 分塊重新轉換(v2.2.14 preserveboundary + 焊接)**

- 從 v2.2.14 98MB 分塊(preserveboundary + 方向性焊接)重新 decimate 為 2MB
  IPA 版,確保 IPA 也受益於跨分塊撕裂修正(雖然 aggressive decimation 不保留
  邊界,但起始網格已對齊)。

**F. Pages 部署間歇失敗修正(`.github/workflows/cicd.yml`)**

- `deploy:docs` 的「暫存網站」步驟改用 `cp -rv`(verbose)+ 前後 `ls -la` 驗證
  + 必要檔案存在檢查(`index.html`/`style.css`/`apps.json` 缺失即 `exit 1`)。
  原因:`cp -r codebase/site/. _site/` 間歇性只複製 `apps.json` → upload-pages-artifact
  僅含 1 檔 → deploy-pages 找無 `index.html` → "No artifacts named github-pages"。
- `deploy:docs` 的 `if` 條件新增 `needs.build.result == 'failure'`:workflow_dispatch
  時若 build:ipa 失敗,deploy 仍執行(維護網站不中斷)。
- 確認 `build:ipa` 未停用(非 `if: false`):可經 `workflow_dispatch` 手動觸發。
  僅 `test:sast` 仍停用(`if: false`,待 Stage C 重建)。
- **`deploy` job 新增 `actions: read` 權限 + artifact 查詢重試。** 根因:deploy job
  設有顯式 `permissions:` 區塊,未列出的權限一律拒絕。`deploy-pages` 須經 Actions API
  查詢 artifact 中繼資料,無 `actions: read` → 「Found 0 artifact(s)」(儘管 upload 成功)。
  錯誤訊息提及 githubstatus.com 為誤導。v2.2.14/v2.2.15 多次觸發;v2.2.13 成功為 GitHub
  尚未收緊權限檢查前之僥倖。
  即使補回權限,upload→deploy 間仍有 ~300ms API 傳播競態(upload 成功但 API 尚未可查)。
  新增「等待 artifact 可查詢」步驟:每 2 秒輪詢一次(最多 30 秒),確認 artifact 可查後
  才執行 deploy-pages。同時升級 `deploy-pages@v4` → `@v5`(Dependabot 建議)。

## [v2.2.14] - 2026-11-24

### 修正(跨分塊表面撕裂 + 地面填補困住玩家)

**A. 轉換器(`convert_osgb_to_chunks.py`):消除真實網格跨分塊撕裂**

- **`preserveboundary=True`(簡化保留邊界頂點)。** pymeshlab 的
  `meshing_decimation_quadric_edge_collapse_with_texture` 新增此旗標(首輪 + 第二輪)。
  禁止簡化碰觸分塊邊界頂點/邊 → 相鄰分塊邊界保持來源位置,消除簡化造成的
  跨分塊縫隙與錯位(原兩側各自簡化 → 邊界頂點位移不一致 → 撕裂/寬縫)。

- **跨分塊邊界頂點焊接(方向性)。** 預掃描每個子圖幅的邊界頂點 + bbox(OSGB 座標);
  每分塊建置時,依相鄰方向(東/西/南/北)僅取**共享面**的邊界頂點,以 KDTree 配對
  (`weld_tolerance=0.5m`),snap 至兩者平均 → 相鄰分塊共享面頂點位置一致,消除裂紋。
  `preserveboundary` 保證邊界頂點簡化前後不變,故預掃描值 = 最終值,焊接可靠。
  方向性配對避免非共享面(如頂/底面)污染配對(診斷顯示全 6 面配對僅 0-2% 命中,
  方向性配對可達 60%+ 命中率)。

- **永不丟棄分塊(僅以簡化控制大小)。** 移除 `total_bytes > max_total → break` 中止邏輯;
  改為自適應每分塊預算 `remaining_budget / remaining_chunks × 0.92`,確保所有分塊都能
  產出。早期分塊偏大時,後期分塊目標自動縮小(更激進簡化)。1.8GB 來源 → 全部分塊保留。

**B. Unity(`ChunkStreamBootstrapper.cs` / `TouchPlayerController.cs` / `ChunkStreamConfig.cs`):地面填補不再困住玩家**

- **`groundFillBelowOffset` 0.5m → 0.05m。** 舊值使填補面低於真實地面 0.5m,玩家跨入
  即跌落 0.5m;新值幾乎與真實地面齊平(僅避開 z-fighting),跨越真實網格↔填補無跌落差。

- **`stepOffset` 0.4 → 0.6。** CharacterController 可踏回 0.6m 以下落差,即使殘留小階梯
  也能爬回(舊 0.4 < 填補 0.5m 落差 → 困住)。

- **坑洞離群值鉗制(`CreateChunkFill`)。** 取樣格 min-Y 若低於鄰居平均超過 8m(攝影測量
  深坑/天井/雜訊),提升至鄰居平均 → 填補面成為連續可走安全網,不跟隨深坑下陷困住玩家。

### 設計決定(未實作:重疊裙邊 overlap skirt)

- 評估後**不採用**跨分塊重疊裙邊:攝影測量網格的重疊面會 z-fighting(閃爍)。
  `preserveboundary` + 焊接已能同時消除細裂紋(焊接)與寬縫(preserveboundary 防止
  簡化開縫),且無 z-fighting 副作用。若測試後仍有殘留間隙,再評估其他方案。

## [v2.2.13] - 2026-11-24

### 新增(跳躍功能:SPACE 鍵,0.5s 冷卻,單跳)

- **桌面 SPACE 鍵跳躍。** 第一人稱 FPS 標準跳躍:
  - `jumpSpeed=5` m/s(≈1.3m 高度,g=9.81)
  - `jumpCooldown=0.5` 秒(著地後需等待才能再跳)
  - 僅地面狀態(`_isGrounded=true`)可跳;跳躍中不可二段跳
  - 行動裝置暫不支援(未來可加雙擊搖桿或獨立按鈕)

- **ApplyMovement() 重構:跳躍狀態分離。**
  - 新增 `_isJumping`/`_isGrounded`/`_lastJumpTime` 狀態
  - 跳躍中(`_isJumping=true`):彈道運動(重力加速度),**不吸附地面**
    (舊版每幀地面吸附會取消跳躍)
  - 著地偵測:向下射線命中 + 正在下降(`verticalVel ≤ 0`)+ 本幀位移穿過地面
    → 鉗制至地面高度,結束跳躍
  - 非跳躍時:保留原地面跟隨邏輯(吸附 + 重力下墜)

- **新欄位(Inspector 可調):** `jumpSpeed`(5)、`jumpCooldown`(0.5)。
  `SetupScene.cs` 無需改動(欄位預設值在 Awake/Inspector 套用)。

## [v2.2.12] - 2026-11-24

### 重寫(地面填補:逐塊取樣真實網格頂點,取代 manifest 統計插值)

- **v2.2.11 雙線性插值 + 鉗制 bbox_min 仍有「地面在地面上方」+「困住玩家」問題。**
  根因:bbox_min[2] 是整個 150×150m 分塊的最低點,但分塊內地形高低差達
  50-140m(平均 68m)。填補面在 bbox_min - 5m = 分塊最低點,山頂上的洞孔
  墜落 100-146m 到填補面 → 深坑,玩家被地形牆困住 = 「困住玩家」;
  抬頭看到地形在上方 = 「地面在地面上方」。

- **根本問題:manifest 只有分塊級統計(min/max/centroid),無法得知分塊內
  局部地形高度。** 任何基於 manifest 的插值(IDW/雙線形/鉗制)都無法解決——
  分塊內地形變化太大。必須取樣**真實載入的網格頂點**。

- **v2.2.12 改為逐塊取樣真實網格頂點。**
  - `ChunkStreamer` 新增 `OnChunkLoaded`/`OnChunkUnloaded` 事件
  - 每分塊載入完成時,`ChunkStreamBootstrapper.OnChunkLoaded()` 收集該分塊
    所有 mesh 頂點(世界座標),按 (X,Z) 分格(8×8),每格取**最低 Y**
    (=地面高度,非屋頂)
  - 空格(攝影測量洞孔)= 無頂點 → 由鄰居插值填補(3 次傳播)
  - 填補面 = 地面Y - `groundFillBelowOffset`(0.5m)
    - 0.5m < stepOffset(0.4)... 微調:0.5m 足以避免 z-fighting,
      玩家跌落不超過 0.5m,可踏回(地形邊緣坡度許可時)
  - 「有數據的位置不另創高度」= 填補面緊貼真實地面下方 0.5m,不過高(埋沒建物)
    不過低(深坑困住)
  - 分塊卸載時同步銷毀對應填補網格(`OnChunkUnloaded`)

- **`ChunkStreamConfig` 欄位變更:**
  - 移除:`groundFillYOffset`(5m)、`groundFillMargin`(200m)、`groundFillGridResolution`(48)
  - 新增:`groundFillBelowOffset`(0.5m)、`groundFillSampleResolution`(8)
  - 保留:`groundFillEnabled`、`groundFillColor`

- **移除方法:** `CreateGroundFill`(v2.2.9-11 全域插值)、`BilinearSample`、
  `SampleGrid`、`FindChunkFloor`。新增:`OnChunkLoaded`、`OnChunkUnloaded`、
  `CreateChunkFill`。

## [v2.2.11] - 2026-11-24

### 修正(地面填補「變天空」——IDW 插值高於地形地板)

- **v2.2.10 地面填補在部分位置「地面變天空」。** 根因:IDW 在深淺分塊間插值
  可產生高於當地地形地板的值。例如深分塊 bbox_min=-75m、淺分塊 bbox_min=-42m,
  IDW 在中間產生 ~-55m,但該位置實際地形地板可能是 -65m(深分塊領域)→
  填補面在 -55m,高於地形地板 -65m 10m → 玩家在 -65m 處抬頭看到綠色填補面
  覆蓋天空(雙面材質使背面亦可見)=「地面變天空」。

- **改用雙線性插值(bilinear)+ 逐頂點鉗制。**
  - **表面擬合**:分塊在 grid_x/grid_y 規則排列(5×4),改用雙線性插值
    (bilinear)取代 IDW。雙線性通過控制點、不超出四角範圍、無 IDW 的牛眼紋。
  - **逐頂點鉗制**(「有數據不另創高度」):每個網格頂點高度 =
    `min(雙線性插值, 所在分塊 bbox_min[2]) - offset`。
    - 有分塊包含時:填補永不高於當地地形地板 → 永不在玩家上方
    - 邊界外(無分塊):僅用雙線性插值(不加鉗制)
  - 控制點仍為 bbox_min[2](地形地板,非 centroid),offset 5m 不變。

- **新增方法**: `BilinearSample()`(grid 雙線性插值)、
  `FindChunkFloor()`(bbox 包含檢查 + 鉗制)、`SampleGrid()`(安全取值)。
  移除 `InterpolateHeight()`(IDW,已淘汰)。

## [v2.2.10] - 2026-11-24

### 修正(地面填補高度錯誤 + 改用插值高度場)

- **地面填補平面高度錯誤 + 假設全區海拔不變(v2.2.9 回歸)。** 兩個問題:
  1. **高度計算錯誤**:v2.2.9 用 `centroid[2]`(分塊平均高度)取最小值作為
     平面 Y,但 `centroid_z` 是平均值(約 -45m),遠高於實際最低地形
     `bbox_min_z`(約 -75m)。平面落在 -50m,比真實地面高出 25m →
     埋沒大量建物與地面。
  2. **單一平坦平面**:假設全區海拔不變,以單一 Y 值覆蓋整個區域。但本區
     分塊間地形高低差達 30m+,平坦平面在低窪區過高(埋沒)、在高地區過低
     (洞孔可見時落差過大)。

- **改用 IDW 插值高度場網格。** `CreateGroundFill()` 重寫:
  - 以各分塊 `bbox_min[2]`(最低地形 Z)為控制點(非 centroid)
  - IDW(power=2)反距離加權插值,產生跟隨地形起伏的高度場
  - `groundFillGridResolution`(預設 48)=49×49 頂點高度場網格
  - 每頂點高度 = 插值地形 − `groundFillYOffset`(5m 安全邊距)
  - MeshCollider 共用網格 → 射線命中跟隨地形(非單一平面)
  - 雙面材質不變(`_Cull=0`)

- **`ChunkStreamConfig` 新增欄位**: `groundFillGridResolution`(int, 預設 48)。

## [v2.2.9] - 2026-11-24

### 新增(地面洞孔修補 + IPA 極低解析度分塊)

- **人工地面填補(priority 2)。** 攝影測量網格有固有洞孔(無資料區域)。
  在最低地形下方建立大片地面色平面,修補視覺洞孔 + 防止玩家穿洞墜落:
  - `ChunkStreamConfig` 新增 `groundFillEnabled/Color/YOffset/Margin`
  - `ChunkStreamBootstrapper.CreateGroundFill()`:從 manifest 計算最低地形 Y,
    建立 Quad 平面(世界空間,非 worldRoot 下),雙面材質 + MeshCollider
  - `groundMask=~0` 射線自動命中平面 → 玩家不穿洞墜落
  - 洞孔可見時看到地面色(非天空)

- **IPA 極低解析度分塊(2MB)(priority 1b)。** 新增 `decimate_glb.py`
  腳本:從現有 L18 raw GLB(108.9MB)重新簡化至 **1.29 MB**、33,437 面,不需 osgconv。
  - v4 流程:合併分塊內所有 mesh → `fast_simplification`(target_count=1500)
    → 紋理平均色轉平坦 PBR color(不含紋理)→ GLB
  - 透過 `IPA_CHUNKS_SOURCE_DIR` 注入 IPA 匯出

- **osgconv 安裝 + 800MB Unity 版本轉換完成(priority 1a)。**
  - 透過 Miniforge3(winget)+ `conda install -c conda-forge openscenegraph` 安裝 osgconv
    (OSG 3.6.5 + `osgPlugins-3.6.5`),路徑 `miniforge3\Library\bin\osgconv.exe`。
  - 以 `--lod l18 --no-floating-removal --max-chunk-mb 40 --max-total-mb 800` 重新轉換
    1.8GB OSGB,產出 20 分塊、**204.6 MB**、1,686,450 面。
  - 轉換器不會向上取樣;L18 為 OSGB 來源最高 LOD,204.6MB 為此資料可達最高細節
    (未達 800MB 預算,但已是最高解析度)。`--no-floating-removal` 保留所有表面,
    減少洞孔。
  - `swap_chunks.ps1` 的 `$LocalSrc` 改指向 `11-NW-15B_chunks_unity_800mb`。

## [v2.2.8] - 2026-11-24

### 還原(放棄手動地下座標,恢復 manifest 中心自動定位)

- **放棄手動起始座標(-296.90, -65.44, 10.37)。** 該座標 Y=-65.44 經數據分析確認
  在地表下方 20-37m(鄰近分塊 centroid Y≈-28~-45),spawn 永遠在地下。
  恢復原始 manifest 中心自動定位:
  - `useManualStartWorldPos = false`(Awake + SetupScene 強制)
  - `_useManualSpawnY = false`
  - `startPitch` -3.2→55、`startYaw` -634.4→0、`defaultFov` 49.5→60(恢復原始)
  - `moveSpeed` 保留 8.75(1.75x,使用者明確要求)
  - `eyeHeight` 維持 1.7m

- **manifest 中心已驗證落地。** manifest 中心 world (0.1, -27.6, 2.0),
  spawn Y+15=-12.6,鄰近地面 Y≈-37.7,spawn 在地面上方 25.1m →
  向下射線可找到地面 → 落地正常。
  - 對應 WGS84 約 [22.3319, 114.1871](九龍城區,15B 圖幅中心)

- **保留雙向地面搜尋(安全網)。** OnSpawned 手動路徑與 ApplyMovement 的
  雙向射線(向下+向上)程式碼保留,但 `_useManualSpawnY=false` 時不啟用。
  若未來需手動座標可重新啟用。

## [v2.2.7] - 2026-11-23

### 修正(起始位置未生效 + eyeHeight/spawn 釐清 + 速度強制套用)

- **起始位置/視角未生效(v2.2.6 回歸)。** 根因:編輯 script field 預設值不會更新
  已序列化的 `Main.unity`;舊場景值(yaw=0、pitch=55、FOV=60、useManualStartWorldPos=false)
  在 Play 時覆寫程式預設 → 攝影機落在 manifest 中心(原點附近)而非指定座標。
  修正:於 `TouchPlayerController.Awake()` 強制套用 v2.2.6 值(與 SetupScene.Build 一致),
  確保 Editor Play 即時生效,無需重跑 SetupScene:
  - startYaw=-634.4、startPitch=-3.2、defaultFov=49.5、eyeHeight=1.7、moveSpeed=8.75
  - Bootstrapper:useManualStartWorldPos=true、manualStartWorldPos=(-296.90, 0, 10.37)

- **eyeHeight 維持 1.7m(非 5m)。** v2.2.6 誤將 eyeHeight 改為 5m。釐清:
  5m 是「spawn 上方高度」(玩家從 5m 高處落下),非眼睛高度。eyeHeight 恢復 1.7m。
  - `OnSpawned` spawn 偏移 15m→5m(從地面上方 5m 開始,射線找地面後落至 ground+1.7)

- **移動速度 1.75x 強制套用。** moveSpeed=8.75(1.75 × 原 5 m/s)。v2.2.6 已設此值
  但因場景序列化未生效;v2.2.7 於 Awake 強制套用,確保實際速度為 8.75 m/s。

## [v2.2.6] - 2026-11-23

### 變更(起始位置 + 移動速度 + 雙面渲染強化)

- **指定起始位置與視角。** 攝影機起始於 X=-296.90、Z=10.37(Unity 世界座標),
  離地 5m(Y 由地面射線決定 = groundY + eyeHeight),yaw=-634.4、pitch=-3.2、FOV=49.5。
  - `ChunkStreamBootstrapper` 新增 `useManualStartWorldPos` + `manualStartWorldPos`:
    啟用後以指定 X/Z 覆寫 manifest 推導的起始位置(向後相容,停用則用 manifest 中心)。
  - `TouchPlayerController.eyeHeight` 1.7→5(離地 5m 觀察)、`defaultFov` 60→49.5、
    `startYaw` 0→-634.4、`startPitch` 55→-3.2。

- **移動速度 1.75×。** `moveSpeed` 5→8.75 m/s(1.75 倍)。影響桌面 WASD 測試移動;
  行動裝置仍為 pan/tilt(v2.2.4),無移動輸入。

- **雙面渲染強化(修正「地面突然消失」)。** 攝影測量網格法線纏繞可能不一致,
  從某些角度地面面被當成背面剔除 → 「連續地面突然無表面」。
  - `ChunkStreamer.ForceDoubleSidedRecursive` 兼容兩種管線:
    Built-in RP(`_Cull`)+ URP/HDRP(`_CullMode`),皆設 0=Off(雙面)。
  - 本專案為 Built-in RP,`_Cull` 生效;額外檢查 `_CullMode` 以防未來切換 URP。
  - 測試座標:X=-190.62, Y=-62.17, Z=0.66, yaw=-735.2, pitch=14.8(原地面消失點)。

## [v2.2.5] - 2026-11-23

### 新增(雙面渲染 + 本地高解析度 + 座標除錯)

- **強制雙面渲染(Cull Mode: Off)。** glTFast 依 glTF 材質的 `doubleSided`
  屬性決定 `_Cull`(預設 2=Back 單面),攝影機從背面/內側看會看穿牆壁與地面。
  新增 `ChunkStreamConfig.forceDoubleSided`(預設 true):分塊載入後覆寫所有
  材質 `_Cull=0`(雙面),修正看穿問題。輕微增加 GPU 負載。

- **本地高解析度 / IPA 低解析度分離。** Editor Play 模式使用高解析度分塊
  (15B L17,37.3 MB,430K 面),IPA 使用低解析度(15B L16 nofloat,18.7 MB)。
  - `BuildIOS.Export` 新增 `IPA_CHUNKS_SOURCE_DIR` 環境變數:匯出前將指定目錄的
    分塊複製到 `StreamingAssets/Chunks`,確保 IPA 不受本地高解析度影響。
  - 未設定時沿用現有內容(向後相容)。
  - 新增 `codebase/scripts/swap_chunks.ps1` 切換腳本(local / ipa / status)。

- **按 L 鍵印出攝影機座標(Unity Editor 除錯用)。** `TouchPlayerController.Update`
  新增 `Input.GetKeyDown(KeyCode.L)` → Debug.Log 印出 (X,Y,Z)、yaw、pitch、FOV。

### 變更
- `ChunkStreamConfig.cs`:新增 `forceDoubleSided` 欄位
- `ChunkStreamer.cs`:載入後 `ForceDoubleSidedRecursive()` 覆寫材質 `_Cull=0`
- `TouchPlayerController.cs`:新增 L 鍵座標除錯
- `SetupScene.cs`:強制 `forceDoubleSided=true`
- `BuildIOS.cs`:新增 `CopyIpaChunksIfNeeded()`(IPA_CHUNKS_SOURCE_DIR)

## [v2.2.4] - 2026-11-20

### 修正(IPhone 藍屏 + 搖桿改 pan/tilt)

- **IPA 部署後藍屏(無 3D 模型可見)。** 根因:攝影機 spawn 在 Y+50 高處,
  俯角僅 25°(FOV 60° → 上半部全看天空),且首次找不到地面時懸停(deltaY=0)
  永遠不下降 → 分塊載入前/載入中攝影機持續看天空 = 藍屏。修正:
  - **startPitch 25→55°**(看地面為主,非天空)
  - **OnSpawned spawn 高度 Y+50→Y+15**(更接近地面,分塊載入即見)
  - **首次找不到地面時緩慢下降 5m/s**(非懸停),直到射線打到地面

- **左搖桿改為 pan/tilt(非移動)。** 原搖桿控制移動(前後左右行走),
  v2.2.4 改為控制視角(yaw/pitch 旋轉速度)。搖桿 X=yaw、Y=pitch,
  靈敏度 90°/秒。行動裝置不再有移動輸入(桌面 WASD 仍可用於測試)。

### 變更(控制)
- **TouchPlayerController.Update()**:新增搖桿→yaw/pitch 套用(_joystickVector
  → _yaw/_pitch 旋轉速度,非移動向量)
- **TouchPlayerController.ApplyMovement()**:行動裝置 input=Vector2.zero(無移動)
- 新增 `joystickLookSpeed=90` 欄位(搖桿視角靈敏度)
- 右側拖曳 pan/tilt 保持不變(與左搖桿並存,兩者皆可環顧)

## [v2.2.3] - 2026-11-20

### 修正(iPhone 17e 卡死 + Play mode 穿模墜落 + 地面洞孔)

- **iPhone 17e 部署後卡死強制關閉。** 根因:IPA 包含 4 圖幅 L18 分塊(522 MB、6.19M 三角面),
  載入/渲染負載過重。修正:
  - **IPA 僅部署 11-NW-15B 單一圖幅**(其餘 15A/15C/15D 移除)。本機測試仍保留 L18 4 圖幅。
  - **分塊解析度降為 L16 + 浮空地面移除停用**(`--no-floating-removal`),單圖幅 20 塊共 18.74 MB
    (原 L18 4 圖幅 522 MB)。停用浮空移除同時修正地面/森林洞孔問題(原演算法過度刪除合法表面)。
  - **可視距離限制 400m**:loadRadius 1000→400、unloadRadius 1350→550、cameraFarClip 2000→450。
  - **加入線性霧(350-450m)** 隱藏遠處分塊彈出。
  - glTFast 每幀載入預算 20%→15%,降低載入卡頓。

- **Play mode 左鍵點擊後墜落穿模→從天空重生。** 根因:地面跟隨射線長度 50m 太短,
  從 spawn 高處(Y+50)打不到 15B 地面(Y≈-45)→ foundGround=false → _groundEverFound=true 時
  施重力 → 持續下墜至 fallRespawnY=-150 → 重生於 startPosition+30(仍打不到地面)→ 循環墜落。
  修正:
  - **groundRaycastLength 50→250m**(地面跟隨)、OnSpawned 初始搜尋 500m(確保從任何高度打到地面)。
  - **maxYSnapPerFrame 8→80 m/s**(原 8 m/s × dt = 0.13m/幀,從 30m 落地需 4 秒 =「從天而降」)。
  - **智慧重生**:追蹤 `_lastGroundY`,穿模時重生至 lastGroundY+eyeHeight+5(非固定 startPosition+30)。
  - **重力限速** -20→-30 m/s(下墜更快找到新分塊的地面)。

- **單圖幅 IPA 攝影機落在圖幅外(無分塊載入 = 藍屏)。** 根因:startPosition=(947,0,-124) 硬編於
  多圖幅 manifest 座標(原點=15A centroid);單圖幅 15B-only manifest 原點=15B centroid,
  攝影機落在圖幅外 947m → 無分塊載入。修正:
  - **ChunkStreamBootstrapper 從 manifest 自動推導起始位置**:chunkCount≤25 時取所有 centroid
    平均(= manifest 中心);>25 時用 15B 中心。透過 `TouchPlayerController.OnSpawned()` 設定。
  - **TouchPlayerController.Awake 不再硬編 startPosition**,改由 Bootstrapper 載入 manifest 後設定。

- **地面/森林洞孔(discontinuous surface)。** 根因:浮空地面移除(步驟 5b)以 KDTree 50m 半徑
  搜尋,3-20m 浮空層判定過度刪除橋梁/平台/低屋頂等合法表面。修正:15B L16 重跑轉換器加
  `--no-floating-removal`,保留所有面(可能有疊層但無洞)。

### 變更(渲染效能)
- **ChunkStreamConfig**:loadRadius 1000→400、unloadRadius 1350→550、新增 cameraFarClip=450、
  gltfFrameBudget 0.2→0.15。
- **SetupScene.Build()**:camera farClipPlane 2000→450、加入霧設定(RenderSettings.fog)、
  強制更新 maxYSnapPerFrame=80。
- **ChunkStreamBootstrapper.Start()**:manifest 載入後設定 cameraFarClip + 霧 + 呼叫 OnSpawned()。

## [v2.2.2] - 2026-11-20

### 修正(Play mode 體驗問題)
- **攝影機隨視角飄移/墜落(look up = 飄起、look down = 墜落)。** 根因:舊場景(`Main.unity`)以 v2.2.0 預設值烘焙,`groundRaycastHeight=100` 使地面跟隨射線從 100m 高處打下,打到鄰近建物屋頂或自身 `CharacterController` 膠囊(頂部在眼睛上方 0.15m),導致 `targetY = cameraY + 1.85` → 每幀上升 0.13m = 持續飄移;上升至失去地面接觸後重力啟動 = 墜落。
  - `TouchPlayerController.Awake` 強制覆寫過時的序列化值(`groundRaycastHeight=2`、`groundRaycastLength=50`、`startPosition=(947,0,-124)`),並停用 `ChunkStreamBootstrapper.overrideStartPos`(舊值 true 會把攝影機設到 `(0,80,0)` 與 startPosition 衝突)。
  - 地面跟隨射線改用 `RaycastNonAlloc` 並**跳過自身 `CharacterController` collider**,修正射線打到自身膠囊導致的上升漂移。
  - `Awake` 補上缺失的 `CharacterController`(`RequireComponent` 在載入已存場景時不一定自動補上,舊場景無此元件)。
  - 新增 `_groundEverFound` 懸停守護:首次找到地面前不施重力(避免分塊尚未載入時穿模墜落);曾找到地面後失去才施重力緩慢下墜;掉至 `fallRespawnY` 以下重生至起始位置。
- **「2 座島」非 2×2 矩陣(15A/15C 未載入)。** 根因:`loadRadius=600` 太小——攝影機起始 `(947,124)` 時,15A 最近分塊 649m、15C 最近 817m,皆超出 600m 載入半徑,僅 15B + 15D 邊緣薄帶載入。修正:`loadRadius=1000`、`unloadRadius=1350`,4 圖幅(~1350×1050m)皆可從起點載入。
- **穿透建物/樹木(無水平碰撞)。** 根因:舊版 `ApplyMovement` 直接寫 `transform.position.x/z`,零牆面碰撞檢查。修正:改用 `CharacterController.Move()` 處理水平碰撞,膠囊 `height=eyeHeight+0.3`、`radius=0.3`、`stepOffset=0.4`、`slopeLimit=75`(攝影測量地形坡度大,放寬)。
- **邊緣瞬移(射線打到屋頂後瞬間吸附)。** 根因:`groundRaycastHeight=100` 射線打到最高 collider(屋頂/樹冠),`groundFollowLerp` 直接吸附到屋頂後掉落。修正:射線起點改為眼睛上方 2m、搜尋 50m,並以 `maxYSnapPerFrame=8` 限幅每幀 Y 移動量。

### 新增(Stage A — 可調式浮空地面移除)
- **`convert_osgb_to_chunks.py` 新增 `--no-floating-removal` 旗標。** 浮空地面移除(步驟 5b)可能過度刪除合法表面(橋梁、平台、低屋頂)造成地面大洞。新增 `floating_removal` 參數控制:預設啟用(移除浮空層);`--no-floating-removal` 完全停用(保留所有面,可能有疊層)。供後續針對洞孔問題重跑轉換器時使用。

### 變更(場景自動化)
- **`SetupScene.Build()` 強制更新 `TouchPlayerController` 與 `ChunkStreamConfig` 欄位。** 確保舊場景重新烘焙時寫入正確值(`groundRaycastHeight=2`、`startPosition=(947,0,-124)`、`loadRadius=1000` 等),不再殘留 v2.2.0 預設值。
- **新增選單項目 `HKGrid > 重建場景 (SetupScene.Build)`。** 可從 Unity 編輯器頂部選單手動重新烘焙場景,不需 batchmode。

## [v2.2.0] - 2026-11-20

### 重大變更(3D 模型解析度提升)
- **分塊資料由 L16(20 MB / 162K 三角面)升級至 L18(109 MB / 1.42M 三角面)。** 同一圖幅 `11-NW-15B`(九龍城區),LOD 由第 16 層提升至第 18 層,三角面增加 8.7×,模型大小增加 5.5×。建物立面與屋頂細節顯著改善。L18 為未修剪原始資料(CSDI L18 源檔已足夠小,decimation 與紋理降解析均未觸發)。
  - 每塊平均 ~5.4 MB / ~71K 三角面(最大塊 8.17 MB / 104K 三角面)。
  - L18 備份:L17 版(39 MB / 430K 三角面)已備份;可回退。
  - 注意:L18 分塊較大,iPhone 串流載入時可能短暫卡頓;後續 v2.3.0+ 可考慮 collider 半徑 < render 半徑的效能優化。

### 新增(Stage A — L17+ LOD 支援)
- **`convert_osgb_to_chunks.py` 支援 L17/L18 等更細 LOD。** 先前 `--lod` 僅接受 `root`/`l15`/`l16`(每子圖幅單一 `.osgb`);L17+ 一個子圖幅會分裂為多個 `.osgb`(四元樹),舊版只取其一。現在 `--lod` 接受任意 `lNN`,自動收集子圖幅內所有對應 LOD 檔,各自 `osgconv` → `pymeshlab` 簡化(預算均分)→ `trimesh` 合併為單一 `.glb` 分塊(多 mesh)。三角面約為 l16 的 4×(l17)/16×(l18)。
  - `SubTile` 欄位重構:`root_osgb: Path` + `lod_files` → `lod_osgbs: list[Path]`(所選 LOD 檔)+ `all_lod_files`(參考用)。
  - `build_chunk_glb` 簽名改為 `obj_paths: list[Path]`:逐 OBJ 簡化 + 暫存,再以 trimesh 收集所有網格合併;第二輪超量簡化亦改為對每個暫存 OBJ 各自再簡化。bbox 改取所有輸入 OBJ 的聯集。
  - 預掃描(`obj_cache`)改為 `dict[int, list[Path]]`,L17+ 多檔全部轉 OBJ 並納入全域 bbox(置中點計算正確)。
  - `--lod` 預設由 `root` 改為 `l16`(與既有文件/推薦一致),新增 `_lod_type` 型別校驗接受 `root` 或 `lNN`。
- **輸出大小調整指引(模組 docstring + converter README)。** 輸出大小主要由 LOD + 紋理像素控制:預設 `l16 + 512px` ≈ 20 MB/圖幅;高解析食譜 `--lod l17/l18 --max-texture-px 1024+`。單塊/總和預算僅為上限,不會無故放大輸出。

### 修正(多圖幅定位 + 攝影機座標)
- **分塊容器節點改用 positionOffset 定位(修正 4 圖幅全疊在原點)。** 先前 ChunkStreamer 將所有分塊容器設於 `localPosition = Vector3.zero`,單圖幅可行(GLB 頂點已含位置),但合併 4 圖幅時各圖幅 GLB 座標獨立 → 全部疊在原點 → 只見 1 個圖幅。修正:`ChunkManifest.ChunkRecord` 新增 `positionOffset` 欄位(圖幅偏移),ChunkStreamer 以 `rec.PositionOffsetVector` 定位容器節點。單圖幅 positionOffset=[0,0,0],行為不變。
- **合併 manifest 的 centroid 改為全域座標(修正距離計算)。** 先前合併後 centroid 仍為本地座標(相對各圖幅 origin),與攝影機全域座標不符 → 距離 ~1300m → 載入半徑內無分塊。修正:centroid = 本地 + 圖幅偏移(全域),bbox 同。positionOffset 與 centroid 分離:前者用於渲染定位,後者用於距離計算。
- **攝影機起始位置修正為 Unity Y-up 座標。** 先前 `startPosition = (947, 0, 124)` 為 manifest Z-up 座標,未套用 -90° X 旋轉。修正:`(947, 0, -124)` = manifest (947, 124, 0) 經 -90° X 旋轉後的 Unity 世界座標(X=東, Y=上, Z=-北)。

### 新增(多圖幅合併 + 起始位置 + 控制修正)
- **4 圖幅合併為單一分塊集。** 11-NW-15A/B/C/D 的 L18 分塊(共 80 塊,522 MB,6.2M 三角面)合併為單一 `manifest.json` + `.glb` 集,座標統一相對於 15A 原點。新 `merge_tiles.py` 腳本:讀取各圖幅 manifest、計算偏移量、重新命名(前綴圖幅代號)、合併 centroid/bbox。
- **起始位置 = WGS84 [22.3319, 114.1871](九龍城區)。** 破解 CSDI 座標系:manifest 的 EPSG:2326 座標實為 HK1980 Grid 減 800000(本地慣例)。目標經緯度 → EPSG:2326 E=837322, N=821424 → manifest 空間 E=37322, N=21424 → 合併世界座標 (947, 124)(相對 15A 原點)。`TouchPlayerController.startPosition` 預設此值。
- **攝影機起始俯角 25°。** 先前 pitch=0(水平看天空)→ 藍屏。現 pitch=25°(向下看城市),起始即可見建物。
- **搖桿固定於左下角。** 先前搖桿出現於觸控點(左半屏任意位置)。現固定於 (joystickMarginX=160, joystickMarginY=160) 左下角,持續顯示。
- **pan/tilt 非反轉。** 先前 `_pitch -= delta.y`(上拖=下看,反轉)。現 `_pitch += -delta.y`(上拖=上看,標準 FPS)。
- **串流半徑擴大。** `loadRadius` 350→600m,`unloadRadius` 500→800m,以覆蓋合併 4 圖幅(~1500×1200m)並載入相鄰圖幅。

### 新增(玩家控制 — 觸控 PTZ + FPS 移動)
- **`TouchPlayerController.cs`** — 第一人稱玩家控制器,取代 `FlyCameraController`(WASD/滑鼠,在 iPhone 無輸入 → 藍屏)。自動偵測平台:
  - **行動(觸控):** 左半屏浮動搖桿(觸控點出現,拖曳移動,放開消失)+ 右半屏單指拖曳 pan/tilt + 雙指捏合 zoom(FOV 25°–90°)。
  - **桌面(Editor):** WASD 移動 + 滑鼠右鍵拖曳視角 + 滾輪縮放 + Shift 衝刺(與舊版 FlyCameraController 行為一致)。
  - **地面跟隨:** 每幀從攝影機上方向下射線(`Physics.Raycast`),保持攝影機在地面 + `eyeHeight`(1.7m)高度。首次命中地面時直接吸附(不 lerp);其後以 `groundFollowLerp` 平滑。
  - **搖桿 UI:** 執行時以程式生成 Canvas + Image(圓形 sprite 由 `MakeCircleSprite` 程序生成,無外部資源依賴)。觸控時顯示、放開時隱藏。
- **`ChunkStreamer.cs` 加入 MeshCollider。** 載入分塊後對所有子網格(`GetComponentsInChildren<MeshFilter>`)加入 `MeshCollider`,供地面跟隨射線使用。由 `ChunkStreamConfig.addChunkColliders`(預設 `true`)控制。
- **`SetupScene.cs`** 以 `TouchPlayerController` 取代 `FlyCameraController`(移除舊版 + 加入新版)。
- **`BuildIOS.cs`** 匯出前先呼叫 `SetupScene.Build()`,確保 `Main.unity` 已烘培最新元件(`TouchPlayerController` 等)。

## [v2.1.0] - 2026-11-20

### 重大變更
- **引擎由 Unity 6 LTS 改為 Tuanjie(團結引擎)2022.3.61t15。** 使用者本機僅有 Tuanjie(Untiy China 的 Unity 2022.3 LTS 中國分支),且明確選擇以既有引擎繼續。**ADR-011** 修訂 ADR-009 的引擎選擇;三階段管線架構不變,glTFast 6.0.1 套件在 Tuanjie 2022.3 上經實測可正常安裝與編譯。

### 新增
- **Stage B 編譯驗證通過(Tuanjie 2022.3.61t15 + glTFast 6.0.1)。** 以 `Tuanjie.exe -batchmode -nographics` 跑無頭編譯,5 個 C# 腳本零錯誤零警告編譯完成(初次 5 個 CS 錯誤已修正,均為 glTFast 6.0 實際 API 與鷹架假設不符)。
  - 修正清單:
    1. `ChunkStreamer.cs`:`_ = LoadChunkAsync(rec)` → `LoadChunkAsync(rec)`(`async void` 不可賦值,CS8209)。
    2. `ChunkStreamBootstrapper.cs`:`da.frameBudget = x` → `da.SetFrameBudget(x)`(`frameBudget` 為 private 欄位,用公開方法,CS1061)。
    3. `ChunkStreamer.cs`:`ImportSettings.generateNormals = true` 移除(`ImportSettings` 在 glTFast 6.0 無此欄位,CS0117)。
    4,5. `ChunkStreamer.cs`:`GltfImport` ctor 改用具名參數 `(downloadProvider: null, deferAgent: _deferAgent, materialGenerator: null, logger: _logger)`,對應實際簽名 `(IDownloadProvider, IDeferAgent, IMaterialGenerator, ICodeLogger)`(CS1503)。
  - `ChunkStreamConfig.gltfFrameBudget` 語義修正:從「秒」改為「目標幀時間比例」(glTFast 實際語義),預設 0.2(占 20% 幀時間)。
- **入口場景自動化(`Assets/Editor/SetupScene.cs` + `Assets/Scenes/Main.unity` + `Assets/Configs/ChunkStreamConfig.asset`)。** Editor 腳本由 batchmode `-executeMethod` 執行,冪等建立入口場景(Main Camera + ChunkStreamBootstrapper + FlyCameraController + 方向光)+ Config 資產,並將 Main.unity 設為 BuildSettings scene 0。
- **Stage B 資料層測試腳本(`Assets/Editor/TestChunkLoad.cs`)。** 由 batchmode 執行,驗證 manifest.json 反序列化 + 第一個 .glb 分塊的 glTFast `LoadFile` + `InstantiateMainSceneAsync` + `Dispose` 完整鏈路(以反射存取 `HKGrid.Streaming.ChunkManifest`,避免 Editor assembly 跨 assembly 參考)。
- **11-NW-15B 分塊資料複製至 `Assets/StreamingAssets/Chunks/`**(20 個 .glb + manifest.json,14 MB),供 Stage B Play-mode 串流測試。已 `.gitignore` 排除(build artifact,可由 Stage A 重新生成)。
- **ADR-011:Tuanjie 取代 Unity 6 LTS** — 記錄引擎版本差異(2022.3 vs 6 LTS)、授權模式差異(Unity China `license.tuanjie.cn` + `seat_required: true` + 4 天有效期)、iOS Build Support 缺失(僅 windowsstandalonesupport)、`.unity`/`.asset` 檔 `%TAG !u! tag:yousandi.cn,2023:` 命名空間差異。
- **`ProjectSettings/ProjectVersion.txt`** 預建(`m_EditorVersion: 2022.3.61t15`),讓 Tuanjie 首次開啟專案時不抱怨版本不符。
- **`ProjectSettings/ProjectSettings.asset`**:`companyName` 由 `DefaultCompany` 改為 `McCmak`,`productName` 由 `unity-project` 改為 `HKGridVoxel`。

### 變更
- **`.gitignore`** 擴充:排除 `Assets/StreamingAssets/Chunks/`(Stage A 分塊資料,build artifact)+ Tuanjie 授權相關檔案(`*.hasp`、`*.ulf`、`*.alf`、`Tuanjie_v*.ulf`、`Tuanjie_v*.alf` 等)。
- **`docbase/adr.md`**:新增 ADR-011;ADR-009 的「Unity 6 LTS」字樣應理解為「Tuanjie 2022.3 LTS」(由 ADR-011 正式修訂)。

### 修正(Stage A — L16 LOD 與浮空地面移除)
- **LOD 由 root 改為 L16。** Root LOD 之建築物僅為水平堆疊平板(無牆面),視覺上像「3 層地面」;L16 LOD 含 19–32% 垂直牆面=真實 3D 建築。11-NW-15B 圖幅由 14 MB / 47,401 三角面(root)增至 24.80 MB / 201,969 三角面(L16)。
- **浮空地面移除(KDTree 局部最低 Z 演算法)。** CSDI 攝影測量資料在同一 XY 位置常有多層水平面(地面 + 浮空層 + 屋頂),全貼同一張空拍紋理 → 視覺上像「地面疊地面」(使用者稱「4 個島」)。演算法:對每個水平面以 KDTree 查 50m 半徑內所有水平面之最低 Z,若面 Z 高出局部地面 3~20m 則移除(保留地面 dz<3m + 屋頂 dz>20m)。11-NW-15B 圖幅移除 40,165 面(19.9%),總三角面 201,969 → 161,804,總大小 24.80 → 19.72 MB。
  - `trimesh 5.x` API 修正:`submesh()[0]` 改為 `update_faces()`(submesh 回傳 Trimesh 非 list,`[0]` 觸發 `'Trimesh' object is not subscriptable`)。
- **`convert_osgb_to_chunks.py` 新增 `--lod {root,l15,l16}` 選項**(預設 l16)。

### 修正(Stage B — 雙重偏移與攝影機位置)
- **分塊雙重偏移 bug(「4 個島」根因)。** `ChunkStreamer.cs:155` 原先將分塊容器節點設為 `localPosition = centroid`,但 GLB 頂點已由轉換器置中(全圖幅 origin 已內嵌於頂點座標),導致雙重偏移 → 分塊散開 150m 間距(4 列 × 5 行散成 4 個獨立島)。修正為 `localPosition = Vector3.zero`。
- **攝影機起始位置 bug。** `startLocalPos = (0, 80, 0)` 在 Z-up 本地座標意為「向北 80m」,經 −90° X 旋轉後 = 世界 `(0, 0, −80)` = 地下 80m。修正為 `(0, 0, 80)` = 向上 80m → 世界 `(0, 80, 0)` = 地面 80m 上空俯視。修正於 `ChunkStreamBootstrapper.cs`、`SetupScene.cs`、`Main.unity`。

### 新增(分塊資料注入架構)
- **分塊資料改以 GitHub Release asset 提供,不提交 git。** `.glb` 分塊(19.72 MB)打包為 `chunks-<dataset>.tar.gz`,上傳至專屬 data release(標籤 `data-*`,不觸發 app 發行管線)。建置前由 `fetch_chunks.sh` 下載並解壓至 `StreamingAssets/Chunks/`。本機與 CI 建置共用同一注入流程。
- **`codebase/scripts/fetch_chunks.sh`** — 從 GitHub Release 下載 `chunks-*.tar.gz` 並解壓至 `StreamingAssets/Chunks/`。使用 `gh release download`(CI 由 `GITHUB_TOKEN` 自動認證,本機需 `gh auth login`)。驗證 manifest.json 存在 + .glb 計數。
- **`DISTRICT-TO-TILENAME.md`**(儲存庫根目錄)— 香港行政區 → CSDI 圖幅名稱對照表(11 區,數百個圖幅),供選取目標圖幅時參考。

### 變更(CI)
- **`cicd.yml`** 重新設計為「本機 Windows 匯出 Xcode 專案 → CI macOS 執行 xcodebuild → .ipa」過渡架構。原因:Tuanjie 授權(.alf)為機器綁定,僅本機可用;Windows 無 xcodebuild。解法:本機用 Tuanjie 匯出 Xcode 專案(授權在本機),上傳至 GitHub Release,CI macOS runner 下載後執行 xcodebuild(不需 Unity 授權)。
  - `build:ipa` job 重新啟用:僅 `workflow_dispatch` 觸發,macOS runner,下載 Xcode 匯出資產 → `xcodebuild archive`(未簽署,4 旗標)→ 打包 `.ipa` + dSYM + manifest.json → 上傳至目標 release。合併原 `release:ipa` job 的 release 建立邏輯。
  - `workflow_dispatch` 新增 `inputs`:`xcode_export_tag`(Xcode 匯出 release 標籤)、`release_tag`(目標發行標籤)。
  - `deploy:docs` 加入 `needs: build`,確保 `workflow_dispatch` 時 deploy 在 build 完成後才執行(重新產生 apps.json);push 時 build 被跳過,deploy 仍正常執行。
  - `test:sast` 維持停用(待 Unity 授權續期 + 設定 CI secret 後以 GameCI 重建)。

### 修正(建置/CI — IL2CPP 崩潰 + storyboard 編譯 + runner 版本)
- **`build:ipa` job 的 IL2CPP 崩潰已修正。** 根因為 [.NET 6 在 macOS Apple Silicon(arm64)虛擬機的 ReadyToRun bug](https://github.com/dotnet/runtime/issues/64103):R2R 編譯的程式碼傳遞 variadic 引數時使用暫存器而非堆疊,違反 Apple arm64 ABI;`ConsolePal..cctor` 建立物件陣列時觸發 → `OutOfMemoryException: Array dimensions exceeded supported range` → `ARCHIVE FAILED`。GitHub Actions `macos-15` runner 為 arm64 VM,觸發此 bug。(此 bug 已於 .NET 6.0.5 修復,但 Tuanjie 的 il2cpp 可能使用較舊 .NET 6.0.x。)
  - **診斷過程:** 先前以 PTY(`pty.fork` + controlling terminal + `TIOCSWINSZ` 24×80)、移除 `TERM` 環境變數等方式嘗試修正,均未成功——run `44657434200` 診斷證實 `isatty(0/1/2)=True`、`TIOCGWINSZ` 回傳 24×80、`TERM=None`,崩潰仍發生。證實崩潰與終端完全無關,純為 .NET arm64 R2R bug。舊 `pty.spawn` 版本的「6 小時 hang」(run `44498856814`)實為 `pty._copy` 輸出死結遮蔽了同樣的即時崩潰。
  - **修正:** `il2cpp_pty_wrapper.sh` 設 `COMPlus_ReadyToRun=0`(及 `DOTNET_ReadyToRun=0`)停用 ReadyToRun,改用 JIT 編譯,避開 R2R bug。另設 `stdin=/dev/null` 防 il2cpp 讀 stdin 阻塞。不再需要 PTY / Python / 非阻塞排空 / 看門狗——崩潰與 hang 均不再發生。
- **CI runner 由 `macos-latest`(Xcode 26.6)改為 `macos-15` + Xcode `16.2`。** Xcode 26.x 與 Tuanjie 2022.3 匯出之 Xcode 專案相容性不佳;Xcode 16.2 為 Unity 2022.3 LTS 同期版本,相容性最佳(已驗證 setup-xcode 於 macos-15 成功選取 16.2)。`maxim-lobanov/setup-xcode` 改釘 `xcode-version: '16.2'`。
- **`cicd.yml` 之「修補 IL2CPP」步驟** copy `il2cpp_pty_wrapper.sh`(R2R 停用 wrapper),job env 設 `COMPlus_ReadyToRun=0` + `DOTNET_ReadyToRun=0`。
- **`ibtool` 編譯 storyboard 失敗(iOS 18.2 Platform Not Installed)已修正。** IL2CPP 崩潰修正後(run `44658176551` 通過 GameAssembly 階段),`xcodebuild` 於 `CompileStoryboard LaunchScreen-iPhone.storyboard` 步驟失敗:`ibtool` 報「iOS 18.2 Platform Not Installed」——`macos-15` runner 的 Xcode 16.2 未預裝 iOS 模擬器平台,而 `ibtool` 編譯 storyboard 需要該平台。修正:`cicd.yml` 新增「安裝 iOS 平台」步驟,於建置前執行 `xcodebuild -downloadPlatform iOS` 安裝缺失平台。修正後 run `44659117033` 首次成功完成完整封存(10m57s),產出 `HKGridVoxel-2.1.0.ipa`(24,091,960 bytes / ~23 MB)+ dSYM + manifest.json 並上傳至 `v2.1.0` release。

### 新增(建置腳本)
- **`codebase/scripts/build_unity_ipa.sh`** — 完整 iOS 建置管線腳本,支援三種模式:
  - 預設(全管線):`fetch_chunks` + Unity 匯出 + `xcodebuild` + 打包(需 macOS)
  - `UNITY_EXPORT_ONLY=1`:`fetch_chunks` + Unity 匯出(Windows 本機用,產出 Xcode 專案)
  - `XCODEBUILD_ONLY=1`:僅 `xcodebuild` + 打包(CI macOS 用,使用預匯出 Xcode 專案,需搭配 `XCODE_PROJECT_DIR`)
  - 所有 `xcodebuild` 皆未簽署(4 旗標);`.ipa` 結構驗證 `Payload/<App>.app/` 在根;產生 `manifest.json` + `GITHUB_OUTPUT`。
- **`codebase/scripts/export_xcode_project.ps1`** — Windows PowerShell 本機匯出腳本:呼叫 Tuanjie 批次模式 `BuildIOS.Export` 匯出 Xcode 專案,打包為 `xcode-export-<ver>.tar.gz`,可選 `-Upload` 自動上傳至 GitHub Release。前置檢查:iOS Build Support 模組、分塊資料、Tuanjie 未執行中。
- **`codebase/unity-project/Assets/Editor/BuildIOS.cs`** — Unity Editor 建置腳本(batchmode `-executeMethod BuildIOS.Export` 入口):設定 iOS Player Settings(bundle ID `com.mccmak.hkgridvoxel`、版本、最低 iOS 12.0、IL2CPP、arm64),呼叫 `BuildPipeline.BuildPlayer` 匯出 Xcode 專案。環境變數可覆寫設定。

### 驗證(Stage C — iOS Build Support 安裝與本機 Xcode 匯出)
- **iOS Build Support 模組安裝(直接下載安裝器)。** Tuanjie Hub 無法偵測本機 Editor 安裝(Hub 掃描 `C:\Program Files\Tuanjie\Hub\Editor\`,但 Editor 實際位於 `C:\Program Files\Tuanjie 2022.3.61t15\`),「Add Modules」功能無法使用。改從 Hub 的 `releases.json` 模組目錄取出 iOS 模組安裝器直連網址,直接下載 + NSIS 靜默安裝:
  - 安裝器:`TuanjieSetup-iOS-Support-for-Editor-2022.3.61t15.exe`(665.9 MB,MD5 `f7deadfe9ede4e28da5a163026dab03e`)。
  - 安裝指令:`TuanjieSetup-iOS-Support-for-Editor-2022.3.61t15.exe /S /D=C:\Program Files\Tuanjie 2022.3.61t15`(NSIS `/S`=靜默,`/D`=目標目錄必須是最後一個參數且不加引號)。
  - 安裝結果:3.1 GB,位於 `Editor\Data\PlaybackEngines\iossupport\`,含 `UnityEditor.iOS.Extensions.dll`、`iOSPlayerBuildProgram.exe`、`il2cpp`、`arm64`/`x64` 工具鏈、`Trampoline`(iOS app 模板)。
  - 授權:啟用 entitlement `com.unity.editor.platforms.ios`(由 Tuanjie 授權伺服器核發)。
- **本機 Xcode 專案匯出驗證通過。** `Tuanjie.exe -batchmode -nographics -executeMethod BuildIOS.Export -quit` 在 53 秒內成功匯出 `Tuanjie-iPhone.xcodeproj`(600.2 MB,exit 0):
  - `Info.plist`:`CFBundleShortVersionString=2.1.0`、`CFBundleDisplayName=HKGridVoxel`、`PRODUCT_BUNDLE_IDENTIFIER=com.mccmak.hkgridvoxel`(BuildIOS.cs 的 `SetApplicationIdentifier` 覆寫了 ProjectSettings.asset 的 `com.McCmak.HKGridVoxel`)。
  - Scheme:`Tuanjie-iPhone`(`xcshareddata/xcschemes/Tuanjie-iPhone.xcscheme`)。
  - 結構:`Il2CppOutputProject/`(293.5 MB C++ 原始碼)、`Libraries/`(281.1 MB 原生函式庫)、`Data/`(含 StreamingAssets/Chunks/*.glb)、`Classes/`、`MainApp/`、`TuanjieFramework/`、launch screens、`Info.plist`、`process_symbols.sh`。
  - 打包為 `xcode-export-v2.1.0.tar.gz`(197.2 MB,67.1% 壓縮率),上傳至 GitHub Release `xcode-export-v2.1.0`。
- **`ProjectSettings.asset` 同步:** BuildIOS.cs 套用的 iOS Player Settings 持久化(`bundleVersion` 1.0→2.1.0、`applicationIdentifier.iPhone`、`scriptingBackend.iPhone=1` IL2CPP、`platformArchitecture.iPhone=1` ARM64、`overrideDefaultApplicationIdentifier=1`)。

### 修正(Stage C — 建置腳本與饋流)
- **`build_unity_ipa.sh` scheme 偵測改進。** Tuanjie 匯出的 Xcode 專案使用 `Tuanjie-iPhone` 作為 scheme 名稱(Unity 為 `Unity-iPhone`)。原本 fallback 寫死為 `Unity-iPhone`,會在 Tuanjie 匯出上失敗。改為偵測失敗時以 `.xcodeproj` 名稱推斷 scheme(`Tuanjie-iPhone.xcodeproj`→`Tuanjie-iPhone`)。
- **`generate_apps_json.sh` 饋流僅考慮 v* 發行版本。** 儲存庫現有三種發行版本:`v*`(App 發行,含 .ipa)、`xcode-export-*`(Xcode 專案 tarball,無 .ipa)、`data-*`(分塊資料,無 .ipa)。原本以 `gh release list --limit 1` 取最新發行版本,若 `xcode-export-*` 或 `data-*` 在 `v*` 之後建立,會被誤選為「最新」→ 饋流因無 .ipa 變空。改用 `--exclude-drafts --json tagName,createdAt` + jq 過濾 `startswith("v")` 並依 `createdAt` 降序取第一個。
- **`export_xcode_project.ps1` iOS 模組提示訊息更新。** Tuanjie Hub 無法偵測此 Editor 安裝,提示改為直接下載模組安裝器 + `/S /D=<Editor 根目錄>` 靜默安裝。

## [v2.0.0] - 2026-11-19

### 重大變更
- **架構轉向:Swift/SceneKit → Unity 6 LTS + 分塊 GLB 串流。** 使用者要求以真實 CSDI 攝影測量網格呈現全圖幅(1.7 GB OSGB),並以執行時分塊串流在行動裝置載入。Swift/SceneKit 方案(ADR-007)與打包單一 GLB 方案(ADR-008)已由 **ADR-009** 取代。原 `codebase/app/HKMinecraftVoxel/` Swift 專案將由 `codebase/unity-project/`(Unity 6 LTS)取代;Stage B/C 待 Unity 安裝後實作。
- **CI 放寬零機密前提:** 為 Unity 建置引入 Unity 授權檔(`.ulf`)作為 GitHub Actions secret(見 **ADR-010**,放寬 ADR-006)。Apple 簽署機密仍維持零(沿用 ADR-001 未簽署側載)。Fork 需自行設定 `UNITY_LICENSE` 才能執行 Unity 建置 job。

### 新增
- **Stage A:OSGB → 分塊 GLB 轉換器(`codebase/converter/`)。** 三階段管線的第一階段,純 Python,Unity 無關:
  - `convert_osgb_to_chunks.py` — 將單一 CSDI OSGB 圖幅(PagedLOD 四元樹)轉為空間分塊 GLB。流程:`osgconv`(OSGB→OBJ+紋理)→ `pymeshlab` 的 `meshing_decimation_quadric_edge_collapse_with_texture`(**保留 UV/紋理**)→ `trimesh`(置中、紋理降解析、GLB 匯出)。每個 CSDI 子圖幅(`Tile_+X_+Y`)= 一個 `.glb` 分塊。無寫死路徑,所有路徑經 CLI/環境變數傳入。
  - `validate_chunks.py` — 硬性大小閘道:任一 `.glb` > 10 MB 或總和 > 200 MB 即非零退出,供 CI gate 使用。同時驗證 manifest 欄位完整性與檔案存在性。
  - `requirements.txt` — Python 相依(pymeshlab、trimesh、Pillow、numpy、py7zr)。**移除 pyfqmr**(經實測會丟失 UV,無法用於紋理網格)。
  - `README.md` — Stage A 完整文件:工具安裝(osgconv、pymeshlab)、CLI 用法、manifest schema、座標系(EPSG:2326 置中)、pymeshlab vs pyfqmr 對照、疑難排解。
- **manifest.json schema:** 每分塊記錄 `id`、`grid_x`/`grid_y`、`bbox_min`/`bbox_max`/`centroid`(本地座標,已相對全圖幅 origin 置中)、`file`、`bytes`、`triangles`、`neighbors`(相鄰子圖幅 id)、`priority`。圖幅級中繼資料:`source`、`crs`、`origin`(HK 1980 Grid 公尺)、`tileSize`、`gridCols`/`gridRows`、`totalBytes`、`totalTriangles`、`attribution`(© 香港特別行政區地政總署)。
- **ADR-009:** 記錄「遷移 Unity 6 LTS + 分塊 GLB 串流」決策(取代 ADR-007 與 ADR-008)。
- **ADR-010:** 記錄「引入 Unity 授權檔作為 CI secret」決策(放寬 ADR-006,僅限 Unity 引擎授權;Apple 機密仍零容忍)。

### 驗證(Stage A,11-NW-15B 九龍城區圖幅 — 使用者提供之實際資料)
- 圖幅位置:lon 114.1816–114.1888,lat 22.3280–22.3335(東九龍,九龍灣/新蒲崗一帶),~750 × 600 m,建築物高度 ~147 m。
- 20 個分塊(5×4 子圖幅網格),全塊保留紋理(每塊 1 張貼圖;該區為低密度住宅區,網格較稀疏)。
- 總大小 **14.16 MB** / 200 MB 預算(93% 餘裕);最大分塊 **0.76 MB** / 10 MB 預算。
- 總三角面 ~50,000;座標系 EPSG:2326 已置中至圖幅中心 `[37124.898, 21302.305, 77.536]`。
- `validate_chunks.py` 硬性閘道通過(退出碼 0)。
- 對照:密集市區(如旺角 11-NW-19B)實測為 36.24 MB、637,565 三角面、密集區 9–14 張紋理/塊,亦在預算內。

### 新增(Stage B 鷹架,待 Unity 編譯驗證)
- **Unity 6 LTS 專案鷹架(`codebase/unity-project/`)**:Stage B 的 C# 腳本與套件清單,標示為鷹架(scaffold),待使用者安裝 Unity 6 LTS + iOS Build Support 後首次開啟編譯驗證。
  - `Packages/manifest.json`:`com.unity.cloud.gltfast` 6.0.1 + UGUI/WebRequest 模組。
  - `Assets/Scripts/ChunkManifest.cs`:Stage A manifest.json 的 Unity 端資料模型(JsonUtility 反序列化)。
  - `Assets/Scripts/ChunkStreamConfig.cs`:ScriptableObject 設定(載入/卸載半徑、每幀預算、座標轉換)。
  - `Assets/Scripts/ChunkStreamer.cs`:核心串流器 — `GltfImport.Load` + `InstantiateMainSceneAsync` + `Dispose()`,依攝影機半徑逐塊載入/卸載,`TimeBudgetPerFrameDeferAgent` 限制每幀預算,HK 座標(EPSG:2326 Z-up)經 −90° X 旋轉轉 Y-up。
  - `Assets/Scripts/ChunkStreamBootstrapper.cs`:場景入口 — 載 manifest、建立串流器、每幀 `Tick()`。
  - `Assets/Scripts/FlyCameraController.cs`:WASD 飛覽攝影機。
  - `README.md`:Unity 專案建立、分塊資料放置、座標系轉換、iOS Player Settings(Stage C)、待驗證 API 清單。
- **待驗證**:glTFast 6.0 API 簽名(`GltfImport` 建構式、`GameObjectInstantiator`、`TimeBudgetPerFrameDeferAgent.frameBudget`、`SetDefaultDeferAgent`),安裝 Unity 後依 Console 編譯錯誤對照官方 API 文件修正。

### 移除
- **Swift/SceneKit App(`codebase/app/HKMinecraftVoxel/` 整個目錄)。** 依 ADR-009 架構轉向,Swift App(含 `HKMinecraftApp.swift`、`CSDIMongKokSceneFactory.swift`、`FlyCameraController.swift`、`VoxelMeshBuilder.swift`、`TerrainDataSource.swift`、`ContentView.swift`、`VoxelSceneFactory.swift`、`Info.plist`、`Assets.xcassets`、`zh-Hant.lproj/`、`HKMinecraftVoxel.xcodeproj/`、`HKMinecraftVoxelTests/`、內嵌 7.5 MB `mongkok_11-NW-19B.glb`、`mongkok_11-NW-19B_summary.json`)全數刪除。Unity 專案(`codebase/unity-project/`)取代為唯一 iOS 客戶端。
- **CI Swift 建置階段(`.github/workflows/cicd.yml` 的 `test:sast`、`build:ipa`、`release:ipa` 三 job)。** 各 job 加 `if: false` 停用並加註「待 Stage C 以 GameCI 重建為 Unity 建置/發行」。`deploy:docs` 解耦(不再 `needs: [build, release]`),獨立維護入口網站與 `apps.json` 饋流。工作流程 `name:` 由「零機密 iOS 管線」改為「iOS 管線」(預告 ADR-010 的 Unity 授權機密將放寬零機密前提)。

### 變更
- **ADR-006:** 狀態由「已接受」改為「已接受(ADR-010 部分放寬)」,註明 Unity 授權機密的引入範圍。
- **ADR-007:** 狀態由「已接受」改為「已取代(由 ADR-009 取代)」。
- **ADR-008:** 狀態由「已接受」改為「已取代(由 ADR-009 取代)」,註明 pyfqmr 丟失 UV 的缺陷已由 pymeshlab 修正。
- **`codebase/converter/README.md`:** 範例與 manifest schema 由 11-NW-19B(旺角)改為 11-NW-15B(九龍城區),並補充低密度 vs 高密度區的實測對照。

## [v1.4.0] - 2026-11-19

### 新增
- **旺角實景(CSDI)場景:** 新增 `CSDIMongKokSceneFactory.swift`,載入香港地政總署(Lands Department)3D 視覺化地圖之旺角圖幅 `11-NW-19B` 真實網格資料。資料來源為 CSDI 開放資料(OSGB 格式,圖幅涵蓋約 751 m × 600 m,座標系 EPSG:2326 HK 1980 Grid),經離線轉換(osgconv → OBJ → trimesh/pyfqmr 簡化)為 glTF 二進位檔(.glb,7.5 MB,179,811 三角面)隨 App 打包。使用者可於 HUD 切換「程序化香港」與「旺角實景(CSDI)」兩種場景。
- **300 m AGL 貼地巡航攝影機:** `FlyCameraController` 在 CSDI 模式下以射線向下取樣地面/建築物頂高度,攝影機維持離地 300 m(AGL),確保不會穿入建築物。程序化模式仍維持原有的 380 m 絕對高度巡航。
- **CSDI 資料來源標註:** App 內附 `mongkok_11-NW-19B_summary.json` 記錄資料來源、座標系、三角面數與授權標註(© 香港特別行政區地政總署)。
- **ADR-008:** 記錄「直接顯示 CSDI 真實網格(非體素化)」的決策,以及離線簡化策略與大小取捨。

### 變更
- **App 版本升至 1.4.0:** `CFBundleShortVersionString` 與 `MARKETING_VERSION` 由 1.3.0 升至 1.4.0。
- **HUD 新增場景切換器:** `FlyHUD` 加入 `Picker`(segmented style)切換場景模式;既有巡航/暫停按鈕保留。
- **預設場景改為旺角實景:** App 啟動時優先顯示 CSDI 旺角實景。

## [v1.3.0] - 2026-11-18

### 新增
- **單一工作流程 `cicd.yml`:** 把原本五支 workflow(`ci`、`release`、`pages`、`codeql`、`health`)整併為一支,內含四個以 `needs` 串接的 job:`test:sast` → `build:ipa` → `release:ipa` → `deploy:docs`。工作流程名稱不含冒號,job 顯示名稱一律為 `階段:產物`;job key 保持 `test`/`build`/`release`/`deploy`(GitHub 的 job key 不接受 `:`)。
- **`test:sast`:** 模擬器測試 + 覆蓋率,以及 CodeQL Swift(`build-mode: manual`)合併於同一 job;測試成品先上傳,再執行 CodeQL 觀察的未簽署建置,避免建置腳本清除 `build/` 時遺失結果。Dependabot PR 與外部 fork PR 自動略過 CodeQL(權杖唯讀),測試照常。
- **`release:ipa`:** 標籤 `v*` 時在 `ubuntu-latest` 以 `actions/download-artifact` 取回 IPA/dSYM,再 `gh release create`/`gh release upload`;以 `nullglob` 處理缺少 dSYM 的情況。
- **`deploy:docs`:** 以 `needs: [build, release]` + `always()` 在同一 run 內等發行成功後部署 Pages,取代原本 `release.yml` 以 `gh workflow run pages.yml` 交叉觸發;`release` 在非標籤事件為 `skipped` 時仍會部署。
- **入口網站完整側載教學:** `codebase/site/index.html` 新增五種安裝方式的逐步說明(AltStore、SideStore、Sideloadly、Xcode、GitHub Actions 成品)、來源 URL 複製按鈕、管線階段與觸發表,以及 7 天憑證/未信任開發者/數量限制等常見問題。版面改為響應式卡片與編號步驟。
- **執行摘要:** `build:ipa` 與 `release:ipa` 寫入 `GITHUB_STEP_SUMMARY`,直接列出 IPA 檔名、版本與 `releases/latest` 連結。

### 變更
- **`build:ipa` 以 `needs: test` 做為發行閘門:** 測試或 CodeQL 失敗即不建置、不發行(原本 CI 的建置 job 為 `always()`)。
- **每日健康檢查併入排程:** `schedule: '17 6 * * *'` 觸發同一條 `test:sast` → `build:ipa`,不再有獨立 `health.yml`。
- **權限下放到 job:** 工作流程預設 `contents: read`;`security-events: write` 僅 `test:sast`、`contents: write` 僅 `release:ipa`、`pages: write` + `id-token: write` 僅 `deploy:docs`。
- **併發策略:** 只在 `pull_request` 取消被取代的執行,避免中斷 `main`/標籤的 Pages 部署;`deploy:docs` 另有 job-level `concurrency: pages`。
- **App 版本對齊:** `CFBundleShortVersionString` 與 `MARKETING_VERSION` 由 `0.1.0` 升至 `1.3.0`,使首個 `v1.3.0` Release 的 `apps.json` 版本與標籤一致。
- **文件同步:** `AGENTS.md`、`README.md` 與 `docbase/`(adr、architecture、api、cicd、configurations、crm、pdr、prd、quick-start、rtm、srs、setup、toctree、uiux、user-stories)全數改為單一工作流程與新 job 名稱;ADR-005 改記「單一工作流程、多 job」的決策轉向。移除「入口網站只放短連結、步驟以 README 為單一來源」的舊規則。

### 修正
- **修正入口網站顯示「尚無發行版本」後無法下載:** 根本原因是儲存庫從未推送 `v*` 標籤,`apps.json` 只依 GitHub Release 產生,CI 的 Actions 成品不會進入饋流。入口網站現在預設就連到 `releases/latest`,空饋流或 `fetch` 失敗時仍可點擊,並在頁面說明「Release 才是饋流來源」與 Actions 成品的替代路徑。
- **修正 `deploy:docs` 對空饋流的呈現:** 不再顯示灰色的停用按鈕。

### 移除
- `.github/workflows/ci.yml`、`release.yml`、`pages.yml`、`codeql.yml`、`health.yml`(由 `cicd.yml` 取代)。

## [v1.2.6] - 2026-11-18

### 修正
- **修正 IPA 版面驗證的偶發誤判:** `unzip -l "$ipa" | grep -q "Payload/..."` 在 `set -o pipefail` 下,`grep -q` 命中即結束會讓 `unzip` 收到 SIGPIPE,管線因此回傳非零 —— 即使確實找到 `Payload/`。CI 的建置 job 僥倖通過,CodeQL 的建置則在 `ARCHIVE SUCCEEDED` 後誤報「Payload 不在 zip 根目錄」。改為先把 `unzip -l` 輸出寫入檔案再 `grep`(無管線),並在 `unzip` 失敗時給出明確訊息。
- **修正 dSYM 存在性檢查的同類問題:** `find ... | grep -q .` 改為 `find ... -print -quit`,避免同樣的 SIGPIPE 競態。
- **修正 `generate_apps_json.sh` 的同類問題:** `gh release list ... | grep -q .` 改為先取值再以 `[[ -n ... ]]` 判斷,避免發布流程偶發誤判「找不到發行版本」。

## [v1.2.5] - 2026-11-18

### 修正
- **修正 IPA 打包失敗(封存成功之後):** `build_unsigned_ipa.sh` 先 `cd` 到 `build/App.xcarchive/ipa-staging` 才用相對路徑 `build/ipa/...` 輸出,造成 `zip I/O error: No such file or directory` / `Could not create output file`。改為在腳本開頭就把 `ARCHIVE_DIR`、`IPA_DIR`、`DSYM_DIR` 轉成絕對路徑;dSYM 打包原本也有同樣問題,一併修正。
- **修正 CodeQL 設定錯誤:** `github/codeql-action/init` 的合法輸入是 `build-mode`,原本的 `config-mode: manual` 被忽略(僅有 warning),導致手動建置未啟用、job 以 configuration error 結束。改用 `build-mode: manual`,並把 `init`/`analyze` 升級至 `v4`(`v3` 將於 2026 年 12 月停用)。
- 同步更新 `AGENTS.md`、`docbase/cicd.md`、`docbase/architecture.md` 的 `build-mode` 說明。

## [v1.2.4] - 2026-11-18

### 修正
- **修正 App 編譯錯誤(CI 測試與封存同時失敗的根因):**
  - `ContentView.swift`:`SceneView.Options` 並無 `.antialiased` 成員,移除該選項;反鋸齒沿用 `SceneView` 預設的 `.multisampling4X`。
  - `VoxelMeshBuilder.swift`:`SCNGeometrySource` 初始化參數標籤錯誤(`offset`/`stride` 應為 `dataOffset`/`dataStride`,且需以 `data:` 開頭)。改以扁平 `[Float]` 緩衝建立幾何來源,同時修正 `SCNVector3` 在 iOS 上 stride 為 16 bytes(非 12)造成的頂點資料佈局錯誤。
- **修正攝影機未加入場景圖:** `FlyCameraController` 改為持有 `SCNScene`,並把 `cameraNode` 加入 `rootNode`;`pointOfView` 不在場景圖中將無法以此視角算圖。場景也不再於每次 `body` 求值時重建。
- **修正 `@testable import` 可能失敗:** 於 App target 的 Debug 設定加入 `ENABLE_TESTABILITY = YES`(手寫專案未如 Xcode 範本預設開啟)。
- **`Info.plist`:** 移除 `UIRequiredDeviceCapabilities`(當中的 `arm64` 可能使非 arm64 執行器的模擬器拒絕安裝),補上標準 `UIApplicationSceneManifest`,並簡化 `UILaunchScreen`。
- 移除 `biomeColor` 未使用的 `topLayerIndex` 參數。

## [v1.2.3] - 2026-11-18

### 修正
- **修正模擬器目的地找不到裝置:** 原 `-destination 'platform=iOS Simulator,name=iPhone 15,OS=latest'` 在 Xcode 26.6 runner(僅有 iPhone 17e 等新機型)上失敗。改為動態探勘可用 iPhone 模擬器 UDID(`xcrun simctl list` + python3 解析),以 `-destination "id=$sim_udid"` 指定,跨 Xcode 版本皆適用。
- **修正 `Localizable.strings` 格式:** 原以 JSON 撰寫,但 `.strings` 需 NeXTstep plist 格式(`"key" = "value";`),JSON 僅適用 `.xcstrings`。改為正確格式,解決 `CopyStringsFile` / `validation failed` 封存錯誤。

## [v1.2.2] - 2026-11-18

### 修正
- **修正 scheme 名稱解析的前導空白:** `xcodebuild -list` 輸出的 scheme 行帶有縮排空白,原 `sed` 僅刪除空白行未修剪前後空白,導致 `xcodebuild` 收到 `"        HKMinecraftVoxel"` 而找不到 scheme(CI 測試/建置、CodeQL、health、release 皆失敗)。`codebase/scripts/build_unsigned_ipa.sh` 與 `.github/workflows/ci.yml` 的 inline 探索皆改用 `sed 's/^[[:space:]]*//;s/[[:space:]]*$//'` 修剪空白。此為所有 workflow 失敗的單一根因。

### 新增
- **`AGENTS.md` 新硬性限制:** git commit 的 subject 與 body 皆須以繁體中文撰寫(Conventional Commits 前綴與程式碼/路徑保持原樣)。

## [v1.2.1] - 2026-11-18

### 新增
- **`AGENTS.md` 新硬性限制:** 禁止 console art / ASCII 圖表,所有圖表一律改用 `mermaid`(純指令/資料 fence 不在此限)。
- **Mermaid 圖表補完:** `charter`(範圍)、`srs`(需求對應)、`pdr`(採用 vs 否決)、`user-stories`(角色流程)、`configurations`(設定資料流)、`crm`(文件-元件-需求關係)、`uiux`(版面 block)。全 18 份 `docbase/` + 根目錄文件皆含 mermaid。

### 變更
- 移除 `architecture.md`(Pipeline + 資料流)、`erd.md`(ERD)、`uiux.md`(版面)的 ASCII 圖表,僅保留 mermaid 版本;「(Mermaid)」標題後綴移除。
- `setup.md` 的 ASCII 目錄樹改為條列。
- `charter.md` 時間表修正為「pipeline 與 App 皆已建置」(原誤為 greenfield)。
- `configurations.md` 修正 `pages.yml` runner 為 `ubuntu-latest`(原誤為 `macos-latest`)。

## [v1.2.0] - 2026-11-18

### 新增
- **HK Minecraft 體素 App(原生 Swift + SceneKit + Metal):** `codebase/app/HKMinecraftVoxel.xcodeproj` —— 香港地形體素飛覽,以 Minecraft 方塊風格呈現太平山、大帽山、鳳凰山等山脊。實作為 `HKMinecraftApp`、`ContentView`、`TerrainDataSource`(可插拔協定 + `ProceduralHKTerrain` 程序化離線地形)、`VoxelMeshBuilder`(單一合併 `SCNGeometry` + per-vertex 生物群落著色)、`VoxelSceneFactory`(天空/光照/霧)、`FlyCameraController`(`CADisplayLink` 巡航 + HUD)。
- **單元測試:** `HKMinecraftVoxelTests/TerrainDataSourceTests.swift` —— 確定性、海平面鉗制、已知山峰、網格維度。共享 scheme `HKMinecraftVoxel` 已提交,供 CI `xcodebuild -list` 探索。
- **ADR-007:** 以原生 Swift + SceneKit 實作體素 App(非 Unity + Cesium) —— 因 Unity 需授權機密、Cesium 需 ion 權杖,違反零機密限制;`CSDIDEMTerrain` 為未來接 HK CSDI 開放資料 DEM 的擴充點。
- **Mermaid 圖表:** 為根目錄與 `docbase/` 文件加入圖表 —— `README`(管線 flowchart)、`AGENTS`(目錄結構 flowchart)、`architecture`(pipeline + sequence)、`erd`(erDiagram)、`cicd`(觸發鏈)、`schema`(classDiagram)、`api`(sequence)、`prd`(功能 flowchart)、`quick-start`(流程)、`rtm`(需求對應)、`toctree`(文件分類)、`adr`(timeline)、`uiux`(安裝流程)、`setup`(前置流程)。

### 變更
- `codebase/app.config.json` 與 `codebase/site/apps.json` 更新為 HK Minecraft App 名稱、描述、bundle ID(`com.mccmak.hkminecraftvoxel`)。
- `codebase/site/index.html` 標題與標語更新為 HK Minecraft。
- `README.md` 簡介更新為 HK 體素飛覽 App。
- `docbase/setup.md`「加入 Xcode 專案」改為「Xcode 專案位置」,反映專案已存在於 `codebase/app/`。

## [v1.1.0] - 2026-11-18

### 新增
- **結構重組:** 所有實作成品集中於 `codebase/`(`scripts/`、`site/`、`app.config.json`、`.swiftlint.yml`);`.github/workflows/` 與 `.github/dependabot.yml` 因 GitHub 硬性要求保留於根目錄,並改為參照 `codebase/` 路徑。
- **`docbase/` 文件套件(18 份,繁體中文):** `toctree`、`charter`、`srs`、`pdr`、`prd`、`user-stories`、`adr`、`architecture`、`api`、`uiux`、`schema`、`erd`、`setup`、`configurations`、`cicd`、`rtm`(需求追溯矩陣)、`crm`(交叉參考矩陣)、`quick-start`。`toctree` 為導覽中樞;`rtm`/`crm` 提供需求與文件的雙向涵蓋。
- **`AGENTS.md` 治理規則:** 新增兩條硬性限制 —— (1) 所有使用者面向內容以繁體中文撰寫;(2) 每次依提示變更須新增 `CHANGELOG.md` 條目並升 semver。並新增「目錄結構」章節。
- **`CHANGELOG.md`:** 建立語意化版本變更記錄(本檔)。

### 變更
- **全面在地化為繁體中文:** `README.md`、`codebase/site/index.html`(`lang="zh-Hant"`)、`codebase/app.config.json`、`codebase/site/apps.json`、所有工作流程與步驟 `name:` 欄位、`codebase/scripts/*.sh` 註解與日誌、`codebase/.swiftlint.yml`。指令、YAML 鍵、檔案路徑、識別碼與產品名稱(AltStore、SideStore、Sideloadly、TestFlight、Xcode、`xcodebuild` 等)保持原樣。
- 工作流程腳本呼叫路徑由根目錄改指向 `codebase/scripts/`;`build_unsigned_ipa.sh` 的專案探索放寬至 `find -maxdepth 4`(根目錄或 `codebase/` 下皆可)。

### 修正
- 校正 `docbase/` 文件事實以對齊實作:`GITHUB_OUTPUT` 共 8 鍵(`ipa-path`、`dsym-path`、`manifest-path`、`app-name`、`app-version`、`bundle-id`、`ipa-file-name`、`dsym-file-name`);`manifest.json` 欄位(`appName`、`bundleIdentifier`、`version`、`ipaFileName`、`dsymFileName`);`app.config.json` 欄位(`description` 而非 `localizedDescription`,並含 `website`/`appName`);補充 `ditto`(保留符號連結)、CodeQL `config-mode: manual`(先未簽署建置再儀器化編譯)。

## [v1.0.0] - 2026-11-18

### 新增
- 建立零成本 iOS CI/CD 管線,供免費 Apple ID 側載使用(零 Apple 機密,僅 `GITHUB_TOKEN`)。
- 模組化工作流程:`ci.yml`(測試 + 建置未簽署 IPA + 上傳成品)、`release.yml`(標籤 `v*` → GitHub Release)、`pages.yml`(入口網站 + `apps.json` 饋流部署)、`codeql.yml`(Swift 安全分析)、`health.yml`(每日建置健康檢查)。
- `.github/dependabot.yml`:`swift` + `github-actions` 生態系,每週更新。
- `scripts/build_unsigned_ipa.sh`(根目錄):確定性未簽署建置與 IPA 打包(`Payload/<App>.app` 位於 zip 根目錄),自動探索專案/scheme(多個則失敗),由 `Info.plist` 推導名稱/版本,產出 `manifest.json` 與 `.dSYM.zip`。
- `scripts/generate_apps_json.sh`(根目錄):從最新 GitHub Release 產生 AltStore/SideStore 相容 `apps.json`,資產來源欄位(version、versionDate、size、downloadURL)取自實際發行資產。
- `site/`(根目錄):靜態 GitHub Pages 入口網站(客戶端抓取 `apps.json` 渲染最新版本與下載鈕),含 `apps.json` 預留檔。
- `app.config.json`、`.swiftlint.yml`(根目錄):饋流中繼資料與 SwiftLint 設定。
- `AGENTS.md`:OpenCode 代理人指引。
- `README.md`:管線總覽、觸發方式、一次性設定、Sideloadly/AltStore/SideStore 側載步驟、7 天憑證週期(英文)。
- `LICENSE`(MIT)、`.gitignore`。

> 註:v1.0.0 的實作成品位於儲存庫根目錄;v1.1.0 將其遷移至 `codebase/` 並調整工作流程參照路徑。

[v1.3.0]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.3.0
[v1.2.6]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.6
[v1.2.5]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.5
[v1.2.4]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.4
[v1.2.3]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.3
[v1.2.2]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.2
[v1.2.1]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.1
[v1.2.0]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.2.0
[v1.1.0]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.1.0
[v1.0.0]: https://github.com/Mcc-Mak/ios-app/releases/tag/v1.0.0
