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
        [Tooltip("攝影機在此半徑內的分塊會被載入。v2.2.3: 400m 限制可視範圍," +
                 "降低 iPhone 渲染負載(原 1000m 導致 iPhone 17e 卡死)")]
        public float loadRadius = 400f;

        [Tooltip("超過此半徑的已載入分塊會被卸載。應 > loadRadius + 一個分塊寬度(~150m)以避免邊界抖動")]
        public float unloadRadius = 550f;

        [Tooltip("攝影機遠裁剪面(公尺)。超過此距離的分塊不渲染。v2.2.3: 450m 搭配霧遮蔽彈出")]
        public float cameraFarClip = 450f;

        [Tooltip("每幀最多新增載入的分塊數(避免一次載入太多造成卡頓)")]
        public int maxLoadsPerFrame = 1;

        [Tooltip("每幀最多卸載的分塊數")]
        public int maxUnloadsPerFrame = 2;

        [Header("glTFast 載入")]
        [Tooltip("每幀 glTFast 載入預算,為「目標幀時間的比例」(0.0–1.0)。0.1 = 占目標幀時間 10%。TimeBudgetPerFrameDeferAgent.SetFrameBudget 的參數。行動裝置建議 0.1–0.25")]
        [Range(0.01f, 1f)]
        public float gltfFrameBudget = 0.15f; // v2.2.3: 20%→15%,降低載入卡頓

        [Tooltip("是否在載入時快取 GltfImport(同一檔案重複載入較快)。行動裝置記憶體有限,預設關閉")]
        public bool cacheGltfImports = false;

        [Header("座標轉換")]
        [Tooltip("HK 網格為 Z-up(EPSG:2326),Unity 為 Y-up。套用 -90° X 旋轉將 grid-Z(上)對應 Unity-Y(上)")]
        public bool applyZupToYupRotation = true;

        [Header("碰撞器(地面跟隨射線)")]
        [Tooltip("是否為載入的分塊加入 MeshCollider(供 TouchPlayerController 地面跟隨射線使用)。"
                 + "效能代價較高(L18 每塊 ~70K 面),但 FPS 地面行走必需。關閉則玩家懸空。")]
        public bool addChunkColliders = true;

        [Header("材質")]
        [Tooltip("v2.2.5: 強制雙面渲染(Cull Mode: Off)。修正從背面/內側看穿牆壁與地面問題。" +
                 "分塊載入後覆寫所有材質的 _Cull=0。輕微增加 GPU 負載。")]
        public bool forceDoubleSided = true;

        [Header("v2.2.9: 地面填補(插值高度場,修補洞孔)")]
        [Tooltip("啟用後建立插值高度場網格,跟隨地形起伏填補攝影測量洞孔(視覺+碰撞)。" +
                 "以各分塊 bbox_min[2](最低地形)為控制點,IDW 插值產生高度場。" +
                 "玩家不會穿過洞孔墜落;洞孔可見時看到地面色而非天空。")]
        public bool groundFillEnabled = true;
        [Tooltip("地面填補顏色(中性地面色)")]
        public Color groundFillColor = new Color(0.35f, 0.37f, 0.28f, 1f);
        [Tooltip("高度場低於插值地形的距離(m),避免與現有網格相交")]
        public float groundFillYOffset = 5f;
        [Tooltip("高度場邊界外擴(m),確保覆蓋整個可視範圍")]
        public float groundFillMargin = 200f;
        [Tooltip("高度場網格解析度(每邊格子數;越大越平滑但頂點越多)")]
        public int groundFillGridResolution = 48;

        [Header("除錯")]
        public bool drawChunkGizmos = false;
        public bool logStreamingEvents = false;
    }
}
