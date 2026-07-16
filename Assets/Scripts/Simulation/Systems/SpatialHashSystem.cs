using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    /// <summary>
    /// ★ bench/01-naive 브랜치 전용 (벤치마크 기준선) ★
    /// Spatial Hash를 쓰지 않고 각 적이 "나머지 전부"를 전수 검사해 이웃 회피를 계산한다 → O(n^2).
    /// main(그리드) 버전과 회피 수식·반경·세기는 완전히 동일하고, 오직 "이웃을 어떻게 찾느냐"만 다르다.
    /// 목적: 기획서 벤치마크 ④단계(O(n^2) → Spatial Hash)의 before 수치 확보.
    /// </summary>
    [BurstCompile]
    [UpdateBefore(typeof(MovementSystem))]
    public partial struct SpatialHashSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Enemy>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().Build();

            // 그리드 대신: 이번 프레임 모든 적 위치 스냅샷을 통째로 넘긴다
            var all = query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            state.Dependency = new NaiveSeparationJob
            {
                All = all,
                Radius = SpatialHash.CellSize,   // 그리드 버전과 동일 (1.0)
                Strength = 5,                    // 동일
                DeltaTime = SystemAPI.Time.DeltaTime,
            }.ScheduleParallel(state.Dependency);

            all.Dispose(state.Dependency);       // 잡 완료 후 해제
        }
    }

    [BurstCompile]
    partial struct NaiveSeparationJob : IJobEntity
    {
        [ReadOnly] public NativeArray<LocalTransform> All;   // 전체 적 (n개)
        public float Radius;
        public float Strength;
        public float DeltaTime;

        void Execute(ref LocalTransform transform)
        {
            float3 me = transform.Position;
            float3 push = float3.zero;

            // ★ 핵심 차이: 3x3 셀(9칸)이 아니라 전부(n개)를 훑는다 → 엔티티당 n번 = 총 n*n
            for (int i = 0; i < All.Length; i++)
            {
                float3 diff = me - All[i].Position;
                float d = math.length(diff);
                if (d > 0.0001f && d < Radius)              // 자기 자신 제외 + 반경 내만
                {
                    push += diff / d * (1f - d / Radius);   // 그리드 버전과 동일한 감쇠식
                }
            }

            transform.Position += push * Strength * DeltaTime;
        }
    }
}
