using UnityEngine;

namespace Simulation.Data
{
    /// <summary>
    /// 한 판(스테이지)의 편성 배치. 어느 편성을 월드 어디에 놓을지의 데이터.
    /// 확정 스테이지: 보스 1편성 + 엘리트 10편성 = 리더 11기 + 병사 1,300.
    /// </summary>
    [CreateAssetMenu(menuName = "ECSWarriors/Stage Definition", fileName = "Stage_")]
    public class StageDefinition : ScriptableObject
    {
        [System.Serializable]
        public struct Placement
        {
            public SquadDefinition squad;
            public Vector3 center;    // 편성이 놓일 월드 위치
            public float   heading;   // 바라보는 방향(도)
        }

        public Placement[] squads;    // 이 판의 편성들
    }
}
