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
        [Tooltip("是否顯示覆蓋層(Editor 中可關閉)")]
        public bool show = true;

        private readonly List<string> _lines = new List<string>();
        private GUIStyle _style;
        private GUIStyle _bgStyle;
        private bool _styleInit;
        private int _loadedChunks;
        private int _loadingChunks;
        private string _manifestStatus = "manifest 未載入";
        private Vector3 _lastCamPos;
        private float _lastCamPosUpdate;

        // 顯示用:攝影機位置(每 0.1s 更新一次,避免每幀重繪)
        private string _camPosText = "";

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

        private void OnGUI()
        {
            if (!show) return;
            InitStyles();

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
