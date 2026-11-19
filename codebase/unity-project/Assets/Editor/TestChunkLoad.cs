// TestChunkLoad.cs — Stage B headless 驗證:載入 manifest + 第一個 .glb 分塊。
//
// 由 batchmode 執行(不需 Play mode):
//   Tuanjie.exe -batchmode -nographics -projectPath <proj> -logFile <log> \
//     -executeMethod HKGrid.Editor.TestChunkLoad.Run -quit
//
// 注意:Editor assembly(Assembly-CSharp-Editor)預設不參考 runtime assembly
// (Assembly-CSharp),因此 HKGrid.Streaming.ChunkManifest 在此看不到。
// 本測試改用反射 + JsonUtility 的泛型 API 載入 manifest,避免跨 assembly 相依。

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GLTFast;
using GLTFast.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace HKGrid.Editor
{
    public static class TestChunkLoad
    {
        // 純字串常量(CS0133 要求 const,不可含 .Replace())
        private const string ChunksDir =
            "C:/Users/ccmak/Desktop/OpenCode Workplace/ios-app/codebase/unity-project/Assets/StreamingAssets/Chunks";
        private const string ManifestUrl =
            "file:///C:/Users/ccmak/Desktop/OpenCode%20Workplace/ios-app/codebase/unity-project/Assets/StreamingAssets/Chunks/manifest.json";

        public static void Run()
        {
            Debug.Log("[TestChunkLoad] 開始(批次測試,非 Play mode)");
            // batchmode 下 -executeMethod 返回後會立刻 -quit;必須在此方法內同步推進,
            // 直到測試完成或逾時。用 EditorApplication.Step() 推進 Editor 主迴圈
            // (讓 UnityWebRequest / Task 有機會跑)。
            _timeoutAt = Time.realtimeSinceStartup + 60f;
            while (_phase != Phase.Done && Time.realtimeSinceStartup < _timeoutAt)
            {
                Step();
                EditorApplication.Step();
            }
            if (_phase != Phase.Done)
            {
                Debug.LogError($"[TestChunkLoad] 逾時(階段={_phase})");
                Finish(false);
            }
        }

        private enum Phase { LoadManifest, ParseManifest, LoadFirstChunk, Instantiate, Dispose, Done }
        private static Phase _phase = Phase.LoadManifest;
        private static UnityWebRequest _req;
        private static object _manifest;           // ChunkManifest(反射)
        private static System.Collections.IList _chunksList;
        private static GltfImport _gltf;
        private static float _timeoutAt;

        private static void Step()
        {
            switch (_phase)
            {
                case Phase.LoadManifest:
                    if (_req == null)
                    {
                        Debug.Log("[TestChunkLoad] 載入 manifest...");
                        _req = UnityWebRequest.Get(ManifestUrl);
                        _req.SendWebRequest();
                    }
                    if (_req.isDone)
                    {
                        if (_req.result != UnityWebRequest.Result.Success)
                        {
                            Debug.LogError($"[TestChunkLoad] manifest 下載失敗: {_req.error}");
                            Finish(false); return;
                        }
                        _phase = Phase.ParseManifest;
                    }
                    break;

                case Phase.ParseManifest:
                    try
                    {
                        // 反射取 ChunkManifest 型別(在 Assembly-CSharp 中)
                        Type cmType = null;
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            cmType = asm.GetType("HKGrid.Streaming.ChunkManifest");
                            if (cmType != null) break;
                        }
                        if (cmType == null)
                        {
                            Debug.LogError("[TestChunkLoad] 找不到 HKGrid.Streaming.ChunkManifest 型別");
                            Finish(false); return;
                        }
                        _manifest = JsonUtility.FromJson(_req.downloadHandler.text, cmType);
                        if (_manifest == null)
                        {
                            Debug.LogError("[TestChunkLoad] JsonUtility 反序列化失敗");
                            Finish(false); return;
                        }

                        // 反射讀欄位
                        var t = cmType;
                        string source = (string)t.GetField("source").GetValue(_manifest);
                        string crs = (string)t.GetField("crs").GetValue(_manifest);
                        float[] origin = (float[])t.GetField("origin").GetValue(_manifest);
                        float[] tileSize = (float[])t.GetField("tileSize").GetValue(_manifest);
                        int gridCols = (int)t.GetField("gridCols").GetValue(_manifest);
                        int gridRows = (int)t.GetField("gridRows").GetValue(_manifest);
                        int chunkCount = (int)t.GetField("chunkCount").GetValue(_manifest);
                        long totalBytes = (long)t.GetField("totalBytes").GetValue(_manifest);
                        long totalTris = (long)t.GetField("totalTriangles").GetValue(_manifest);
                        string attribution = (string)t.GetField("attribution").GetValue(_manifest);
                        _chunksList = (System.Collections.IList)t.GetField("chunks").GetValue(_manifest);

                        Debug.Log($"[TestChunkLoad] manifest 解析成功:");
                        Debug.Log($"  source       = {source}");
                        Debug.Log($"  crs          = {crs}");
                        Debug.Log($"  origin       = [{string.Join(", ", origin)}]");
                        Debug.Log($"  tileSize     = [{string.Join(", ", tileSize)}]");
                        Debug.Log($"  grid         = {gridCols} x {gridRows}");
                        Debug.Log($"  chunkCount   = {chunkCount}");
                        Debug.Log($"  totalBytes   = {totalBytes:N0}");
                        Debug.Log($"  totalTris    = {totalTris:N0}");
                        Debug.Log($"  attribution  = {attribution}");
                        Debug.Log($"  chunks list  = {_chunksList.Count} 筆(應等於 chunkCount)");

                        if (_chunksList == null || _chunksList.Count != chunkCount)
                        {
                            Debug.LogError($"[TestChunkLoad] chunks 清單長度不符:manifest={chunkCount}, 實際={_chunksList?.Count ?? 0}");
                            Finish(false); return;
                        }

                        // 反射讀第一塊的欄位
                        var firstChunk = _chunksList[0];
                        var ct = firstChunk.GetType();
                        string cid = (string)ct.GetField("id").GetValue(firstChunk);
                        string cfile = (string)ct.GetField("file").GetValue(firstChunk);
                        long cbytes = (long)ct.GetField("bytes").GetValue(firstChunk);
                        int ctris = (int)ct.GetField("triangles").GetValue(firstChunk);
                        Debug.Log($"  first chunk  = id={cid} file={cfile} bytes={cbytes} tris={ctris}");

                        _phase = Phase.LoadFirstChunk;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TestChunkLoad] manifest 解析例外: {e}");
                        Finish(false); return;
                    }
                    break;

                case Phase.LoadFirstChunk:
                    if (_gltf == null)
                    {
                        var firstChunk = _chunksList[0];
                        string chunkFile = (string)firstChunk.GetType().GetField("file").GetValue(firstChunk);
                        string full = Path.Combine(ChunksDir, chunkFile);
                        if (!File.Exists(full))
                        {
                            Debug.LogError($"[TestChunkLoad] 分塊檔不存在: {full}");
                            Finish(false); return;
                        }
                        Debug.Log($"[TestChunkLoad] 載入分塊: {chunkFile} ({new FileInfo(full).Length / 1e6:F2} MB)");
                        _gltf = new GltfImport(downloadProvider: null, deferAgent: null, materialGenerator: null, logger: new ConsoleLogger());
                        var task = _gltf.LoadFile(full);
                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                Debug.LogError($"[TestChunkLoad] LoadFile 例外: {t.Exception}");
                                _phase = Phase.Done;
                                EditorApplication.delayCall += () => Finish(false);
                            }
                            else if (!t.Result)
                            {
                                Debug.LogError("[TestChunkLoad] LoadFile 回傳 false");
                                _phase = Phase.Done;
                                EditorApplication.delayCall += () => Finish(false);
                            }
                            else
                            {
                                Debug.Log("[TestChunkLoad] LoadFile 成功");
                                _phase = Phase.Instantiate;
                            }
                        });
                    }
                    break;

                case Phase.Instantiate:
                    {
                        var tempGo = new GameObject("TestChunk_Temp");
                        var inst = new GameObjectInstantiator(_gltf, tempGo.transform, new ConsoleLogger());
                        var task = _gltf.InstantiateMainSceneAsync(inst);
                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                Debug.LogError($"[TestChunkLoad] InstantiateMainSceneAsync 例外: {t.Exception}");
                                UnityEngine.Object.DestroyImmediate(tempGo);
                                _phase = Phase.Done;
                                EditorApplication.delayCall += () => Finish(false);
                            }
                            else if (!t.Result)
                            {
                                Debug.LogError("[TestChunkLoad] InstantiateMainSceneAsync 回傳 false");
                                UnityEngine.Object.DestroyImmediate(tempGo);
                                _phase = Phase.Done;
                                EditorApplication.delayCall += () => Finish(false);
                            }
                            else
                            {
                                int childCount = tempGo.transform.childCount;
                                var renderers = tempGo.GetComponentsInChildren<MeshRenderer>();
                                var filters = tempGo.GetComponentsInChildren<MeshFilter>();
                                Debug.Log($"[TestChunkLoad] InstantiateMainSceneAsync 成功");
                                Debug.Log($"  child transforms = {childCount}");
                                Debug.Log($"  MeshRenderers     = {renderers.Length}");
                                Debug.Log($"  MeshFilters       = {filters.Length}");
                                if (filters.Length > 0)
                                {
                                    var m = filters[0].sharedMesh;
                                    if (m != null)
                                    {
                                        Debug.Log($"  first mesh: verts={m.vertexCount}, tris={m.triangles.Length/3}, submeshes={m.subMeshCount}");
                                    }
                                }
                                UnityEngine.Object.DestroyImmediate(tempGo);
                                _phase = Phase.Dispose;
                            }
                        });
                    }
                    break;

                case Phase.Dispose:
                    Debug.Log("[TestChunkLoad] Dispose GltfImport...");
                    _gltf?.Dispose();
                    Debug.Log("[TestChunkLoad] 完成 — Stage B 資料層串流鏈路驗證通過");
                    Finish(true);
                    break;
            }
        }

        private static void Finish(bool success)
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            Debug.Log($"[TestChunkLoad] 結束: {(success ? "PASS" : "FAIL")}");
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
