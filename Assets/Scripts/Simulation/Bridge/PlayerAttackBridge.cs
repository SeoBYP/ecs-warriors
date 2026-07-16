using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Simulation.Components
{
    public class PlayerAttackBridge : MonoBehaviour
    {
        EntityManager _em; 
        EntityQuery _q;  
        bool _ready;

        void Start() {
            var world = World.DefaultGameObjectInjectionWorld;   // ✅ Start에 존재 보장 (BeforeSceneLoad)
            if (world == null) return;
            _em = world.EntityManager;
            _q  = _em.CreateEntityQuery(typeof(PlayerState));    // 쿼리 캐싱
            _ready = true;
        }
        
        void Update() {
            if (!_ready) return;
            if (!Keyboard.current.spaceKey.wasPressedThisFrame) return;      // 임시 입력 (콤보는 Week 4)

            var e = _em.CreateEntity();                         // 요청 엔티티 생성
            _em.AddComponentData(e, new AttackRequest {
                Center = transform.position,                    // 지금은 플레이어 위치 = 히트박스
                Radius = 5f,
                Damage = 10f,
            });
        }
    }
}