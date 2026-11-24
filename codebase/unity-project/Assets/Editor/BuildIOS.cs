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
        // v2.2.5: 匯出前確保 StreamingAssets/Chunks 為低解析度 IPA 版(若設定 IPA_CHUNKS_SOURCE_DIR)
        CopyIpaChunksIfNeeded();
        // 先更新場景(確保 TouchPlayerController 等最新元件已烘焙至 Main.unity)
        HKGrid.Editor.SetupScene.Build();
        ConfigurePlayerSettings();
        EnsureGltfFastShadersIncluded();  // v2.2.16: 防止 glTFast 著色器被 stripping 移除
        BuildPlayer();
    }

    // StreamingAssets/Chunks 的 Assets 相對路徑(供 IPA 分塊覆寫)
    private const string ChunksAssetPath = "Assets/StreamingAssets/Chunks";

    /// <summary>
    /// v2.2.5: 若設定 IPA_CHUNKS_SOURCE_DIR 環境變數,匯出前將該目錄的分塊資料
    /// (manifest.json + *.glb)複製到 StreamingAssets/Chunks,確保 IPA 使用低解析度版本,
    /// 不受本地 Editor Play 模式使用的高解析度分塊影響。未設定時沿用現有內容(向後相容)。
    /// </summary>
    private static void CopyIpaChunksIfNeeded()
    {
        var src = System.Environment.GetEnvironmentVariable("IPA_CHUNKS_SOURCE_DIR");
        if (string.IsNullOrEmpty(src) || !System.IO.Directory.Exists(src))
        {
            Debug.Log("[BuildIOS] IPA_CHUNKS_SOURCE_DIR 未設定或不存在,沿用現有 StreamingAssets/Chunks");
            return;
        }
        // 使用 Application.streamingAssetsPath(專案絕對路徑,不受 batchmode CWD 影響)
        string dst = System.IO.Path.Combine(Application.streamingAssetsPath, "Chunks");
        System.IO.Directory.CreateDirectory(dst);
        // 清空目的地舊檔(避免殘留高解析度 .glb)
        foreach (var f in System.IO.Directory.GetFiles(dst))
            System.IO.File.Delete(f);
        // 複製來源全部檔案(manifest.json + *.glb)
        int n = 0;
        long totalBytes = 0;
        foreach (var f in System.IO.Directory.GetFiles(src))
        {
            System.IO.File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), overwrite: true);
            n++;
            totalBytes += new System.IO.FileInfo(f).Length;
        }
        // 重新匯入,確保 BuildPipeline 使用最新資源
        AssetDatabase.ImportAsset(ChunksAssetPath, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        Debug.Log($"[BuildIOS] 已複製 {n} 個 IPA 分塊檔至 StreamingAssets/Chunks " +
                  $"({totalBytes / 1024.0 / 1024.0:F1} MB,來源: {src})");
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
    /// v2.2.16: 確保 glTFast 著色器包含於建置中。
    ///
    /// 問題:iOS 上 glTFast 回報 "ShaderMissing:glTF/PbrMetallicRoughness",
    /// 隨後 BuiltInMaterialGenerator.GenerateDefaultMaterial 擲回 NullReferenceException。
    /// 根因:glTFast 著色器經 Shader.Find() 動態查詢,Unity 建置管線視為無引用 →
    /// 由 engine code stripping 移除。link.xml 僅保留 managed 組件,不涵蓋著色器資產。
    ///
    /// 修正:建置前將 glTFast 著色器加入 GraphicsSettings 的 Always Included Shaders
    /// 清單,確保著色器資產保留於建置。AddAlwaysIncludedShader 為內部方法,以 reflection 呼叫。
    /// </summary>
    private static void EnsureGltfFastShadersIncluded()
    {
        // glTFast 6.0 內建著色器清單(BuiltInMaterialGenerator 所引用)
        string[] shaderNames = {
            "glTF/PbrMetallicRoughness",
            "glTF/PbrSpecularGlossiness",
            "glTF/Unlit",
            "glTF/Points",
            "glTF/Point"
        };

        var shaderUtilType = System.Type.GetType("UnityEditor.ShaderUtil,UnityEditor");
        if (shaderUtilType == null)
        {
            Debug.LogError("[BuildIOS] 找不到 UnityEditor.ShaderUtil 型別,無法加入 Always Included Shaders");
            return;
        }
        var addMethod = shaderUtilType.GetMethod(
            "AddAlwaysIncludedShader",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (addMethod == null)
        {
            Debug.LogError("[BuildIOS] 找不到 ShaderUtil.AddAlwaysIncludedShader 方法");
            return;
        }

        int added = 0;
        foreach (var name in shaderNames)
        {
            var shader = Shader.Find(name);
            if (shader != null)
            {
                addMethod.Invoke(null, new object[] { shader });
                Debug.Log($"[BuildIOS] 已加入 Always Included Shader: {name}");
                added++;
            }
            else
            {
                Debug.LogWarning($"[BuildIOS] 找不到著色器(可能未安裝或名稱不同): {name}");
            }
        }
        Debug.Log($"[BuildIOS] glTFast 著色器保護:已加入 {added}/{shaderNames.Length} 個至 Always Included Shaders");
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
