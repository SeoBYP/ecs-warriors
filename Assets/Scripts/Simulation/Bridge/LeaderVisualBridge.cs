using System.Collections.Generic;
using Simulation.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Simulation.Components
{
    /// <summary>
    /// 리더(엘리트/보스) 엔티티의 GO 비주얼 팔로워.
    /// LeaderTag 엔티티를 찾아 티어별 프리팹을 스폰하고 매 프레임 위치/회전을 따라간다.
    /// 엔티티가 사라지면 GO도 파괴. (ECS=시뮬 소유, GO=비주얼만 — Week8 §6 하이브리드.)
    /// </summary>
    public class LeaderVisualBridge : MonoBehaviour
    {
        [Tooltip("티어별 비주얼 소스 — 엘리트/보스 MonsterDefinition을 넣는다.")]
        [SerializeField] private MonsterDefinition[] _leaderDefs;

        EntityManager _em;
        EntityQuery _query;
        bool _ready;

        readonly Dictionary<MonsterTier, MonsterDefinition> _defByTier = new();
        readonly Dictionary<Entity, GameObject> _visuals = new();
        readonly HashSet<Entity> _alive = new();
        readonly List<Entity> _toRemove = new();

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;   // BeforeSceneLoad에 존재 보장
            if (world == null) return;
            _em = world.EntityManager;
            _query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<LeaderTag>(),
                ComponentType.ReadOnly<TierTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            if (_leaderDefs != null)
                foreach (var d in _leaderDefs)
                    if (d != null) _defByTier[d.tier] = d;

            _ready = true;
        }

        void LateUpdate()
        {
            if (!_ready) return;

            _alive.Clear();
            var entities = _query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                _alive.Add(e);
                var lt = _em.GetComponentData<LocalTransform>(e);

                if (!_visuals.TryGetValue(e, out var go))
                {
                    var tier = _em.GetComponentData<TierTag>(e).Value;
                    if (!_defByTier.TryGetValue(tier, out var def) || def.prefab == null) continue;

                    go = Instantiate(def.prefab, lt.Position, lt.Rotation);
                    go.name = "LeaderVisual_" + tier + "_" + e.Index;
                    go.transform.localScale = Vector3.one * def.scale;

                    if (def.animOverride != null)   // 지금은 null → 기본 포즈. STEP D-2에서 채움.
                    {
                        var anim = go.GetComponentInChildren<Animator>();
                        if (anim != null) anim.runtimeAnimatorController = def.animOverride;
                    }
                    _visuals[e] = go;
                }

                go.transform.SetPositionAndRotation(lt.Position, lt.Rotation);   // 팔로우
            }
            entities.Dispose();

            // 사라진(파괴된) 엔티티의 GO 정리
            _toRemove.Clear();
            foreach (var kv in _visuals)
                if (!_alive.Contains(kv.Key) || !_em.Exists(kv.Key))
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    _toRemove.Add(kv.Key);
                }
            foreach (var e in _toRemove) _visuals.Remove(e);
        }
    }
}
