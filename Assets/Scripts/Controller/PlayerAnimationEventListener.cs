using System.Collections.Generic;
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
    /// 좌클릭 → Attack 트리거 → Combo 클립 재생
    ///   → [Event] OnAttackStart(스트링=기점 본 이름) : 스윙 판정 윈도우 시작 (SwingId++)
    ///   → (윈도우 동안 매 프레임) 지정한 손 위치로 AttackRequest 발사
    ///   → [Event] OnAttackEnd : 윈도우 종료
    ///
    /// ★ 콤보 클립마다 공격하는 손이 다르다(왼손/오른손 번갈아).
    ///   그래서 기점을 코드에 고정하지 않고 Animation Event의 String 파라미터로 클립마다 지정한다.
    ///   Combo1 이벤트 String="hand_r", Combo2="hand_l" … 처럼. 비우면 기본값(_defaultAnchorBone).
    ///
    /// AttackRequest.SwingId 덕에 각 적은 스윙당 1번만 맞는다(AttackResolveSystem 중복제거).
    /// </summary>
    public class PlayerAnimationEventListener : MonoBehaviour
    {
        [SerializeField] private string _defaultAnchorBone = "hand_r";   // 이벤트에 본 이름이 없을 때 기본 기점
        [SerializeField] private float _attackRadius = 2.5f;
        [SerializeField] private int _attackDamage = 40;
        [SerializeField] private float _knockbackScale = 3f;
        [SerializeField] private float _stunDuration = 0.4f;

        private Animator _animator;
        private EntityManager _em;
        private bool _ecsReady;

        // 본 이름 → Transform 캐시 (매 스윙마다 계층 탐색하지 않도록)
        private readonly Dictionary<string, Transform> _boneCache = new();
        private Transform _activeAnchor;   // 이번 스윙의 기점 (OnAttackStart에서 결정)

        public bool IsSwinging { get; private set; }
        public int SwingId { get; private set; }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
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
            // 스윙 판정 윈도우 동안 매 프레임 기점(손) 위치로 AttackRequest 발사.
            // 손이 아크를 그리며 지나가는 적들이 차례로 맞고, SwingId로 각 적은 1번만.
            if (!IsSwinging || !_ecsReady || _activeAnchor == null) return;

            Vector3 wp = _activeAnchor.position;
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

        // ── Animation Event 콜백 ──
        // ★ (AnimationEvent) 시그니처로 받으면 파라미터가 있든 없든 항상 바인딩된다.
        //   evt.stringParameter = 클립에서 지정한 기점 본 이름 (비면 기본값).
        public void OnAttackStart(AnimationEvent evt)
        {
            string boneName = string.IsNullOrEmpty(evt.stringParameter) ? _defaultAnchorBone : evt.stringParameter;
            _activeAnchor = ResolveBone(boneName);
            IsSwinging = true;
            SwingId++;
        }

        public void OnAttackEnd() { IsSwinging = false; }

        // 본 이름으로 Transform을 찾고 캐시. 못 찾으면 기본 기점으로 폴백.
        private Transform ResolveBone(string boneName)
        {
            if (_boneCache.TryGetValue(boneName, out var cached)) return cached;

            var t = FindChildByName(transform, boneName);
            if (t == null && boneName != _defaultAnchorBone)   // 오타 등 → 기본 손으로라도 판정
                t = FindChildByName(transform, _defaultAnchorBone);

            _boneCache[boneName] = t;   // null이어도 캐시(반복 탐색 방지)
            return t;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }
    }
}
