# 設定參考

本文件彙整所有設定檔、環境變數與 workflow 觸發條件。Pipeline 僅用內建 `GITHUB_TOKEN`，**零 Apple 機密**（無憑證、無 provisioning profile、無 App Store Connect key；fork 後零設定即可運作）。

```mermaid
flowchart LR
  cfg["app.config.json"] -->|"饋流層中繼資料"| gen["generate_apps_json.sh"]
  manifest["manifest.json(Release 資產)"] -->|"asset-sourced 身分/版本"| gen
  gen --> apps["apps.json"]
  apps --> site["site/index.html<br/>client-side fetch"]
  site --> portal["GitHub Pages 入口"]
  swiftlint[".swiftlint.yml"] -.->|--config| build["build_unsigned_ipa.sh<br/>xcodebuild archive"]
  build -->|"寫入"| manifest
  dependabot["dependabot.yml"] -.->|"週更"| workflows[".github/workflows/*.yml"]
```

## 設定檔總覽

| 路徑 | 用途 | 關鍵欄位 |
|------|------|---------|
| `../codebase/app.config.json` | AltStore/SideStore feed metadata | `name`、`subtitle`、`description`、`website`、`iconURL`、`appName`、`bundleIdentifier`、`developerName` |
| `../codebase/.swiftlint.yml` | SwiftLint 規則 | `disabled_rules`、`opt_in_rules`、`included`、`line_length`；lint 時用 `--config codebase/.swiftlint.yml` |
| `../.github/dependabot.yml` | 依賴更新 | `ecosystems: swift` + `github-actions`；`schedule: weekly`（週一） |
| `../.gitignore` | 忽略建置產物 | `build/`、`*.ipa`、`*.dSYM.zip`、`DerivedData/`、`*.xcuserstate` |
| `../AGENTS.md` | agent 規範 | hard constraints、workflow 規範、IPA 布局規則 |

## `build_unsigned_ipa.sh` 環境變數覆寫

| 變數 | 預設 | 說明 |
|------|------|------|
| `SCHEME` / `PROJECT` / `WORKSPACE` | 自動探勘 | 指定 scheme / `.xcodeproj` / `.xcworkspace` |
| `IPA_NAME` / `DSYM_NAME` | `<App>-<Ver>` | 輸出檔名 |
| `CONFIGURATION` | `Release` | xcodebuild configuration |
| `ARCHIVE_DIR` | `build` | archive 輸出根目錄 |

`manifest.json` 欄位（資產來源中繼資料）：`appName`、`bundleIdentifier`、`version`、`ipaFileName`、`dsymFileName`，皆由 `Info.plist` 推導。

腳本寫入 `GITHUB_OUTPUT` 的鍵：`ipa-path`、`dsym-path`、`manifest-path`、`app-name`、`app-version`、`bundle-id`、`ipa-file-name`、`dsym-file-name`。

## `generate_apps_json.sh` 環境變數

| 變數 | 必要 | 說明 |
|------|------|------|
| `GITHUB_REPOSITORY` | 是 | `owner/repo` |
| `GITHUB_TOKEN` | 是 | 內建 token，查詢 Release asset |
| `APP_CONFIG` | 否 | 指向 `app.config.json`；腳本預設 `app.config.json`，`deploy:docs` 會傳入 `codebase/app.config.json` |

用法：`generate_apps_json.sh [output_path] [config_path]`；需 `gh` + `jq`（`GH_TOKEN` 或 `GITHUB_TOKEN`）。

## Workflow 觸發條件

| Job（`cicd.yml`） | 觸發 | Runner |
|----------|------|--------|
| `test:sast` | `push` / `pull_request` / 標籤 `v*` / 每日排程 / `workflow_dispatch` | `macos-latest` |
| `build:ipa` | 接續 `test:sast` 成功後 | `macos-latest` |
| `release:ipa` | `push` tag `v*`（semver）且 `build:ipa` 成功 | `ubuntu-latest` |
| `deploy:docs` | `push` / `workflow_dispatch`，且 `build:ipa` 成功；標籤事件需 `release:ipa` 成功 | `ubuntu-latest` |
| Dependabot | 每週一 | n/a |

相關檔案：`../codebase/scripts/build_unsigned_ipa.sh`、`../codebase/scripts/generate_apps_json.sh`、`../codebase/app.config.json`、`../codebase/.swiftlint.yml`、`../.github/dependabot.yml`、`../AGENTS.md`。
