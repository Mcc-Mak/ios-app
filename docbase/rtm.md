# 需求追溯矩陣 (RTM)

本矩陣將功能需求 (FR) 與非功能需求 (NFR) 對應到實作位置與驗證方式，確保每項需求皆被涵蓋且可驗證。

```mermaid
flowchart LR
  subgraph FR["功能需求"]
    FR1["FR-1 測試"] --> CI["ci.yml test job"]
    FR2["FR-2 建置 IPA"] --> BUILD["build_unsigned_ipa.sh<br/>+ ci.yml build-ipa"]
    FR3["FR-3 上傳成品"] --> UP["ci.yml upload-artifact"]
    FR4["FR-4 發行"] --> REL["release.yml"]
    FR5["FR-5 Pages+饋流"] --> PAGES["pages.yml<br/>+ generate_apps_json.sh<br/>+ site/"]
    FR6["FR-6 監控"] --> MON["codeql + health + dependabot"]
  end
  subgraph NFR["非功能需求"]
    NFR1["NFR-1 零機密"] -.-> ALL["所有 workflow 僅 GITHUB_TOKEN"]
    NFR2["NFR-2 確定性打包"] -.-> BUILD
    NFR3["NFR-3 7 天週期"] -.-> PAGES
    NFR4["NFR-4 可重現建置"] -.-> BUILD
  end
```

## 功能需求

| 需求ID | 需求說明 | 實作位置 | 驗證方式 |
|--------|---------|---------|---------|
| FR-1 | 模擬器測試 + 覆蓋率 | `../.github/workflows/ci.yml`（test job） | `xcodebuild test` 通過；`xcrun xccov view --report --json` 產生覆蓋率報告 |
| FR-2 | 建置未簽署 IPA | `../codebase/scripts/build_unsigned_ipa.sh` + `ci.yml` build-ipa job | IPA 產出；四個未簽署旗標齊備；`ditto` 保留符號連結；`unzip -l` 顯示 `Payload/<App>.app` 在 zip 根目錄 |
| FR-3 | 上傳建置成品 | `ci.yml` upload-artifact steps | Artifacts 含 `.ipa` + `.dSYM.zip` + `manifest.json`，可下載 |
| FR-4 | 發行版本 | `../.github/workflows/release.yml` | `v*` tag 觸發；`gh release create --generate-notes --verify-tag` 產生 Release，asset 含 `.ipa`/`.dSYM`/`manifest.json` |
| FR-5 | Pages 入口與 feed | `../.github/workflows/pages.yml` + `../codebase/scripts/generate_apps_json.sh` + `../codebase/site/` | https://mcc-mak.github.io/ios-app/ 部署成功；`apps.json` 可達且欄位完整 |
| FR-6 | 持續監控 | `../.github/workflows/codeql.yml` + `../.github/workflows/health.yml` + `../.github/dependabot.yml` | CodeQL 掃描無新增警示；每日 cron 建置通過；Dependabot 每週提交更新 PR |

## 非功能需求

| 需求ID | 需求說明 | 實作位置 | 驗證方式 |
|--------|---------|---------|---------|
| NFR-1 | 零 Apple 機密 | 所有 workflow 僅用 `GITHUB_TOKEN` | repo Settings → Secrets 無任何 Apple 憑證/profile/key；fork 後零設定即可運作 |
| NFR-2 | 確定性打包 | `build_unsigned_ipa.sh`：`ditto` 複製 `.app` 保留符號連結，`cd` 到含 `Payload/` 的目錄再 `zip -r` | `unzip -l <App>-<Ver>.ipa` 列出 `Payload/<App>.app/...` 在 zip 根目錄；AltStore/Sideloadly 接受 |
| NFR-3 | 7 天憑證週期對應 | Release asset download URL + `apps.json` feed | `downloadURL` 指向 `https://github.com/Mcc-Mak/ios-app/releases/download/<tag>/<App>-<Ver>.ipa`；使用者可於 7 天內透過 feed 重新安裝/刷新 |
| NFR-4 | 可重現建置 | `build_unsigned_ipa.sh` 由 `Info.plist` 推導名稱/版本，不自動猜多專案 | 多於一個 `.xcodeproj`/`.xcworkspace` 時 fail fast；`<App>`/`<Ver>` 來自 `CFBundleName`/`CFBundleShortVersionString` |

## 涵蓋摘要

- 全部 6 項 FR 與 4 項 NFR 皆有明確實作位置與驗證方式。
- 無遺漏需求；無未實作需求。

相關檔案：`../codebase/scripts/build_unsigned_ipa.sh`、`../codebase/scripts/generate_apps_json.sh`、`../.github/workflows/`、`../codebase/site/`。
