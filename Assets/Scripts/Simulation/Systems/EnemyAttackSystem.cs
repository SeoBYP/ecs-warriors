using Simulation.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Simulation.Systems
{
    [BurstCompile]
    public partial struct EnemyAttackSystem : ISystem
    {
        private NativeQueue<PlayerDamageEvent> _damageQueue;
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerState>();
            _damageQueue = new NativeQueue<PlayerDamageEvent>(Allocator.Persistent);
            state.EntityManager.CreateSingleton(new PlayerDamageQueue { Value = _damageQueue });
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var player = SystemAPI.GetSingleton<PlayerState>();
            
            if(player.IsDead)
                return;
            
            var handle = new EnemyAttackJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                PlayerState = player, 
                Writer = _damageQueue.AsParallelWriter(),
            }.ScheduleParallel(state.Dependency);
            
            state.Dependency = handle;
            SystemAPI.SetSingleton(new PlayerDamageQueue { Value = _damageQueue, WriteHandle = handle });
        }
        
        public void OnDestroy(ref SystemState state)
        {
            if (_damageQueue.IsCreated)
            {
                _damageQueue.Dispose();
            }
        }
    }
    
    [BurstCompile]
    [WithDisabled(typeof(DeadTag))]  
    public partial  struct EnemyAttackJob : IJobEntity
    {
        public float DeltaTime;     // 잡에 넘길 데이터는 "필드"로
        public PlayerState PlayerState;
        public NativeQueue<PlayerDamageEvent>.ParallelWriter Writer;
        
        void Execute(in LocalTransform transform,ref EnemyAttack stats)
        {
            if (math.distance(transform.Position, PlayerState.Position) > stats.Range)
            {
                return;
            }
            stats.Timer -= DeltaTime;
            // Timer가 다 되면 공격 된다
            if (stats.Timer <= 0)
            {
                stats.Timer = stats.Cooldown;
                Writer.Enqueue(new PlayerDamageEvent { Amount = stats.Damage });
            }
        }
    }
}