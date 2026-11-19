// FlyCameraController.cs — 簡易飛覽攝影機:WASD 平移 + 滑鼠右鍵轉向 + Shift 加速。
//
// 用於在 HK 3D 網格分塊世界中飛覽,驗證 ChunkStreamer 的載入/卸載。
// 正式 App 可替換為貼地巡航或軌道攝影機。攝影機在「世界座標」(Y-up)移動;
// ChunkStreamer 內部會將世界座標轉換為本地網格座標(Z-up)做距離判斷。
//
// 鷹架(scaffold):未經 Unity 編譯驗證。

using UnityEngine;

namespace HKGrid.Streaming
{
    public class FlyCameraController : MonoBehaviour
    {
        public float moveSpeed = 60f;       // m/s
        public float fastMoveMultiplier = 3f;
        public float lookSpeed = 2f;        // 滑鼠靈敏度
        public float damping = 10f;

        private Vector3 _velocity;
        private float _yaw;
        private float _pitch;

        private void Start()
        {
            Vector3 e = transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        private void Update()
        {
            // 轉向(右鍵拖曳)
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * lookSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // 平移
            Vector3 input = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) input += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) input -= Vector3.forward;
            if (Input.GetKey(KeyCode.A)) input -= Vector3.right;
            if (Input.GetKey(KeyCode.D)) input += Vector3.right;
            if (Input.GetKey(KeyCode.E)) input += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) input -= Vector3.up;

            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMoveMultiplier : 1f);
            Vector3 target = transform.rotation * input.normalized * speed;
            _velocity = Vector3.Lerp(_velocity, target, Time.deltaTime * damping);
            transform.position += _velocity * Time.deltaTime;
        }
    }
}
