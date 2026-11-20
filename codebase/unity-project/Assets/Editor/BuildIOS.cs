// BuildIOS.cs — iOS 建置腳本(供 Tuanjie/Unity batchmode -executeMethod 呼叫)
//
// 用途:設定 iOS Player Settings 並匯出 Xcode 專案。
//   由 build_unity_ipa.sh 透過以下命令呼叫:
//     Tuanjie.exe -batchmode -nographics -projectPath <proj> \
//       -executeMethod BuildIOS.Export -logFile <log> -quit
//
// 匯出後由 build_unity_ipa.sh 接手:xcodebuild archive(未簽署)→ Payload/<App>.app → .ipa
//
// 注意:此腳本僅「匯出」Xcode 專案;實際編譯為 .ipa 需要 macOS + Xcode。
//
// 設計考量:
//   此腳本僅使用核心 UnityEditor API(BuildTarget.iOS、PlayerSettings 核心方法、
//   BuildPipeline),不依賴 iOS Build Support 模組提供的擴充 API
//   (PlayerSettings.iOS.*、Architecture 列舉等)。如此即使未安裝 iOS 模組,
//   專案仍可正常編譯(Play 模式可用);iOS 專屬設定(最低 iOS 版本、build number、
//   相機用途描述等)請於安裝 iOS 模組後,透過 Editor GUI 一次性設定,
//   並持久保存於 ProjectSettings/ProjectSettings.asset。
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// iOS 建置入口。由 batchmode -executeMethod BuildIOS.Export 呼叫。
/// </summary>
public static class BuildIOS
{
    // 從環境變數讀取設定(由 build_unity_ipa.sh 注入),提供回退預設值
    private const string DefaultBundleId    = "com.mccmak.hkgridvoxel";
    private const string DefaultVersion     = "1.0";
    private const string DefaultProductName = "HKGridVoxel";
    private const string DefaultOutputDir   = "build/ios-xcode";

    /// <summary>
    /// 匯出 iOS Xcode 專案。batchmode 入口點。
    /// </summary>
    public static void Export()
    {
        // 先更新場景(確保 TouchPlayerController 等最新元件已烘培至 Main.unity)
        HKGrid.Editor.SetupScene.Build();
        ConfigurePlayerSettings();
        BuildPlayer();
    }

    /// <summary>
    /// 設定 iOS Player Settings(僅使用核心 API,不依賴 iOS 模組擴充)。
    /// iOS 專屬設定(最低 iOS 版本、build number 等)由 Editor GUI 持久設定。
    /// </summary>
    private static void ConfigurePlayerSettings()
    {
        var bundleId    = System.Environment.GetEnvironmentVariable("IOS_BUNDLE_ID")    ?? DefaultBundleId;
        var version     = System.Environment.GetEnvironmentVariable("IOS_VERSION")      ?? DefaultVersion;
        var productName = System.Environment.GetEnvironmentVariable("IOS_PRODUCT_NAME") ?? DefaultProductName;

        // 產品名稱與版本(核心 API,不需 iOS 模組)
        PlayerSettings.productName   = productName;
        PlayerSettings.bundleVersion = version;

        // Bundle Identifier(SetApplicationIdentifier 為核心方法,BuildTargetGroup.iOS 為核心列舉值)
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, bundleId);

        // IL2CPP + arm64(iOS App Store 要求;sideload 亦需要 arm64)
        // SetScriptingBackend / SetArchitecture 皆為核心 PlayerSettings 方法;
        // architecture 參數為 int:0=None, 1=ARM64, 2=Universal(避免使用 iOS 模組的 Architecture 列舉)
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(BuildTargetGroup.iOS, 1);  // 1 = ARM64

        Debug.Log($"[BuildIOS] Player Settings 已配置(核心 API):");
        Debug.Log($"  productName     = {productName}");
        Debug.Log($"  bundleId        = {bundleId}");
        Debug.Log($"  version         = {version}");
        Debug.Log($"  scriptingBackend= IL2CPP");
        Debug.Log($"  architecture    = ARM64 (1)");
        Debug.Log("[BuildIOS] 注意:iOS 專屬設定(最低 iOS 版本、build number、相機用途描述)");
        Debug.Log("  請透過 Editor GUI(File → Build Settings → Player Settings)一次性設定,");
        Debug.Log("  會持久保存於 ProjectSettings/ProjectSettings.asset。");
    }

    /// <summary>
    /// 執行 BuildPipeline.BuildPlayer,匯出 Xcode 專案至輸出目錄。
    /// </summary>
    private static void BuildPlayer()
    {
        var outputDir = System.Environment.GetEnvironmentVariable("IOS_OUTPUT_DIR") ?? DefaultOutputDir;

        // 確保輸出目錄乾淨
        if (System.IO.Directory.Exists(outputDir))
        {
            System.IO.Directory.Delete(outputDir, recursive: true);
        }
        System.IO.Directory.CreateDirectory(outputDir);

        // 場景清單(只建置 Main 場景)
        string[] scenes = { "Assets/Scenes/Main.unity" };

        var buildOptions = BuildOptions.None;  // Release 建置,不附加 Dev 模式

        Debug.Log($"[BuildIOS] 開始匯出 Xcode 專案 → {outputDir}");

        var report = BuildPipeline.BuildPlayer(
            scenes,
            outputDir,
            BuildTarget.iOS,
            buildOptions);

        var summary = report.summary;
        Debug.Log($"[BuildIOS] 建置完成: result={summary.result}, " +
                  $"time={summary.totalTime.TotalSeconds:F1}s, " +
                  $"size={summary.totalSize / 1024 / 1024:F1}MB, " +
                  $"output={outputDir}");

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("[BuildIOS] ✅ 匯出成功");
        }
        else
        {
            Debug.LogError($"[BuildIOS] ❌ 匯出失敗: result={summary.result}, " +
                           $"errors={summary.totalErrors}, warnings={summary.totalWarnings}");
            // batchmode 時以非零結束碼結束(讓 build_unity_ipa.sh 能偵測失敗)
            UnityEditor.EditorApplication.Exit(1);
        }
    }
}
