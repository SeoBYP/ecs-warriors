using Unity.Mathematics;

namespace Simulation
{
    public static class SpatialHash
    {
        public const float CellSize = 1f;
        public static int2 ToCell(float3 pos) => (int2)math.floor(pos.xz / CellSize);
        public static int  Key(int2 c)        => (c.x * 73856093) ^ (c.y * 19349663);
        public static int  Key(float3 pos)    => Key(ToCell(pos));
    }
}