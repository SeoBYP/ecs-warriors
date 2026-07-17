using Unity.Entities;
using Unity.Mathematics;

namespace Simulation.Components
{
    public struct HashedEnemy 
    {
        public Entity Entity;      // ← 공격이 버퍼에 쓰려면 필요
        public float3 Position;    // ← 회피가 거리 계산에 쓰던 것
    }
}