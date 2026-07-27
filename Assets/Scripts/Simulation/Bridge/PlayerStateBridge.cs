using System;
using Unity.Entities;
using UnityEngine;

namespace Simulation.Components
{
    public class PlayerStateBridge : MonoBehaviour
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

            // ★ 초기 위치를 첫 프레임 ECS 전에 반영 — SpawnSystem이 플레이어 중심 링을 쓰도록
            //   (Update는 ECS SimulationSystemGroup보다 늦게 돌 수 있어 스폰이 원점 링이 되던 문제)
            if (!_q.IsEmpty) {
                var e = _q.GetSingletonEntity();
                var state = _em.GetComponentData<PlayerState>(e);
                state.Position = transform.position;
                _em.SetComponentData(e, state);
            }
        }

        private void Update()
        {
            if(!_ready) return;
            if(_q.IsEmpty) return;
            
            var e = _q.GetSingletonEntity();
            var state = _em.GetComponentData<PlayerState>(e);
            state.Position = transform.position;
            _em.SetComponentData(e, state);
        }
    }
}