using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation.Components
{
    public class PlayerAttackBridge : MonoBehaviour
    {
        EntityManager _em; 
        bool _ready;

        [SerializeField] private float _normalAttackRadius = 5f;
        [SerializeField] private int _normalAttackDamage = 10;
        [SerializeField] private float _normalAttackCoolTime = 1f;
        [SerializeField] private float _normalAttackKnockbackScale = 2.5f;
        [SerializeField] private float _normalAttackStunDuration = 0.5f;
        
        [SerializeField] private float _areaAttackRadius = 25f;
        [SerializeField] private int _areaAttackDamage = 100;
        [SerializeField] private float _areaAttackCoolTime = 2f;
        [SerializeField] private float _areaAttackKnockbackScale = 5f;
        [SerializeField] private float _areaAttackStunDuration = 0.5f;
        
        private float _normalAttackTimer = 0.0f;
        private float _areaAttackTimer = 0.0f;
        
        void Start() {
            var world = World.DefaultGameObjectInjectionWorld;   // ✅ Start에 존재 보장 (BeforeSceneLoad)
            if (world == null) return;
            _em = world.EntityManager;
            _ready = true;
        }
        
        void Update()
        {
            if (!_ready) return;

            HandleNormalAttackInput();
            HandleAreaAttackInput();
        }

        private void HandleNormalAttackInput()
        {
            if (_normalAttackTimer > 0.0f)
            {
                _normalAttackTimer -= Time.deltaTime;
                return;
            }
            
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            var e = _em.CreateEntity();                         // 요청 엔티티 생성
            _em.AddComponentData(e, new AttackRequest {
                Center = transform.position,                    // 지금은 플레이어 위치 = 히트박스
                Radius = _normalAttackRadius,
                Damage = _normalAttackDamage,
                KnockbackScale = _normalAttackKnockbackScale,
                StunDuration = _normalAttackStunDuration,
            });
            _normalAttackTimer = _normalAttackCoolTime;
        }

        private void HandleAreaAttackInput()
        {
            if (_areaAttackTimer > 0.0f)
            {
                _areaAttackTimer -= Time.deltaTime;
                return;
            }
            
            if (!Mouse.current.rightButton.wasPressedThisFrame) return;

            var e = _em.CreateEntity();                         // 요청 엔티티 생성
            _em.AddComponentData(e, new AttackRequest {
                Center = transform.position,                    // 지금은 플레이어 위치 = 히트박스
                Radius = _areaAttackRadius,
                Damage = _areaAttackDamage,
                KnockbackScale = _areaAttackKnockbackScale,
                StunDuration = _areaAttackStunDuration,
            });
            _areaAttackTimer = _areaAttackCoolTime;
        }
    }
}