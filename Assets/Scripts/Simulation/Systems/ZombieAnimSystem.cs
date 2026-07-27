using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct ZombieAnimSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerState>();
            state.RequireForUpdate<VATClipTable>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var player = SystemAPI.GetSingleton<PlayerState>();
            var table = SystemAPI.GetSingleton<VATClipTable>();
            var ast = SystemAPI.GetSingleton<AnimClock>();           
            new ZombieAnimJob
            {
                PlayerPos = player.Position,
                Now       = ast.Time,
                Table     = table.Blob,
            }.ScheduleParallel();
        }
    }
    
    [BurstCompile]
    [WithAll(typeof(Enemy))]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    partial struct ZombieAnimJob : IJobEntity
    {
        public float3 PlayerPos;
        public float  Now;
        [ReadOnly] public BlobAssetReference<VATClipBlob> Table;

        void Execute(Entity e, in LocalTransform tf, in EnemyAttack atk, in MoveStats mv, in Stun stun,
            ref VATAnimParams ap, ref VATAnimStart ast, ref ZombieAnim za, in Knockback kb,
            EnabledRefRO<DeadTag> dead)
        {
            ref var prm = ref Table.Value.Params;          // ★ ref 유지
            float d = math.distance(tf.Position, PlayerPos);

            // 우선순위: 사망 > 피격 > (이동중이면 걷기) > 사거리내 공격 > 대기
            //  ※ 이동/정지 판정(StopDistance)을 공격보다 먼저 봐야
            //    "아직 걸어오는 중인데 공격모션" 이 안 나온다.
            //  ※ Idle은 "멈췄는데 사거리 밖" — 현재 값(Stop 1.5 < Range 2.0)에선 도달 불가.
            //    idle을 쓰려면 StopDistance를 Range보다 크게 하거나 별도 조건(쿨다운 등) 필요.
            ZAnim want = kb.Remaining > 0f   ? ZAnim.Damage   // ★ 넉백 중엔 죽었어도 히트 모션
                : dead.ValueRO        ? ZAnim.Death    // 넉백 끝나야 사망 모션
                : stun.Remaining > 0f ? ZAnim.Damage
                : d > mv.StopDistance ? ZAnim.Walk
                : d < atk.Range       ? ZAnim.Attack
                :                       ZAnim.Idle;

            if ((byte)want != za.Current)
            {
                float4 p = prm[(int)want];
                ap.Value = p;

                // 위상 분산: 루프 클립만, 엔티티별 고정 오프셋만큼 과거에 시작한 것으로
                float phase = 0f;
                if (p.w > 0.5f)                                   // loop == 1
                {
                    float r = (math.hash(new int2(e.Index, 0)) & 0xFFFF) / 65535f;  // 0~1 고정 난수
                    phase = r * (p.y / p.z);                      // × 클립 길이(frames/fps)
                }
                ast.Value = Now - phase;                          // 원샷(death/damage)은 phase=0 → 처음부터
                za.Current = (byte)want;
            }
        }
    }
}