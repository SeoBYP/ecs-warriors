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

                // 플레이어 중심 링 [InnerRadius, Radius] — 안쪽은 비워 즉시공격 방지.
                // ★ 플레이어 위치가 보고될 때까지 스폰 대기 — SubScene 비동기 스트리밍 때문에
                //   첫 프레임엔 PlayerState.Position이 (0,0,0)이라 원점 링이 되던 문제 방지.
                //   (플레이어 시작 위치가 원점이 아님을 가정 — 현재 (20,20)에서 시작.)
                if (!SystemAPI.TryGetSingleton<PlayerState>(out var pstate)) return;
                float3 center = pstate.Position;
                center.y = 0f;
                if (math.lengthsq(center) < 1f) return;   // 아직 (0,0,0) → 대기
                float rInner = config.InnerRadius;
                float rOuter = math.max(config.Radius, rInner + 1f);

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
                        float ang = random.NextFloat(0f, 2f * math.PI);
                        float r   = math.sqrt(random.NextFloat(rInner * rInner, rOuter * rOuter)); // 면적 균등
                        float3 pos = center + new float3(math.cos(ang) * r, 0f, math.sin(ang) * r);
                        state.EntityManager.SetComponentData(ents[k], LocalTransform.FromPosition(pos));
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
