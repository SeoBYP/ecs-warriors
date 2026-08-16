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
        [SerializeField] private float _duration = 2.4f;
        [SerializeField] private int _pulses = 8;
        [SerializeField] private float _pulseRadius = 18f;
        [SerializeField] private int _pulseDamage = 120;
        [SerializeField] private float _pulseKnockback = 4f;
        [SerializeField] private float _pulseStun = 0.6f;

        [Header("연출")]
        [SerializeField] private CinemachineImpulseSource _impulse;
        [SerializeField] private float _pulseShake = 0.25f;

        EntityManager _em;
        bool _ready;

        DeathEventBridge _deaths;
        PlayerHealthBridge _health;
        int _lastKillCount;

        float _gauge;
        bool _active;
        float _activeUntil;
        float _nextPulseAt;
        int _pulsesLeft;

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

        void Activate()
        {
            _gauge = 0f;
            _active = true;
            _activeUntil = Time.time + _duration;
            _pulsesLeft = Mathf.Max(1, _pulses);
            _nextPulseAt = Time.time;                              // 첫 펄스 즉시
            if (_health != null) _health.IsInvulnerable = true;    // 발동 중 무적
        }

        void TickActive()
        {
            if (!_active) return;

            if (_pulsesLeft > 0 && Time.time >= _nextPulseAt)
            {
                FirePulse();
                _pulsesLeft--;
                _nextPulseAt = Time.time + _duration / Mathf.Max(1, _pulses);
            }

            if (Time.time >= _activeUntil)
            {
                _active = false;
                if (_health != null) _health.IsInvulnerable = false;
            }
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
            // 발동 중 비활성/플레이 종료 시 무적이 남지 않게
            if (_active && _health != null) _health.IsInvulnerable = false;
            _active = false;
        }
    }
}
