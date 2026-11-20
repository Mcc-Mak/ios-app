// ChunkManifest.cs — Stage A manifest.json 的 Unity 端資料模型。
//
// 對應 codebase/converter/convert_osgb_to_chunks.py 產出的 manifest.json schema。
// 以 JsonUtility 反序列化(Unity 內建,不需 Newtonsoft)。
//
// 注意:本檔為 Stage B 鷹架(scaffold),未經 Unity 編譯驗證。
// 安裝 Unity 6 LTS 後首次開啟專案時需驗證編譯。
//
// 座標系:manifest 的 bbox/centroid 為「本地座標」(已相對全圖幅 origin 置中),
// 單位為公尺,EPSG:2326(HK 1980 Grid)。X=東, Y=北, Z=上(Z-up)。
// Unity 為 Y-up,載入時由 ChunkStreamer 套用 -90° X 旋轉轉換。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HKGrid.Streaming
{
    /// <summary>
    /// Stage A manifest.json 的根模型。由 <see cref="ChunkStreamer"/> 載入。
    /// </summary>
    [Serializable]
    public class ChunkManifest
    {
        public string version;
        public string source;          // 圖幅名,如 "11-NW-15B"
        public string crs;             // "EPSG:2326"
        public float[] origin;         // 全圖幅中心(HK1980 公尺),作為本地原點偏移
        public float[] tileSize;       // [X, Y, Z] 全圖幅尺寸(公尺)
        public int gridCols;
        public int gridRows;
        public int chunkCount;
        public long totalBytes;
        public long totalTriangles;
        public string attribution;     // © 香港特別行政區地政總署 ...
        public List<ChunkRecord> chunks;

        /// <summary>origin 轉成 Vector3(東, 北, 上)。</summary>
        public Vector3 OriginVector => ToVec3(origin);

        /// <summary>以 chunk id 為鍵的查詢表(惰性建立)。</summary>
        private Dictionary<string, ChunkRecord> _byId;
        public ChunkRecord GetById(string id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, ChunkRecord>(chunks.Count);
                foreach (var c in chunks) _byId[c.id] = c;
            }
            return _byId.TryGetValue(id, out var r) ? r : null;
        }

        public static Vector3 ToVec3(float[] a) => new Vector3(a[0], a[1], a[2]);
    }

    /// <summary>manifest.json 中單一分塊的記錄。</summary>
    [Serializable]
    public class ChunkRecord
    {
        public string id;              // "x245_y143" 或合併後 "15B_x245_y143"
        public int grid_x;
        public int grid_y;
        public float[] bbox_min;       // 本地座標(已置中),公尺
        public float[] bbox_max;
        public float[] centroid;       // 本地座標(已置中),公尺
        public float[] positionOffset; // 合併多圖幅時的圖幅偏移(相對全域原點);單圖幅為 [0,0,0]
        public string file;            // "chunk_x245_y143.glb"
        public long bytes;
        public int triangles;
        public List<string> neighbors; // 相鄰 chunk id
        public int priority;
        public string tile;            // 合併時的圖幅代號(如 "15B");單圖幅可能為空

        public Vector3 CentroidVector => ChunkManifest.ToVec3(centroid);
        public Vector3 BboxMinVector => ChunkManifest.ToVec3(bbox_min);
        public Vector3 BboxMaxVector => ChunkManifest.ToVec3(bbox_max);

        /// <summary>合併多圖幅時的圖幅偏移(相對全域原點)。單圖幅為 Vector3.zero。</summary>
        public Vector3 PositionOffsetVector =>
            positionOffset != null && positionOffset.Length >= 3
                ? ChunkManifest.ToVec3(positionOffset)
                : Vector3.zero;

        /// <summary>本地座標的半徑(用於載入距離判斷的包圍球)。</summary>
        public float BoundingRadius =>
            Vector3.Distance(BboxMinVector, BboxMaxVector) * 0.5f;
    }
}
