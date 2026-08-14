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
        /// <summary>
        /// 걷기 클립(TZ_aggresive_walk)이 원래 만들어내는 지면 이동속도(m/s).
        /// 루트모션판(_rm)의 averageSpeed 실측값 = 1.154. 이동속도가 이보다 빠르면
        /// 그만큼 클립 fps를 올려야 발이 미끄러지지 않는다(stride ↔ 실제 이동거리 일치).
        /// </summary>
        public const float WalkClipGroundSpeed = 1.154f;

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
                AggroRange = 40f,   // MovementSystem과 일치 — 범위 밖은 Idle
                WalkClipSpeed = WalkClipGroundSpeed,
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
        public float  AggroRange;
        public float  WalkClipSpeed;   // 걷기 클립의 원래 지면 속도(m/s)
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
                : d > AggroRange      ? ZAnim.Idle     // ★ 감지범위 밖 → 대기(제자리걷기 방지)
                : d > mv.StopDistance ? ZAnim.Walk
                : d < atk.Range       ? ZAnim.Attack
                :                       ZAnim.Idle;

            if ((byte)want != za.Current)
            {
                float4 p = prm[(int)want];

                // ★ 발 속도 = 이동 속도. 걷기만 fps를 이동속도 비례로 스케일 → 풋 슬라이딩 제거.
                //   (몬스터별 MoveStats.Speed가 다르면 각자 알맞은 보행 템포가 된다)
                if (want == ZAnim.Walk && WalkClipSpeed > 0.01f)
                    p.z *= mv.Speed / WalkClipSpeed;

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