// ChunkStreamer.cs — Stage B 核心:依攝影機位置逐塊載入/卸載 .glb 分塊。
//
// 設計重點(對應專案 brief Stage B):
//   - 使用 com.unity.cloud.gltfast 的 GltfImport + InstantiateMainSceneAsync + Dispose()
//   - TimeBudgetPerFrameDeferAgent 限制每幀載入預算,避免主執行緒卡頓
//   - 依攝影機半徑載入鄰近分塊,一次一塊(不批次載入大量分塊)
//   - 卸載遠端分塊並 Dispose() GPU/mesh/texture 資源,保持 iOS 記憶體扁平
//   - HK 座標(EPSG:2326, Z-up)正規化至本地原點,Y-up 旋轉轉換
//
// 注意:本檔為 Stage B 鷹架(scaffold),未經 Unity 編譯驗證。
// glTFast API 依據 com.unity.cloud.gltfast 6.0 官方文件:
//   https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.0/manual/ImportRuntime.html

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;
// ConsoleLogger 在 GLTFast.Logging 命名空間;GltfImport ctor 需 IDownloadProvider/IDeferAgent/IMaterialGenerator/ICodeLogger

namespace HKGrid.Streaming
{
    /// <summary>
    /// 依攝影機位置串流載入/卸載 .glb 分塊。應由 ChunkStreamBootstrapper 驅動 Update。
    /// </summary>
    public class ChunkStreamer
    {
        private readonly ChunkManifest _manifest;
        private readonly ChunkStreamConfig _config;
        private readonly Transform _worldRoot;       // 所有分塊的父節點(套用 Z-up→Y-up 旋轉)
        private readonly Camera _camera;
        private readonly IDeferAgent _deferAgent;
        private readonly ConsoleLogger _logger;

        // chunk id → 已載入狀態
        private readonly Dictionary<string, LoadedChunk> _loaded = new Dictionary<string, LoadedChunk>();
        // 載入中(防重複觸發)
        private readonly HashSet<string> _loading = new HashSet<string>();
        // v2.4.0: 載入失敗的分塊(預載入完成判定用,避免單一失敗永久卡住)
        private readonly HashSet<string> _failed = new HashSet<string>();

        // 攝影機在「本地網格座標」(Z-up,已置中)的位置,每幀更新
        private Vector3 _cameraLocalPos;
        private Quaternion _worldToLocalRot; // world→local 的旋轉(逆 of Z-up→Y-up)

        public int LoadedCount => _loaded.Count;
        public int LoadingCount => _loading.Count;

        // v2.4.0: 預載入模式(忽略半徑載入全部分塊、不卸載)
        public bool PreloadMode { get; set; } = false;
        public int TotalChunkCount => _manifest.chunks.Count;
        public int FailedCount => _failed.Count;
        public bool IsPreloadComplete =>
            _loaded.Count + _failed.Count >= _manifest.chunks.Count && _loading.Count == 0;

        /// <summary>v2.2.12: 分塊載入完成時觸發(網格+碰撞器已就緒)。用於逐塊建立地面填補。</summary>
        public event Action<ChunkRecord, GameObject> OnChunkLoaded;
        /// <summary>v2.2.12: 分塊卸載前觸發。用於清理對應的地面填補網格。</summary>
        public event Action<string> OnChunkUnloaded;

        public ChunkStreamer(ChunkManifest manifest, ChunkStreamConfig config,
                             Transform worldRoot, Camera camera,
                             IDeferAgent deferAgent = null, ConsoleLogger logger = null)
        {
            _manifest = manifest;
            _config = config;
            _worldRoot = worldRoot;
            _camera = camera;
            _deferAgent = deferAgent;
            _logger = logger ?? new ConsoleLogger();

            // 設定 Z-up→Y-up 旋轉:-90° about X 將 grid-Z(上)對應 Unity-Y(上)
            if (_config.applyZupToYupRotation)
            {
                _worldRoot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                _worldToLocalRot = Quaternion.Inverse(_worldRoot.localRotation);
            }
            else
            {
                _worldToLocalRot = Quaternion.identity;
            }
        }

        /// <summary>每幀由 Bootstrapper 呼叫:更新攝影機本地座標並觸發載入/卸載。</summary>
        public void Tick()
        {
            // v2.4.0: 預載入模式 — 載入所有分塊(忽略半徑)、不卸載
            if (PreloadMode)
            {
                int startedThisFrame = 0;
                foreach (var rec in _manifest.chunks)
                {
                    if (_loaded.ContainsKey(rec.id) || _loading.Contains(rec.id) || _failed.Contains(rec.id)) continue;
                    if (startedThisFrame >= _config.maxLoadsPerFrame) break;
                    LoadChunkAsync(rec);
                    startedThisFrame++;
                    if (_config.logStreamingEvents)
                        Debug.Log($"[ChunkStreamer] 預載入 {rec.id} ({rec.bytes / 1e6f:F2} MB, {rec.triangles} tris)");
                }
                return;
            }

            // 將攝影機世界座標轉換為本地網格座標(Z-up,已置中)
            // world = _worldRoot.rotation * local  =>  local = inverse(_worldRoot.rotation) * world
            Vector3 camWorld = _camera.transform.position;
            _cameraLocalPos = _worldToLocalRot * (camWorld - _worldRoot.position);

            int loadsThisFrame = 0;
            int unloadsThisFrame = 0;

            // 1. 卸載遠端分塊(優先,釋放記憶體)
            if (unloadsThisFrame < _config.maxUnloadsPerFrame)
            {
                var toUnload = new List<string>();
                foreach (var kvp in _loaded)
                {
                    float dist = Vector3.Distance(_cameraLocalPos, kvp.Value.centroidLocal);
                    // 加上分塊包圍球半徑,避免分塊仍可見時被卸載
                    if (dist - kvp.Value.boundingRadius > _config.unloadRadius)
                        toUnload.Add(kvp.Key);
                }
                foreach (var id in toUnload)
                {
                    if (unloadsThisFrame >= _config.maxUnloadsPerFrame) break;
                    UnloadChunk(id);
                    unloadsThisFrame++;
                }
            }

            // 2. 找出待載入的分塊,依距離排序(近的先載入)
            var candidates = new List<(ChunkRecord rec, float dist)>();
            foreach (var rec in _manifest.chunks)
            {
                if (_loaded.ContainsKey(rec.id) || _loading.Contains(rec.id)) continue;
                float dist = Vector3.Distance(_cameraLocalPos, rec.CentroidVector);
                if (dist - rec.BoundingRadius <= _config.loadRadius)
                    candidates.Add((rec, dist));
            }
            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

            // 3. 逐塊啟動載入(一次一塊,符合 brief「never batch-loading large sets」)
            foreach (var (rec, _) in candidates)
            {
                if (loadsThisFrame >= _config.maxLoadsPerFrame) break;
                LoadChunkAsync(rec); // fire-and-forget(async void);載入狀態由 _loading 集合守護
                loadsThisFrame++;
                if (_config.logStreamingEvents)
                    Debug.Log($"[ChunkStreamer] 載入 {rec.id} ({rec.bytes / 1e6f:F2} MB, {rec.triangles} tris)");
            }
        }

        private async void LoadChunkAsync(ChunkRecord rec)
        {
            _loading.Add(rec.id);
            string path = GetChunkPath(rec.file);

            // v2.2.15: 詳細載入記錄(iOS 螢幕診斷需要看到路徑與結果)
            Debug.Log($"[ChunkStreamer] 載入 {rec.id}: {rec.file} ({rec.bytes / 1e6f:F2} MB, {rec.triangles} tris) path={path}");

            // ImportSettings:glTFast 6.0 的 ImportSettings 無 generateNormals 欄位
            // (僅有 NodeNameMethod/AnimationMethod/GenerateMipMaps/過濾模式/異向性)。
            // 法線由網格本身提供(CSDI OSGB 已含法線),不需額外產生。
            var importSettings = new ImportSettings();

            // GltfImport ctor:(IDownloadProvider, IDeferAgent, IMaterialGenerator, ICodeLogger)
            // 全部可 null;deferAgent 傳入以套用 TimeBudgetPerFrameDeferAgent 每幀預算。
            var gltf = new GltfImport(
                downloadProvider: null,
                deferAgent: _deferAgent,
                materialGenerator: null,
                logger: _logger);
            try
            {
                // path 已含 file:// 前綴(GetChunkPath 加的),用 Load(url, settings) 多載
                bool ok = await gltf.Load(path, importSettings);

                if (!ok)
                {
                    Debug.LogWarning($"[ChunkStreamer] glTF 載入失敗: {rec.file} (gltf.Load returned false)");
                    _failed.Add(rec.id);
                    return;
                }

                Debug.Log($"[ChunkStreamer] glTF 載入 OK,開始實例化: {rec.file}");

                // 在 worldRoot 下建立分塊容器節點。
                // GLB 頂點已由轉換器置中(相對各圖幅 origin),容器節點需設於 positionOffset
                // (合併多圖幅時為圖幅偏移;單圖幅為 Vector3.zero,行為不變)。
                // 若仍在 localPosition=zero,多圖幅分塊會全部疊在原點 → 只見 1 個圖幅。
                var chunkGo = new GameObject($"chunk_{rec.id}");
                chunkGo.transform.SetParent(_worldRoot, worldPositionStays: false);
                chunkGo.transform.localPosition = rec.PositionOffsetVector;

                // GameObjectInstantiator ctor:(IGltfReadable, Transform, ICodeLogger, InstantiationSettings)
                var instantiator = new GameObjectInstantiator(gltf, chunkGo.transform, _logger);
                ok = await gltf.InstantiateMainSceneAsync(instantiator);
                if (!ok)
                {
                    Debug.LogWarning($"[ChunkStreamer] glTF 實例化失敗: {rec.file} (InstantiateMainSceneAsync returned false)");
                    UnityEngine.Object.Destroy(chunkGo);
                    gltf.Dispose();
                    _failed.Add(rec.id);
                    return;
                }

                Debug.Log($"[ChunkStreamer] ✅ 分塊就緒: {rec.id}");

                // 加入 MeshCollider 供地面跟隨射線(TouchPlayerController)使用
                if (_config.addChunkColliders)
                {
                    AddCollidersRecursive(chunkGo);
                }

                // v2.2.5: 強制雙面渲染(Cull Off),修正從背面/內側看穿牆壁與地面
                if (_config.forceDoubleSided)
                {
                    ForceDoubleSidedRecursive(chunkGo);
                }

                _loaded[rec.id] = new LoadedChunk
                {
                    id = rec.id,
                    gameObject = chunkGo,
                    gltfImport = gltf,
                    centroidLocal = rec.CentroidVector,
                    boundingRadius = rec.BoundingRadius,
                };

                // v2.2.12: 通知訂閱者(Bootstrapper 建立逐塊地面填補)
                OnChunkLoaded?.Invoke(rec, chunkGo);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ChunkStreamer] 載入 {rec.id} 例外: {e}");
                _failed.Add(rec.id);
            }
            finally
            {
                _loading.Remove(rec.id);
            }
        }

        private void UnloadChunk(string id)
        {
            if (!_loaded.TryGetValue(id, out var c)) return;
            // v2.2.12: 通知訂閱者清理地面填補(在銷毀前)
            OnChunkUnloaded?.Invoke(id);
            // 銷毀 GameObject(釋放 mesh/texture GPU 資源)
            if (c.gameObject != null)
                UnityEngine.Object.Destroy(c.gameObject);
            // Dispose GltfImport(釋放 glTFast 內部資源)
            c.gltfImport?.Dispose();
            _loaded.Remove(id);
            if (_config.logStreamingEvents)
                Debug.Log($"[ChunkStreamer] 卸載 {id}");
        }

        /// <summary>計算分塊 .glb 的完整路徑(iOS 用 file:// 前綴)。</summary>
        private string GetChunkPath(string fileName)
        {
            string baseDir = Path.Combine(Application.streamingAssetsPath, _config.chunksRelativePath);
            string full = Path.Combine(baseDir, fileName);
            // iOS 與 Editor 需要 file:// 前綴給 UnityWebRequest
            return "file://" + full.Replace('\\', '/');
        }

        /// <summary>為分塊的所有子網格加入 MeshCollider(供地面跟隨射線)。</summary>
        private static void AddCollidersRecursive(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
                {
                    var mc = mf.GetComponent<MeshCollider>();
                    if (mc == null)
                        mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
        }

        /// <summary>v2.2.5: 將分塊所有材質設為雙面渲染(Cull Mode: Off)。</summary>
        /// <remarks>
        /// glTFast 依 glTF 材質的 doubleSided 屬性決定背面剔除。攝影測量網格可能因
        /// 法線纏繞不一致,導致部分地面/牆面從某些角度「突然消失」(被當成背面剔除)。
        /// 此處一律覆寫為雙面(關閉剔除),修正「無表面」問題。
        /// 兼容兩種管線:Built-in RP 的 Standard shader 用 _Cull;URP/HDRP Lit 用 _CullMode。
        /// 值皆為:0 = Off(雙面), 2 = Back(預設單面)。
        /// 使用 sharedMaterials + HashSet 去重,避免重複設定同一材質。
        /// cacheGltfImports=false 時各分塊材質互不共用,覆寫安全。
        /// </remarks>
        private static void ForceDoubleSidedRecursive(GameObject root)
        {
            var seen = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (!seen.Add(mat)) continue;  // 已處理過此材質
                    // Built-in RP(Standard shader):_Cull
                    if (mat.HasProperty("_Cull"))
                        mat.SetInt("_Cull", 0);
                    // URP / HDRP(Lit shader):_CullMode
                    if (mat.HasProperty("_CullMode"))
                        mat.SetInt("_CullMode", 0);
                }
            }
        }

        /// <summary>全部卸載(場景切換/離開時呼叫)。</summary>
        public void UnloadAll()
        {
            var ids = new List<string>(_loaded.Keys);
            foreach (var id in ids) UnloadChunk(id);
        }

        private class LoadedChunk
        {
            public string id;
            public GameObject gameObject;
            public GltfImport gltfImport;
            public Vector3 centroidLocal;
            public float boundingRadius;
        }
    }
}
