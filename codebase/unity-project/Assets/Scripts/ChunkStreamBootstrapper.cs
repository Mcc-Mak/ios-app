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
        // v2.2.12: 逐塊地面填補(chunk id → fill GameObject)
        private readonly Dictionary<string, GameObject> _chunkFills = new Dictionary<string, GameObject>();

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

                // v2.2.12: 訂閱分塊載入/卸載事件,逐塊建立地面填補(取樣真實網格頂點)
                _streamer.OnChunkLoaded += OnChunkLoaded;
                _streamer.OnChunkUnloaded += OnChunkUnloaded;

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

                _ready = true;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // v2.2.12: 逐塊地面填補——取樣真實網格頂點,緊貼地形下方
        // ════════════════════════════════════════════════════════════════
        // 版本演進:
        //   v2.2.9:  單一平坦平面(Quad),用 min(centroid_z)。bug:過高埋沒建物。
        //   v2.2.10: IDW 插值 + bbox_min_z。bug:插值高於地形地板 → 「地面變天空」。
        //   v2.2.11: 雙線性插值 + 鉗制 bbox_min。bug:bbox_min 是全分塊最低點,
        //            分塊內地形高低差達 50-140m → 填補面在山頂下方 100m+ → 深坑困住玩家。
        //   v2.2.12: 逐塊取樣真實網格頂點。每分塊載入時,收集其所有 mesh 頂點
        //            (世界座標),按 (X,Z) 分格取每格最低 Y(=地面,非屋頂),
        //            空格(洞孔)由鄰居插值。填補面 = 地面Y - belowOffset(0.5m)。
        //            「有數據的位置不另創高度」= 填補面緊貼真實地面下方,不過高不過低。

        /// <summary>分塊載入完成:取樣真實網格建立逐塊地面填補。</summary>
        private void OnChunkLoaded(ChunkRecord rec, GameObject chunkGo)
        {
            if (!config.groundFillEnabled) return;
            CreateChunkFill(rec, chunkGo);
        }

        /// <summary>分塊卸載:銷毀對應的地面填補。</summary>
        private void OnChunkUnloaded(string chunkId)
        {
            if (_chunkFills.TryGetValue(chunkId, out var fill))
            {
                if (fill != null) Destroy(fill);
                _chunkFills.Remove(chunkId);
            }
        }

        /// <summary>
        /// 逐塊地面填補:取樣分塊的真實 mesh 頂點(世界座標),按 (X,Z) 分格,
        /// 每格取最低 Y(=地面高度),建立緊貼地面下方 belowOffset 的高度場網格。
        /// 空格(攝影測量洞孔)由鄰居插值填補。
        /// 在世界空間(非 worldRoot 下),有 MeshCollider + 雙面地面色材質。
        /// </summary>
        private void CreateChunkFill(ChunkRecord rec, GameObject chunkGo)
        {
            int res = Mathf.Max(2, config.groundFillSampleResolution);

            // ── 1. 收集所有 mesh 頂點(世界座標)+ 計算覆蓋範圍 ──
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float overallMinY = float.MaxValue;
            var verts = new List<Vector3>();

            foreach (var mf in chunkGo.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                var l2w = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var wv = l2w.MultiplyPoint(v);
                    verts.Add(wv);
                    if (wv.x < minX) minX = wv.x;
                    if (wv.x > maxX) maxX = wv.x;
                    if (wv.z < minZ) minZ = wv.z;
                    if (wv.z > maxZ) maxZ = wv.z;
                    if (wv.y < overallMinY) overallMinY = wv.y;
                }
            }
            if (verts.Count == 0) return;

            float sizeX = maxX - minX;
            float sizeZ = maxZ - minZ;
            if (sizeX < 0.1f || sizeZ < 0.1f) return;

            // ── 2. 分格:每格取最低 Y(=地面,非屋頂)──
            var groundY = new float[res, res];
            var hasData = new bool[res, res];
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                    groundY[i, j] = float.MaxValue;

            foreach (var v in verts)
            {
                int gi = Mathf.Clamp(Mathf.FloorToInt((v.x - minX) / sizeX * res), 0, res - 1);
                int gj = Mathf.Clamp(Mathf.FloorToInt((v.z - minZ) / sizeZ * res), 0, res - 1);
                if (v.y < groundY[gi, gj])
                {
                    groundY[gi, gj] = v.y;
                    hasData[gi, gj] = true;
                }
            }

            // ── 3. 空格(洞孔)由鄰居插值填補,多次傳播 ──
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int i = 0; i < res; i++)
                {
                    for (int j = 0; j < res; j++)
                    {
                        if (hasData[i, j]) continue;
                        float sum = 0f; int count = 0;
                        for (int di = -1; di <= 1; di++)
                        {
                            for (int dj = -1; dj <= 1; dj++)
                            {
                                if (di == 0 && dj == 0) continue;
                                int ni = i + di, nj = j + dj;
                                if (ni >= 0 && ni < res && nj >= 0 && nj < res && hasData[ni, nj])
                                {
                                    sum += groundY[ni, nj];
                                    count++;
                                }
                            }
                        }
                        if (count > 0)
                        {
                            groundY[i, j] = sum / count;
                            hasData[i, j] = true;
                            changed = true;
                        }
                    }
                }
                if (!changed) break;
            }
            // 仍空的格用全分塊最低 Y(極端情況)
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                    if (!hasData[i, j]) groundY[i, j] = overallMinY;

            // ── 4. 建立高度場網格(填補面 = 地面Y - belowOffset)──
            int vCount = res * res;
            var vertices = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    int idx = j * res + i;
                    float cx = minX + sizeX * (i + 0.5f) / res;
                    float cz = minZ + sizeZ * (j + 0.5f) / res;
                    float cy = groundY[i, j] - config.groundFillBelowOffset;
                    vertices[idx] = new Vector3(cx, cy, cz);
                    uvs[idx] = new Vector2((float)i / res, (float)j / res);
                }
            }

            // ── 5. 三角形索引 ──
            var triangles = new int[(res - 1) * (res - 1) * 6];
            int t = 0;
            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    int v00 = j * res + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + res;
                    int v11 = v01 + 1;
                    triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v11;
                    triangles[t++] = v00; triangles[t++] = v11; triangles[t++] = v10;
                }
            }

            // ── 6. 建立 GameObject + Mesh + Collider ──
            var go = new GameObject($"GroundFill_{rec.id}");
            go.transform.SetParent(null);

            var mesh = new Mesh { name = $"GroundFill_{rec.id}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mfComp = go.AddComponent<MeshFilter>();
            mfComp.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = config.groundFillColor;
            mat.SetInt("_Cull", 0);  // 雙面(與分塊一致)
            mr.sharedMaterial = mat;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;  // groundMask=~0,射線會命中

            _chunkFills[rec.id] = go;

            // 統計
            float minH = float.MaxValue, maxH = float.MinValue;
            foreach (var v in vertices)
            {
                if (v.y < minH) minH = v.y;
                if (v.y > maxH) maxH = v.y;
            }
            Debug.Log($"[Bootstrapper] GroundFill_{rec.id}: {verts.Count} 頂點取樣, " +
                      $"{res}x{res} 網格, 高度 {minH:F1}~{maxH:F1}m " +
                      $"(offset {config.groundFillBelowOffset}m), 覆蓋 {sizeX:F0}x{sizeZ:F0}m");
        }

        private void Update()
        {
            if (_ready) _streamer.Tick();
        }

        private void OnDestroy()
        {
            _streamer?.UnloadAll();
            // v2.2.12: 清理所有逐塊地面填補
            foreach (var kvp in _chunkFills)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _chunkFills.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            if (!config?.drawChunkGizmos ?? false) return;
            // 可在此繪製載入/卸載半徑;需 manifest 載入後才有意義,省略實作
        }
    }
}
