using UnityEngine;
using Unity.Entities;

namespace Simulation.Components
{
    public class SpawnAuthoring : MonoBehaviour
    {
        [System.Serializable]
        public struct Variant
        {
            public GameObject Prefab;
            [Min(0f), Tooltip("가중치. 클수록 자주 뽑힌다.")]
            public float Weight;
        }

        [Tooltip("스폰 풀 — 변종 프리팹 + 가중치. 비면 아무것도 안 나옴.")]
        public Variant[] Variants;
        public int Count;
        public float Radius;
        [Tooltip("플레이어 주변 안전 반경 — 이 안엔 스폰 안 함(시작 즉시공격 방지).")]
        public float InnerRadius = 15f;
    }

    class SpawnBaker : Baker<SpawnAuthoring>
    {
        public override void Bake(SpawnAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new SpawnConfig
            {
                Count       = authoring.Count,
                Radius      = authoring.Radius,
                InnerRadius = authoring.InnerRadius,
            });

            var buf = AddBuffer<SpawnPrefab>(entity);
            if (authoring.Variants != null)
            {
                foreach (var v in authoring.Variants)
                {
                    if (v.Prefab == null || v.Weight <= 0f) continue;
                    buf.Add(new SpawnPrefab
                    {
                        Prefab = GetEntity(v.Prefab, TransformUsageFlags.Dynamic),
                        Weight = v.Weight,
                    });
                }
            }
        }
    }
}
