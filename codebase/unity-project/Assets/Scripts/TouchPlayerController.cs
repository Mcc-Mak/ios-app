// TouchPlayerController.cs — 第一人稱玩家控制器:v2.6.0 雙隱形搖桿 + 地面跟隨射線。
//
// 取代舊版 FlyCameraController(僅 WASD + 滑鼠,在 iPhone 無輸入 → 藍屏;已刪除)。
//
// 控制方式:
//   桌面(Editor/Standalone):WASD 移動 + 滑鼠右鍵拖曳視角 + 滾輪縮放(Shift 衝刺)+ SPACE 跳躍。
//   行動(觸控 v2.6.0 / v2.6.2):
//     - 左半屏隱形搖桿 → 移動(觸控起點=搖桿原點,拖曳方向=移動方向)
//     - 右半屏隱形搖桿 → pan/tilt 視角(類比:推越遠轉越快)
//     - 右半屏雙擊 → 跳躍(v2.6.2:由左側改為右側;兩次輕擊間隔 < mobileJumpDoubleTapTime)
//
// 地面跟隨:每幀從攝影機上方向下射線,保持攝影機在地面 + eyeHeight 高度。
// 需要分塊 MeshCollider(ChunkStreamer 載入時加入,見 addChunkColliders 設定)。
//
// 起始位置:startPosition 為合併世界座標(相對於全域原點)。
// 預設由 ChunkStreamBootstrapper 依 manifest 中心自動決定。

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
        [Tooltip("步行速度(m/s)。v2.2.6: 1.75x = 8.75")]
        public float moveSpeed = 8.75f;
        [Tooltip("衝刺倍率(桌面 Shift / 行動雙擊搖桿)")]
        public float sprintMultiplier = 2f;

        [Header("跳躍(v2.2.13)")]
        [Tooltip("跳躍初速(m/s)。5 m/s ≈ 1.3m 高度(g=9.81)。僅地面狀態可跳(單跳,無二段跳)")]
        public float jumpSpeed = 5f;
        [Tooltip("跳躍冷卻時間(秒)。著地後需等待此時間才能再跳。v2.6.7:設為 0 停用冷卻(著地即可再跳)")]
        public float jumpCooldown = 0f;

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

        [Header("觸控搖桿(v2.6.0:雙隱形搖桿)")]
        [Tooltip("搖桿半徑(像素)。觸控起點=搖桿原點(隱形,不顯示)")]
        public float joystickRadius = 120f;
        [Tooltip("搖桿死區(低於此值視為無輸入)")]
        public float joystickDeadzone = 0.1f;
        [Tooltip("v2.6.0: 右側視角搖桿靈敏度(度/秒,類比:推越遠轉越快)")]
        public float lookJoystickSpeed = 120f;
        [Tooltip("v2.6.0: 是否顯示搖桿視覺(底盤+拇指)。false=隱形(預設),輸入仍正常")]
        public bool showJoystickUI = false;

        [Header("跳躍(行動 v2.6.0 / v2.6.2)")]
        [Tooltip("v2.6.2: 行動裝置跳躍:右側雙擊間隔(秒)。0=停用行動跳躍")]
        public float mobileJumpDoubleTapTime = 0.3f;
        [Tooltip("v2.6.0: 雙擊判定最大位移(像素)。移動少於此值視為點擊")]
        public float doubleTapMaxMove = 20f;

        // ── 內部狀態 ──
        private Camera _cam;
        private CharacterController _cc;
        private bool _isMobile;
        private float _yaw;
        private float _pitch;
        private float _verticalVel = 0f;  // 重力垂直速度

        // 地面跟隨射線命中緩衝(靜態,避免每幀 GC)
        private static readonly RaycastHit[] _rayHits = new RaycastHit[8];

        // 觸控搖桿(v2.6.0:雙隱形搖桿——左=移動,右=視角)
        private int _moveFingerId = -1;
        private Vector2 _joystickCenter;
        private Vector2 _joystickVector = Vector2.zero;

        // 觸控視角(v2.6.0:右半屏隱形搖桿,類比)
        private int _lookFingerId = -1;
        private Vector2 _lookCenter;
        private Vector2 _lookVector = Vector2.zero;

        // 搖桿 UI(v2.6.0:隱形——showJoystickUI=false 時不顯示)
        private Image _joystickBaseImg;
        private Image _joystickThumbImg;
        private Sprite _circleSprite;
        private GameObject _joystickCanvas;

        // 地面跟隨:首次找到地面前懸停(等分塊載入),之後失去地面才施重力
        private bool _groundEverFound = false;
        // v2.2.3: 由 ChunkStreamBootstrapper 在 manifest 載入後呼叫 OnSpawned() 啟動
        private bool _spawned = false;
        // v2.2.3: 最後已知地面 Y(穿模重生用,取代固定 startPosition 重生)
        private float _lastGroundY = 0f;
        // v2.2.8: 手動起始 Y(精確座標),OnSpawned 直接使用 worldPos.y 不做射線覆寫
        private bool _useManualSpawnY = false;
        // v2.2.8: 握住位置等分塊載入(非下降)。修正:精確座標 spawn 時下降會穿過地面,
        // 之後向下射線永遠找不到地面 → 永久墜落 = 「在網格外」
        private bool _holdUntilGround = false;
        // v2.2.13: 跳躍狀態
        private bool _isGrounded = false;       // 目前在地面(可跳)
        private bool _isJumping = false;        // 跳躍中(上升或下降,尚未著地)
        private float _lastJumpTime = -Mathf.Infinity;  // 上次跳躍時間(Time.time)
        // v2.6.2: 行動跳躍——右側單指雙擊偵測(由左側改為右側)
        private float _lastTapTime = -Mathf.Infinity;   // 上次單擊時間
        private Vector2 _lastTapPos;                     // 上次單擊位置
        private bool _mobileJumpQueued = false;          // 本幀待處理的行動跳躍請求
        // v2.6.2: 右側視角觸控起點(雙擊判定用——放開時比較位移)
        private Vector2 _touchStartPos;
        private float _touchStartTime;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _isMobile = Application.isMobilePlatform || Input.touchSupported;

            // v2.2.7: 強制套用起始值(覆寫場景序列化的舊值,確保 Editor Play 即時生效)。
            // v2.2.8: 恢復原始自動定位(manifest 中心),放棄手動地下座標。
            // pitch/FOV 恢復原始值(原值是為手動位置視角設定,不再適用)。
            startYaw = 0f;
            startPitch = 55f;
            defaultFov = 60f;
            eyeHeight = 1.7f;
            moveSpeed = 8.75f;      // v2.2.6: 1.75x(原 5 m/s),使用者明確要求保留

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
            _cc.stepOffset = 0.6f;            // v2.2.14: 0.4→0.6,可踏回 0.5m 以下的地面落差(填補面/真實網格邊界)
            _cc.slopeLimit = 75f;            // 攝影測量地形坡度大,放寬

            // 停用 Bootstrapper 的舊式起始位置覆寫(overrideStartPos)。
            // v2.2.8: 恢復原始 manifest 中心自動定位(放棄手動地下座標)。
            var boot = GetComponent<ChunkStreamBootstrapper>();
            if (boot != null)
            {
                boot.overrideStartPos = false;
                boot.useManualStartWorldPos = false;
            }
            _useManualSpawnY = false;

            // 起始視角(俯視城市,非水平看天空)
            _yaw = startYaw;
            _pitch = startPitch;
            // v2.2.3: 不在 Awake 設定 transform.position;由 Bootstrapper.OnSpawned() 處理
            _spawned = false;

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
            // v2.2.8: 手動指定起始 Y(精確座標)——直接使用 worldPos.y,不做 +5 偏移與射線覆寫。
            // 攝影機落在使用者按 L 鍵記錄的精確位置;地面跟隨由 ApplyMovement 每幀射線接手。
            if (_useManualSpawnY)
            {
                // v2.2.9: 使用精確 X/Z,但 Y 由雙向射線找實際地面。
                // 使用者指定的 Y 可能在地下(記錄時可能已在穿模狀態)。
                transform.position = worldPos;
                _verticalVel = 0f;
                _spawned = true;

                // 向下射線(正常情況:地面在 spawn 下方)
                Vector3 downOrigin = new Vector3(worldPos.x, worldPos.y + groundRaycastHeight, worldPos.z);
                int downHits = Physics.RaycastNonAlloc(downOrigin, Vector3.down, _rayHits, groundRaycastLength, groundMask);
                float bestDownY = float.NaN;
                float closestDown = float.MaxValue;
                for (int i = 0; i < downHits; i++)
                {
                    if (_rayHits[i].collider == _cc) continue;
                    if (_rayHits[i].distance < closestDown) { closestDown = _rayHits[i].distance; bestDownY = _rayHits[i].point.y; }
                }

                // 向上射線(spawn 在地下:地面在 spawn 上方)
                Vector3 upOrigin = new Vector3(worldPos.x, worldPos.y - groundRaycastHeight, worldPos.z);
                int upHits = Physics.RaycastNonAlloc(upOrigin, Vector3.up, _rayHits, groundRaycastLength, groundMask);
                float bestUpY = float.NaN;
                float closestUp = float.MaxValue;
                for (int i = 0; i < upHits; i++)
                {
                    if (_rayHits[i].collider == _cc) continue;
                    if (_rayHits[i].distance < closestUp) { closestUp = _rayHits[i].distance; bestUpY = _rayHits[i].point.y; }
                }

                if (!float.IsNaN(bestDownY))
                {
                    // 地面在下方:正常落地
                    transform.position = new Vector3(worldPos.x, bestDownY + eyeHeight, worldPos.z);
                    _groundEverFound = true; _holdUntilGround = false; _lastGroundY = bestDownY;
                    _isGrounded = true;  // v2.2.13
                    Debug.Log($"[TouchPlayerController] OnSpawned: 地面在下方 Y={bestDownY:F1}, 攝影機 Y={transform.position.y:F1}");
                }
                else if (!float.IsNaN(bestUpY))
                {
                    // 地面在上方:spawn 原在地下,移至地面頂部 + eyeHeight
                    transform.position = new Vector3(worldPos.x, bestUpY + eyeHeight, worldPos.z);
                    _groundEverFound = true; _holdUntilGround = false; _lastGroundY = bestUpY;
                    _isGrounded = true;  // v2.2.13
                    Debug.Log($"[TouchPlayerController] OnSpawned: 地面在上方 Y={bestUpY:F1}(spawn 原在地下), 攝影機 Y={transform.position.y:F1}");
                }
                else
                {
                    // 分塊尚未載入:握住位置,等 ApplyMovement 雙向搜尋找到地面
                    _groundEverFound = false; _holdUntilGround = true; _lastGroundY = worldPos.y - eyeHeight;
                    Debug.Log("[TouchPlayerController] OnSpawned: 地面未找到(分塊載入中),握住等待雙向搜尋");
                }
                return;
            }

            // 從目標位置上方 5m 開始(v2.2.7: 原 15m;玩家從 5m 高處落下至地面+eyeHeight)
            Vector3 spawnPos = worldPos + Vector3.up * 5f;
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
                _isGrounded = true;  // v2.2.13
                Debug.Log($"[TouchPlayerController] OnSpawned: 地面 Y={bestHit.point.y:F1}, 攝影機 Y={transform.position.y:F1}, " +
                          $"collider='{bestHit.collider?.gameObject.name}'");
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
            {
                HandleTouchInput();
                // v2.6.0: 右側隱形搖桿 = 類比視角(推越遠轉越快),每幀套用
                if (_lookVector.sqrMagnitude > joystickDeadzone * joystickDeadzone)
                {
                    _yaw += _lookVector.x * lookJoystickSpeed * Time.deltaTime;
                    _pitch += -_lookVector.y * lookJoystickSpeed * Time.deltaTime;
                    _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
                }
            }
            else
                HandleDesktopInput();

            // v2.6.0: 左側搖桿 = 移動。移動在 ApplyMovement 中套用。

            // v2.2.5: 按 L 鍵印出攝影機座標 + 面向方向(Unity Editor 除錯用)
            // v2.6.5: 新增羅盤方位(北/東/南/西),方便判斷螢幕左右對應的地理方向
            if (Input.GetKeyDown(KeyCode.L))
            {
                Vector3 p = transform.position;
                // 羅盤方位:yaw=0→南(S), 90→東(E), 180→北(N), 270→西(W)
                // bearing = 180 - yaw(正規化至 0–360°)
                float bearing = ((180f - _yaw) % 360f + 360f) % 360f;
                string[] dirs = { "北 (N)", "東北 (NE)", "東 (E)", "東南 (SE)", "南 (S)", "西南 (SW)", "西 (W)", "西北 (NW)" };
                int idx = Mathf.RoundToInt(bearing / 45f) % 8;
                Debug.Log($"[TouchPlayerController] 座標: X={p.x:F2}(東+), Y={p.y:F2}(高), Z={p.z:F2}(南+) | " +
                          $"面向:{dirs[idx]} ({bearing:F0}°) | yaw={_yaw:F1}, pitch={_pitch:F1} | FOV={_cam.fieldOfView:F1}");
            }

            // v2.2.13: 跳躍輸入(桌面 SPACE;v2.6.2: 行動右側雙擊)
            // 僅地面狀態 + 冷卻結束可跳;單跳(無二段跳)
            bool jumpPressed = false;
            if (!_isMobile && Input.GetKeyDown(KeyCode.Space))
                jumpPressed = true;
            // v2.6.2: 行動跳躍由 HandleTouchInput 右側雙擊偵測設定 _mobileJumpQueued
            if (_isMobile && _mobileJumpQueued)
            {
                jumpPressed = true;
                _mobileJumpQueued = false;
            }
            if (jumpPressed && _isGrounded && !_isJumping)
            {
                if (Time.time - _lastJumpTime >= jumpCooldown)
                {
                    _verticalVel = jumpSpeed;   // 設定向上初速
                    _isJumping = true;
                    _isGrounded = false;
                    _lastJumpTime = Time.time;
                    Debug.Log($"[TouchPlayerController] 跳躍: verticalVel={_verticalVel:F2} m/s (mobile={_isMobile})");
                }
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
            // v2.6.0: 雙隱形搖桿——左半屏=移動,右半屏=視角(pan/tilt)
            //   右半屏雙擊=跳躍(v2.6.2:由左側改為右側)。齒輪按鈕(右上)區域排除。
            //   搖桿搖桿隱形:觸控起點=搖桿原點,拖曳方向=輸入方向(不顯示視覺)
            float halfWidth = Screen.width * 0.5f;

            foreach (Touch t in Input.touches)
            {
                // 跳過齒輪按鈕區域的觸控(讓 OnGUI 按鈕接收)
                if (IsTouchOnGearButton(t.position)) continue;

                bool isLeftSide = t.position.x < halfWidth;

                switch (t.phase)
                {
                    case TouchPhase.Began:
                        if (isLeftSide && _moveFingerId == -1)
                        {
                            // 左側:移動搖桿(隱形,觸控起點=原點)
                            _moveFingerId = t.fingerId;
                            _joystickCenter = t.position;
                            ShowJoystick(_joystickCenter);
                            UpdateJoystick(t.position);
                        }
                        else if (!isLeftSide && _lookFingerId == -1)
                        {
                            // 右側:視角搖桿(隱形,觸控起點=原點)
                            _lookFingerId = t.fingerId;
                            _lookCenter = t.position;
                            _lookVector = Vector2.zero;
                            // v2.6.2: 記錄右側觸控起點(雙擊跳躍判定用)
                            _touchStartPos = t.position;
                            _touchStartTime = Time.time;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (t.fingerId == _moveFingerId)
                            UpdateJoystick(t.position);
                        else if (t.fingerId == _lookFingerId)
                            UpdateLookJoystick(t.position);
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
                            // v2.6.2: 右側雙擊=跳躍(放開時,若位移小且間隔短=點擊)
                            float moveDist = Vector2.Distance(t.position, _touchStartPos);
                            float elapsed = Time.time - _touchStartTime;
                            if (moveDist <= doubleTapMaxMove && elapsed <= mobileJumpDoubleTapTime * 2f)
                            {
                                if (mobileJumpDoubleTapTime > 0f &&
                                    Time.time - _lastTapTime <= mobileJumpDoubleTapTime)
                                {
                                    _mobileJumpQueued = true;
                                    _lastTapTime = -Mathf.Infinity;  // 防三擊
                                    Debug.Log("[TouchPlayerController] 行動跳躍:右側雙擊偵測");
                                }
                                else
                                {
                                    _lastTapTime = Time.time;
                                    _lastTapPos = t.position;
                                }
                            }
                            _lookFingerId = -1;
                            _lookVector = Vector2.zero;
                        }
                        break;
                }
            }
        }

        // v2.6.0: 右側視角搖桿——類比(推越遠轉越快),向量存入 _lookVector
        private void UpdateLookJoystick(Vector2 touchPos)
        {
            Vector2 delta = touchPos - _lookCenter;
            float dist = delta.magnitude;
            if (dist > joystickRadius)
                delta = delta.normalized * joystickRadius;

            Vector2 normalized = delta / joystickRadius;
            if (normalized.magnitude < joystickDeadzone)
                normalized = Vector2.zero;
            _lookVector = normalized;
        }

        // v2.5.0: 齒輪按鈕觸控排除
        private static bool IsTouchOnGearButton(Vector2 touchPos)
        {
            Rect g = HKGrid.Debugging.DebugOverlay.GearButtonRect;
            if (g.width <= 0f) return false;  // 齒輪未初始化
            return g.Contains(touchPos);
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
                // v2.6.0: 左側隱形搖桿 = 移動(joystickVector 已含死區處理)
                input = _joystickVector;
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
            if (_isJumping)
            {
                // v2.2.13: 跳躍中——彈道運動(重力),不吸附地面。著地時結束跳躍。
                _verticalVel += gravity * Time.deltaTime;
                deltaY = _verticalVel * Time.deltaTime;

                // 著地偵測:向下射線命中且正在下降(verticalVel ≤ 0)
                if (foundGround && _verticalVel <= 0f)
                {
                    float groundY = bestHit.point.y + eyeHeight;
                    // 若本幀位移會穿過地面,鉗制至地面高度
                    if (transform.position.y + deltaY <= groundY)
                    {
                        deltaY = groundY - transform.position.y;
                        _verticalVel = 0f;
                        _isJumping = false;
                        _isGrounded = true;
                        _groundEverFound = true;
                        _holdUntilGround = false;
                        _lastGroundY = bestHit.point.y;
                    }
                }
            }
            else if (foundGround)
            {
                float targetY = bestHit.point.y + eyeHeight;
                deltaY = targetY - transform.position.y;
                // 限幅:每幀最多移動 maxYSnapPerFrame 速度(m/s × dt)
                deltaY = Mathf.Clamp(deltaY, -maxYSnapPerFrame * Time.deltaTime,
                                             maxYSnapPerFrame * Time.deltaTime);
                _verticalVel = 0f;  // 著地,重設垂直速度
                _groundEverFound = true;
                _isGrounded = true;
                _holdUntilGround = false;  // v2.2.8: 找到地面,解除握住
                _lastGroundY = bestHit.point.y;  // v2.2.3: 追蹤最後已知地面(穿模重生用)
            }
            else if (_groundEverFound)
            {
                // 曾找到地面但現在失去(走到邊緣/分塊卸載):施加重力緩慢下墜
                _verticalVel += gravity * Time.deltaTime;
                if (_verticalVel < -30f) _verticalVel = -30f; // 限速
                deltaY = _verticalVel * Time.deltaTime;
                _isGrounded = false;
            }
            else
            {
                // v2.2.9: 握住位置並雙向搜尋地面(向下未命中時嘗試向上——spawn 可能在地下)。
                if (_holdUntilGround)
                {
                    // 向上射線:若 spawn 在地下,找到頭上地面後向上移至 ground + eyeHeight
                    Vector3 upOrigin = new Vector3(transform.position.x,
                        transform.position.y - groundRaycastHeight, transform.position.z);
                    int upHits = Physics.RaycastNonAlloc(upOrigin, Vector3.up, _rayHits,
                        groundRaycastLength, groundMask);
                    RaycastHit bestUpHit = default;
                    bool foundUp = false;
                    float closestUp = float.MaxValue;
                    for (int i = 0; i < upHits; i++)
                    {
                        if (_rayHits[i].collider == _cc) continue;
                        if (_rayHits[i].distance < closestUp)
                        {
                            closestUp = _rayHits[i].distance;
                            bestUpHit = _rayHits[i];
                            foundUp = true;
                        }
                    }
                    if (foundUp)
                    {
                        // 地面在上方:向上移至地面 + eyeHeight
                        float targetY = bestUpHit.point.y + eyeHeight;
                        deltaY = Mathf.Clamp(targetY - transform.position.y,
                            -maxYSnapPerFrame * Time.deltaTime,
                            maxYSnapPerFrame * Time.deltaTime);
                        // 接近目標時鎖定為已找到地面
                        if (Mathf.Abs(targetY - transform.position.y) < 0.5f)
                        {
                            _groundEverFound = true;
                            _holdUntilGround = false;
                            _lastGroundY = bestUpHit.point.y;
                        }
                    }
                    else
                    {
                        deltaY = 0f;  // 握住:等分塊載入
                    }
                }
                else
                {
                    // v2.2.4: 高處 spawn 緩慢下降尋找地面(非懸停)
                    deltaY = -5f * Time.deltaTime;
                }
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

            _joystickCanvas = new GameObject("JoystickCanvas");
            var canvas = _joystickCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            _joystickCanvas.AddComponent<CanvasScaler>();
            // 搖桿僅為視覺;輸入由 Input.touches 處理,不需 GraphicRaycaster

            // 搖桿底盤
            var baseGo = new GameObject("JoystickBase");
            baseGo.transform.SetParent(_joystickCanvas.transform, false);
            _joystickBaseImg = baseGo.AddComponent<Image>();
            _joystickBaseImg.sprite = _circleSprite;
            // v2.5.0: showJoystickUI 控制透明度;浮動搖桿預設隱藏(觸控時顯示)
            _joystickBaseImg.color = showJoystickUI
                ? new Color(1f, 1f, 1f, 0.25f)
                : new Color(1f, 1f, 1f, 0f);
            _joystickBaseImg.rectTransform.sizeDelta = Vector2.one * joystickRadius * 2f;
            _joystickBaseImg.raycastTarget = false;

            // 搖桿拇指
            var thumbGo = new GameObject("JoystickThumb");
            thumbGo.transform.SetParent(baseGo.transform, false);
            _joystickThumbImg = thumbGo.AddComponent<Image>();
            _joystickThumbImg.sprite = _circleSprite;
            _joystickThumbImg.color = showJoystickUI
                ? new Color(1f, 1f, 1f, 0.55f)
                : new Color(1f, 1f, 1f, 0f);
            _joystickThumbImg.rectTransform.sizeDelta = Vector2.one * joystickRadius * 0.7f;
            _joystickThumbImg.raycastTarget = false;

            // v2.5.0: 浮動搖桿——初始隱藏,觸控 Began 時顯示於觸控點
            _joystickBaseImg.gameObject.SetActive(false);
        }

        // v2.6.0: 隱形搖桿——showJoystickUI=false 時不顯示(輸入仍正常)
        private void ShowJoystick(Vector2 screenPos)
        {
            if (!showJoystickUI) return;  // v2.6.0: 隱形模式不顯示
            if (_joystickBaseImg == null) return;
            _joystickBaseImg.gameObject.SetActive(true);
            _joystickBaseImg.transform.position = new Vector3(screenPos.x, screenPos.y, 0f);
            _joystickThumbImg.rectTransform.localPosition = Vector3.zero;
        }

        private void HideJoystick()
        {
            // v2.5.0: 浮動搖桿——放開時隱藏(不再持續顯示)
            if (_joystickBaseImg != null)
                _joystickBaseImg.gameObject.SetActive(false);
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
