# 變更記錄 (CHANGELOG)

本專案所有顯著變更皆記錄於此檔案。版本遵循[語意化版本](https://semver.org/lang/zh-TW/)
(`vX.X.X`)。每次依提示進行變更時,依變更性質升版並於此新增條目:

- **主版本 (X.0.0):** 不相容的破壞性變更
- **次版本 (1.X.0):** 向後相容的新功能
- **修訂版 (1.1.X):** 向後相容的修正

格式參考 [Keep a Changelog](https://keepachangelog.com/zh-TW/)。

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

### 驗證(Stage A,11-NW-19B 旺角圖幅)
- 20 個分塊(5×4 子圖幅網格),全部保留紋理(密集區 9–14 張貼圖/塊,稀疏區 1 張/塊)。
- 總大小 **36.24 MB** / 200 MB 預算(82% 餘裕);最大分塊 **4.52 MB** / 10 MB 預算。
- 總三角面 637,565;座標系 EPSG:2326 已置中至圖幅中心 `[35625.42, 20100.076, 119.955]`。
- `validate_chunks.py` 硬性閘道通過(退出碼 0)。

### 驗證(Stage A,11-NW-15B 圖幅 — 使用者提供之實際資料)
- 20 個分塊(5×4 網格),全塊保留紋理(每塊 1 張貼圖,稀疏區)。
- 總大小 **14.16 MB** / 200 MB 預算;最大分塊 **0.76 MB** / 10 MB 預算。
- 總三角面 ~50,000;`validate_chunks.py` 硬性閘道通過。

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

### 變更
- **ADR-006:** 狀態由「已接受」改為「已接受(ADR-010 部分放寬)」,註明 Unity 授權機密的引入範圍。
- **ADR-007:** 狀態由「已接受」改為「已取代(由 ADR-009 取代)」。
- **ADR-008:** 狀態由「已接受」改為「已取代(由 ADR-009 取代)」,註明 pyfqmr 丟失 UV 的缺陷已由 pymeshlab 修正。

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
