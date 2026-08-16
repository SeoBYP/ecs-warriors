using Simulation.Components;
using Unity.Entities;

namespace Simulation.Systems
{
    [UpdateBefore(typeof(ZombieAnimSystem))]
    public partial class VatAnimClockSystem : SystemBase
    {
        private float _t;
        private int _id;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            EntityManager.CreateSingleton<AnimClock>();
            _id = UnityEngine.Shader.PropertyToID("_AnimTime");
        }

        protected override void OnUpdate()
        {
            bool frozen = SystemAPI.TryGetSingleton<HitStop>(out var hs) && hs.Remaining > 0f;
            if (!frozen)
                _t += SystemAPI.Time.DeltaTime;
            
            
            UnityEngine.Shader.SetGlobalFloat(_id, _t);
            SystemAPI.GetSingletonRW<AnimClock>().ValueRW.Time = _t;
        }
    }
}