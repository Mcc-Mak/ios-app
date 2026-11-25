# 文件目錄與導覽

本目錄（`docbase/`）收錄 ios-app 專案的所有文件。內容以繁體中文撰寫，技術專有名詞保留英文。路徑皆相對於 `docbase/`。

```mermaid
flowchart TD
  root["docbase/"] --> plan["規劃類"]
  root --> tech["技術類"]
  root --> ops["營運/參考類"]
  plan --> toctree["toctree（本文件）"]
  plan --> charter["charter 章程"]
  plan --> srs["srs 需求規格"]
  plan --> pdr["pdr 設計審查"]
  plan --> prd["prd 產品需求"]
  plan --> us["user-stories 使用者故事"]
  tech --> adr["adr 決策紀錄"]
  tech --> arch["architecture 架構"]
  tech --> api["api 介面契約"]
  tech --> uiux["uiux 介面/體驗"]
  tech --> schema["schema 綱要"]
  tech --> erd["erd 實體關係"]
  ops --> setup["setup 環境設定"]
  ops --> conf["configurations 設定參考"]
  ops --> cicd["cicd CI/CD 參考"]
  ops --> rtm["rtm 需求追溯"]
  ops --> crm["crm 交叉參考"]
  ops --> qs["quick-start 快速入門"]
```

## docbase 文件

| 文件 | 說明 | 路徑 |
| --- | --- | --- |
| toctree | 文件目錄與導覽中樞（本文件）。 | [`文件目錄與導覽中樞`](./toctree.md) |
| charter | 專案章程：目的、範圍、目標、成功標準、關係人、時間表。 | [`專案章程`](./charter.md) |
| srs | 軟體需求規格：將六個 pipeline 階段對應至功能性與非功能性需求。 | [`軟體需求規格`](./srs.md) |
| pdr | 初步設計審查：關鍵設計決策、理由、取捨與被否決的替代方案。 | [`初步設計審查`](./pdr.md) |
| prd | 產品需求文件：以「側載發行系統」為產品，定義角色與功能驗收標準。 | [`產品需求文件`](./prd.md) |
| user-stories | 使用者故事：依角色分組，附驗收條件。 | [`使用者故事`](./user-stories.md) |
| adr | 架構決策紀錄（Architecture Decision Records）。 | [`架構決策紀錄`](./adr.md) |
| architecture | 系統架構總覽與元件互動。 | [`系統架構總覽`](./architecture.md) |
| api | 自動化腳本與產出物（manifest.json、apps.json）的介面說明。 | [`介面說明`](./api.md) |
| uiux | Pages 入口網站的介面與體驗設計。 | [`介面與體驗設計`](./uiux.md) |
| schema | apps.json 與 manifest.json 的欄位結構定義。 | [`欄位結構定義`](./schema.md) |
| erd | 資料模型關係（發行、成品、饋流之間的關聯）。 | [`資料模型關係`](./erd.md) |
| setup | 本機開發與前置作業說明。 | [`本機開發與前置作業`](./setup.md) |
| configurations | 建置設定、Xcode scheme、Dependabot 等組態。 | [`組態設定`](./configurations.md) |
| cicd | 單一工作流程的 job 流程、觸發條件與 apps.json 機制詳解。 | [`CI/CD 工作流程`](./cicd.md) |
| rtm | 需求追溯矩陣（Requirements Traceability Matrix）。 | [`需求追溯矩陣`](./rtm.md) |
| crm | 交叉參考矩陣（Cross-Reference Matrix）：文件與需求/實作間的對應。 | [`交叉參考矩陣`](./crm.md) |
| quick-start | 快速上手：從 clone 到觸發首次發行的最短路徑。 | [`快速上手`](./quick-start.md) |

## 根目錄文件

| 文件 | 說明 | 路徑 |
| --- | --- | --- |
| AGENTS.md | 給 OpenCode agent 的硬性限制與規範（先讀再動 CI/CD）。 | `../AGENTS.md` |
| README.md | 專案簡介與側載教學（單一來源）。 | `../README.md` |
| CHANGELOG.md | semver 版本變更紀錄（`vX.X.X`）。 | `../CHANGELOG.md` |

## 關鍵實作路徑

- 建置腳本：`../codebase/scripts/build_unity_ipa.sh`（三模式：預設全管線、`UNITY_EXPORT_ONLY=1`、`XCODEBUILD_ONLY=1`）
- 饋流產生：`../codebase/scripts/generate_apps_json.sh`
- 單一工作流程：`../.github/workflows/cicd.yml`（`build:ipa` → `deploy:docs`；`test:sast` 已移除、`release:ipa` 已併入 `build:ipa`）
