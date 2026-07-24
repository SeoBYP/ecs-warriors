using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct DeathSystem : ISystem
    {
        NativeQueue<DeathEvent> _queue;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DeadTag>();
            state.RequireForUpdate<VATClipTable>();          // 사망 클립 길이를 얻으려고
            _queue = new NativeQueue<DeathEvent>(Allocator.Persistent);
            state.EntityManager.CreateSingleton(new DeathEventQueue { Value = _queue });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var table = SystemAPI.GetSingleton<VATClipTable>();
            float4 dp = table.Blob.Value.Params[(int)ZAnim.Death];
            float deathDur = dp.y / dp.z;                    // frameCount / fps ≈ 1.67초
            float dt  = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (timer, tr, kb, e) in
                     SystemAPI.Query<RefRW<DeathTimer>, RefRO<LocalTransform>, RefRO<Knockback>>()
                         .WithAll<DeadTag>().WithEntityAccess())      // 여기선 "켜진 것만"이 맞음(죽은 좀비)
            {
                if (kb.ValueRO.Remaining > 0f) 
                    continue; 
                
                if (timer.ValueRO.Remaining < 0f)
                {
                    timer.ValueRW.Remaining = deathDur;                                  // 죽은 첫 프레임 → 타이머 시작
                    _queue.Enqueue(new DeathEvent { Position = tr.ValueRO.Position });   // 점수·VFX는 즉시 피드백
                }
                else
                {
                    timer.ValueRW.Remaining -= dt;
                    if (timer.ValueRO.Remaining <= 0f) ecb.DestroyEntity(e);             // 애니 끝나면 파괴
                }
            }
            ecb.Playback(state.EntityManager); ecb.Dispose();
        }
        
        public void OnDestroy(ref SystemState state)
        {
            if (_queue.IsCreated)
            {
                _queue.Dispose();
            }
        }
    }
}