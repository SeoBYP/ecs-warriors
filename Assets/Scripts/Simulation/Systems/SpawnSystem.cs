using Simulation.Components;
using Simulation.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    /// <summary>
    /// 편성 스폰: 리더(스쿼드 중심) 주위에 병사를 1회 스폰.
    /// 보스=300 / 엘리트=100 (Stage_01: 보스1+엘리트10 = 병사 1,300).
    /// 병사(리더 제외 Enemy)가 이미 있으면 스킵 — 리더 사망/이동과 무관하게 초기 편성 1회.
    /// </summary>
    public partial struct SpawnSystem : ISystem
    {
        // 티어별 스쿼드(호위 병사) 수·반경 — 데이터화는 후속(현재 SquadDefinition 값과 정합)
        const int   BossSquad   = 300;
        const int   EliteSquad  = 100;
        const float BossRadius  = 20f;
        const float EliteRadius = 12f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpawnConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var configEntity = SystemAPI.GetSingletonEntity<SpawnConfig>();
            // 편성은 1회만 — 이미 스폰했으면 병사가 전멸해도 재스폰 안 함
            if (SystemAPI.HasComponent<FormationSpawned>(configEntity)) return;

            // 리더(스쿼드 중심). 아직 안 구워졌으면 대기.
            var leaderQuery = SystemAPI.QueryBuilder().WithAll<LeaderTag, TierTag, LocalTransform>().Build();
            if (leaderQuery.CalculateEntityCount() == 0) return;

            var poolBuf = SystemAPI.GetBuffer<SpawnPrefab>(configEntity);
            if (poolBuf.Length == 0) return;

            // 변종 풀 로컬 복사 (Instantiate 구조변경이 버퍼 무효화)
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

            // 리더 위치/티어 스냅샷 (복사본 → 구조변경에 안전)
            var leaderPos  = leaderQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var leaderTier = leaderQuery.ToComponentDataArray<TierTag>(Allocator.Temp);
            var random = Random.CreateFromIndex(9273u);

            for (int L = 0; L < leaderPos.Length; L++)
            {
                bool boss = leaderTier[L].Value == MonsterTier.Boss;
                int   squad  = boss ? BossSquad  : EliteSquad;
                float radius = boss ? BossRadius : EliteRadius;
                float3 c = leaderPos[L].Position; c.y = 0f;

                int spawned = 0;
                for (int i = 0; i < poolLen; i++)
                {
                    int n = (i == poolLen - 1)
                        ? (squad - spawned)
                        : (int)math.round(squad * (weights[i] / totalW));
                    if (n <= 0) continue;
                    spawned += n;

                    var ents = state.EntityManager.Instantiate(prefabs[i], n, Allocator.Temp);
                    for (int k = 0; k < ents.Length; k++)
                    {
                        float ang = random.NextFloat(0f, 2f * math.PI);
                        float r   = math.sqrt(random.NextFloat(0f, radius * radius));   // 면적 균등
                        float3 pos = c + new float3(math.cos(ang) * r, 0f, math.sin(ang) * r);
                        state.EntityManager.SetComponentData(ents[k], LocalTransform.FromPosition(pos));
                        state.EntityManager.SetComponentData(ents[k], new VATAnimStart { Value = random.NextFloat(0f, 2f) });
                    }
                    ents.Dispose();
                }
            }

            leaderPos.Dispose();
            leaderTier.Dispose();
            prefabs.Dispose();
            weights.Dispose();

            // ★ 편성 스폰 완료 마킹 → 이후 재스폰 안 함
            state.EntityManager.AddComponent<FormationSpawned>(configEntity);
        }

        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
