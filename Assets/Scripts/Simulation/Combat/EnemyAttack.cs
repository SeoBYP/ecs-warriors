using Unity.Entities;

namespace Simulation.Components
{
    public struct EnemyAttack : IComponentData
    {
        public float Range;
        public float Cooldown;
        public float Timer;
        public int Damage;
    }
}