// TouchPlayerController.cs — 第一人稱玩家控制器:觸控搖桿移動 + 右側拖曳視角(pan/tilt)
// + 捏合縮放(zoom/FOV)+ 地面跟隨射線。
//
// 取代 FlyCameraController(僅 WASD + 滑鼠,在 iPhone 無輸入 → 藍屏)。
//
// 控制方式:
//   桌面(Editor/Standalone):WASD 移動 + 滑鼠右鍵拖曳視角 + 滾輪縮放(Shift 衝刺)。
//   行動(觸控):
//     - 左下角固定搖桿(拖曳移動,放開回中)
//     - 右半屏單指拖曳 → pan/tilt 視角(非反轉:上拖=上看)
//     - 雙指捏合 → zoom(FOV,捏合拉近=放大,拉開=縮小)
//
// 地面跟隨:每幀從攝影機上方向下射線,保持攝影機在地面 + eyeHeight 高度。
// 需要分塊 MeshCollider(ChunkStreamer 載入時加入,見 addChunkColliders 設定)。
//
// 起始位置:startPosition 為合併世界座標(相對於全域原點 15A origin)。
// 預設 (947, 124, 0) = WGS84 [22.3319, 114.1871](九龍城區)。

using UnityEngine;
using UnityEngine.UI;

namespace HKGrid.Streaming
{
    /// <summary>
    /// 第一人稱玩家控制器。自動偵測平台:觸控(iPhone)或桌面(Editor)。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TouchPlayerController : MonoBehaviour
    {
        [Header("起始位置")]
        [Tooltip("起始位置(Unity 世界座標,Y-up。已套用 Z-up→Y-up 旋轉)。預設 = WGS84 [22.3319, 114.1871] 九龍城區")]
        public Vector3 startPosition = new Vector3(947f, 0f, -124f);
        [Tooltip("起始水平視角(Yaw,度)。0=+Z/南,90=+X/東")]
        public float startYaw = 0f;
        [Tooltip("起始俯角(Pitch,度)。正值=向下看,看到地面/城市")]
        public float startPitch = 25f;
        [Header("移動")]
        [Tooltip("步行速度(m/s)")]
        public float moveSpeed = 5f;
        [Tooltip("衝刺倍率(桌面 Shift / 行動雙擊搖桿)")]
        public float sprintMultiplier = 2f;

        [Header("視角(pan/tilt)")]
        public float lookSensitivityTouch = 0.4f;
        public float lookSensitivityMouse = 2f;
        public float minPitch = -85f;
        public float maxPitch = 85f;

        [Header("縮放(FOV)")]
        public float defaultFov = 60f;
        public float minFov = 25f;     // 放大(zoom in / ADS)
        public float maxFov = 90f;     // 縮小(zoom out)
        public float zoomSpeedTouch = 0.08f;
        public float zoomSpeedMouse = 15f;

        [Header("地面跟隨")]
        [Tooltip("玩家眼睛高度(m)")]
        public float eyeHeight = 1.7f;
        [Tooltip("從攝影機上方此高度向下射線(確保射線起點在地面之上)")]
        public float groundRaycastHeight = 100f;
        [Tooltip("射線最大長度(m)")]
        public float groundRaycastLength = 500f;
        [Tooltip("地面 Y 平滑速度(值越大越貼合地面,越小越平滑)")]
        public float groundFollowLerp = 10f;
        [Tooltip("地面圖層遮罩(預設全部;分塊 MeshCollider 在預設圖層)")]
        public LayerMask groundMask = ~0;

        [Header("觸控搖桿")]
        [Tooltip("搖桿半徑(像素)")]
        public float joystickRadius = 120f;
        [Tooltip("搖桿死區(低於此值視為無輸入)")]
        public float joystickDeadzone = 0.1f;
        [Tooltip("搖桿固定位置:距左邊緣像素")]
        public float joystickMarginX = 160f;
        [Tooltip("搖桿固定位置:距下邊緣像素")]
        public float joystickMarginY = 160f;

        // ── 內部狀態 ──
        private Camera _cam;
        private bool _isMobile;
        private float _yaw;
        private float _pitch;

        // 觸控搖桿
        private int _moveFingerId = -1;
        private Vector2 _joystickCenter;
        private Vector2 _joystickVector = Vector2.zero;

        // 觸控視角
        private int _lookFingerId = -1;
        private Vector2 _lastLookPos;

        // 搖桿 UI
        private Image _joystickBaseImg;
        private Image _joystickThumbImg;
        private Sprite _circleSprite;

        // 地面跟隨
        private bool _groundFound = false;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _isMobile = Application.isMobilePlatform || Input.touchSupported;
            _cam.fieldOfView = defaultFov;

            // 起始位置(合併世界座標)與視角(俯視城市,非水平看天空)
            _yaw = startYaw;
            _pitch = startPitch;
            transform.position = startPosition;

            // 若舊版 FlyCameraController 仍在攝影機上,停用之
            var fly = GetComponent<FlyCameraController>();
            if (fly != null) fly.enabled = false;

            if (_isMobile)
            {
                CreateJoystickUI();
            }
        }

        private void Update()
        {
            if (_isMobile)
                HandleTouchInput();
            else
                HandleDesktopInput();

            ApplyLook();
            ApplyMovement();
            ApplyGroundFollow();
            ApplyZoomDesktop();
        }

        // ── 輸入處理 ──────────────────────────────────────────

        private void HandleDesktopInput()
        {
            // 視角:滑鼠右鍵拖曳(非反轉:上拖=上看)
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * lookSensitivityMouse;
                _pitch -= Input.GetAxis("Mouse Y") * lookSensitivityMouse;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            // 縮放:滑鼠滾輪
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _cam.fieldOfView = Mathf.Clamp(
                    _cam.fieldOfView - scroll * zoomSpeedMouse, minFov, maxFov);
            }
        }

        private void HandleTouchInput()
        {
            // 雙指 → 捏合縮放(優先於視角)
            if (Input.touchCount >= 2)
            {
                HandlePinch();
                // 捏合期間仍可移動(左手指持續搖桿)
                if (_moveFingerId != -1)
                {
                    Touch moveTouch = GetTouchByFingerId(_moveFingerId);
                    if (moveTouch.phase == TouchPhase.Moved || moveTouch.phase == TouchPhase.Stationary)
                        UpdateJoystick(moveTouch.position);
                }
                return;
            }

            // 單指:分左右半屏
            foreach (Touch t in Input.touches)
            {
                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (t.position.x < Screen.width * 0.5f && _moveFingerId == -1)
                        {
                            _moveFingerId = t.fingerId;
                            // 搖桿固定在左下角(joystickMarginX, joystickMarginY from bottom-left)
                            _joystickCenter = new Vector2(joystickMarginX, joystickMarginY);
                            ShowJoystick(_joystickCenter);
                            UpdateJoystick(t.position);
                        }
                        else if (t.position.x >= Screen.width * 0.5f && _lookFingerId == -1)
                        {
                            _lookFingerId = t.fingerId;
                            _lastLookPos = t.position;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (t.fingerId == _moveFingerId)
                            UpdateJoystick(t.position);
                        else if (t.fingerId == _lookFingerId)
                            UpdateLook(t.position);
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (t.fingerId == _moveFingerId)
                        {
                            _moveFingerId = -1;
                            _joystickVector = Vector2.zero;
                            HideJoystick();
                        }
                        else if (t.fingerId == _lookFingerId)
                        {
                            _lookFingerId = -1;
                        }
                        break;
                }
            }
        }

        private void HandlePinch()
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            float currDist = Vector2.Distance(t0.position, t1.position);
            float prevDist = Vector2.Distance(
                t0.position - t0.deltaPosition,
                t1.position - t1.deltaPosition);

            if (prevDist < 1f) return;

            // 捏合拉近(兩指距離縮小)→ delta 正 → FOV 降 → 放大
            float delta = prevDist - currDist;
            _cam.fieldOfView = Mathf.Clamp(
                _cam.fieldOfView + delta * zoomSpeedTouch, minFov, maxFov);
        }

        private void UpdateLook(Vector2 touchPos)
        {
            Vector2 delta = touchPos - _lastLookPos;
            _lastLookPos = touchPos;
            _yaw += delta.x * lookSensitivityTouch;
            // 非反轉:手指上拖(delta.y<0)= 抬頭看上方(pitch 增加)
            // 但 touch 座標 Y 向下增長,所以 pitch += (-delta.y) * sens
            // 標準 FPS:上拖=上看。delta.y 為負(向上)= pitch 應增加。
            _pitch += -delta.y * lookSensitivityTouch;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private Touch GetTouchByFingerId(int fingerId)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).fingerId == fingerId)
                    return Input.GetTouch(i);
            }
            return default;
        }

        // ── 應用:視角 / 移動 / 地面跟隨 / 縮放 ────────────────

        private void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void ApplyMovement()
        {
            Vector2 input;
            if (_isMobile)
            {
                input = _joystickVector;
            }
            else
            {
                input.x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                input.y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            }

            if (input.sqrMagnitude < 0.0001f) return;

            // 移動方向基於 yaw(水平面),Y 由地面跟隨控制
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 forward = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            float speed = moveSpeed;
            if (!_isMobile && Input.GetKey(KeyCode.LeftShift))
                speed *= sprintMultiplier;

            Vector3 moveDir = (forward * input.y + right * input.x);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            // 只在水平面移動(XZ),Y 由地面跟隨控制
            Vector3 pos = transform.position;
            pos.x += moveDir.x * speed * Time.deltaTime;
            pos.z += moveDir.z * speed * Time.deltaTime;
            transform.position = pos;
        }

        private void ApplyGroundFollow()
        {
            Vector3 origin = transform.position + Vector3.up * groundRaycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                groundRaycastHeight + groundRaycastLength, groundMask))
            {
                float targetY = hit.point.y + eyeHeight;
                if (!_groundFound)
                {
                    // 首次找到地面:直接吸附(不 lerp,避免從高空緩慢下降)
                    Vector3 p = transform.position;
                    p.y = targetY;
                    transform.position = p;
                    _groundFound = true;
                }
                else
                {
                    Vector3 p = transform.position;
                    p.y = Mathf.Lerp(p.y, targetY, Time.deltaTime * groundFollowLerp);
                    transform.position = p;
                }
            }
            // 若射線未命中(分塊尚未載入),保持當前高度不變
        }

        private void ApplyZoomDesktop()
        {
            // 桌面縮放在 HandleDesktopInput 已處理;此方法保留供未來擴充
        }

        // ── 搖桿 UI(行動專用,執行時建立,不需 prefab)──────────

        private void CreateJoystickUI()
        {
            // 生成圓形 sprite(不需外部資源)
            if (_circleSprite == null)
                _circleSprite = MakeCircleSprite(64);

            var canvasGo = new GameObject("JoystickCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>();
            // 搖桿僅為視覺;輸入由 Input.touches 處理,不需 GraphicRaycaster

            // 搖桿底盤
            var baseGo = new GameObject("JoystickBase");
            baseGo.transform.SetParent(canvasGo.transform, false);
            _joystickBaseImg = baseGo.AddComponent<Image>();
            _joystickBaseImg.sprite = _circleSprite;
            _joystickBaseImg.color = new Color(1f, 1f, 1f, 0.2f);
            _joystickBaseImg.rectTransform.sizeDelta = Vector2.one * joystickRadius * 2f;
            _joystickBaseImg.raycastTarget = false;

            // 搖桿拇指
            var thumbGo = new GameObject("JoystickThumb");
            thumbGo.transform.SetParent(baseGo.transform, false);
            _joystickThumbImg = thumbGo.AddComponent<Image>();
            _joystickThumbImg.sprite = _circleSprite;
            _joystickThumbImg.color = new Color(1f, 1f, 1f, 0.5f);
            _joystickThumbImg.rectTransform.sizeDelta = Vector2.one * joystickRadius * 0.7f;
            _joystickThumbImg.raycastTarget = false;

            // 搖桿固定在左下角,持續顯示
            PositionJoystickAtBottomLeft();
            _joystickBaseImg.gameObject.SetActive(true);
        }

        private void PositionJoystickAtBottomLeft()
        {
            //螢幕座標:(joystickMarginX, joystickMarginY) from bottom-left
            _joystickBaseImg.transform.position = new Vector3(joystickMarginX, joystickMarginY, 0f);
            _joystickThumbImg.rectTransform.localPosition = Vector3.zero;
        }

        private void ShowJoystick(Vector2 screenPos)
        {
            if (_joystickBaseImg == null) return;
            _joystickBaseImg.gameObject.SetActive(true);
            _joystickBaseImg.transform.position = new Vector3(joystickMarginX, joystickMarginY, 0f);
            _joystickThumbImg.rectTransform.localPosition = Vector3.zero;
        }

        private void HideJoystick()
        {
            // 搖桿保持顯示(固定位置),拇指回中
            if (_joystickThumbImg != null)
                _joystickThumbImg.rectTransform.localPosition = Vector3.zero;
        }

        private void UpdateJoystick(Vector2 touchPos)
        {
            Vector2 delta = touchPos - _joystickCenter;
            float dist = delta.magnitude;
            if (dist > joystickRadius)
                delta = delta.normalized * joystickRadius;

            if (_joystickThumbImg != null)
                _joystickThumbImg.rectTransform.localPosition = delta;

            Vector2 normalized = delta / joystickRadius;
            if (normalized.magnitude < joystickDeadzone)
                normalized = Vector2.zero;
            _joystickVector = normalized;
        }

        private static Sprite MakeCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "JoystickCircle";
            float center = size / 2f;
            float radius = size / 2f - 1f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = d <= radius
                        ? new Color(1f, 1f, 1f, 1f)
                        : new Color(0f, 0f, 0f, 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
