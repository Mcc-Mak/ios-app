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
using UnityEditor;
using UnityEngine;

/// <summary>
/// iOS 建置入口。由 batchmode -executeMethod BuildIOS.Export 呼叫。
/// </summary>
public static class BuildIOS
{
    // 從環境變數讀取設定(由 build_unity_ipa.sh 注入),提供回退預設值
    private const string DefaultBundleId    = "com.mccmak.hkgridvoxel";
    private const string DefaultVersion     = "1.0";
    private const string DefaultBuildNumber = "1";
    private const string DefaultMinIosVer   = "12.0";
    private const string DefaultProductName = "HKGridVoxel";
    private const string DefaultOutputDir   = "build/ios-xcode";

    /// <summary>
    /// 匯出 iOS Xcode 專案。batchmode 入口點。
    /// </summary>
    public static void Export()
    {
        ConfigurePlayerSettings();
        BuildPlayer();
    }

    /// <summary>
    /// 設定 iOS Player Settings(bundle ID、版本、最低 iOS 版本、IL2CPP、arm64)。
    /// </summary>
    private static void ConfigurePlayerSettings()
    {
        var bundleId    = System.Environment.GetEnvironmentVariable("IOS_BUNDLE_ID")    ?? DefaultBundleId;
        var version     = System.Environment.GetEnvironmentVariable("IOS_VERSION")      ?? DefaultVersion;
        var buildNum    = System.Environment.GetEnvironmentVariable("IOS_BUILD_NUMBER") ?? DefaultBuildNumber;
        var minIosVer   = System.Environment.GetEnvironmentVariable("IOS_MIN_VERSION")  ?? DefaultMinIosVer;
        var productName = System.Environment.GetEnvironmentVariable("IOS_PRODUCT_NAME") ?? DefaultProductName;

        // 產品名稱與版本
        PlayerSettings.productName      = productName;
        PlayerSettings.bundleVersion    = version;
        PlayerSettings.iOS.buildNumber  = buildNum;

        // Bundle Identifier(.applicationIdentifier 在 2022.x 使用「平台:識別碼」格式)
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, bundleId);

        // 最低 iOS 版本
        PlayerSettings.iOS.targetOSVersionString = minIosVer;

        // IL2CPP + arm64(iOS App Store 要求;sideload 亦需要 arm64)
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(BuildTargetGroup.iOS, (int)Architecture.Arm64);

        // 其他 iOS 設定
        PlayerSettings.iOS.requiresARKitSupport   = false;
        PlayerSettings.iOS.appInBackground        = iOSAppInBackgroundOption.Disabled;
        PlayerSettings.iOS.cameraUsageDescription = "";   // 本 App 不使用相機
        PlayerSettings.iOS.disableMetalAndEnforceOpenGL = false;  // 預設 Metal

        // 允許背景建置時的 HTTP 下載(glTFast 在 batchmode 不會實際下載,但避免警告)
        PlayerSettings.insecureHttpAllowed = true;

        Debug.Log($"[BuildIOS] Player Settings 已配置:");
        Debug.Log($"  productName     = {productName}");
        Debug.Log($"  bundleId        = {bundleId}");
        Debug.Log($"  version         = {version} (build {buildNum})");
        Debug.Log($"  minIOSVersion   = {minIosVer}");
        Debug.Log($"  scriptingBackend= IL2CPP");
        Debug.Log($"  architecture    = arm64");
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
