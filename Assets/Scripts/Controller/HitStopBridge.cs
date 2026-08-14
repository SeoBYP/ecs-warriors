using Simulation.Components;
using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;

namespace Controller
{
    /// <summary>
    /// 히트스톱(ECS HitStop 싱글톤)을 GameObject 세계에 반영하는 브릿지.
    ///
    /// ECS는 이미 멈춘다(이동·해시·넉백·VAT 애니 시계). 하지만 플레이어와 카메라는 GO라
    /// 그대로 움직여서 "세계가 멈춘" 느낌이 깨진다. 여기서 프리즈 시작/해제 **엣지**에만
    /// 플레이어 조작·애니를 정지/복구하고, 시작 순간 카메라 셰이크(Cinemachine Impulse)를 쏜다.
    ///
    /// 셰이크 세기는 HitStop.Remaining으로 티어를 역산한다(보스 0.20s / 엘리트 0.10s).
    /// 카메라는 CinemachineBrain이 트랜스폼을 덮어쓰므로 직접 흔들면 안 되고 Impulse를 쓴다.
    /// </summary>
    public class HitStopBridge : MonoBehaviour
    {
        [Header("정지 대상 (비우면 자동 탐색)")]
        [SerializeField] private PlayerController _player;
        [SerializeField] private Animator _playerAnimator;

        [Header("카메라 셰이크")]
        [Tooltip("Cinemachine Impulse Source. 리스너는 CinemachineCamera 쪽에 있어야 한다.")]
        [SerializeField] private CinemachineImpulseSource _impulse;
        [SerializeField] private float _shakeElite = 0.35f;
        [SerializeField] private float _shakeBoss = 0.9f;
        [Tooltip("이 시간 이상이면 보스 처치로 간주(엘리트 0.7 / 보스 1.0)")]
        [SerializeField] private float _bossThreshold = 0.85f;

        EntityManager _em;
        EntityQuery _query;
        bool _ready;
        bool _frozen;

        void Start()
        {
            if (_player == null) _player = GetComponentInParent<PlayerController>();
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_playerAnimator == null && _player != null) _playerAnimator = _player.GetComponentInChildren<Animator>();
            if (_impulse == null) _impulse = GetComponent<CinemachineImpulseSource>();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;
            _query = _em.CreateEntityQuery(typeof(HitStop));
            _ready = true;
        }

        void Update()
        {
            if (!_ready || _query.IsEmpty) return;

            float remaining = _query.GetSingleton<HitStop>().Remaining;
            bool frozen = remaining > 0f;
            if (frozen == _frozen) return;          // 엣지에서만 처리
            _frozen = frozen;

            if (frozen)
            {
                if (_player != null) _player.enabled = false;          // 이동·시점 입력 정지
                if (_playerAnimator != null) _playerAnimator.speed = 0f;
                if (_impulse != null)
                    _impulse.GenerateImpulseWithForce(remaining >= _bossThreshold ? _shakeBoss : _shakeElite);
            }
            else
            {
                if (_player != null) _player.enabled = true;
                if (_playerAnimator != null) _playerAnimator.speed = 1f;
            }
        }
    }
}
