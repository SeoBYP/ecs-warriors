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

        [Header("회피(Space)")]
        [SerializeField] private float _dodgeDistance = 5f;
        [SerializeField] private float _dodgeDuration = 0.25f;    // ≈20m/s
        [SerializeField] private float _dodgeCooldown = 0.8f;
        [SerializeField] private float _dodgeIFrameGrace = 0.05f; // 대시 끝나고 살짝 더
        [SerializeField] private Simulation.Components.PlayerHealthBridge _health;

        private PlayerAnimationEventListener _attackState;   // 공격 중 이동 잠금 판정
        private float _pitch;

        private float _dodgeTimer;     // >0이면 대시 중(남은 시간)
        private float _dodgeReadyAt;   // 쿨다운 만료 시각
        private Vector3 _dodgeDir;     // 대시 방향(시작할 때 고정 — 중간에 마우스를 돌려도 궤적이 안 휜다)

        /// <summary>대시 중인가. 루트모션(공격 러시)이 대시를 밀지 않도록 리스너가 참고한다.</summary>
        public bool IsDodging => _dodgeTimer > 0f;

        private void Start()
        {
            if (_cameraTarget == null)
            {
                var t = transform.Find("CameraTarget");
                if (t != null) _cameraTarget = t;
            }
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_health == null) _health = FindAnyObjectByType<Simulation.Components.PlayerHealthBridge>();
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

            // 2.5) 회피(Space) — 대시 중엔 이동/공격 잠금보다 우선한다
            if (_dodgeTimer > 0f)
            {
                transform.position += _dodgeDir * (_dodgeDistance / _dodgeDuration * dt);
                _dodgeTimer -= dt;
                if (_animator != null) _animator.SetFloat("Speed", 1f, _blendDamp, dt);   // 달리는 포즈로
                return;                                   // 이번 프레임은 여기서 끝(시점 회전은 위에서 이미 처리)
            }

            if (kb.spaceKey.wasPressedThisFrame && Time.time >= _dodgeReadyAt)
            {
                // 방향은 시작 시점에 한 번만 고정. 입력이 없으면 정면으로 대시.
                Vector3 dir = moving ? (transform.forward * input.y + transform.right * input.x) : transform.forward;
                dir.y = 0f;
                _dodgeDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
                _dodgeTimer = _dodgeDuration;
                _dodgeReadyAt = Time.time + _dodgeCooldown;

                // 콤보 중에도 회피로 캔슬 — 무쌍류에선 공격에 갇혀 못 피하면 답답하다
                if (_animator != null) _animator.ResetTrigger("Attack");

                // 무적은 "만료 시각" 방식이라 무쌍난무 무적과 겹쳐도 서로 취소하지 않는다
                if (_health != null) _health.AddInvulnerability(_dodgeDuration + _dodgeIFrameGrace);
                return;
            }

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
