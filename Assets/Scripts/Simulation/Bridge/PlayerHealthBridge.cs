using System;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Simulation.Components
{
    public class PlayerHealthBridge : MonoBehaviour
    {
        const string k_Format = "HP : {0}";

        [SerializeField] private TextMeshProUGUI _hpText;
        [SerializeField] private Slider _hpSlider;
        
        public int Hp = 10000;

        /// <summary>
        /// 무적(i-frame). 무쌍난무·회피가 켠다.
        /// ★ 켜져 있어도 큐는 계속 비운다 — 안 비우면 해제 순간 밀린 데미지가 한꺼번에 들어온다.
        /// </summary>
        public bool IsInvulnerable { get; set; }
        
        EntityManager _em; 
        EntityQuery _q;  
        EntityQuery _playerStateQuery; 
        bool _ready;

        void Start() {
            var world = World.DefaultGameObjectInjectionWorld;   // ✅ Start에 존재 보장 (BeforeSceneLoad)
            if (world == null) return;
            _em = world.EntityManager;
            _q  = _em.CreateEntityQuery(typeof(PlayerDamageQueue));    // 쿼리 캐싱
            _playerStateQuery = _em.CreateEntityQuery(typeof(PlayerState));
            _ready = true;
            
            _hpText.SetText(k_Format, Hp);
            _hpSlider.maxValue = Hp;
            _hpSlider.value = Hp;
        }

        private void LateUpdate()
        {
            if(!_ready) return;
            if (_q.IsEmpty) return;
            
            var queue = _q.GetSingleton<PlayerDamageQueue>();
            queue.WriteHandle.Complete();          // ★ 잡이 큐에 쓰는 중일 수 있다

            var before = Hp;
            while (queue.Value.TryDequeue(out var ev))
            {
                if (IsInvulnerable) continue;      // 무적: 큐는 비우되 HP는 안 깎는다
                Hp = Math.Max(0, Hp - ev.Amount);  // 루프는 합산만
            }
            if (Hp == before) return;              // 안 맞은 프레임엔 UI 건드리지 않음

            _hpText.SetText(k_Format, Hp);
            _hpSlider.value = Hp;
            
            if (Hp <= 0)
            {
                var e = _playerStateQuery.GetSingletonEntity();
                var state = _em.GetComponentData<PlayerState>(e);
                
                state.IsDead = true;
                _em.SetComponentData(e, state);
            }
        }
    }
}