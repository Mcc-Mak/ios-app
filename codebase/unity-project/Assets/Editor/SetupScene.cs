// SetupScene.cs — Stage B 場景自動化:建立入口場景 + ChunkStreamConfig 資產。
//
// 由 batchmode 執行:
//   Tuanjie.exe -batchmode -nographics -projectPath <proj> -logFile <log> \
//     -executeMethod HKGrid.Editor.SetupScene.Build -quit
//
// 冪等(re-runnable):已存在的資產/物件會更新而非重複建立。
//
// 產出:
//   Assets/Scenes/Main.unity          — 入口場景(Camera + Bootstrapper + TouchPlayerController)
//   Assets/Configs/ChunkStreamConfig.asset — 串流器設定資產
//   ProjectSettings/EditorBuildSettings.asset — Main.unity 設為 scene 0

using HKGrid.Streaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HKGrid.Editor
{
    public static class SetupScene
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string SceneDir = "Assets/Scenes";
        private const string ConfigPath = "Assets/Configs/ChunkStreamConfig.asset";
        private const string ConfigDir = "Assets/Configs";

        /// <summary>batchmode 進入點。建立/更新場景與 Config 資產。</summary>
        [MenuItem("HKGrid/重建場景 (SetupScene.Build)")]
        public static void Build()
        {
            Debug.Log("[SetupScene] 開始自動化場景建置");

            // 1. 建立 Config 資產(若不存在)
            if (!AssetDatabase.IsValidFolder(ConfigDir))
            {
                AssetDatabase.CreateFolder("Assets", "Configs");
            }
            var config = AssetDatabase.LoadAssetAtPath<ChunkStreamConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ChunkStreamConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                Debug.Log($"[SetupScene] 建立 ChunkStreamConfig: {ConfigPath}");
            }
            else
            {
                Debug.Log($"[SetupScene] 既有 ChunkStreamConfig: {ConfigPath}(保留設定)");
            }

            // 2. 建立場景目錄
            if (!AssetDatabase.IsValidFolder(SceneDir))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            // 3. 開啟既有 Main.unity 或建立新場景
            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 4. 主攝影機(若場景內無)
            Camera cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.5f, 0.7f, 0.9f, 1f); // 淡藍天色
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = 450f;  // v2.2.3: 限制可視距離(原 2000m)
                cam.transform.position = new Vector3(0f, 80f, 0f); // 圖幅中心上空 80m(Y-up)
                cam.transform.rotation = Quaternion.Euler(60f, 0f, 0f); // 俯視
                Debug.Log("[SetupScene] 建立 Main Camera");
            }

            // 5. 掛 Bootstrapper(若攝影機上無)
            var boot = cam.GetComponent<ChunkStreamBootstrapper>();
            if (boot == null)
            {
                boot = cam.gameObject.AddComponent<ChunkStreamBootstrapper>();
                Debug.Log("[SetupScene] 掛上 ChunkStreamBootstrapper");
            }
            boot.config = config;
            boot.camera = cam;
            // 不由 Bootstrapper 覆寫起始位置;TouchPlayerController.Awake 負責定位
            // (舊版 overrideStartPos=true 會與 TouchPlayerController 競爭 Awake 順序)
            boot.overrideStartPos = false;
            // v2.2.8: 恢復原始 manifest 中心自動定位(放棄手動地下座標)
            boot.useManualStartWorldPos = false;

            // 6. 掛 TouchPlayerController(觸控 FPS;桌面自動回退 WASD)。
            var player = cam.GetComponent<TouchPlayerController>();
            if (player == null)
            {
                player = cam.gameObject.AddComponent<TouchPlayerController>();
                Debug.Log("[SetupScene] 掛上 TouchPlayerController");
            }

            // 6a. 確保 CharacterController 存在(TouchPlayerController 需要它處理建物碰撞)
            var cc = cam.GetComponent<CharacterController>();
            if (cc == null)
            {
                cc = cam.gameObject.AddComponent<CharacterController>();
                Debug.Log("[SetupScene] 掛上 CharacterController");
            }

            // 6b. 強制更新 TouchPlayerController 欄位(覆寫舊場景殘留的預設值)
            //     v2.2.8: 恢復原始值(放棄 v2.2.6 手動位置/視角);moveSpeed 保留 1.75x
            player.groundRaycastHeight = 2f;
            player.groundRaycastLength = 250f;
            player.maxYSnapPerFrame = 80f;
            player.eyeHeight = 1.7f;
            player.startPitch = 55f;          // v2.2.8: 恢復原始(俯視城市)
            player.startYaw = 0f;             // v2.2.8: 恢復原始
            player.defaultFov = 60f;          // v2.2.8: 恢復原始
            player.moveSpeed = 8.75f;         // v2.2.6: 1.75x(使用者要求保留)
            player.lookJoystickSpeed = 120f;  // v2.6.0: 右側視角搖桿靈敏度
            player.jumpCooldown = 0f;         // v2.6.7: 停用跳躍冷卻(著地即可再跳)
            EditorUtility.SetDirty(player);

            // 6b-2. v2.2.15: 掛上 DebugOverlay(iOS 螢幕診斷;Editor 中也可顯示)
            var dbg = cam.GetComponent<HKGrid.Debugging.DebugOverlay>();
            if (dbg == null)
            {
                dbg = cam.gameObject.AddComponent<HKGrid.Debugging.DebugOverlay>();
                Debug.Log("[SetupScene] 掛上 DebugOverlay(iOS 螢幕診斷)");
            }
            // v2.6.5: 預設隱藏診斷覆蓋層(畫面乾淨);齒輪按鈕仍永遠顯示可供開啟。
            dbg.show = false;
            EditorUtility.SetDirty(dbg);

            // 6c. 強制更新 Config 值(確保舊資產也套用新參數)。
            // ⚠ 此處為建置權威來源——.asset 磁碟編輯會被此處覆寫。
            // v2.6.3: groundFillEnabled 改為 false(真實網格碰撞器取代 GroundFill)。
            //   舊版 v2.2.9 在此硬編 groundFillEnabled=true,導致每次匯出 Xcode 專案時
            //   覆寫 .asset 的 false 回 true → IPA 仍啟用 GroundFill(可見橄欖綠偽地面、
            //   建物頂點污染、邊界空隙)。此為 v2.6.3 IPA「未遵循磁碟設定」的根因。
            // v2.2.3: loadRadius 1000→400、unloadRadius 1350→550(限制可視範圍,降低 iPhone 負載)
            config.loadRadius = 400f;
            config.unloadRadius = 550f;
            config.cameraFarClip = 450f;
            config.gltfFrameBudget = 0.15f;
            config.addChunkColliders = true;   // v2.6.3: 真實攝影測量網格 MeshCollider(地面跟隨射線命中真實地形)
            config.forceDoubleSided = true;    // v2.2.5: 雙面渲染(Cull Off)
            config.groundFillEnabled = false;  // v2.6.3: 停用 GroundFill(偽地面三 bug:邊界空隙/建物污染/可見平面)
            config.groundFillBelowOffset = 0.05f;  // v2.2.14: 0.5→0.05(供未來重新啟用 GroundFill 時使用)
            config.groundFillSampleResolution = 8;  // v2.2.12: 8×8 取樣(供未來重新啟用時使用)
            // v2.6.3: 確保預載入 + spawn 圖幅設定(避免舊 .asset 殘留空值)
            // v2.6.7: spawn 點改為 6-SE-21A(五圖幅最西側,玩家出生於 21A,可向東進入 21B→22A)
            config.spawnTileCode = "6-SE-21A";
            config.preloadAllChunks = true;
            config.keepAllChunksAfterPreload = true;
            config.preloadDeferBudget = 0.5f;
            EditorUtility.SetDirty(config);
            Debug.Log($"[SetupScene] ChunkStreamConfig 強制設定: addChunkColliders={config.addChunkColliders}, " +
                      $"groundFillEnabled={config.groundFillEnabled}, spawnTileCode={config.spawnTileCode}, " +
                      $"preloadAllChunks={config.preloadAllChunks}, keepAllChunksAfterPreload={config.keepAllChunksAfterPreload}");

            // 7. 方向光(若場景內無,讓網格有基本照明)
            Light dir = Object.FindObjectOfType<Light>();
            if (dir == null)
            {
                var lightGo = new GameObject("Directional Light");
                dir = lightGo.AddComponent<Light>();
                dir.type = LightType.Directional;
                dir.color = Color.white;
                dir.intensity = 1.2f;
                dir.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                Debug.Log("[SetupScene] 建立方向光");
            }

            // 7a. v2.2.3: 霧設定(隱藏遠處分塊彈出,搭配 cameraFarClip=450)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 350f;
            RenderSettings.fogEndDistance = 450f;
            RenderSettings.fogColor = new Color(0.5f, 0.7f, 0.9f, 1f); // 淡藍天色
            Debug.Log("[SetupScene] 霧設定: 350-450m 線性");

            // 8. 儲存場景
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SetupScene] 場景儲存: {ScenePath}");

            // 9. 加入 BuildSettings 並設為 scene 0(若未在清單中)
            var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            bool found = false;
            foreach (var s in buildScenes)
            {
                if (s.path == ScenePath)
                {
                    s.enabled = true;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }
            // 確保 Main.unity 為 scene 0
            buildScenes.Sort((a, b) =>
                a.path == ScenePath ? -1 : (b.path == ScenePath ? 1 : 0));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log("[SetupScene] EditorBuildSettings 已更新(Main.unity = scene 0)");

            AssetDatabase.SaveAssets();
            Debug.Log("[SetupScene] 完成");
        }
    }
}
