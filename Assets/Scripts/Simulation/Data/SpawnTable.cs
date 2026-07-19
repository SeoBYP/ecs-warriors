using System;
using UnityEngine;

namespace Simulation.Data
{
    /// <summary>
    /// 스포너가 "무엇을 얼마나" 뽑을지 정의. 일반은 가중 랜덤, 엘리트/보스는 규칙.
    /// ECS SpawnSystem은 런타임에 이 SO를 직접 읽지 않는다 — Baker로 구운 blob에서 읽는다
    /// (다음 단계 배선). 엘리트/보스는 GameObject 스폰 경로가 이 규칙을 참조.
    /// </summary>
    [CreateAssetMenu(menuName = "ECSWarriors/Spawn Table", fileName = "SpawnTable")]
    public class SpawnTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public MonsterDefinition monster;
            [Min(0f), Tooltip("가중치. 클수록 자주 뽑힌다.")]
            public float weight;
        }

        [Header("일반 — 가중 랜덤 풀 (ECS 군중)")]
        public Entry[] normalPool;

        [Header("엘리트 — N킬마다 (GameObject)")]
        public MonsterDefinition[] elites;
        [Min(0), Tooltip("이 킬 수마다 엘리트 1기. 0이면 비활성.")]
        public int eliteEveryKills = 100;

        [Header("보스 — 킬 수 도달 시 (GameObject)")]
        public MonsterDefinition boss;
        [Min(0), Tooltip("이 킬 수에 도달하면 보스. 0이면 비활성.")]
        public int bossAtKills = 1000;
    }
}
