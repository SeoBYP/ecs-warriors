using Simulation.Components;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class SpawnCountSlider : MonoBehaviour
    {
        public Slider slider;
        public TextMeshProUGUI label;

        EntityManager _em;
        EntityQuery _enemyQuery;  // ← 캐싱
        bool _ready;
        bool _synced;
        
        void Start()
        {
            slider.onValueChanged.AddListener(OnChanged);
            slider.value = 10000;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null) {
                _em = world.EntityManager;
                _enemyQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<Enemy>()); // 한 번만
                _ready = true;
            }
        }

        void OnChanged(float v)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if(world == null) return;
            
            var em =world.EntityManager;
            var q = em.CreateEntityQuery(typeof(SpawnConfig));
            if(q.IsEmpty) return;
            var e = q.GetSingletonEntity();
            var cfg = em.GetComponentData<SpawnConfig>(e);
            cfg.Count = Mathf.RoundToInt(v);
            em.SetComponentData(e, cfg);
        }
        
        void Update()
        {
            if (!_ready) return;
            if (!_synced) {
                var q = _em.CreateEntityQuery(typeof(SpawnConfig));
                if (!q.IsEmpty) {
                    var cfg = _em.GetComponentData<SpawnConfig>(q.GetSingletonEntity());
                    slider.SetValueWithoutNotify(cfg.Count);   // 메아리 방지
                    _synced = true;
                }
            }
            int current = _enemyQuery.CalculateEntityCount();      // 재사용
            label.text  = $"Enemy: {current} / Goal: {Mathf.RoundToInt(slider.value)}";
        }
    }
}