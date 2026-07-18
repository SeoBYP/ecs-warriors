using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Entities;
using Unity.Mathematics;
using Simulation.Components;

namespace Controller
{
    /// <summary>
    /// 캐릭터(Animator와 같은 GameObject)에 붙어 Animation Event를 받는다.
    /// ★ Animation Event는 Animator가 있는 GO의 컴포넌트로만 전달되므로 여기(자식)에 둔다.
    ///
    /// 좌클릭 → Attack 트리거 → Combo1 재생
    ///   → [Event] OnAttackStart : 스윙 판정 윈도우 시작 (SwingId++)
    ///   → (윈도우 동안 매 프레임) 칼(손) 위치로 AttackRequest 발사
    ///   → [Event] OnAttackEnd   : 윈도우 종료
    /// AttackRequest.SwingId 덕에 각 적은 스윙당 1번만 맞는다(AttackResolveSystem 중복제거).
    /// </summary>
    public class PlayerAnimationEventListener : MonoBehaviour
    {
        [SerializeField] private Transform _weaponAnchor;   // 히트박스 원점. 비면 hand_r 자동 탐색
        [SerializeField] private float _attackRadius = 2.5f;
        [SerializeField] private int _attackDamage = 40;
        [SerializeField] private float _knockbackScale = 3f;
        [SerializeField] private float _stunDuration = 0.4f;

        private Animator _animator;
        private EntityManager _em;
        private bool _ecsReady;

        public bool IsSwinging { get; private set; }
        public int SwingId { get; private set; }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_weaponAnchor == null)
                _weaponAnchor = FindChildByName(transform, "hand_r");   // 무기 메시가 없어 오른손 본을 원점으로
        }

        private void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null) { _em = world.EntityManager; _ecsReady = true; }
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                _animator.SetTrigger("Attack");
            }
        }

        private void LateUpdate()
        {
            // 스윙 판정 윈도우 동안 매 프레임 손 위치로 AttackRequest 발사.
            // 손이 아크를 그리며 지나가는 적들이 차례로 맞고, SwingId로 각 적은 1번만.
            if (!IsSwinging || !_ecsReady || _weaponAnchor == null) return;

            Vector3 wp = _weaponAnchor.position;
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new AttackRequest
            {
                Center = new float3(wp.x, 0f, wp.z),   // 바닥 투영 (적이 y=0)
                Radius = _attackRadius,
                Damage = _attackDamage,
                KnockbackScale = _knockbackScale,
                StunDuration = _stunDuration,
                SwingId = SwingId,                     // >0 → 스윙당 1히트
            });
        }

        // ── Animation Event 콜백 (Combo1 클립의 스윙 시작/끝 프레임에 심음) ──
        public void OnAttackStart() { IsSwinging = true; SwingId++; }
        public void OnAttackEnd()   { IsSwinging = false; }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }
    }
}
