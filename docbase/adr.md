# 架構決策紀錄

本文件記錄 ios-app 零成本 iOS CI/CD pipeline 的關鍵架構決策（Architecture Decision Records, ADR）。每筆紀錄包含編號、標題、狀態、內容、決策與後果。狀態多為「已接受」；ADR-007 / ADR-008 已標註為「已取代（由 ADR-009 取代）」，保留作為歷史決策紀錄。

```mermaid
timeline
    title ADR 決策時間軸
    section 建置與簽署
        ADR-001 : 未簽署建置
        ADR-002 : Payload/ 根目錄打包
    section 中繼資料與分發
        ADR-003 : manifest.json asset-sourced
        ADR-004 : GitHub Pages 饋流
    section 組織與安全
        ADR-005 : 單一 workflow 多 job
        ADR-006 : 僅 GITHUB_TOKEN
        ADR-010 : 放寬 ADR-006 引入 Unity 授權機密
    section App 實作
        ADR-007 : 原生 Swift + SceneKit（已取代）
        ADR-008 : 離線簡化打包 GLB（已取代）
        ADR-009 : 遷移 Unity + 分塊 GLB 串流
        ADR-011 : 改採 Tuanjie 2022.3.61t15（修訂 ADR-009）
```

---

## ADR-001：使用未簽署建置

- **狀態：** 已接受
- **內容：** pipeline 需在無付費 Apple Developer Program 的前提下產出可側載的 IPA。傳統做法（`fastlane match`、簽署憑證、provisioning profile、App Store Connect keys）皆需 Apple 機密，無法在零 secret 的 fork 上運作。
- **決策：** 每次 `xcodebuild` 呼叫（封存）一律帶上四個未簽署旗標：`CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" CODE_SIGN_ENTITLEMENTS=""`。實作見 `../codebase/scripts/build_unity_ipa.sh`（CI 以 `XCODEBUILD_ONLY=1` 模式呼叫）。
- **後果：** IPA 不含簽章，需由 AltStore/SideStore/Sideloadly 以使用者免費 Apple ID 現場簽署。免費憑證每 7 天過期，促成 Releases + 饋流自動重灌流程（見 ADR-004）。CI 可在零 Apple 機密下完整運作。

## ADR-002：IPA 打包為 Payload/<App>.app 在 zip 根目錄

- **狀態：** 已接受
- **內容：** AltStore 與 Sideloadly 僅接受 `Payload/<App>.app/...` 位於 zip 根目錄的 IPA。直接壓縮裸 `.app`、或從 `Payload/` 內部壓縮，皆會被拒絕。
- **決策：** 以 `ditto` 將 `.app` 複製進暫存目錄的 `Payload/`（保留 framework 符號連結與中繼資料），再 `cd` 到含 `Payload/` 的目錄執行 `zip -r "<App>-<Ver>.ipa" Payload`，並以 `unzip -l` 驗證版面。實作見 `../codebase/scripts/build_unity_ipa.sh`。
- **後果：** 產出的 IPA 可被 AltStore/SideStore/Sideloadly 直接安裝。`ditto` 僅在 macOS 可用，故建置限定 `macos-latest` runner。

## ADR-003：透過 manifest.json 資產傳遞 asset-sourced 中繼資料

- **狀態：** 已接受
- **內容：** 饋流產生器需知道 App 名稱、bundleIdentifier、版本、檔名。若在饋流腳本中猜測或硬編碼，會與實際建置結果脫鉤。
- **決策：** 建置腳本從已建置 `Info.plist`（`CFBundleName`/`CFBundleShortVersionString`/`CFBundleIdentifier`）萃取值，寫入 `manifest.json` 並作為 Release 資產上傳。饋流產生器 `../codebase/scripts/generate_apps_json.sh` 下載該資產，從中讀取這些欄位（`version`、`bundleIdentifier`、`appName`）。
- **後果：** 饋流的 App 身分欄位永遠對應到實際建置物，而非猜測。新增欄位需同時更新 manifest schema 與饋流產生器。

## ADR-004：GitHub Pages 部署 AltStore/SideStore 來源饋流

- **狀態：** 已接受
- **內容：** 免費 Apple ID 憑證每 7 天過期，使用者需重新安裝/刷新。需要一個無後端、自動更新、零成本的分發管道。
- **決策：** 以 GitHub Pages 託管靜態入口網站（`../codebase/site/`）與 `apps.json` 饋流。來源 URL 為 `https://mcc-mak.github.io/ios-app/apps.json`。`apps.json` 於每次發行後由 `../.github/workflows/cicd.yml` 的 `deploy:docs` job 重新產生。儲存庫 Pages 設定必須為 **Source: GitHub Actions**。
- **後果：** 無伺服器維護成本；饋流隨發行自動更新。使用者於 AltStore/SideStore 加入來源後，可一鍵安裝並於 7 天週期內刷新。

## ADR-005：單一工作流程、多 job（階段化管線）

- **狀態：** 已接受（取代原本「每支 workflow 一項職責」的分割）
- **內容：** 多支 workflow 需要以 `gh workflow run` 交叉觸發，難以一眼看懂階段順序，也重複 checkout 與建置；使用者要求可追溯的單一鏈路。
- **決策：** `.github/workflows/cicd.yml` 為唯一工作流程，依序 `build:ipa` → `deploy:docs`（`test:sast` 已移除、`release:ipa` 已併入 `build:ipa`）；job 之間以 `needs` 與 artifacts 傳遞產物，不再交叉觸發。工作流程名稱不含冒號，job 顯示名稱為 `階段:產物`，權限下放到各 job（`build` 為 `contents: write`；`deploy` 為 `contents: read` + `actions: read` + `pages: write` + `id-token: write`）。
- **後果：** 一次執行即可看到完整鏈路，發行與部署有明確順序，權限仍最小化。代價是單一檔案較長，且 `deploy:docs` 需要 `build:ipa` 結果（`if: always()` 守護，build 為 `skipped` 時仍可部署文件）；每日健康檢查改由 `schedule` 觸發 `deploy:docs`。`test:sast` 待 Stage C GameCI 重建後再以新 ADR 重新引入（見 ADR-010）。

## ADR-006：僅使用 GITHUB_TOKEN,零 Apple 機密

- **狀態：** 已接受(ADR-010 部分放寬:Unity 建置階段引入 Unity 授權機密,Apple 簽署機密仍維持零)
- **內容：** 引入任何 Apple 機密（certs、profiles、App Store Connect keys）即破壞「零成本、可 fork」前提。
- **決策：** 全 pipeline 僅使用內建 `GITHUB_TOKEN`（`permissions` 於 `cicd.yml` 各 job 內宣告最小範圍）。永不引入 `fastlane match`、簽署憑證、provisioning profile 或 App Store Connect keys。
- **後果：** 任何 fork 在零額外 secret 設定下即可運作 CI。安全性完全依賴 GitHub 內建權限模型,無外部信任邊界。**注意:** ADR-009 遷移 Unity 後,Unity 建置階段需 Unity 授權檔(`.ulf`)作為 secret(見 ADR-010),此為 Unity 引擎授權而非 Apple 簽署機密;Apple 簽署仍維持未簽署(ADR-001)由使用者側載。

## ADR-007：以原生 Swift + SceneKit 實作體素 App(非 Unity + Cesium)

- **狀態：** 已取代(由 ADR-009 取代)
- **內容：** App 需求為「3D HK Minecraft」體素飛覽。Unity iOS 建置需 Unity 授權（即使 Personal 亦需啟用機密）；Cesium for Unity 需 Cesium ion 權杖。兩者皆與 ADR-006「僅 `GITHUB_TOKEN`、零機密、可 fork」硬性限制衝突，且維護者無法提供 Unity 授權機密、亦不在本機安裝 Unity。
- **決策：** 以原生 Swift + SceneKit + Metal 實作體素地形飛覽（`../codebase/app/HKMinecraftVoxel/`）。地形資料來源抽象為 `TerrainDataSource` 協定，預設 `ProceduralHKTerrain`（離線程序化香港山脊），`CSDIDEMTerrain` 為未來接 HK CSDI 開放資料 DEM 的擴充點（無需 ion 權杖）。沿用既有 `xcodebuild` 未簽署建置管線，無需任何新機密。
- **後果：** 無 Unity/Cesium 相依，CI 仍僅用 `GITHUB_TOKEN`，確定性建置。視覺效果受限於 SceneKit（無 Cesium 3D Tiles 真實攝影測量），但可完全離線運作。**已由 ADR-009 取代:** 使用者明確要求遷移至 Tuanjie(Unity)以取得真實 CSDI 攝影測量網格的完整紋理呈現與執行時分塊串流,並接受引擎授權機密(ADR-010 / ADR-011)。

## ADR-008：直接顯示 CSDI 真實網格(非體素化)並以離線簡化縮小檔案

- **狀態：** 已取代(由 ADR-009 取代)
- **內容：** 使用者要求以香港 CSDI 開放資料的真實 3D 模型檔(非程序化、非猜測)呈現旺角。CSDI 地政總署「3D 視覺化地圖(圖幅式模型)」涵蓋全港,每個圖幅(約 700 m × 550 m)提供 OBJ/OSGB/Cesium 3D Tiles 三種格式。然而單一圖幅的完整檔案為 1.7–11 GB(含多 LOD 攝影測量網格與高解析紋理),無法打包進可側載的 IPA,亦無法在行動裝置上即時串流。CSDI 的非紋理 3D Tiles API 需申請 API key(= 機密,違反 ADR-006)。使用者要求「真實紋理網格直接顯示」而非體素化。
- **決策：** 採用 OSGB 格式(單一圖幅 1.70 GB,為三種格式中最小),取其根 LOD(最粗略層級,20 個子圖幅的根 `.osgb`,共 27 MB)以 `osgconv` 轉為 OBJ,再以 `trimesh` + `pyfqmr`(`preserve_border=False`)離線簡化至 179,811 三角面。較小的網格保留完整紋理(UV + 256px 貼圖),較大的密集網格因 pyfqmr 不保留 UV 而改用頂點色(取自原紋理平均色)。結果輸出為 glTF 二進位檔(.glb,7.5 MB)隨 App 打包,座標系 EPSG:2326 已重新置中至圖幅中心。攝影機以 300 m AGL 射線取樣貼地巡航。API key `ad5940a63bd344c48b0351ef1c7a905e` 為 CSDI 自身在公開 GeoJSON 圖幅索引中發布的開放金鑰,非個別使用者機密,不違反 ADR-006。
- **後果：** App 內含真實旺角建築/地形幾何(非程序化),7.5 MB 可輕易打包進 IPA。密集核心區(旺角主要建築)因簡化失去細部紋理,僅保留頂點色調;邊緣稀疏區保留完整紋理。**已由 ADR-009 取代:** pyfqmr 丟失 UV 的缺陷已由 pymeshlab 的 `meshing_decimation_quadric_edge_collapse_with_texture`(保留 UV/紋理)修正;打包單一 GLB 的方式已改為分塊串流(每子圖幅一個 .glb),讓全圖幅 36 MB 的高品質網格可逐塊載入而非一次打包。

## ADR-009：遷移 Tuanjie(Unity)+ 分塊 GLB 串流(取代 Swift/SceneKit)

- **狀態：** 已接受(取代 ADR-007 與 ADR-008;引擎選擇經 ADR-011 修訂為 Tuanjie 2022.3.61t15)
- **內容：** 使用者要求以真實 CSDI 攝影測量網格呈現香港 3D 地形,並以執行時分塊串流(runtime chunk streaming)在行動裝置上載入全圖幅(1.8 GB OSGB → ≤ 200 MB 分塊 GLB)。Swift/SceneKit 方案(ADR-007)與打包單一 GLB 方案(ADR-008)皆無法支援:(a) SceneKit 缺乏 `glTFast` 等級的執行時 GLB 串流與 frame-budgeted defer agent;(b) 打包單一 GLB 限於 ≤ 10 MB,無法容納全圖幅高品質網格;(c) ADR-008 的 pyfqmr 簡化會丟失 UV,密集市區失去紋理。
- **決策：** 三階段管線:
  1. **Stage A(純 Python):** `codebase/converter/convert_osgb_to_chunks.py` 以 `osgconv`(OSGB→OBJ+紋理)+ `pymeshlab`(`meshing_decimation_quadric_edge_collapse_with_texture`,**保留 UV/紋理**)+ `trimesh`(置中、紋理降解析、GLB 匯出)將單一 OSGB 圖幅轉為空間分塊(每子圖幅一個 `.glb`,≤ 10 MB/塊,總和 ≤ 200 MB),附 `manifest.json`(chunk id、bbox、centroid、鄰居、三角面數、位元組數)。`validate_chunks.py` 為硬性大小閘道。`decimate_glb.py` 可再簡化既有分塊(`--textured` 模式以 trimesh + fast_simplification + scipy KDTree UV 重投影,無需 pymeshlab)。
  2. **Stage B(Tuanjie 2022.3 LTS):** `codebase/unity-project/` 使用 `com.unity.cloud.gltfast`(`GltfImport` + `InstantiateMainSceneAsync` + `Dispose()`)執行時逐塊載入 GLB,以 `TimeBudgetPerFrameDeferAgent` 限制每幀載入預算,依攝影機半徑載入/卸載鄰近分塊,HK 座標(EPSG:2326,Z-up)正規化至本地原點並以 −90° X 旋轉轉 Y-up。
  3. **Stage C(iOS):** Tuanjie iOS Player Settings + Xcode 匯出(本機 Windows `export_xcode_project.ps1`,經 `-ChunksSource` 注入分塊資料)→ CI macOS `xcodebuild` 未簽署封存為單一 `.ipa`(沿用 ADR-001 未簽署旗標供側載)。
  - 採用 **Tuanjie 2022.3.61t15**(Unity China 的 Unity 2022.3 LTS 中國分支;原 ADR-009 選 Unity 6 LTS,經 ADR-011 修訂為 Tuanjie)。glTFast 6.0.1 支援 2021.3+,在 Tuanjie 2022.3 上經實測可正常編譯。
  - 分塊策略:每個 CSDI 子圖幅(`Tile_+X_+Y`)= 一個 `.glb` 分塊(11-NW-15B 為 20 塊,自然空間單元)。
  - 無寫死機器路徑:轉換器經 CLI/環境變數傳入路徑,Unity 端經 ScriptableObjects/設定檔。
- **後果：**
  - **真實紋理:** pymeshlab UV 保留簡化讓密集市區(九龍城核心)保留建築物紋理,而非 ADR-008 的頂點色灰塊。
  - **全圖幅載入:** 分塊串流讓 22.5 MB(11-NW-15B 實測)的全圖幅網格可逐塊載入,記憶體扁平化。
  - **代價:** Unity/Tuanjie 建置需引擎授權(見 ADR-010/ADR-011,放寬 ADR-006 的零機密前提);維護者需在本機安裝 Tuanjie Hub + Tuanjie 2022.3.61t15 + iOS Build Support 模組;CI 採預先匯出 Xcode 專案模式(`XCODEBUILD_ONLY=1`),不在 CI 內跑 Unity/GameCI(待 ADR-010 Stage C 重建評估)。
  - **iOS shader stripping:** BuildIOS.cs `EnsureGltfFastShadersIncluded()` 將 glTFast + `Standard` shaders 加入 GraphicsSettings Always Included Shaders;`link.xml` 保留 C# 組件但**不**保留 shader 資產,故需顯式納入。
  - Stage A 已驗證(11-NW-15B 九龍城區:1.8 GB OSGB → 22.5 MB textured GLB、20 塊、全塊保留紋理)。Stage B 已運作(IPA 在 iPhone 上渲染分塊含紋理)。Stage C 已運作(本機 Windows Tuanjie 匯出 + CI macOS `xcodebuild`,`workflow_dispatch` 觸發)。

## ADR-010：引入 Unity 授權檔(.ulf)作為 CI secret(放寬 ADR-006)

- **狀態：** 已接受(放寬 ADR-006,使用者明確同意);**目前 CI 尚未啟用 GameCI**——`build:ipa` 改以預先匯出的 Xcode 專案(`xcode-export-*` Release)+ `XCODEBUILD_ONLY=1` 模式執行 `xcodebuild`,不在 CI 內跑 Unity,故暫無需 `UNITY_LICENSE` secret。本 ADR 於 Stage C 以 GameCI 重建 CI 時才正式啟用。
- **內容：** ADR-009 遷移 Unity 後,CI 需以 GameCI 在 GitHub Actions 執行 Unity 建置。Unity 引擎授權(即使 Personal 免費版)需以授權檔(`.ulf`)啟用,該檔含機器指紋與啟用權杖,無法公開。此與 ADR-006「僅 `GITHUB_TOKEN`、零機密、可 fork」的前提衝突。
- **決策：** **僅**為 Unity 建置階段引入 Unity 授權檔作為 GitHub Actions secret(`UNITY_LICENSE`),經 GameCI `unity-request-manual-activation-file` / `unity-activate` 工作流程產生並加密儲存。其餘階段(文件部署、Release 上傳、AltStore 饋流)仍僅用 `GITHUB_TOKEN`。Apple 簽署機密(cert、profile、App Store Connect key)**仍維持零**(沿用 ADR-001 未簽署建置 + 側載)。**現況過渡:** 在 GameCI 重建前,CI 採預先匯出 Xcode 專案流程——維護者本機以 Tuanjie 匯出 Xcode 專案(含分塊資料)上傳至 `xcode-export-*` Release,CI 僅下載並執行 `xcodebuild`,完全不需要 Unity 授權,Apple 機密亦維持零。
- **後果：**
  - Fork 需自行設定 `UNITY_LICENSE` secret 才能執行 Unity 建置 job(否則該 job 跳過或失敗);其餘 CI 階段仍可在零 secret 下運作。**目前過渡期連 `UNITY_LICENSE` 亦不需要**——CI 僅需 `xcode-export-*` Release 存在。
  - 信任邊界擴大:除 GitHub 內建權限外,新增 Unity 授權伺服器為信任對象(授權驗證時的網路往返)。Tuanjie 授權伺服器為 `license.tuanjie.cn`(見 ADR-011)。
  - 使用者明確接受此放寬(問答中選擇「CI with Unity license secret」)。
  - ADR-006 的 Apple 機密零容忍維持不變;此 ADR 僅放寬 Unity 引擎授權。

## ADR-011：採用 Tuanjie(團結引擎)2022.3.61t15 取代 Unity 6 LTS(修訂 ADR-009)

- **狀態：** 已接受(修訂 ADR-009 的引擎選擇;使用者明確同意)
- **背景：** ADR-009 原選 Unity 6 LTS。實際安裝時使用者本機僅有 **Tuanjie 2022.3.61t15**(Unity China 的 Unity 2022.3 LTS 中國分支,安裝於 `C:\Program Files\Tuanjie 2022.3.61t15\`)。本機無 Unity 6 LTS,且使用者選擇以既有 Tuanjie 繼續(問答中選「用 Tuanjie 2022.3 繼續」)。
- **內容：** Stage B/C 改用 Tuanjie 2022.3.61t15 作為編輯器與建置引擎。三階段管線架構(Stage A Python / Stage B Unity 串流 / Stage C iOS 封裝)不變;glTFast 6.0.1 套件在 Tuanjie 2022.3 上經實測可正常安裝與編譯。
- **與 ADR-009 的差異:**
  - **版本基礎:** Unity 2022.3 LTS(非 Unity 6 LTS)。API 大致相容(同 C# 版本、同 UPM 套件系統、glTFast 6.0.1 支援 2021.3+)。Unity 6 新 API(如 `Awaitable`、新版 SRP)在 Tuanjie 上不可用;Stage B 鷹架未使用這些,故無影響。
  - **授權模式:** Tuanjie 用 Unity China 授權伺服器(`license.tuanjie.cn`),非 Unity Global(`license.unity3d.com`)。授權檔格式仍為 `.ulf`(與 Unity 相同,機器綁定),但 **`seat_required: true`**(需有 seat 才能啟用)。Personal 免費版可用,4 天有效期,到期需重新啟用。本機啟用後 `.ulf` 落地於 `C:\ProgramData\Tuanjie\Tuanjie_lic.ulf`(系統目錄,非使用者設定檔)。
  - **CI 授權(放寬 ADR-010):** ADR-010 假設 `.ulf` 放 GitHub secret 即可讓 GameCI 在 Actions runner 上啟用。Tuanjie 的 `seat_required` 模式與機器綁定使此假設需實測驗證 — fork/CI runner 上的 seat 啟用流程待 Stage C 重建 CI 時確認。可能的替代方案:(a) Tuanjie 浮動授權服務;(b) 自架 macOS runner。此不確定性記錄於此,不阻塞 Stage B。
  - **iOS Build Support:** Tuanjie 安裝時未含 iOS 模組(僅 `windowsstandalonesupport`)。Stage C 封裝 `.ipa` 前需另案安裝 Tuanjie 的 iOS Build Support(透過 Tuanjie 官方安裝器,非 Unity Hub)。Stage B 編譯與 Play 測試不需要 iOS 模組。
- **決策理由:** 使用者本機已有 Tuanjie 且明確選擇繼續;重裝 Unity 6 LTS + iOS 模組(5–8 GB 下載 + 額外授權啟用)成本高且重複。Tuanjie 2022.3 對 Stage B 的 glTFast 串流場景已足夠;Unity 6 專屬功能非本專案所需。
- **後果：**
  - **Stage B 編譯驗證通過:** 5 個 C# 腳本在 Tuanjie 2022.3.61t15 + glTFast 6.0.1 上零錯誤零警告編譯(初次有 5 個 CS 錯誤,均為 glTFast 6.0 實際 API 與鷹架假設不符,已修正)。
  - **`ProjectVersion.txt` 改為 `2022.3.61t15`**(非 Unity 6 LTS 版本號)。
  - **檔案標記差異:** Tuanjie 產生的 `.unity`/`.asset` 檔使用 `%TAG !u! tag:yousandi.cn,2023:`(Tuanjie 的命名空間),與 Unity 的 `tag:unity3d.com` 不同。對 Tuanjie 內部處理無影響,但跨引擎遷移時需注意。
  - **ADR-009 的「Unity 6 LTS」字樣應理解為「Tuanjie 2022.3 LTS」**;本 ADR 為正式修訂紀錄。Stage C 完成後若評估 Tuanjie 的 iOS 模組/CI 授權不可行,可再以新 ADR 切回 Unity 6 LTS。

---

## 關聯文件

- 架構總覽與資料流：`./architecture.md`
- 饋流與 manifest 介面契約：`./api.md`
- 欄位結構定義：`./schema.md`
