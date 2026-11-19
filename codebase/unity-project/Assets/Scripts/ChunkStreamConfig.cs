// ChunkStreamConfig.cs — Stage B 串流器的可設定參數(ScriptableObject)。
//
// 於 Unity 中以 Assets > Create > HKGrid > ChunkStreamConfig 建立設定資產,
// 指派給 ChunkStreamBootstrapper。所有路徑與半徑皆可在此調整,無寫死值。

using UnityEngine;

namespace HKGrid.Streaming
{
    [CreateAssetMenu(fileName = "ChunkStreamConfig", menuName = "HKGrid/ChunkStreamConfig", order = 0)]
    public class ChunkStreamConfig : ScriptableObject
    {
        [Header("分塊資料來源")]
        [Tooltip("相對於 StreamingAssets 的分塊目錄。完整路徑 = Application.streamingAssetsPath + '/' + 此值")]
        public string chunksRelativePath = "Chunks";

        [Tooltip("manifest.json 檔名(位於 chunksRelativePath 下)")]
        public string manifestFileName = "manifest.json";

        [Header("空間串流半徑(公尺,本地座標)")]
        [Tooltip("攝影機在此半徑內的分塊會被載入")]
        public float loadRadius = 350f;

        [Tooltip("超過此半徑的已載入分塊會被卸載。應 > loadRadius 以避免邊界抖動")]
        public float unloadRadius = 500f;

        [Tooltip("每幀最多新增載入的分塊數(避免一次載入太多造成卡頓)")]
        public int maxLoadsPerFrame = 1;

        [Tooltip("每幀最多卸載的分塊數")]
        public int maxUnloadsPerFrame = 2;

        [Header("glTFast 載入")]
        [Tooltip("每幀 glTFast 載入預算,為「目標幀時間的比例」(0.0–1.0)。0.1 = 占目標幀時間 10%。TimeBudgetPerFrameDeferAgent.SetFrameBudget 的參數。行動裝置建議 0.1–0.25")]
        [Range(0.01f, 1f)]
        public float gltfFrameBudget = 0.2f; // 占目標幀時間 20%(30 FPS 下約 6.6 ms)

        [Tooltip("是否在載入時快取 GltfImport(同一檔案重複載入較快)。行動裝置記憶體有限,預設關閉")]
        public bool cacheGltfImports = false;

        [Header("座標轉換")]
        [Tooltip("HK 網格為 Z-up(EPSG:2326),Unity 為 Y-up。套用 -90° X 旋轉將 grid-Z(上)對應 Unity-Y(上)")]
        public bool applyZupToYupRotation = true;

        [Header("除錯")]
        public bool drawChunkGizmos = false;
        public bool logStreamingEvents = false;
    }
}
