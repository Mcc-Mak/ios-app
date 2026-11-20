// ChunkStreamBootstrapper.cs — 場景入口:載入 manifest、建立串流器、每幀驅動。
//
// 使用方式:在入口場景建立空 GameObject,掛上此元件,於 Inspector 指派:
//   - Config:ChunkStreamConfig 資產(Assets > Create > HKGrid > ChunkStreamConfig)
//   - Main Camera:主攝影機
// 執行時自動:
//   1. 從 StreamingAssets/Chunks/manifest.json 載入 manifest
//   2. 建立 worldRoot GameObject(套用 Z-up→Y-up 旋轉)
//   3. 設定全域 glTFast defer agent(TimeBudgetPerFrameDeferAgent)
//   4. 建立 ChunkStreamer 並每幀 Tick()
//
// 鷹架(scaffold):未經 Unity 編譯驗證。安裝 Unity 6 LTS 後首次開啟需驗證。

using System.Collections;
using System.IO;
using GLTFast;
using UnityEngine;

namespace HKGrid.Streaming
{
    [RequireComponent(typeof(Camera))]
    public class ChunkStreamBootstrapper : MonoBehaviour
    {
        [Header("設定")]
        public ChunkStreamConfig config;
        public new Camera camera;

        [Header("選用:覆寫攝影機起始位置(本地網格座標,公尺)")]
        public bool overrideStartPos = false;
        public Vector3 startLocalPos = new Vector3(0f, 0f, 80f); // 預設圖幅中心上空 80m(Z-up 本地座標)

        private ChunkStreamer _streamer;
        private Transform _worldRoot;
        private bool _ready;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[Bootstrapper] 未指派 ChunkStreamConfig");
                enabled = false;
                return;
            }
            if (camera == null) camera = GetComponent<Camera>();

            // 建立全域 defer agent(TimeBudgetPerFrameDeferAgent)給所有 GltfImport 共用
            var deferGo = new GameObject("glTFast-DeferAgent");
            deferGo.transform.SetParent(transform);
            var deferAgent = deferGo.AddComponent<TimeBudgetPerFrameDeferAgent>();
            // TimeBudgetPerFrameDeferAgent 的 frameBudget 可由 SetFrameBudget(float) 調整;
            // 此處透過 config.gltfFrameBudget 於 Start 中套用。
            // SetDefaultDeferAgent 讓後續 new GltfImport(...) 自動套用此 agent。
            GltfImport.SetDefaultDeferAgent(deferAgent);

            // 設定目標幀率,讓 TimeBudgetPerFrameDeferAgent 的 frameBudget(幀時間比例)有意義。
            // 預設 -1 會讓 agent 退回 30 FPS 假設;設為 30 讓 0.2 = 6.6ms 預算。
            Application.targetFrameRate = 30;

            // 建立 worldRoot
            _worldRoot = new GameObject("HKGridWorld").transform;
            _worldRoot.position = Vector3.zero;

            // 起始攝影機位置(若覆寫):local→world 轉換
            if (overrideStartPos)
            {
                Quaternion yup = config.applyZupToYupRotation
                    ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
                camera.transform.position = yup * startLocalPos;
            }
        }

        private IEnumerator Start()
        {
            string manifestPath = Path.Combine(
                Application.streamingAssetsPath, config.chunksRelativePath, config.manifestFileName);
            string url = "file://" + manifestPath.Replace('\\', '/');

            // iOS/Editor 透過 UnityWebRequest 讀 manifest(StreamingAssets 在 iOS 為唯讀封裝)
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Bootstrapper] manifest 載入失敗: {req.error}\n路徑: {manifestPath}");
                    yield break;
                }
                var manifest = JsonUtility.FromJson<ChunkManifest>(req.downloadHandler.text);
                Debug.Log($"[Bootstrapper] manifest 載入: {manifest.source}, {manifest.chunkCount} 分塊, " +
                          $"{manifest.totalBytes / 1e6f:F2} MB, {manifest.totalTriangles:N0} 三角面");
                Debug.Log($"[Bootstrapper] 授權: {manifest.attribution}");

                // 套用 defer agent frame budget
                // TimeBudgetPerFrameDeferAgent.frameBudget 為 private 欄位;
                // 透過公開方法 SetFrameBudget(float) 設定(參數為「目標幀時間的比例」,0.5 = 半幀)
                var da = FindObjectOfType<TimeBudgetPerFrameDeferAgent>();
                if (da != null)
                {
                    da.SetFrameBudget(config.gltfFrameBudget);
                }

                _streamer = new ChunkStreamer(manifest, config, _worldRoot, camera);

                // ── v2.2.3: 從 manifest 自動推導起始位置(不再硬編)──
                // 單圖幅(15B-only)manifest 原點 = 15B centroid;多圖幅(merged)原點 = 15A centroid。
                // 硬編 startPosition=(947,0,-124) 僅對多圖幅正確;單圖幅時攝影機落在圖幅外 → 無分塊 → 墜落。
                Vector3 startLocal;
                if (manifest.chunkCount <= 25)
                {
                    // 單圖幅:所有分塊 centroid 的平均 = manifest 中心
                    startLocal = Vector3.zero;
                    foreach (var c in manifest.chunks)
                        startLocal += c.CentroidVector;
                    startLocal /= manifest.chunks.Count;
                    Debug.Log($"[Bootstrapper] 單圖幅模式:起始位置 = manifest 中心 {startLocal}");
                }
                else
                {
                    // 多圖幅:15B 中心(九龍城區 WGS84 [22.3319, 114.1871] → manifest-local)
                    startLocal = new Vector3(947f, 0f, 124f);
                    Debug.Log($"[Bootstrapper] 多圖幅模式:起始位置 = 15B 中心 {startLocal}");
                }

                // 轉換至 Unity 世界座標(Z-up→Y-up)
                Quaternion yup = config.applyZupToYupRotation
                    ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
                Vector3 startWorld = yup * startLocal;

                // 設定攝影機並觸發 TouchPlayerController 初始地面搜尋
                var player = camera.GetComponent<TouchPlayerController>();
                if (player != null)
                {
                    player.OnSpawned(startWorld);
                }
                else
                {
                    camera.transform.position = startWorld + Vector3.up * 50f;
                }

                // v2.2.3: 設定可視距離 + 霧(隱藏遠處分塊彈出,降低渲染負載)
                camera.farClipPlane = config.cameraFarClip;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = config.cameraFarClip * 0.78f;  // ~350m
                RenderSettings.fogEndDistance = config.cameraFarClip;             // ~450m
                RenderSettings.fogColor = camera.backgroundColor;  // 與天色一致

                _ready = true;
            }
        }

        private void Update()
        {
            if (_ready) _streamer.Tick();
        }

        private void OnDestroy()
        {
            _streamer?.UnloadAll();
        }

        private void OnDrawGizmosSelected()
        {
            if (!config?.drawChunkGizmos ?? false) return;
            // 可在此繪製載入/卸載半徑;需 manifest 載入後才有意義,省略實作
        }
    }
}
