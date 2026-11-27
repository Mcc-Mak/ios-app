// DebugOverlay.cs — v2.2.15: iOS 螢幕診斷覆蓋層
//
// 在 iPhone 螢幕上顯示 Debug.Log 訊息 + 關鍵狀態(manifest 載入、分塊載入、
// 攝影機位置),讓無法連接 Xcode 主控台時仍能診斷藍畫面問題。
//
// 用法:由 SetupScene.cs 自動掛載至攝影機。不需手動設定。
// 顯示內容:
//   - 最近 N 筆 Debug.Log/LogWarning/LogError 訊息(捲動)
//   - 攝影機位置(每幀更新)
//   - 分塊載入計數(由 ChunkStreamer 更新)
//   - manifest 載入狀態

using System.Collections.Generic;
using UnityEngine;

namespace HKGrid.Debugging
{
    /// <summary>
    /// 螢幕診斷覆蓋層:捕捉 Application.logMessageReceived 並顯示為 UI 文字。
    /// 在 iOS 裝置上無 Xcode 主控台時,這是唯一的執行時診斷管道。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DebugOverlay : MonoBehaviour
    {
        [Header("顯示設定")]
        [Tooltip("螢幕上顯示的最大訊息行數")]
        public int maxLines = 25;
        [Tooltip("字型大小(螢幕高度比例,0.02 = 2%)")]
        public float fontSizeRatio = 0.022f;
        [Tooltip("是否顯示覆蓋層(預設隱藏;可用齒輪按鈕或 Q 鍵開啟)")]
        public bool show = false;

        // v2.5.0: 齒輪按鈕螢幕矩形(供 TouchPlayerController 排除該區域觸控)
        public static Rect GearButtonRect = Rect.zero;

        private readonly List<string> _lines = new List<string>();
        private GUIStyle _style;
        private GUIStyle _bgStyle;
        private GUIStyle _gearStyle;
        private bool _styleInit;
        private int _loadedChunks;
        private int _loadingChunks;
        private string _manifestStatus = "manifest 未載入";
        private Vector3 _lastCamPos;
        private float _lastCamPosUpdate;

        // 顯示用:攝影機位置(每 0.1s 更新一次,避免每幀重繪)
        private string _camPosText = "";

        // v2.6.5: L 鍵羅盤方位顯示(暫時顯示 5 秒,即使覆蓋層隱藏也可見)
        private string _compassText = "";
        private float _compassShowUntil;

        // v2.6.5: 北朝上小地圖(顯示各圖幅矩形 + 連接關係 + 玩家位置/面向)
        // - L 鍵:暫時顯示 5 秒(即使覆蓋層隱藏也可見)
        // - M 鍵:持續切換顯示/隱藏(即使覆蓋層隱藏也可見)
        // 圖幅資料由 ChunkStreamBootstrapper 於 manifest 載入時注入一次(SetMiniMapTiles);
        // 玩家位置/面向由本元件每幀自行更新(本元件掛於攝影機上)。
        private struct MiniMapTile { public string code; public Vector3 worldCenter; public Vector2 worldSizeXZ; }
        private readonly List<MiniMapTile> _mmTiles = new List<MiniMapTile>();
        private Vector3 _mmPlayer;
        private float _mmBearing;          // 玩家面向方位(0=北,90=東,180=南,270=西)
        private bool _mmTilesReady;        // 圖幅資料已注入
        private bool _mmPersistent;        // M 鍵持續顯示
        private float _mmShowUntil;        // L 鍵暫時顯示截止時間

        /// <summary>由 Bootstrapper 於 manifest 載入後呼叫一次:注入圖幅世界中心 + 尺寸(供小地圖繪製)。</summary>
        public void SetMiniMapTiles(System.Collections.Generic.List<string> codes,
                                    System.Collections.Generic.List<Vector3> worldCenters,
                                    System.Collections.Generic.List<Vector2> worldSizesXZ)
        {
            _mmTiles.Clear();
            int n = Mathf.Min(codes.Count, Mathf.Min(worldCenters.Count, worldSizesXZ.Count));
            for (int i = 0; i < n; i++)
                _mmTiles.Add(new MiniMapTile { code = codes[i], worldCenter = worldCenters[i], worldSizeXZ = worldSizesXZ[i] });
            _mmTilesReady = _mmTiles.Count > 0;
        }

        private void Awake()
        {
            // v2.6.5: 執行時強制預設隱藏(覆寫場景殘留的序列化 show=true)。
            // Awake 只在元件實體化時執行一次,不會影響後續齒輪按鈕/Q 鍵的切換。
            // 此為「預設隱藏」的執行時保證,涵蓋 IPA 與 Unity Play 模式
            // (SetupScene.Build 僅於匯出時執行,不涵蓋 Editor Play,故需此保險)。
            show = false;
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            // 只保留訊息本身(不含 stack trace),避免佔滿螢幕
            string prefix = type == LogType.Error ? "[E] " :
                            type == LogType.Warning ? "[W] " : "";
            string line = prefix + logString;
            // 截斷過長行
            if (line.Length > 200) line = line.Substring(0, 200) + "...";
            _lines.Add(line);
            while (_lines.Count > maxLines)
                _lines.RemoveAt(0);
        }

        /// <summary>由 Bootstrapper 呼叫:更新 manifest 載入狀態。</summary>
        public void SetManifestStatus(string status)
        {
            _manifestStatus = status;
        }

        /// <summary>由 Bootstrapper/Streamer 呼叫:更新分塊計數。</summary>
        public void SetChunkCounts(int loaded, int loading)
        {
            _loadedChunks = loaded;
            _loadingChunks = loading;
        }

        private void Update()
        {
            // Q 鍵切換覆蓋層顯示/隱藏(桌面 Unity Editor 用;iOS 無實體鍵盤不觸發)
            if (Input.GetKeyDown(KeyCode.Q))
            {
                show = !show;
                Debug.Log("[DebugOverlay] 覆蓋層:" + (show ? "顯示" : "隱藏"));
            }
            if (Time.unscaledTime - _lastCamPosUpdate > 0.1f)
            {
                _lastCamPosUpdate = Time.unscaledTime;
                var p = Camera.main != null ? Camera.main.transform.position : transform.position;
                if (Vector3.Distance(p, _lastCamPos) > 0.01f)
                {
                    _lastCamPos = p;
                    _camPosText = "cam=(" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ", " + p.z.ToString("F1") + ")";
                }
            }

            // v2.6.5: 每幀更新小地圖用的玩家位置 + 面向方位(供 M 鍵持續顯示)
            if (_mmTilesReady)
            {
                _mmPlayer = transform.position;
                Vector3 f = transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 0.0001f)
                {
                    float b = Mathf.Atan2(f.x, -f.z) * Mathf.Rad2Deg;
                    if (b < 0f) b += 360f;
                    _mmBearing = b;
                }
            }

            // v2.6.5: M 鍵切換小地圖持續顯示/隱藏(即使覆蓋層隱藏也可見)
            if (Input.GetKeyDown(KeyCode.M))
            {
                _mmPersistent = !_mmPersistent;
                Debug.Log("[DebugOverlay] 小地圖(M):" + (_mmPersistent ? "顯示" : "隱藏"));
            }

            // v2.6.5: L 鍵顯示面對的羅盤方位(暫時顯示 5 秒;即使覆蓋層隱藏也可見)
            if (Input.GetKeyDown(KeyCode.L))
            {
                Vector3 fwd = transform.forward;
                Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude < 0.0001f)
                {
                    _compassText = "面向:正下方(俯視)";
                }
                else
                {
                    flat.Normalize();
                    // Unity 世界座標(Z-up→Y-up 旋轉後):+X=東, +Z=南, -Z=北
                    // bearing: 0°=北(-Z), 90°=東(+X), 180°=南(+Z), 270°=西(-X)
                    float bearing = Mathf.Atan2(flat.x, -flat.z) * Mathf.Rad2Deg;
                    if (bearing < 0f) bearing += 360f;
                    string[] dirs = { "北 (N)", "東北 (NE)", "東 (E)", "東南 (SE)", "南 (S)", "西南 (SW)", "西 (W)", "西北 (NW)" };
                    int idx = Mathf.RoundToInt(bearing / 45f) % 8;
                    Vector3 p = transform.position;
                    // 提示螢幕左右與地理方向的對應(面南時左右翻轉,易誤判東西)
                    string screenHint;
                    if (bearing > 135f && bearing < 225f)
                        screenHint = "面南:螢幕左=東, 右=西";
                    else if (bearing > 315f || bearing < 45f)
                        screenHint = "面北:螢幕左=西, 右=東";
                    else if (bearing >= 45f && bearing <= 135f)
                        screenHint = "面東:螢幕左=北, 右=南";
                    else
                        screenHint = "面西:螢幕左=南, 右=北";
                    _compassText = $"面向:{dirs[idx]} ({bearing:F0}°)\n" +
                                   $"位置: X={p.x:F0}(東+), Z={p.z:F0}(南+)\n" +
                                   screenHint;
                    _mmBearing = bearing;
                }
                _compassShowUntil = Time.unscaledTime + 5f;
                _mmShowUntil = Time.unscaledTime + 5f;   // 小地圖也暫時顯示 5 秒
                Debug.Log("[DebugOverlay] " + _compassText.Replace("\n", " | "));
            }
        }

        private void InitStyles()
        {
            if (_styleInit) return;
            int fontSize = Mathf.Max(10, Mathf.RoundToInt(Screen.height * fontSizeRatio));

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };
            _style.normal.textColor = Color.white;

            _bgStyle = new GUIStyle();
            _bgStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.65f));

            // v2.5.0: 齒輪按鈕樣式(半透明深色圓鈕)
            _gearStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(20, Mathf.RoundToInt(Screen.height * 0.03f)),
                alignment = TextAnchor.MiddleCenter,
            };
            _gearStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.5f));
            _gearStyle.normal.textColor = new Color(0.85f, 0.88f, 0.92f, 0.95f);
            _gearStyle.hover = _gearStyle.normal;

            _styleInit = true;
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>v2.6.5: 小地圖用 — 於指定中心繪製實心圓點(用 Texture2D.whiteTexture)。</summary>
        private static void DrawDot(Vector2 center, Color col, float radius)
        {
            var prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
                            Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>v2.6.5: 小地圖用 — 繪製線段(以白色材質旋轉拉伸)。</summary>
        private static void DrawLine(Vector2 a, Vector2 b, Color col, float width)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = col;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        /// <summary>v2.6.5: 小地圖用 — 繪製矩形外框(四條線)。</summary>
        private static void DrawRectOutline(Rect r, Color col, float width)
        {
            var tl = new Vector2(r.x, r.y);
            var tr = new Vector2(r.xMax, r.y);
            var bl = new Vector2(r.x, r.yMax);
            var br = new Vector2(r.xMax, r.yMax);
            DrawLine(tl, tr, col, width);
            DrawLine(tr, br, col, width);
            DrawLine(br, bl, col, width);
            DrawLine(bl, tl, col, width);
        }

        private void OnGUI()
        {
            InitStyles();

            // v2.5.0: 齒輪按鈕 — 永遠顯示(即使覆蓋層隱藏),點擊切換顯示/隱藏
            float gearSize = Mathf.Max(44f, Screen.height * 0.06f);
            float gearMargin = Screen.height * 0.02f;
            var gearRect = new Rect(Screen.width - gearSize - gearMargin, gearMargin, gearSize, gearSize);
            GearButtonRect = gearRect;
            if (GUI.Button(gearRect, "\u2699", _gearStyle))
            {
                show = !show;
                Debug.Log("[DebugOverlay] 覆蓋層:" + (show ? "顯示" : "隱藏"));
            }

            // v2.6.5: L 鍵羅盤方位顯示(即使覆蓋層隱藏也顯示,暫時 5 秒)
            if (Time.unscaledTime < _compassShowUntil)
            {
                float cw = Screen.width * 0.7f;
                float ch = Screen.height * 0.14f;
                var cr = new Rect((Screen.width - cw) / 2f, Screen.height * 0.10f, cw, ch);
                GUI.Box(cr, GUIContent.none, _bgStyle);
                var cs = new GUIStyle(_style)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.028f))
                };
                GUI.Label(cr, _compassText, cs);
            }

            // v2.6.5: 北朝上小地圖(L 鍵暫時 5 秒 / M 鍵持續切換;即使覆蓋層隱藏也可見)
            // 以「北朝上」平面圖呈現各圖幅矩形 + 連接關係 + 玩家位置/面向,
            // 無歧義地顯示圖幅間的相對位置(避免面南時螢幕左右與地理東西相反的誤判)。
            bool mmShow = (Time.unscaledTime < _mmShowUntil || _mmPersistent) && _mmTilesReady;
            if (mmShow)
            {
                float mmSize = Mathf.Min(Screen.width, Screen.height) * 0.46f;
                var mmRect = new Rect((Screen.width - mmSize) / 2f, Screen.height * 0.28f, mmSize, mmSize);
                GUI.Box(mmRect, GUIContent.none, _bgStyle);

                // bounds:東=X, 北=-Z(含圖幅矩形範圍 + 玩家)
                float minE = float.MaxValue, maxE = float.MinValue;
                float minN = float.MaxValue, maxN = float.MinValue;
                foreach (var t in _mmTiles)
                {
                    float e = t.worldCenter.x, n = -t.worldCenter.z;
                    float he = t.worldSizeXZ.x * 0.5f, hn = t.worldSizeXZ.y * 0.5f;
                    minE = Mathf.Min(minE, e - he); maxE = Mathf.Max(maxE, e + he);
                    minN = Mathf.Min(minN, n - hn); maxN = Mathf.Max(maxN, n + hn);
                }
                {
                    float e = _mmPlayer.x, n = -_mmPlayer.z;
                    minE = Mathf.Min(minE, e); maxE = Mathf.Max(maxE, e);
                    minN = Mathf.Min(minN, n); maxN = Mathf.Max(maxN, n);
                }
                float padE = (maxE - minE) * 0.12f + 1f; minE -= padE; maxE += padE;
                float padN = (maxN - minN) * 0.12f + 1f; minN -= padN; maxN += padN;

                float pad = mmSize * 0.11f;
                float innerX = mmRect.x + pad, innerY = mmRect.y + pad;
                float innerW = mmRect.width - pad * 2f, innerH = mmRect.height - pad * 2f;

                System.Func<float, float, Vector2> toScreen = (east, north) =>
                {
                    float sx = innerX + (east - minE) / (maxE - minE) * innerW;
                    float sy = innerY + (maxN - north) / (maxN - minN) * innerH;  // 北朝上:北在頂
                    return new Vector2(sx, sy);
                };

                var dirStyle = new GUIStyle(_style)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height * 0.018f))
                };
                dirStyle.normal.textColor = new Color(0.8f, 0.85f, 0.9f, 0.9f);
                string title = _mmPersistent ? "圖幅連接圖(北朝上) [M 切換]" : "圖幅連接圖(北朝上)";
                GUI.Label(new Rect(mmRect.x, mmRect.y - pad * 0.3f, mmRect.width, pad * 0.8f), title, dirStyle);
                GUI.Label(new Rect(mmRect.x, mmRect.y + pad * 0.2f, mmRect.width, pad * 0.7f), "北 N", dirStyle);
                GUI.Label(new Rect(mmRect.x, mmRect.yMax - pad * 0.9f, mmRect.width, pad * 0.7f), "南 S", dirStyle);
                GUI.Label(new Rect(mmRect.x, mmRect.y + pad, pad, mmRect.height - pad * 2f), "西W", dirStyle);
                GUI.Label(new Rect(mmRect.xMax - pad, mmRect.y + pad, pad, mmRect.height - pad * 2f), "東E", dirStyle);

                // 圖幅間連接線(相鄰圖幅中心相連,顯示格網連接關係)
                for (int i = 0; i < _mmTiles.Count; i++)
                {
                    for (int j = i + 1; j < _mmTiles.Count; j++)
                    {
                        var a = toScreen(_mmTiles[i].worldCenter.x, -_mmTiles[i].worldCenter.z);
                        var b = toScreen(_mmTiles[j].worldCenter.x, -_mmTiles[j].worldCenter.z);
                        DrawLine(a, b, new Color(0.5f, 0.6f, 0.7f, 0.5f), 2f);
                    }
                }

                // 圖幅矩形(藍框 + 半透明藍底 + 代碼標籤)
                var tileStyle = new GUIStyle(dirStyle) { fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.020f)) };
                tileStyle.normal.textColor = new Color(0.55f, 0.88f, 1f);
                foreach (var t in _mmTiles)
                {
                    float e = t.worldCenter.x, n = -t.worldCenter.z;
                    float he = t.worldSizeXZ.x * 0.5f, hn = t.worldSizeXZ.y * 0.5f;
                    var tl = toScreen(e - he, n + hn);   // 左上(西北角)
                    var br = toScreen(e + he, n - hn);   // 右下(東南角)
                    var tileRect = new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
                    // 半透明藍底
                    var prevC = GUI.color;
                    GUI.color = new Color(0.2f, 0.45f, 0.7f, 0.25f);
                    GUI.DrawTexture(tileRect, Texture2D.whiteTexture);
                    GUI.color = prevC;
                    // 藍框
                    DrawRectOutline(tileRect, new Color(0.45f, 0.82f, 1f, 0.9f), 2f);
                    // 代碼標籤(置中)
                    GUI.Label(tileRect, t.code, tileStyle);
                }

                // 玩家(黃點 + 面向箭頭)
                {
                    var ps = toScreen(_mmPlayer.x, -_mmPlayer.z);
                    DrawDot(ps, new Color(1f, 0.85f, 0.2f), mmSize * 0.022f);
                    // 面向箭頭:bearing 0=北(上),90=東(右),180=南(下),270=西(左)
                    float rad = _mmBearing * Mathf.Deg2Rad;
                    Vector2 arrowDir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));  // 螢幕座標(Y 下)
                    float arrowLen = mmSize * 0.06f;
                    var pe = ps + arrowDir * arrowLen;
                    DrawLine(ps, pe, new Color(1f, 0.85f, 0.2f), 3f);
                    var plStyle = new GUIStyle(dirStyle) { fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.020f)) };
                    plStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
                    GUI.Label(new Rect(ps.x - pad, ps.y - pad * 1.6f, pad * 2f, pad * 0.8f), "你", plStyle);
                }
            }

            if (!show) return;

            // 螢幕左上角,佔寬度的 60%、高度的 55%
            float w = Screen.width * 0.6f;
            float h = Screen.height * 0.55f;
            float margin = Screen.height * 0.01f;
            var rect = new Rect(margin, margin, w, h);

            // 半透明背景
            GUI.Box(rect, GUIContent.none, _bgStyle);

            // 狀態行(置頂,粗體)
            var statusRect = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
            string status = "<b>" + _manifestStatus + "</b>\n" +
                            "已載入 " + _loadedChunks + " 塊 / 載入中 " + _loadingChunks + " 塊\n" +
                            "<b>" + _camPosText + "</b>\n" +
                            "────────────────\n";
            string allText = status;
            // 最近的訊息在底部
            int start = Mathf.Max(0, _lines.Count - (maxLines - 4));
            for (int i = start; i < _lines.Count; i++)
            {
                // 標記錯誤為紅色、警告為黃色
                string l = _lines[i];
                if (l.StartsWith("[E] ")) l = "<color=#ff6666>" + l + "</color>";
                else if (l.StartsWith("[W] ")) l = "<color=#ffcc66>" + l + "</color>";
                allText += l + "\n";
            }

            GUI.Label(statusRect, allText, _style);
        }
    }
}
