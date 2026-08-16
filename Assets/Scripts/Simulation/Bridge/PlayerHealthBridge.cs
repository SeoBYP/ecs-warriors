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
        /// 무적(i-frame) 만료 시각. 무쌍난무·회피가 각각 요청한다.
        /// ★ bool 플래그가 아니라 "만료 시각"인 이유: 소유자가 둘 이상이면 bool은 서로 덮어쓴다.
        ///   (무쌍난무 무적 중 회피를 쓰면 회피가 끝나는 순간 무쌍난무 무적까지 꺼진다.)
        ///   더 늦은 만료 시각만 취하면 어느 쪽도 남의 무적을 취소하지 않는다.
        /// </summary>
        float _invulnerableUntil;

        public bool IsInvulnerable => Time.time < _invulnerableUntil;

        /// <summary>이 시간(초)만큼 무적을 요청한다. 이미 더 긴 무적이 걸려 있으면 그대로 둔다.</summary>
        public void AddInvulnerability(float duration)
        {
            if (duration <= 0f) return;
            _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + duration);
        }
        
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