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
        // v2.2.15: 螢幕診斷覆蓋層(iOS 藍畫面診斷)
        private HKGrid.Debugging.DebugOverlay _dbg;

        // v2.4.0: 預載入 + 載入頁
        private bool _preloading = false;
        private bool _showLoadingScreen = false;
        private Vector3 _pendingStartWorld;        // 預載入完成後用的 spawn 位置
        private TouchPlayerController _player;     // 玩家控制器(預載入期間停用)
        private float _preloadStartTime;
        private const float PRELOAD_TIMEOUT = 300f; // 5 分鐘逾時安全網

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[Bootstrapper] 未指派 ChunkStreamConfig");
                enabled = false;
                return;
            }
            if (camera == null) camera = GetComponent<Camera>();

            // v2.2.15: 取得 DebugOverlay(若有)用於 iOS 螢幕診斷
            _dbg = GetComponent<HKGrid.Debugging.DebugOverlay>();

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

            // v2.2.15: 螢幕診斷 — 顯示 manifest 路徑
            if (_dbg != null) _dbg.SetManifestStatus("manifest 載入中: " + config.chunksRelativePath + "/" + config.manifestFileName);
            Debug.Log("[Bootstrapper] streamingAssetsPath = " + Application.streamingAssetsPath);
            Debug.Log("[Bootstrapper] manifest 路徑: " + manifestPath);
            Debug.Log("[Bootstrapper] manifest URL: " + url);
            // v2.2.15: 檢查檔案是否存在(Editor/Android 可用 File.Exists;iOS 唯讀封裝亦可)
            Debug.Log("[Bootstrapper] manifest File.Exists = " + File.Exists(manifestPath));
            // 列出 Chunks 目錄內容(診斷空目錄)
            string chunksDir = Path.Combine(Application.streamingAssetsPath, config.chunksRelativePath);
            if (Directory.Exists(chunksDir))
            {
                var files = Directory.GetFiles(chunksDir);
                Debug.Log("[Bootstrapper] Chunks 目錄有 " + files.Length + " 檔案");
                if (files.Length > 0)
                    Debug.Log("[Bootstrapper] 第一個檔案: " + Path.GetFileName(files[0]));
            }
            else
            {
                Debug.LogError("[Bootstrapper] Chunks 目錄不存在: " + chunksDir);
                if (_dbg != null) _dbg.SetManifestStatus("Chunks 目錄不存在!");
            }

            // iOS/Editor 透過 UnityWebRequest 讀 manifest(StreamingAssets 在 iOS 為唯讀封裝)
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Bootstrapper] manifest 載入失敗: {req.error}\n路徑: {manifestPath}");
                    if (_dbg != null) _dbg.SetManifestStatus("manifest 載入失敗: " + req.error);
                    yield break;
                }
                var manifest = JsonUtility.FromJson<ChunkManifest>(req.downloadHandler.text);
                Debug.Log($"[Bootstrapper] manifest 載入: {manifest.source}, {manifest.chunkCount} 分塊, " +
                          $"{manifest.totalBytes / 1e6f:F2} MB, {manifest.totalTriangles:N0} 三角面");
                Debug.Log($"[Bootstrapper] 授權: {manifest.attribution}");

                // v2.2.15: 螢幕診斷 — manifest 載入成功
                if (_dbg != null) _dbg.SetManifestStatus("manifest OK: " + manifest.chunkCount + " 分塊, " +
                    (manifest.totalBytes / 1e6f).ToString("F1") + " MB");

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
                // v2.5.0: 支援 spawnTileCode——spawn 於指定圖幅中心(如 6-SE-17C)。
                //   - spawnTileCode 非空:該圖幅所有分塊 centroid 的平均
                //   - spawnTileCode 為空 + 單圖幅:manifest 中心(所有 centroid 平均)
                //   - spawnTileCode 為空 + 多圖幅:manifest 全域中心(所有 centroid 平均)
                Vector3 startLocal = Vector3.zero;
                int spawnChunkCount = 0;

                if (!string.IsNullOrEmpty(config.spawnTileCode))
                {
                    // v2.5.0: spawn 於指定圖幅中心
                    foreach (var c in manifest.chunks)
                    {
                        if (c.tile == config.spawnTileCode)
                        {
                            startLocal += c.CentroidVector;
                            spawnChunkCount++;
                        }
                    }
                    if (spawnChunkCount > 0)
                    {
                        startLocal /= spawnChunkCount;
                        Debug.Log($"[Bootstrapper] spawnTileCode 模式:起始位置 = {config.spawnTileCode} 中心 " +
                                  $"({spawnChunkCount} 分塊平均) {startLocal}");
                    }
                    else
                    {
                        Debug.LogWarning($"[Bootstrapper] spawnTileCode='{config.spawnTileCode}' " +
                                         $"在 manifest 中找不到相符分塊,回退至全域中心");
                    }
                }

                if (spawnChunkCount == 0)
                {
                    // 回退:manifest 全域中心(所有分塊 centroid 平均)
                    foreach (var c in manifest.chunks)
                        startLocal += c.CentroidVector;
                    startLocal /= manifest.chunks.Count;
                    Debug.Log($"[Bootstrapper] 全域中心模式:起始位置 = manifest 中心 {startLocal}");
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

                // v2.2.3: 設定可視距離 + 霧(隱藏遠處分塊彈出,降低渲染負載)
                camera.farClipPlane = config.cameraFarClip;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = config.cameraFarClip * 0.78f;  // ~350m
                RenderSettings.fogEndDistance = config.cameraFarClip;             // ~450m
                RenderSettings.fogColor = camera.backgroundColor;  // 與天色一致

                _player = camera.GetComponent<TouchPlayerController>();

                // ── v2.4.0: 預載入模式 ──
                // 顯示載入頁,載入所有分塊完成後才顯示場景。
                // 修正:分塊非同步載入期間玩家已 OnSpawned → 施重力 → 地面不存在 → 墜落穿模。
                if (config.preloadAllChunks)
                {
                    _streamer.PreloadMode = true;
                    _preloading = true;
                    _showLoadingScreen = true;
                    _pendingStartWorld = startWorld;
                    _preloadStartTime = Time.time;

                    // 預載入期間使用較高載入預算(載入頁可接受幀率下降)
                    var daPreload = FindObjectOfType<TimeBudgetPerFrameDeferAgent>();
                    if (daPreload != null) daPreload.SetFrameBudget(config.preloadDeferBudget);

                    // 停用玩家控制器(避免分塊載入前施重力墜落)
                    if (_player != null) _player.enabled = false;

                    if (_dbg != null)
                        _dbg.SetManifestStatus("預載入中: 0/" + _streamer.TotalChunkCount + " 分塊");

                    Debug.Log($"[Bootstrapper] 預載入模式啟用: {_streamer.TotalChunkCount} 分塊, " +
                              $"deferBudget={config.preloadDeferBudget}, keepAll={config.keepAllChunksAfterPreload}");
                }
                else
                {
                    // 傳統模式:立即顯示場景,半徑串流(單圖幅快速啟動)
                    if (_player != null)
                        _player.OnSpawned(startWorld);
                    else
                        camera.transform.position = startWorld + Vector3.up * 50f;
                }

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
            // v2.6.3: 過濾建物頂點——只取 overallMinY + GROUND_BAND 範圍內的頂點。
            // 原因:分塊網格包含建物(攝影測量),GroundFill 逐格取 min-Y 會把屋頂
            // 當地面(如 17C_x162_y168 高度 -93~+99m,屋頂格 Y=+99)。
            // 玩家在該格上方 spawn 時,向下射線打不到屋頂 GroundFill(在射線起點上方)
            // → 找不到地面 → 墜落 → 「無法站立」。
            // 合法地形高差 ≤34m(實測),建物 >40m。GROUND_BAND=40m 保留地形、濾除建物。
            // 建物下的格(無地形頂點)由步驟 3 鄰居插值填補 → 地面高度。
            const float GROUND_BAND = 40f;
            float groundCeiling = overallMinY + GROUND_BAND;
            var groundY = new float[res, res];
            var hasData = new bool[res, res];
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                    groundY[i, j] = float.MaxValue;

            foreach (var v in verts)
            {
                if (v.y > groundCeiling) continue;  // v2.6.3: 跳過建物頂點(屋頂/高樓)
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

            // ── 3b. v2.2.14: 坑洞離群值鉗制 ──
            // 某些格的 min-Y 可能是攝影測量深坑(天井/下沉庭院/雜訊),遠低於鄰居。
            // 若填補面跟隨此深坑下移,玩家跨入即被困住(即使 belowOffset 已降至 0.05m)。
            // 對「低於鄰居平均超過 PIT_THRESHOLD」的格,將填補高度提升至鄰居平均,
            // 使填補面成為連續可走的安全網(真實網格仍顯示實際地形;填補僅為防墜地板)。
            const float PIT_THRESHOLD = 8f;  // m,低於鄰居平均此值視為坑洞離群值
            for (int i = 0; i < res; i++)
            {
                for (int j = 0; j < res; j++)
                {
                    float sum = 0f; int count = 0;
                    for (int di = -1; di <= 1; di++)
                    {
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            if (di == 0 && dj == 0) continue;
                            int ni = i + di, nj = j + dj;
                            if (ni >= 0 && ni < res && nj >= 0 && nj < res)
                            {
                                sum += groundY[ni, nj];
                                count++;
                            }
                        }
                    }
                    if (count > 0)
                    {
                        float nbrAvg = sum / count;
                        if (groundY[i, j] < nbrAvg - PIT_THRESHOLD)
                            groundY[i, j] = nbrAvg;  // 提升至鄰居平均(消除深坑)
                    }
                }
            }

            // ── 4. 建立高度場網格(填補面 = 地面Y - belowOffset)──
            // v2.6.3: 頂點置於完整 extent(i/(res-1)),非格中心((i+0.5)/res)。
            // 原因:(i+0.5)/res 使 GroundFill 網格內縮 ~9m/邊(res=8)→ 相鄰分塊
            // GroundFill 之間有 ~18m 空隙 → 玩家在邊界墜落(含 spawn 點)。
            // 改為 i/(res-1):頂點由 0%..100%,相鄰 GroundFill 相接(或微重疊)。
            int vCount = res * res;
            var vertices = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            float denomX = res - 1;
            float denomZ = res - 1;
            if (denomX < 1f) denomX = 1f;
            if (denomZ < 1f) denomZ = 1f;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    int idx = j * res + i;
                    float cx = minX + sizeX * i / denomX;
                    float cz = minZ + sizeZ * j / denomZ;
                    float cy = groundY[i, j] - config.groundFillBelowOffset;
                    vertices[idx] = new Vector3(cx, cy, cz);
                    uvs[idx] = new Vector2((float)i / denomX, (float)j / denomZ);
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
            // v2.2.17: 防禦性著色器查找——若 Standard 被 stripping 移除,回退至內建著色器。
            // BuildIOS.EnsureGltfFastShadersIncluded() 已將 Standard 加入 Always Included Shaders,
            // 但此防禦確保即使遺漏也不會擲回 ArgumentNullException。
            var groundShader = Shader.Find("Standard") ?? Shader.Find("UI/Default") ?? Shader.Find("Hidden/InternalErrorShader");
            var mat = new Material(groundShader);
            mat.color = config.groundFillColor;
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);  // 雙面(與分塊一致)
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
            if (!_ready) return;

            _streamer.Tick();

            // v2.4.0: 預載入完成判定 — 所有分塊載入完成後才顯示場景
            if (_preloading)
            {
                // 更新載入頁進度(供 DebugOverlay 與載入頁 OnGUI 顯示)
                if (_dbg != null)
                    _dbg.SetManifestStatus("預載入中: " + _streamer.LoadedCount + "/" +
                        _streamer.TotalChunkCount + " 分塊" +
                        (_streamer.FailedCount > 0 ? " (失敗 " + _streamer.FailedCount + ")" : ""));
                if (_dbg != null)
                    _dbg.SetChunkCounts(_streamer.LoadedCount, _streamer.LoadingCount);

                bool complete = _streamer.IsPreloadComplete;
                bool timeout = Time.time - _preloadStartTime > PRELOAD_TIMEOUT;

                if (complete || timeout)
                {
                    if (timeout && !complete)
                        Debug.LogWarning($"[Bootstrapper] 預載入逾時({PRELOAD_TIMEOUT}s):" +
                            $" 已載入 {_streamer.LoadedCount}/{_streamer.TotalChunkCount}" +
                            $", 失敗 {_streamer.FailedCount}, 載入中 {_streamer.LoadingCount}");

                    // 預載入完成 — 顯示場景
                    _preloading = false;
                    _showLoadingScreen = false;

                    // 恢復正常載入預算
                    var da = FindObjectOfType<TimeBudgetPerFrameDeferAgent>();
                    if (da != null) da.SetFrameBudget(config.gltfFrameBudget);

                    // keepAllChunksAfterPreload=false → 恢復半徑串流(遠處卸載)
                    // keepAllChunksAfterPreload=true  → 保持 PreloadMode(不卸載,全部分塊駐留)
                    if (!config.keepAllChunksAfterPreload)
                        _streamer.PreloadMode = false;

                    // 啟用玩家控制器 + 定位至 spawn(地面已存在,射線命中)
                    if (_player != null)
                    {
                        _player.enabled = true;
                        _player.OnSpawned(_pendingStartWorld);
                    }

                    if (_dbg != null)
                        _dbg.SetManifestStatus("預載入完成: " + _streamer.LoadedCount + "/" +
                            _streamer.TotalChunkCount + " 分塊已載入");

                    Debug.Log($"[Bootstrapper] 預載入完成: {_streamer.LoadedCount}/{_streamer.TotalChunkCount}" +
                              $" 分塊, keepAll={config.keepAllChunksAfterPreload}, 耗時 {Time.time - _preloadStartTime:F1}s");
                }
            }
            else
            {
                // v2.2.15: 螢幕診斷 — 更新分塊計數(一般串流模式)
                if (_dbg != null)
                    _dbg.SetChunkCounts(_streamer.LoadedCount, _streamer.LoadingCount);
            }
        }

        // ════════════════════════════════════════════════════════════════
        // v2.4.0: 載入頁(預載入期間顯示,隱藏分塊彈出)
        // ════════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            if (!_showLoadingScreen) return;

            // 全螢幕深色背景(隱藏分塊彈出)
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.98f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            int loaded = _streamer != null ? _streamer.LoadedCount : 0;
            int total = _streamer != null ? _streamer.TotalChunkCount : 0;
            float pct = total > 0 ? (float)loaded / total : 0f;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(24, Screen.height / 22),
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = Color.white;

            var detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, Screen.height / 36),
                alignment = TextAnchor.MiddleCenter,
            };
            detailStyle.normal.textColor = new Color(0.75f, 0.82f, 0.92f);

            Rect titleRect = new Rect(0, Screen.height * 0.40f, Screen.width, Screen.height * 0.08f);
            Rect detailRect = new Rect(0, Screen.height * 0.50f, Screen.width, Screen.height * 0.05f);

            GUI.Label(titleRect, "載入中", titleStyle);
            GUI.Label(detailRect, loaded + " / " + total + " 分塊  (" + (pct * 100f).ToString("F0") + " %)", detailStyle);

            // 進度條
            float barW = Screen.width * 0.6f;
            float barH = Mathf.Max(6f, Screen.height * 0.008f);
            float barX = (Screen.width - barW) * 0.5f;
            float barY = Screen.height * 0.58f;

            GUI.color = new Color(0.18f, 0.20f, 0.26f, 1f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);
            GUI.color = new Color(0.30f, 0.62f, 1.0f, 1f);
            GUI.DrawTexture(new Rect(barX, barY, barW * pct, barH), Texture2D.whiteTexture);
            GUI.color = Color.white;
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
