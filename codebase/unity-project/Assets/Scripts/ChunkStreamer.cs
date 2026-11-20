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

        // 攝影機在「本地網格座標」(Z-up,已置中)的位置,每幀更新
        private Vector3 _cameraLocalPos;
        private Quaternion _worldToLocalRot; // world→local 的旋轉(逆 of Z-up→Y-up)

        public int LoadedCount => _loaded.Count;
        public int LoadingCount => _loading.Count;

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
                    Debug.LogWarning($"[ChunkStreamer] glTF 載入失敗: {rec.file}");
                    return;
                }

                // 在 worldRoot 下建立分塊容器節點。
                // 重要:GLB 頂點已由轉換器置中(全圖幅 origin 已內嵌於頂點座標),
                // 因此容器節點必須在本地原點(Vector3.zero),不可再偏移 centroid,
                // 否則會造成雙重偏移 → 分塊散開 150m 間距(「多個島」視覺缺陷)。
                var chunkGo = new GameObject($"chunk_{rec.id}");
                chunkGo.transform.SetParent(_worldRoot, worldPositionStays: false);
                chunkGo.transform.localPosition = Vector3.zero;

                // GameObjectInstantiator ctor:(IGltfReadable, Transform, ICodeLogger, InstantiationSettings)
                var instantiator = new GameObjectInstantiator(gltf, chunkGo.transform, _logger);
                ok = await gltf.InstantiateMainSceneAsync(instantiator);
                if (!ok)
                {
                    Debug.LogWarning($"[ChunkStreamer] glTF 實例化失敗: {rec.file}");
                    UnityEngine.Object.Destroy(chunkGo);
                    gltf.Dispose();
                    return;
                }

                // 加入 MeshCollider 供地面跟隨射線(TouchPlayerController)使用
                if (_config.addChunkColliders)
                {
                    AddCollidersRecursive(chunkGo);
                }

                _loaded[rec.id] = new LoadedChunk
                {
                    id = rec.id,
                    gameObject = chunkGo,
                    gltfImport = gltf,
                    centroidLocal = rec.CentroidVector,
                    boundingRadius = rec.BoundingRadius,
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[ChunkStreamer] 載入 {rec.id} 例外: {e}");
            }
            finally
            {
                _loading.Remove(rec.id);
            }
        }

        private void UnloadChunk(string id)
        {
            if (!_loaded.TryGetValue(id, out var c)) return;
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
