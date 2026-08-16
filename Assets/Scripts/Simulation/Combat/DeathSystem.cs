using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Simulation.Data;

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
            state.RequireForUpdate<HitStop>();
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

 
            
            foreach (var (timer, tr, kb, tier, flying, e) in
                     SystemAPI.Query<RefRW<DeathTimer>, RefRO<LocalTransform>, RefRO<Knockback>, RefRO<TierTag>,
                                     EnabledRefRO<Airborne>>()
                         .WithAll<DeadTag>()
                         .WithPresent<Airborne>()                  // 지상/공중 둘 다 봐야 하므로 Present
                         .WithEntityAccess())    // DeadTag는 "켜진 것만"이 맞음(죽은 좀비)
            {
                // 넉백/체공이 끝나야 사망 타이머 시작 — 공중에서 쓰러지는 모션이 나오면 어색하다
                if (kb.ValueRO.Remaining > 0f || flying.ValueRO)
                    continue;
                
                if (timer.ValueRO.Remaining < 0f)
                {
                    timer.ValueRW.Remaining = deathDur;   
                    // 죽은 첫 프레임 → 타이머 시작
                    if (tier.ValueRO.Value != MonsterTier.Normal && SystemAPI.TryGetSingletonRW<HitStop>(out var hitStop))
                    {
                        // 프리즈 길이(초). 리더는 넉백 면역이라 벤 즉시 걸린다.
                        float value = tier.ValueRO.Value == MonsterTier.Boss ? 1.0f : 0.7f;
                        hitStop.ValueRW.Remaining = math.max(hitStop.ValueRO.Remaining, value);
                    }
                    _queue.Enqueue(new DeathEvent
                    {
                        Position = tr.ValueRO.Position,
                        Tier = tier.ValueRO.Value
                    });   // 점수·VFX는 즉시 피드백
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