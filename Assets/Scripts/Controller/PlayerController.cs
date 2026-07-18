using UnityEngine;
using UnityEngine.InputSystem;

namespace Controller
{
    /// <summary>
    /// 3인칭 조준형(스트레이프) 이동.
    /// - 마우스 X → 루트(캐릭터) yaw. 몸과 카메라가 같이 좌우로 돈다.
    /// - 마우스 Y → CameraTarget만 pitch(상하). 몸은 안 눕고 카메라만 위아래를 본다.
    ///   (pitch를 몸에 주면 캐릭터가 고꾸라지므로 카메라 타깃에만 적용한다.)
    /// - WASD는 캐릭터 로컬 기준 이동 → 카메라 기준 스트레이프. 이동 방향으로 돌지 않으므로
    ///   WASD 입력이 그대로 방향별 이동 애니(전/후/좌/우) 블렌드가 된다.
    /// 루트가 움직이면 PlayerStateBridge가 위치를 ECS에 전파 → 적 1만이 추적.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 7f;          // 적(3)보다 빨라야 포위를 벗어난다
        [SerializeField] private float _mouseSensitivity = 0.1f; // 마우스 카운트 → 각도
        [SerializeField] private Transform _cameraTarget;        // 가슴 높이 자식 (Cinemachine Follow/LookAt)
        [SerializeField] private float _minPitch = -35f;         // 위로 올려다보는 한계
        [SerializeField] private float _maxPitch = 60f;          // 아래로 내려다보는 한계
        [SerializeField] private Animator _animator;             // 자식 캐릭터 (3단계 블렌드 트리가 소비)

        private float _pitch;

        private void Start()
        {
            if (_cameraTarget == null)
            {
                var t = transform.Find("CameraTarget");
                if (t != null) _cameraTarget = t;
            }
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            Cursor.lockState = CursorLockMode.Locked;   // 마우스 캡처 (Esc로 해제됨)
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // 1) 마우스 → 시점
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 md = mouse.delta.ReadValue();

                // X → 루트 yaw (몸 + 카메라)
                transform.Rotate(0f, md.x * _mouseSensitivity, 0f, Space.World);

                // Y → CameraTarget pitch (카메라만). 마우스 위 = 위를 본다.
                if (_cameraTarget != null)
                {
                    _pitch = Mathf.Clamp(_pitch - md.y * _mouseSensitivity, _minPitch, _maxPitch);
                    _cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
            }

            // 2) WASD → 캐릭터 로컬 기준 이동 (= 카메라 기준 스트레이프)
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector2 input = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

            Vector3 moveDir = transform.forward * input.y + transform.right * input.x;
            transform.position += moveDir * (_moveSpeed * Time.deltaTime);

            // 3) 방향별 이동 애니 파라미터 (3단계에서 2D 블렌드 트리가 소비)
            if (_animator != null)
            {
                _animator.SetFloat("MoveX", input.x);
                _animator.SetFloat("MoveY", input.y);
                _animator.SetFloat("Speed", input.magnitude);
            }
        }
    }
}
