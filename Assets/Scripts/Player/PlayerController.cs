using UnityEngine;
using UnityEngine.InputSystem;

namespace Controller
{
    /// <summary>
    /// 3인칭 조준형(스트레이프) 이동. 평소 달리기 / Shift 유지 시 걷기.
    /// - 마우스 X → 루트(캐릭터) yaw. 몸과 카메라가 같이 좌우로 돈다.
    /// - 마우스 Y → CameraTarget만 pitch(상하). 몸은 안 눕고 카메라만 위아래를 본다.
    /// - WASD는 캐릭터 로컬 기준 이동 → 카메라 기준 스트레이프. 이동 방향으로 돌지 않으므로
    ///   WASD 입력이 그대로 방향별 이동 애니(전/후/좌/우) 블렌드가 된다.
    /// 애니: Speed(0=Idle, 0.5=Walk, 1=Run) 1D 블렌드 안에 방향별 2D 블렌드(MoveX/MoveY).
    /// 루트가 움직이면 PlayerStateBridge가 위치를 ECS에 전파 → 적 1만이 추적.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float _runSpeed = 7f;           // 기본 (적 3보다 빨라야 포위 탈출)
        [SerializeField] private float _walkSpeed = 2.5f;        // Shift 유지 시 (걷기 애니 속도에 맞춤 → 발 안 미끄러짐)
        [SerializeField] private float _mouseSensitivity = 0.1f; // 마우스 카운트 → 각도
        [SerializeField] private Transform _cameraTarget;        // 카메라 타깃 자식 (Cinemachine Follow/LookAt)
        [SerializeField] private float _minPitch = -35f;
        [SerializeField] private float _maxPitch = 60f;
        [SerializeField] private float _blendDamp = 0.1f;        // 애니 파라미터 보간 시간(부드러운 전환)
        [SerializeField] private Animator _animator;

        private PlayerAnimationEventListener _attackState;   // 공격 중 이동 잠금 판정
        private float _pitch;

        private void Start()
        {
            if (_cameraTarget == null)
            {
                var t = transform.Find("CameraTarget");
                if (t != null) _cameraTarget = t;
            }
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _attackState = GetComponentInChildren<PlayerAnimationEventListener>();
            Cursor.lockState = CursorLockMode.Locked;   // 마우스 캡처 (Esc로 해제됨)
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float dt = Time.deltaTime;

            // 1) 마우스 → 시점
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 md = mouse.delta.ReadValue();
                transform.Rotate(0f, md.x * _mouseSensitivity, 0f, Space.World);   // X → yaw
                if (_cameraTarget != null)                                          // Y → 카메라 pitch만
                {
                    _pitch = Mathf.Clamp(_pitch - md.y * _mouseSensitivity, _minPitch, _maxPitch);
                    _cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
            }

            // 2) WASD → 캐릭터 로컬 기준 이동
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector2 input = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            bool moving = input.sqrMagnitude > 0.0001f;

            // 평소 달리기, Shift 유지 시 걷기
            bool walking = kb.leftShiftKey.isPressed;
            float speed = walking ? _walkSpeed : _runSpeed;

            // 공격 중에는 WASD 병진 잠금 (전진은 루트모션 러시가 담당). 마우스 시점은 유지.
            bool attacking = _attackState != null && _attackState.IsAttacking;
            Vector3 moveDir = transform.forward * input.y + transform.right * input.x;
            if (!attacking)
                transform.position += moveDir * (speed * dt);

            // 3) 애니 파라미터 (damping으로 부드럽게)
            if (_animator != null)
            {
                float speedParam = !moving ? 0f : (walking ? 0.5f : 1f);   // Idle / Walk / Run
                _animator.SetFloat("Speed", speedParam, _blendDamp, dt);
                _animator.SetFloat("MoveX", input.x, _blendDamp, dt);
                _animator.SetFloat("MoveY", input.y, _blendDamp, dt);
            }
        }
    }
}
