using Unity.Entities;

namespace Simulation.Components
{
    public struct MoveStats : IComponentData
    {
        public float Speed;          // 이동 속도
        public float StopDistance;   // 이 거리 안이면 정지(플레이어 주변 밀집)
    }
}