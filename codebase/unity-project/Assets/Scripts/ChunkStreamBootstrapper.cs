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
using System.Collections.Generic;
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

        [Header("v2.2.6: 手動起始位置(Unity 世界座標)")]
        [Tooltip("啟用後使用 manualStartWorldPos 的 X/Z 作為攝影機起始位置;" +
                 "Y 由 OnSpawned 射線找地面後決定(groundY + eyeHeight)。" +
                 "用於跳至特定觀察點(例如按 L 鍵記錄的座標);停用則用 manifest 中心。")]
        public bool useManualStartWorldPos = false;
        [Tooltip("攝影機起始位置(Unity 世界座標)。v2.2.8: Y=-65.44 為精確座標(按 L 鍵記錄)。")]
        public Vector3 manualStartWorldPos = new Vector3(-296.90f, -65.44f, 10.37f);

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

                // v2.2.6: 手動起始位置覆寫(Unity 世界座標)。
                // v2.2.8: Y 也使用精確座標(-65.44),由 TouchPlayerController.OnSpawned 直接採用。
                if (useManualStartWorldPos)
                {
                    startWorld.x = manualStartWorldPos.x;
                    startWorld.y = manualStartWorldPos.y;
                    startWorld.z = manualStartWorldPos.z;
                    Debug.Log($"[Bootstrapper] 手動起始位置覆寫(精確): {startWorld}");
                }

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

                // v2.2.9: 人工地面填補(修補攝影測量洞孔)
                CreateGroundFill(manifest);

                _ready = true;
            }
        }

        /// <summary>
        /// v2.2.9: 人工地面填補——以 IDW 插值高度場網格跟隨地形起伏,修補攝影測量洞孔。
        /// v2.2.10: 改用插值高度場(非單一平坦平面),修正:
        ///   (1) 平面高度錯誤(用 centroid_z 而非 bbox_min_z,導致平面過高、埋沒建物)
        ///   (2) 假設全區海拔不變(單一平坦 Y,忽略地形高低差)
        /// 以各分塊 bbox_min[2](最低地形 Z)為控制點,IDW(power=2)插值產生高度場。
        /// 網格有 MeshCollider(地面跟隨射線命中,玩家不穿洞墜落)+
        /// 地面色材質(洞孔可見時看到地面色而非天空)。
        /// 在世界空間(非 worldRoot 下,避免 Z-up→Y-up 旋轉)。
        /// </summary>
        private void CreateGroundFill(ChunkManifest manifest)
        {
            if (!config.groundFillEnabled || manifest.chunks == null || manifest.chunks.Count == 0)
                return;

            // 收集插值控制點:每分塊的 (centroid X, centroid Y, bbox_min Z=最低地形)
            var samples = new List<(float x, float y, float elev)>();
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var c in manifest.chunks)
            {
                if (c.centroid == null || c.centroid.Length < 2) continue;
                // 高度樣本:優先 bbox_min[2](最低地形),退回 centroid[2]
                float elev;
                if (c.bbox_min != null && c.bbox_min.Length >= 3)
                    elev = c.bbox_min[2];
                else if (c.centroid.Length >= 3)
                    elev = c.centroid[2];
                else
                    continue;

                samples.Add((c.centroid[0], c.centroid[1], elev));

                // 邊界用 bbox(更精確的覆蓋範圍)
                if (c.bbox_min != null && c.bbox_min.Length >= 2)
                {
                    if (c.bbox_min[0] < minX) minX = c.bbox_min[0];
                    if (c.bbox_min[1] < minY) minY = c.bbox_min[1];
                }
                if (c.bbox_max != null && c.bbox_max.Length >= 2)
                {
                    if (c.bbox_max[0] > maxX) maxX = c.bbox_max[0];
                    if (c.bbox_max[1] > maxY) maxY = c.bbox_max[1];
                }
            }
            if (samples.Count == 0) return;

            // 外擴 margin 確保覆蓋可視範圍
            minX -= config.groundFillMargin;
            maxX += config.groundFillMargin;
            minY -= config.groundFillMargin;
            maxY += config.groundFillMargin;

            // 建立插值高度場網格
            int res = Mathf.Max(2, config.groundFillGridResolution);
            int vCount = (res + 1) * (res + 1);
            var vertices = new Vector3[vCount];
            var uvs = new Vector2[vCount];

            for (int j = 0; j <= res; j++)
            {
                for (int i = 0; i <= res; i++)
                {
                    int idx = j * (res + 1) + i;
                    // manifest 座標
                    float mx = minX + (maxX - minX) * i / res;
                    float my = minY + (maxY - minY) * j / res;
                    float mz = InterpolateHeight(samples, mx, my) - config.groundFillYOffset;
                    // manifest (X, Y, Z) → Unity world (X, Z, -Y)
                    vertices[idx] = new Vector3(mx, mz, -my);
                    uvs[idx] = new Vector2((float)i / res, (float)j / res);
                }
            }

            // 三角形索引(兩三角/格)
            var triangles = new int[res * res * 6];
            int t = 0;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    int v00 = j * (res + 1) + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + (res + 1);
                    int v11 = v01 + 1;
                    triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v11;
                    triangles[t++] = v00; triangles[t++] = v11; triangles[t++] = v10;
                }
            }

            var go = new GameObject("GroundFill");
            go.transform.SetParent(null);

            var mesh = new Mesh { name = "GroundFillMesh" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = config.groundFillColor;
            mat.SetInt("_Cull", 0);  // 雙面(與分塊一致)
            mr.sharedMaterial = mat;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;  // groundMask=~0,射線會命中

            // 統計
            float minH = float.MaxValue, maxH = float.MinValue;
            foreach (var v in vertices)
            {
                if (v.y < minH) minH = v.y;
                if (v.y > maxH) maxH = v.y;
            }
            Debug.Log($"[Bootstrapper] GroundFill 高度場建立: 控制點={samples.Count}, " +
                      $"網格={res}x{res}({vCount} 頂點), " +
                      $"高度範圍={minH:F1}~{maxH:F1}m(已偏移 {config.groundFillYOffset}m), " +
                      $"覆蓋={(maxX - minX):F0}x{(maxY - minY):F0}m");
        }

        /// <summary>
        /// IDW(power=2)反距離加權插值。以各分塊 bbox_min[2] 為控制點,
        /// 平滑插值出任意 (x, y) 處的地面高度。
        /// </summary>
        private static float InterpolateHeight(List<(float x, float y, float elev)> samples, float x, float y)
        {
            float weightSum = 0f, valueSum = 0f;
            foreach (var s in samples)
            {
                float dx = x - s.x;
                float dy = y - s.y;
                float d2 = dx * dx + dy * dy;
                if (d2 < 0.01f) return s.elev;  // 精確命中控制點
                float w = 1f / d2;  // IDW power=2
                weightSum += w;
                valueSum += s.elev * w;
            }
            return weightSum > 0f ? valueSum / weightSum : samples[0].elev;
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
