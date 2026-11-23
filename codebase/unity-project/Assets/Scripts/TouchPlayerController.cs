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
    /// 使用 CharacterController 處理建物/樹木碰撞(水平),射線地面跟隨處理垂直。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(CharacterController))]
    public class TouchPlayerController : MonoBehaviour
    {
        [Header("起始位置")]
        [Tooltip("起始位置(Unity 世界座標,Y-up。已套用 Z-up→Y-up 旋轉)。預設 = WGS84 [22.3319, 114.1871] 九龍城區")]
        public Vector3 startPosition = new Vector3(947f, 0f, -124f);
        [Tooltip("起始水平視角(Yaw,度)。0=+Z/南,90=+X/東")]
        public float startYaw = 0f;
        [Tooltip("起始俯角(Pitch,度)。正值=向下看,看到地面/城市")]
        public float startPitch = 55f;
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
        [Tooltip("玩家眼睛高度(m),CharacterController 膠囊頂部 = 眼睛")]
        public float eyeHeight = 1.7f;
        [Tooltip("地面搜尋射線起點:眼睛上方此高度(避免打到鄰近建物屋頂)")]
        public float groundRaycastHeight = 2f;
        [Tooltip("射線最大長度(m),從起點向下搜尋地面")]
        public float groundRaycastLength = 250f;
        [Tooltip("每幀 Y 最大移動速度(m/s),避免瞬間吸附到屋頂/樹冠")]
        public float maxYSnapPerFrame = 80f;
        [Tooltip("失去地面時的重力加速度(m/s²)")]
        public float gravity = -9.81f;
        [Tooltip("掉落至此 Y 以下視為穿模,重生至起始位置")]
        public float fallRespawnY = -150f;
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
        [Tooltip("v2.2.4: 搖桿視角靈敏度(度/秒)。搖桿控制 pan/tilt 而非移動")]
        public float joystickLookSpeed = 90f;

        // ── 內部狀態 ──
        private Camera _cam;
        private CharacterController _cc;
        private bool _isMobile;
        private float _yaw;
        private float _pitch;
        private float _verticalVel = 0f;  // 重力垂直速度

        // 地面跟隨射線命中緩衝(靜態,避免每幀 GC)
        private static readonly RaycastHit[] _rayHits = new RaycastHit[8];

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

        // 地面跟隨:首次找到地面前懸停(等分塊載入),之後失去地面才施重力
        private bool _groundEverFound = false;
        // v2.2.3: 由 ChunkStreamBootstrapper 在 manifest 載入後呼叫 OnSpawned() 啟動
        private bool _spawned = false;
        // v2.2.3: 最後已知地面 Y(穿模重生用,取代固定 startPosition 重生)
        private float _lastGroundY = 0f;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _isMobile = Application.isMobilePlatform || Input.touchSupported;
            _cam.fieldOfView = defaultFov;
            _cam.farClipPlane = 450f;  // v2.2.3: 限制可視距離,不渲染視線外分塊

            // ── 覆寫過時的場景序列化值 ──
            // v2.2.3: 不再硬編 startPosition(單圖幅/多圖幅 manifest 原點不同)。
            //   改由 ChunkStreamBootstrapper 載入 manifest 後透過 OnSpawned() 設定。
            //   舊值 groundRaycastLength=15/50 太短,從 spawn 高處打不到地面 → 懸空墜落。
            groundRaycastHeight = 2f;
            groundRaycastLength = 250f;

            // 確保 CharacterController 存在(舊場景可能無此元件;
            // RequireComponent 在載入已存場景時不一定自動補上)
            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                Debug.Log("[TouchPlayerController] CharacterController 缺失,已於執行時補上");
            }

            // 設定 CharacterController 膠囊:頂部對齊眼睛(transform.position),向下延伸 eyeHeight
            _cc.height = eyeHeight + 0.3f;   // 略高於眼睛,涵蓋頭頂
            _cc.radius = 0.3f;               // 薄膠囊,可穿過門
            _cc.center = new Vector3(0f, -(_cc.height * 0.5f) + 0.15f, 0f); // 底部在腳,頂部略高於眼
            _cc.stepOffset = 0.4f;
            _cc.slopeLimit = 75f;            // 攝影測量地形坡度大,放寬

            // 停用 Bootstrapper 的起始位置覆寫(舊場景 overrideStartPos=true 會把攝影機
            // 設到 (0,80,0);v2.2.3 由 Bootstrapper.Start 載入 manifest 後透過 OnSpawned 處理)
            var boot = GetComponent<ChunkStreamBootstrapper>();
            if (boot != null) boot.overrideStartPos = false;

            // 起始視角(俯視城市,非水平看天空)
            _yaw = startYaw;
            _pitch = startPitch;
            // v2.2.3: 不在 Awake 設定 transform.position;由 Bootstrapper.OnSpawned() 處理
            _spawned = false;

            // 若舊版 FlyCameraController 仍在攝影機上,停用之
            var fly = GetComponent<FlyCameraController>();
            if (fly != null) fly.enabled = false;

            if (_isMobile)
            {
                CreateJoystickUI();
            }
        }

        /// <summary>
        /// v2.2.3: 由 ChunkStreamBootstrapper 在 manifest 載入後呼叫。
        /// 將攝影機定位至 worldPos 上方,做長射線找地面,然後啟動地面跟隨。
        /// 解決:單圖幅(15B-only)與多圖幅(merged)manifest 原點不同,
        /// 硬編 startPosition 在單圖幅時落在圖幅外 → 無分塊載入 → 無地面 → 墜落。
        /// </summary>
        public void OnSpawned(Vector3 worldPos)
        {
            // 從目標位置上方 15m 開始(v2.2.4: 原 50m 太高,看天空=藍屏)
            Vector3 spawnPos = worldPos + Vector3.up * 15f;
            transform.position = spawnPos;

            // 長射線找地面(500m 搜尋範圍,涵蓋任何 spawn 高度差)
            Vector3 rayOrigin = new Vector3(worldPos.x, spawnPos.y, worldPos.z);
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, _rayHits, 500f, groundMask);
            RaycastHit bestHit = default;
            bool foundGround = false;
            float closestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (_rayHits[i].collider == _cc) continue;
                if (_rayHits[i].distance < closestDist)
                {
                    closestDist = _rayHits[i].distance;
                    bestHit = _rayHits[i];
                    foundGround = true;
                }
            }

            if (foundGround)
            {
                transform.position = new Vector3(worldPos.x, bestHit.point.y + eyeHeight, worldPos.z);
                _lastGroundY = bestHit.point.y;
                _groundEverFound = true;
                Debug.Log($"[TouchPlayerController] OnSpawned: 地面 Y={bestHit.point.y:F1}, 攝影機 Y={transform.position.y:F1}");
            }
            else
            {
                // 分塊尚未載入(非同步 glTFast);先懸停,等 ApplyMovement 地面跟隨找到地面
                _groundEverFound = false;
                Debug.Log("[TouchPlayerController] OnSpawned: 地面未找到(分塊載入中),懸停等待");
            }

            _verticalVel = 0f;
            _spawned = true;
        }

        private void Update()
        {
            if (_isMobile)
                HandleTouchInput();
            else
                HandleDesktopInput();

            // v2.2.4: 左搖桿 = pan/tilt(非移動)。搖桿向量 → yaw/pitch 旋轉速度
            if (_isMobile && _joystickVector.sqrMagnitude > joystickDeadzone * joystickDeadzone)
            {
                _yaw += _joystickVector.x * joystickLookSpeed * Time.deltaTime;
                _pitch -= _joystickVector.y * joystickLookSpeed * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            // v2.2.5: 按 L 鍵印出攝影機座標(Unity Editor 除錯用)
            if (Input.GetKeyDown(KeyCode.L))
            {
                Vector3 p = transform.position;
                Debug.Log($"[TouchPlayerController] 座標: X={p.x:F2}, Y={p.y:F2}, Z={p.z:F2} | " +
                          $"yaw={_yaw:F1}, pitch={_pitch:F1} | FOV={_cam.fieldOfView:F1}");
            }

            ApplyLook();
            if (_spawned)
                ApplyMovement();  // 含 CharacterController 碰撞 + 地面跟隨(v2.2.3: 等 OnSpawned 後才啟動)
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
                // 捏合期間仍可 pan/tilt(左手指持續搖桿)
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
                // v2.2.4: 行動裝置無移動(搖桿改為 pan/tilt,不再控制移動)
                input = Vector2.zero;
            }
            else
            {
                input.x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                input.y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            }

            // 水平移動方向基於 yaw,Y 由地面跟隨控制
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 forward = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            float speed = moveSpeed;
            if (!_isMobile && Input.GetKey(KeyCode.LeftShift))
                speed *= sprintMultiplier;

            Vector3 moveDir = (forward * input.y + right * input.x);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            Vector3 horizontal = moveDir * speed * Time.deltaTime;

            // ── 地面跟隨(射線起點在眼睛上方 2m,向下搜尋;跳過自身膠囊)──
            // 修正:舊版從 100m 高處射線會打到鄰近建物屋頂→玩家瞬移到屋頂。
            // 新版起點接近玩家(2m),僅搜尋腳下地面(50m),避免誤觸屋頂/樹冠。
            // 此外,射線可能打到自身 CharacterController 膠囊(頂部在眼睛上方 0.15m),
            // 導致 targetY = 攝影機Y + 1.85 → 每幀上升 →「看上方=飄起」。
            // 使用 RaycastNonAlloc 並跳過自身 collider 修正此問題。
            float rayOriginY = transform.position.y + groundRaycastHeight;
            Vector3 rayOrigin = new Vector3(transform.position.x, rayOriginY, transform.position.z);

            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down,
                _rayHits, groundRaycastLength, groundMask);
            RaycastHit bestHit = default;
            bool foundGround = false;
            float closestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (_rayHits[i].collider == _cc) continue;  // 跳過自身 CharacterController 膠囊
                if (_rayHits[i].distance < closestDist)
                {
                    closestDist = _rayHits[i].distance;
                    bestHit = _rayHits[i];
                    foundGround = true;
                }
            }

            float deltaY;
            if (foundGround)
            {
                float targetY = bestHit.point.y + eyeHeight;
                deltaY = targetY - transform.position.y;
                // 限幅:每幀最多移動 maxYSnapPerFrame 速度(m/s × dt)
                deltaY = Mathf.Clamp(deltaY, -maxYSnapPerFrame * Time.deltaTime,
                                             maxYSnapPerFrame * Time.deltaTime);
                _verticalVel = 0f;  // 著地,重設垂直速度
                _groundEverFound = true;
                _lastGroundY = bestHit.point.y;  // v2.2.3: 追蹤最後已知地面(穿模重生用)
            }
            else if (_groundEverFound)
            {
                // 曾找到地面但現在失去(走到邊緣/分塊卸載):施加重力緩慢下墜
                _verticalVel += gravity * Time.deltaTime;
                if (_verticalVel < -30f) _verticalVel = -30f; // 限速
                deltaY = _verticalVel * Time.deltaTime;
            }
            else
            {
                // v2.2.4: 首次找到地面前,緩慢下降尋找地面(非懸停)
                // 原懸停(deltaY=0)導致攝影機停留在高空看天空=藍屏
                deltaY = -5f * Time.deltaTime;
            }

            Vector3 move = horizontal + Vector3.up * deltaY;
            _cc.Move(move);  // CharacterController 處理水平碰撞(牆/建物/樹)

            // v2.2.3: 智慧重生——掉至最後已知地面 50m 以下時,重生至該地面(非固定 startPosition)
            // 修正「左鍵點擊後墜落穿模→從天空重生」問題:改用 lastGroundY 而非 startPosition+30
            if (_groundEverFound && transform.position.y < _lastGroundY - 50f)
            {
                transform.position = new Vector3(transform.position.x,
                    _lastGroundY + eyeHeight + 5f, transform.position.z);
                _verticalVel = 0f;
                Debug.Log($"[TouchPlayerController] 穿模重生: Y={transform.position.y:F1} (lastGroundY={_lastGroundY:F1})");
            }
            // 緊急重生:掉至絕對最低點(分塊完全未載入 / lastGroundY 無效時)
            else if (transform.position.y < fallRespawnY)
            {
                float respawnY = _groundEverFound ? _lastGroundY + eyeHeight + 30f : 100f;
                transform.position = new Vector3(transform.position.x, respawnY, transform.position.z);
                _verticalVel = 0f;
            }
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
