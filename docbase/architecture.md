# 架構概觀

ios-app 是一套**零成本 iOS CI/CD pipeline**，以未簽署建置 + GitHub Release + GitHub Pages 饋流，支援免費 Apple ID 側載。全程僅用內建 `GITHUB_TOKEN`，無任何 Apple 機密。

## Pipeline 觸發與流程

```mermaid
flowchart TD
  A["push / PR / 標籤 v* / 排程 / 手動"] --> B["cicd.yml<br/>test:sast"]
  B --> C["build:ipa"]
  C --> D[("Artifacts: IPA / dSYM / 測試")]
  C -->|"僅 v* 標籤"| E["release:ipa<br/>gh release create + upload"]
  E --> F[("Release: .ipa / .dSYM.zip / manifest.json")]
  E --> G["deploy:docs<br/>產生 apps.json + deploy-pages"]
  C -->|"main / 手動"| G
  G --> H["https://mcc-mak.github.io/ios-app/apps.json"]
  I["每日 ~06:17 UTC"] --> B
  J["每週一"] --> K["dependabot<br/>swift + actions"]
```

## 資料流

```mermaid
sequenceDiagram
  participant Tag as v* 標籤
  participant Test as test:sast
  participant Build as build:ipa
  participant Rel as release:ipa
  participant GH as GitHub Release
  participant Pages as deploy:docs
  participant Gen as generate_apps_json.sh
  participant Site as Pages 站台
  Tag->>Test: 觸發(測試 + CodeQL)
  Test->>Build: 測試通過
  Build->>Build: xcodebuild archive(未簽署)
  Build-->>Rel: Artifacts(.ipa + manifest.json + .dSYM.zip)
  Rel->>GH: gh release create + upload
  GH-->>Pages: 最新 Release 資產
  Pages->>Gen: 下載 manifest.json + 讀 Release 資產
  Gen-->>Pages: apps.json(asset-sourced)
  Pages->>Site: deploy-pages
```

建置物從 Xcode 流到使用者裝置，中繼資料全程 **asset-sourced**（取自實際建置物，非猜測）。

## 元件清單

| 元件 | 檔案 | 職責 |
| --- | --- | --- |
| 建置腳本 | `../codebase/scripts/build_unsigned_ipa.sh` | 未簽署封存、打包 IPA（`Payload/` 在根目錄）、產出 dSYM.zip 與 manifest.json、驗證版面。 |
| 饋流產生器 | `../codebase/scripts/generate_apps_json.sh` | 從最新 Release 資產 + `app.config.json` 重建 AltStore/SideStore `apps.json`。 |
| 饋流設定 | `../codebase/app.config.json` | 饋流層級中繼資料（名稱、描述、網站、圖示、開發者）。 |
| Pages 入口網站 | `../codebase/site/index.html`, `style.css` | 靜態站台，client-side fetch `apps.json` 渲染最新版本與下載連結，並列出所有側載方式步驟。 |
| SwiftLint 設定 | `../codebase/.swiftlint.yml` | Swift 風格規則。 |
| `test:sast` | `../.github/workflows/cicd.yml` | 模擬器測試（未簽署、`-enableCodeCoverage YES`）+ CodeQL Swift（`build-mode: manual`）。 |
| `build:ipa` | `../.github/workflows/cicd.yml` | 建置未簽署 IPA，上傳 `unsigned-ipa` / `dsym` / `test-results` 成品。 |
| `release:ipa` | `../.github/workflows/cicd.yml` | `v*` tag 觸發；下載成品、`gh release create --generate-notes`、上傳資產。 |
| `deploy:docs` | `../.github/workflows/cicd.yml` | 暫存站台、產生 `apps.json`、`upload-pages-artifact` + `deploy-pages`。 |
| 健康檢查 | `cicd.yml` 每日排程 | 每日 cron 執行 `test:sast` → `build:ipa`，偵測 runner/Xcode 版本漂移。 |
| Dependabot | `../.github/dependabot.yml` | `swift` + `github-actions` 生態系，每週一更新。 |

**設計原則：** 未簽署四旗標每次必帶(ADR-001);版本/bundleID/大小/日期 asset-sourced(ADR-003);單一 workflow、多 job、固定 `階段:產物` 命名(ADR-005);僅 `GITHUB_TOKEN`、零 Apple 機密(ADR-006);以原生 Swift + SceneKit + Metal 實作(ADR-007);直接顯示 CSDI 真實網格並以離線簡化縮小檔案(ADR-008)。

## App 場景架構

App 支援兩種場景模式,可由 HUD 切換:

```mermaid
flowchart LR
  mode["場景模式 Picker"] -->|"程序化香港"| proc["VoxelSceneFactory<br/>ProceduralHKTerrain<br/>6 座山峰體素網格<br/>380 m 絕對高度巡航"]
  mode -->|"旺角實景 CSDI"| csdi["CSDIMongKokSceneFactory<br/>mongkok_11-NW-19B.glb<br/>179,811 三角面<br/>300 m AGL 貼地巡航"]
  proc --> cam["FlyCameraController<br/>CADisplayLink 環繞"]
  csdi --> cam
  cam --> scn["SceneKit SCNScene<br/>+ 攝影機節點"]
```

| 元件 | 檔案 | 職責 |
| --- | --- | --- |
| 程序化地形工廠 | `../codebase/app/HKMinecraftVoxel/VoxelSceneFactory.swift` | 6 座香港山峰的體素網格、天空、光照、霧。 |
| CSDI 旺角實景工廠 | `../codebase/app/HKMinecraftVoxel/CSDIMongKokSceneFactory.swift` | 載入 bundled glb(ModelIO)、組裝 SceneKit 場景。圖幅尺寸 751×600×258 m。 |
| 飛行攝影機 | `../codebase/app/HKMinecraftVoxel/FlyCameraController.swift` | CADisplayLink 環繞巡航;CSDI 模式以射線取樣地面高度維持 300 m AGL。 |
| CSDI 網格資源 | `../codebase/app/HKMinecraftVoxel/mongkok_11-NW-19B.glb` | LandsD 3D 視覺化地圖 OSGB 根 LOD,離線簡化為 glTF 二進位(7.5 MB)。 |
| 體素地形來源協定 | `../codebase/app/HKMinecraftVoxel/TerrainDataSource.swift` | 可插拔高度場介面;預設 `ProceduralHKTerrain`。 |

## 關聯文件

- 決策理由:`./adr.md`
- 介面契約:`./api.md`　·　欄位綱要:`./schema.md`　·　實體關係:`./erd.md`
