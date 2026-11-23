# 變更記錄 (CHANGELOG)

本專案所有顯著變更皆記錄於此檔案。版本遵循[語意化版本](https://semver.org/lang/zh-TW/)
(`vX.X.X`)。每次依提示進行變更時,依變更性質升版並於此新增條目:

- **主版本 (X.0.0):** 不相容的破壞性變更
- **次版本 (1.X.0):** 向後相容的新功能
- **修訂版 (1.1.X):** 向後相容的修正

格式參考 [Keep a Changelog](https://keepachangelog.com/zh-TW/)。

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
