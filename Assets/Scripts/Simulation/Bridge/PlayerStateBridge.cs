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