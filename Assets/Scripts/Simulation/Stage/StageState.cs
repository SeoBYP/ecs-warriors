using Unity.Entities;

namespace Simulation.Stage
{
    public enum StagePhase : byte { Playing = 0, Cleared = 1, Failed = 2 }

    public struct StageState : IComponentData
    {
        public StagePhase Phase;
        public int   LeadersRemaining;   // 승리 조건
        public int   LeadersTotal;       // UI "3 / 11"
        public float Elapsed;            // 클리어 기록
    }
}