// SetupScene.cs — Stage B 場景自動化:建立入口場景 + ChunkStreamConfig 資產。
//
// 由 batchmode 執行:
//   Tuanjie.exe -batchmode -nographics -projectPath <proj> -logFile <log> \
//     -executeMethod HKGrid.Editor.SetupScene.Build -quit
//
// 冪等(re-runnable):已存在的資產/物件會更新而非重複建立。
//
// 產出:
//   Assets/Scenes/Main.unity          — 入口場景(Camera + Bootstrapper + FlyCamera)
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
                cam.farClipPlane = 2000f;
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

            // 6. 掛 TouchPlayerController(觸控 FPS;桌面自動回退 WASD)。移除舊版 FlyCameraController。
            var oldFly = cam.GetComponent<FlyCameraController>();
            if (oldFly != null)
            {
                Object.DestroyImmediate(oldFly);
                Debug.Log("[SetupScene] 移除舊版 FlyCameraController");
            }
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

            // 6b. 強制更新 TouchPlayerController 欄位(覆寫舊場景殘留的 v2.2.0 預設值)
            //     舊值 groundRaycastHeight=100 會讓射線從 100m 高處打下,打到屋頂/自身膠囊
            //     →「看上方=飄起、看下方=墜落」問題
            player.groundRaycastHeight = 2f;
            player.groundRaycastLength = 50f;
            player.startPosition = new Vector3(947f, 0f, -124f);
            player.startPitch = 25f;
            player.startYaw = 0f;
            EditorUtility.SetDirty(player);

            // 6c. 強制更新 Config 值(確保舊資產也套用新參數)
            config.loadRadius = 1000f;
            config.unloadRadius = 1350f;
            config.addChunkColliders = true;
            EditorUtility.SetDirty(config);

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
