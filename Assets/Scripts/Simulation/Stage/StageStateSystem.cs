using Simulation.Components;
using Unity.Entities;

namespace Simulation.Stage
{
    public partial struct StageStateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new StageState
            {
                Phase = StagePhase.Playing,
            }); 
            state.RequireForUpdate<PlayerState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if(!SystemAPI.TryGetSingleton(out StageState stageState))
                return;
            
            if(stageState.Phase != StagePhase.Playing)
                return;

            var leadersRemaining = SystemAPI.QueryBuilder()
                .WithAll<LeaderTag>().Build().CalculateEntityCount();
            if (stageState.LeadersTotal == 0 && leadersRemaining > 0)
            {
                stageState.LeadersTotal = leadersRemaining;
            }
            stageState.LeadersRemaining = leadersRemaining;
            
            
            var playerState = SystemAPI.GetSingleton<PlayerState>();
            if (playerState.IsDead)
            {
                stageState.Phase = StagePhase.Failed;
            }
            else if (leadersRemaining <= 0 && stageState.LeadersTotal > 0)
            {
                stageState.Phase = StagePhase.Cleared;
            }
            
            // 플레이 중일때만 시간 축적
            if(stageState.Phase == StagePhase.Playing)
                stageState.Elapsed += SystemAPI.Time.DeltaTime;
            
            SystemAPI.SetSingleton(stageState);
        }
    }
}