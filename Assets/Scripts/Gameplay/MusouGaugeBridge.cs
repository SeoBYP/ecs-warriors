using Simulation.Components;
using Unity.Cinemachine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Controller
{
    /// <summary>
    /// 무쌍난무 — 시그니처 특수기.
    ///
    /// 적을 벨수록 게이지가 차고, 가득 차면 R로 발동한다.
    /// 발동 = **기존 광역기(AttackRequest)를 짧은 간격으로 여러 번** 쏘는 것 → ECS 신규 코드 0.
    /// (Week3 "공격 = 데이터" 설계의 배당. 반경·데미지·넉백·경직이 전부 파라미터라 가능.)
    ///
    /// 충전은 DeathEventQueue를 직접 읽지 않고 <see cref="DeathEventBridge.KillCount"/>의 증가분을 본다 —
    /// 큐는 소비하면 사라져서 소비자가 둘이면 킬카운트와 서로 이벤트를 뺏는다(Week9 §2.3-②).
    /// </summary>
    public class MusouGaugeBridge : MonoBehaviour
    {
        [Header("게이지")]
        [SerializeField] private float _max = 100f;
        [Tooltip("킬 1당 충전량")]
        [SerializeField] private float _chargePerKill = 1.5f;
        [SerializeField] private Slider _gaugeBar;
        [SerializeField] private Image _gaugeFill;      // 준비/발동 상태 색 구분(선택)

        [Header("발동")]
        [SerializeField] private Key _activateKey = Key.R;
        [SerializeField] private float _pulseRadius = 18f;
        [Tooltip("평타와 같은 일반 공격 판정 — 넉백 0(강타가 아님), 데미지·경직도 콤보 1~3타와 동일.")]
        [SerializeField] private int _pulseDamage = 40;
        [SerializeField] private float _pulseKnockback = 0f;
        [SerializeField] private float _pulseStun = 0.4f;

        [Header("연출 — ComboB1~B5 × 2사이클")]
        [SerializeField] private Animator _animator;
        [Tooltip("반복 구간. 이 순서를 _cycles번 돌고 _finisherState로 끝낸다.")]
        [SerializeField] private string[] _musouStates = { "MusouB2", "MusouB4" };
        [SerializeField] private int _cycles = 5;
        [Tooltip("마무리 일격 — 반복 구간과 달리 끝까지 재생한다.")]
        [SerializeField] private string _finisherState = "MusouB5";
        [Tooltip("재생 배속. 전체 길이와 펄스 간격이 함께 줄어든다(1 = 클립 원속도).")]
        [SerializeField] private float _animSpeed = 1f;
        [Tooltip("클립 어느 지점에서 타격 판정을 낼지(0~1). 대부분 앞부분이 준비동작이라 중반 직전.")]
        [SerializeField] private float _hitAtNormalized = 0.35f;
        [Tooltip("클립을 어디까지 쓰고 다음으로 넘길지(0~1). 스윙이 끝나면 마무리 동작을 버리고 바로 다음 타로 — 속도감의 핵심.")]
        [SerializeField] private float _clipUseNormalized = 0.6f;
        [Tooltip("상태 전환 블렌드(초). 잘라 붙인 클립 사이를 이걸로 메운다.")]
        [SerializeField] private float _crossFade = 0.12f;

        [SerializeField] private CinemachineImpulseSource _impulse;
        [SerializeField] private float _pulseShake = 0.25f;

        EntityManager _em;
        bool _ready;

        DeathEventBridge _deaths;
        PlayerHealthBridge _health;
        int _lastKillCount;

        float _gauge;
        bool _active;

        // 발동 스케줄 — 클립 길이에서 만든다(하드코딩 시간 없음)
        struct Step { public string State; public float PlayAt; public float HitAt; }
        readonly System.Collections.Generic.List<Step> _steps = new();
        int _playIndex, _hitIndex;
        float _startedAt;
        float _prevAnimSpeed = 1f;

        /// <summary>발동 전체 길이(초). 클립 길이 × 사이클 ÷ 배속.</summary>
        public float Duration { get; private set; }

        public float Ratio => _max > 0f ? Mathf.Clamp01(_gauge / _max) : 0f;
        public bool IsReady => _gauge >= _max;
        public bool IsActive => _active;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;

            _deaths = FindAnyObjectByType<DeathEventBridge>();
            _health = FindAnyObjectByType<PlayerHealthBridge>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            BuildSchedule();
            if (_impulse == null) _impulse = GetComponent<CinemachineImpulseSource>();

            // ★ 시작 시점 기준선 — 안 잡으면 이미 쌓인 킬로 시작하자마자 게이지가 찬다
            if (_deaths != null) _lastKillCount = _deaths.KillCount;

            if (_gaugeBar != null) { _gaugeBar.minValue = 0f; _gaugeBar.maxValue = 1f; _gaugeBar.value = 0f; }
            _ready = true;
        }

        void Update()
        {
            if (!_ready) return;
            Charge();
            HandleInput();
            TickActive();
            UpdateUI();
        }

        /// <summary>킬 증가분만큼 충전(발동 중엔 정지). 큐는 DeathEventBridge만 소비한다.</summary>
        void Charge()
        {
            if (_deaths == null || _active) return;

            int now = _deaths.KillCount;
            int delta = now - _lastKillCount;
            _lastKillCount = now;
            if (delta <= 0) return;

            _gauge = Mathf.Min(_max, _gauge + delta * _chargePerKill);
        }

        void HandleInput()
        {
            if (_active || !IsReady) return;
            var kb = Keyboard.current;
            if (kb == null || !kb[_activateKey].wasPressedThisFrame) return;
            Activate();
        }

        /// <summary>
        /// 클립 길이에서 재생·타격 시각표를 만든다. 시간을 코드에 박지 않으므로
        /// 클립을 바꾸거나 배속을 만져도 펄스가 저절로 따라온다.
        /// </summary>
        void BuildSchedule()
        {
            _steps.Clear();
            Duration = 0f;
            if (_animator == null || _animator.runtimeAnimatorController == null || _musouStates == null) return;

            var clips = _animator.runtimeAnimatorController.animationClips;
            float speed = Mathf.Max(0.1f, _animSpeed);
            float use = Mathf.Clamp(_clipUseNormalized, 0.1f, 1f);
            float t = 0f;

            for (int c = 0; c < Mathf.Max(1, _cycles); c++)
                foreach (var state in _musouStates)
                    t = AddStep(clips, state, t, speed, use);

            // 마무리는 끝까지 재생한다(잘라내면 기술이 끊긴 것처럼 보인다)
            if (!string.IsNullOrEmpty(_finisherState))
                t = AddStep(clips, _finisherState, t, speed, 1f);

            Duration = t;
        }

        /// <summary>스텝 1개 추가. 반환값은 다음 스텝의 시작 시각.</summary>
        float AddStep(AnimationClip[] clips, string state, float t, float speed, float use)
        {
            float len = ClipLengthForState(clips, state) / speed;
            if (len <= 0f) return t;
            _steps.Add(new Step { State = state, PlayAt = t, HitAt = t + len * Mathf.Clamp01(_hitAtNormalized) });
            return t + len * use;   // ★ use<1 이면 클립 뒷부분(마무리 동작)을 버리고 다음 타로 넘어간다
        }

        /// <summary>상태 이름(MusouB3) → 그 상태가 쓰는 클립(ARPG_Warrior_Attack_ComboB3)의 길이.</summary>
        float ClipLengthForState(AnimationClip[] clips, string state)
        {
            // 상태 이름 끝의 식별자(B3)로 클립을 찾는다 — 상태와 클립 이름이 1:1로 대응하지 않아도 된다
            string suffix = state.Substring(state.LastIndexOf('B') >= 0 ? state.LastIndexOf('B') : 0);
            foreach (var c in clips)
                if (c != null && c.name.EndsWith(suffix)) return c.length;
            return 0f;
        }

        void Activate()
        {
            if (_steps.Count == 0) BuildSchedule();
            if (_steps.Count == 0) return;                        // 상태·클립이 없으면 발동 자체를 안 한다

            _gauge = 0f;
            _active = true;
            _startedAt = Time.time;
            _playIndex = _hitIndex = 0;

            if (_animator != null)
            {
                _prevAnimSpeed = _animator.speed;
                _animator.speed = Mathf.Max(0.1f, _animSpeed);
                _animator.ResetTrigger("Attack");                 // 대기 중인 평타가 무쌍을 끊지 않게
            }
            // 무적은 애니 길이에 맞춘다(만료 시각 방식이라 회피 등 다른 소스와 겹쳐도 서로 취소하지 않는다)
            if (_health != null) _health.AddInvulnerability(Duration);
        }

        void TickActive()
        {
            if (!_active) return;
            float t = Time.time - _startedAt;

            // 재생: 다음 클립 차례가 되면 CrossFade.
            // _crossFade만큼 **미리** 시작한다 — 블렌드가 끝나는 시점이 PlayAt이 되어야
            // 앞 클립이 마지막 프레임에서 정지한 채 끌리지 않는다(norm>1 관측).
            while (_playIndex < _steps.Count && t >= _steps[_playIndex].PlayAt - _crossFade)
            {
                if (_animator != null)
                    _animator.CrossFadeInFixedTime(_steps[_playIndex].State, _crossFade, 0, 0f);
                _playIndex++;
            }

            // 타격: 클립마다 1펄스 — 휘두르는 순간에 맞춰 나간다
            while (_hitIndex < _steps.Count && t >= _steps[_hitIndex].HitAt)
            {
                FirePulse();
                _hitIndex++;
            }

            if (t >= Duration) Finish();
        }

        /// <summary>마무리 — 로코모션으로 복귀. 무적은 만료 시각이라 끄지 않는다(남의 무적을 취소할 수 있다).</summary>
        void Finish()
        {
            _active = false;
            if (_animator == null) return;
            _animator.speed = _prevAnimSpeed;
            _animator.CrossFadeInFixedTime("Locomotion", _crossFade * 2f, 0);
        }

        /// <summary>광역 타격 1회 — 기존 AttackRequest 그대로(ECS 신규 코드 없음).</summary>
        void FirePulse()
        {
            Vector3 p = transform.position;
            var e = _em.CreateEntity();
            _em.AddComponentData(e, new AttackRequest
            {
                Center = new float3(p.x, 0f, p.z),   // 적은 y=0 → 바닥 투영
                Radius = _pulseRadius,
                Damage = _pulseDamage,
                KnockbackScale = _pulseKnockback,
                StunDuration = _pulseStun,
                SwingId = 0,                          // ★ 단발 — 펄스마다 범위 내 전원 타격
            });
            if (_impulse != null) _impulse.GenerateImpulseWithForce(_pulseShake);
        }

        void UpdateUI()
        {
            if (_gaugeBar != null) _gaugeBar.value = Ratio;
            if (_gaugeFill != null)
                _gaugeFill.color = _active ? new Color(1f, 0.95f, 0.45f)      // 발동 = 밝은 금색
                                 : IsReady ? new Color(1f, 0.78f, 0.15f)      // 준비 = 금색
                                           : new Color(0.35f, 0.65f, 1f);     // 충전 중 = 파랑
        }

        void OnDisable()
        {
            if (_active) Finish();   // 배속을 원복하지 않으면 애니메이터가 빨라진 채로 남는다
        }
    }
}
