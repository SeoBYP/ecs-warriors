using Simulation.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    public partial struct SpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpawnConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config       = SystemAPI.GetSingleton<SpawnConfig>();
            var configEntity = SystemAPI.GetSingletonEntity<SpawnConfig>();
            var poolBuf      = SystemAPI.GetBuffer<SpawnPrefab>(configEntity);
            if (poolBuf.Length == 0) return;

            int target = config.Count;
            var enemyQuery = SystemAPI.QueryBuilder().WithAll<Enemy>().Build();
            int current = enemyQuery.CalculateEntityCount();

            if (current < target)
            {
                int toSpawn = target - current;

                // ★ 풀을 로컬로 복사 — Instantiate(구조변경)가 버퍼 참조를 무효화하므로
                int poolLen = poolBuf.Length;
                var prefabs = new NativeArray<Entity>(poolLen, Allocator.Temp);
                var weights = new NativeArray<float>(poolLen, Allocator.Temp);
                float totalW = 0f;
                for (int i = 0; i < poolLen; i++)
                {
                    prefabs[i] = poolBuf[i].Prefab;
                    weights[i] = poolBuf[i].Weight;
                    totalW += poolBuf[i].Weight;
                }

                var random = Random.CreateFromIndex((uint)current + 1u);
                int spawned = 0;
                for (int i = 0; i < poolLen; i++)
                {
                    // 마지막 변종이 반올림 나머지를 흡수 → 합계가 정확히 toSpawn
                    int n = (i == poolLen - 1)
                        ? (toSpawn - spawned)
                        : (int)math.round(toSpawn * (weights[i] / totalW));
                    if (n <= 0) continue;
                    spawned += n;

                    var ents = state.EntityManager.Instantiate(prefabs[i], n, Allocator.Temp);
                    for (int k = 0; k < ents.Length; k++)
                    {
                        float x = random.NextFloat(-config.Radius, config.Radius);
                        float z = random.NextFloat(-config.Radius, config.Radius);
                        state.EntityManager.SetComponentData(ents[k], LocalTransform.FromPosition(new float3(x, 0f, z)));
                        state.EntityManager.SetComponentData(ents[k], new VATAnimStart { Value = random.NextFloat(0f, 2f) });
                    }
                    ents.Dispose();
                }
                prefabs.Dispose();
                weights.Dispose();
            }
            else if (current > target)
            {
                int count = current - target;
                var removes = enemyQuery.ToEntityArray(Allocator.Temp);
                state.EntityManager.DestroyEntity(removes.GetSubArray(0, count));
                removes.Dispose();
            }
        }

        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
