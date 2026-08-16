using Simulation.Components;
using Unity.Entities;

namespace Simulation.Systems
{
    public partial struct PlayerStateSystem : ISystem 
    {
        public void OnCreate(ref SystemState state) {
            state.EntityManager.CreateSingleton<PlayerState>();   // 시스템이 자기 데이터를 보장
        }
    }
}