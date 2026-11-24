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
        /// 人工地面填補——以雙線性插值(bilinear)高度場網格跟隨地形起伏,修補攝影測量洞孔。
        ///
        /// 版本演進:
        ///   v2.2.9:  單一平坦平面(Quad),用 min(centroid_z)。bug:平面過高埋沒建物。
        ///   v2.2.10: IDW 插值 + bbox_min_z。bug:IDW 在深淺分塊間插值可產生
        ///            高於當地地形地板的值 → 填補面在玩家上方 → 「地面變天空」。
        ///   v2.2.11: 雙線性插值(分塊 grid 規則排列)+ 逐頂點鉗制。
        ///            每個頂點高度 = min(雙線性插值, 所在分塊 bbox_min_z) - offset。
        ///            「有數據的位置不另創高度」= 鉗制確保填補永遠 ≤ 當地地形地板。
        ///
        /// 原理:
        ///   - 分塊在 grid_x/grid_y 上規則排列(5×4),適用雙線性插值(表面擬合)。
        ///   - 控制點 = 各分塊 bbox_min[2](最低地形 Z=地板)。
        ///   - 雙線性插值在分塊間平滑過渡(表面擬合),但逐頂點鉗制至所在分塊的
        ///     bbox_min[2] - offset,確保填補永不高於當地地形地板 → 永不在玩家上方。
        ///   - 邊界外(margin)無分塊包含時,僅用雙線性插值(不鉗制)。
        ///
        /// 網格有 MeshCollider(地面跟隨射線命中,玩家不穿洞墜落)+
        /// 地面色材質(洞孔可見時看到地面色而非天空)。
        /// 在世界空間(非 worldRoot 下,避免 Z-up→Y-up 旋轉)。
        /// </summary>
        private void CreateGroundFill(ChunkManifest manifest)
        {
            if (!config.groundFillEnabled || manifest.chunks == null || manifest.chunks.Count == 0)
                return;

            // ── 1. 建立分塊 grid 查詢表 + 收集邊界 ──
            // gridMap[(grid_x, grid_y)] = ChunkRecord,用於雙線性插值 + 鉗制
            var gridMap = new Dictionary<(int gx, int gy), ChunkRecord>();
            int maxGx = 0, maxGy = 0;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var c in manifest.chunks)
            {
                if (c.grid_x > maxGx) maxGx = c.grid_x;
                if (c.grid_y > maxGy) maxGy = c.grid_y;
                gridMap[(c.grid_x, c.grid_y)] = c;

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
            int cols = maxGx + 1;  // grid_x: 0..maxGx
            int rows = maxGy + 1;  // grid_y: 0..maxGy

            // ── 2. 建立雙線性插值用的高度 grid + centroid 座標 grid ──
            // elevGrid[gx, gy] = 該分塊 bbox_min[2](地板高度)
            // centX[gx], centY[gy] = 該行/列的 centroid 座標(用於插值定位)
            var elevGrid = new float[cols, rows];
            var centX = new float[cols];
            var centY = new float[rows];
            var hasData = new bool[cols, rows];
            for (int gx = 0; gx < cols; gx++)
            {
                for (int gy = 0; gy < rows; gy++)
                {
                    if (gridMap.TryGetValue((gx, gy), out var c))
                    {
                        float elev = float.MaxValue;
                        if (c.bbox_min != null && c.bbox_min.Length >= 3)
                            elev = c.bbox_min[2];
                        else if (c.centroid != null && c.centroid.Length >= 3)
                            elev = c.centroid[2];
                        if (elev != float.MaxValue)
                        {
                            elevGrid[gx, gy] = elev;
                            hasData[gx, gy] = true;
                        }
                        if (c.centroid != null && c.centroid.Length >= 2)
                        {
                            centX[gx] = c.centroid[0];
                            centY[gy] = c.centroid[1];
                        }
                    }
                }
            }

            // 外擴 margin 確保覆蓋可視範圍
            minX -= config.groundFillMargin;
            maxX += config.groundFillMargin;
            minY -= config.groundFillMargin;
            maxY += config.groundFillMargin;

            // ── 3. 建立高度場網格 ──
            int res = Mathf.Max(2, config.groundFillGridResolution);
            int vCount = (res + 1) * (res + 1);
            var vertices = new Vector3[vCount];
            var uvs = new Vector2[vCount];

            for (int j = 0; j <= res; j++)
            {
                for (int i = 0; i <= res; i++)
                {
                    int idx = j * (res + 1) + i;
                    float mx = minX + (maxX - minX) * i / res;
                    float my = minY + (maxY - minY) * j / res;

                    // (a) 雙線性插值(表面擬合):在分塊 grid 上平滑插值
                    float interpZ = BilinearSample(elevGrid, hasData, centX, centY,
                                                    cols, rows, mx, my);

                    // (b) 鉗制:找出包含 (mx, my) 的分塊,確保填補 ≤ 該分塊地板
                    float floorZ = FindChunkFloor(manifest.chunks, mx, my);

                    // (c) final = min(插值, 地板) - offset
                    //     有分塊包含時:永不高於地板(「有數據不另創高度」)
                    //     邊界外無分塊時:僅用插值(不加鉗制)
                    float baseZ = float.IsNaN(floorZ) ? interpZ : Mathf.Min(interpZ, floorZ);
                    float mz = baseZ - config.groundFillYOffset;

                    // manifest (X, Y, Z) → Unity world (X, Z, -Y)
                    vertices[idx] = new Vector3(mx, mz, -my);
                    uvs[idx] = new Vector2((float)i / res, (float)j / res);
                }
            }

            // ── 4. 三角形索引(兩三角/格)──
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

            // ── 5. 建立 GameObject + Mesh + Collider ──
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
            Debug.Log($"[Bootstrapper] GroundFill 高度場建立: grid={cols}x{rows}, " +
                      $"網格={res}x{res}({vCount} 頂點), " +
                      $"高度範圍={minH:F1}~{maxH:F1}m(偏移 {config.groundFillYOffset}m + 逐頂點鉗制), " +
                      $"覆蓋={(maxX - minX):F0}x{(maxY - minY):F0}m");
        }

        /// <summary>
        /// 雙線性插值(bilinear):在分塊 grid 上,以 centroid 座標為節點,
        /// 平滑插值出任意 (x, y) 處的地板高度。表面擬合——通過控制點、不超出範圍。
        /// </summary>
        private static float BilinearSample(float[,] elevGrid, bool[,] hasData,
            float[] centX, float[] centY, int cols, int rows, float x, float y)
        {
            // 單分塊邊界情況:直接回傳該格值
            if (cols < 2 || rows < 2)
                return SampleGrid(elevGrid, hasData, cols, rows, 0, 0);

            // 找出 x 落在哪兩列之間
            int gi = 0;
            for (int gx = 0; gx < cols - 1; gx++)
            {
                if (x >= centX[gx] && x <= centX[gx + 1]) { gi = gx; break; }
                if (x < centX[0]) { gi = 0; break; }
                if (x > centX[cols - 1]) { gi = cols - 2; break; }
                gi = gx;
            }
            // 找出 y 落在哪兩行之間
            int gj = 0;
            for (int gy = 0; gy < rows - 1; gy++)
            {
                if (y >= centY[gy] && y <= centY[gy + 1]) { gj = gy; break; }
                if (y < centY[0]) { gj = 0; break; }
                if (y > centY[rows - 1]) { gj = rows - 2; break; }
                gj = gy;
            }

            // 四個角的值(缺失資料時用鄰近角填充)
            float z00 = SampleGrid(elevGrid, hasData, cols, rows, gi,     gj);
            float z10 = SampleGrid(elevGrid, hasData, cols, rows, gi + 1, gj);
            float z01 = SampleGrid(elevGrid, hasData, cols, rows, gi,     gj + 1);
            float z11 = SampleGrid(elevGrid, hasData, cols, rows, gi + 1, gj + 1);

            // 插值比例(防除零)
            float dx = centX[gi + 1] - centX[gi];
            float dy = centY[gj + 1] - centY[gj];
            float fu = Mathf.Approximately(dx, 0f) ? 0f : Mathf.Clamp01((x - centX[gi]) / dx);
            float fv = Mathf.Approximately(dy, 0f) ? 0f : Mathf.Clamp01((y - centY[gj]) / dy);

            // 雙線性: (1-fu)(1-fv)z00 + fu(1-fv)z10 + (1-fu)fv z01 + fu fv z11
            return (1f - fu) * (1f - fv) * z00
                 + fu * (1f - fv) * z10
                 + (1f - fu) * fv * z01
                 + fu * fv * z11;
        }

        /// <summary>安全取 grid 值(越界或缺失時用鄰近有效值)。</summary>
        private static float SampleGrid(float[,] elevGrid, bool[,] hasData,
            int cols, int rows, int gx, int gy)
        {
            gx = Mathf.Clamp(gx, 0, cols - 1);
            gy = Mathf.Clamp(gy, 0, rows - 1);
            if (hasData[gx, gy]) return elevGrid[gx, gy];
            // 缺失:搜尋最近的有效格(最多 8 鄰)
            for (int r = 1; r <= 2; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = gx + dx, ny = gy + dy;
                        if (nx >= 0 && nx < cols && ny >= 0 && ny < rows && hasData[nx, ny])
                            return elevGrid[nx, ny];
                    }
                }
            }
            return 0f;  // fallback
        }

        /// <summary>
        /// 找出包含 (x, y) 的分塊,回傳其 bbox_min[2](地形地板高度)。
        /// 「有數據的位置不另創高度」——用於鉗制填補面不超過當地地形地板。
        /// 無分塊包含時回傳 NaN(邊界外,不加鉗制)。
        /// </summary>
        private static float FindChunkFloor(List<ChunkRecord> chunks, float x, float y)
        {
            // 同時追蹤最近分塊(邊界外用)
            float nearestDist = float.MaxValue;
            float nearestFloor = float.NaN;

            foreach (var c in chunks)
            {
                if (c.bbox_min == null || c.bbox_min.Length < 2 ||
                    c.bbox_max == null || c.bbox_max.Length < 2) continue;

                // 精確包含檢查
                if (x >= c.bbox_min[0] && x <= c.bbox_max[0] &&
                    y >= c.bbox_min[1] && y <= c.bbox_max[1])
                {
                    if (c.bbox_min.Length >= 3) return c.bbox_min[2];
                    if (c.centroid != null && c.centroid.Length >= 3) return c.centroid[2];
                    return float.NaN;
                }

                // 追蹤最近分塊(用 centroid 距離)
                if (c.centroid != null && c.centroid.Length >= 2)
                {
                    float ddx = x - c.centroid[0];
                    float ddy = y - c.centroid[1];
                    float d2 = ddx * ddx + ddy * ddy;
                    if (d2 < nearestDist)
                    {
                        nearestDist = d2;
                        nearestFloor = c.bbox_min != null && c.bbox_min.Length >= 3
                            ? c.bbox_min[2]
                            : (c.centroid != null && c.centroid.Length >= 3 ? c.centroid[2] : float.NaN);
                    }
                }
            }
            return nearestFloor;  // NaN if no data at all
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
