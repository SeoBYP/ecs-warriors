using Simulation.Components;
using Unity.Entities;

namespace Simulation.Systems
{
    public partial struct HitStopSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton<HitStop>();
        }
        
        public void OnUpdate(ref SystemState state)
        {
            var hitStop = SystemAPI.GetSingletonRW<HitStop>();
            if (hitStop.ValueRO.Remaining > 0)
            {
                hitStop.ValueRW.Remaining -= SystemAPI.Time.DeltaTime;
                if(hitStop.ValueRO.Remaining <= 0)
                {
                    hitStop.ValueRW.Remaining = 0;
                }
            }
        }
    }
}