using System.Collections.Generic;
using Simulation.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Simulation.Components
{
    /// <summary>
    /// 리더(엘리트/보스) 엔티티의 GO 비주얼 팔로워 + Animator 구동.
    /// LeaderTag 엔티티를 티어별 프리팹으로 스폰, 매 프레임 위치/회전 추적 + ECS 상태를
    /// Animator 파라미터로 구동(Speed/Attack/Hit/Die). 엔티티 소멸 시 GO 파괴.
    /// (ECS=시뮬 소유, GO=비주얼만 — Week7 §6 하이브리드.)
    /// </summary>
    public class LeaderVisualBridge : MonoBehaviour
    {
        [Tooltip("티어별 비주얼 소스 — 엘리트/보스 MonsterDefinition을 넣는다.")]
        [SerializeField] private MonsterDefinition[] _leaderDefs;

        EntityManager _em;
        EntityQuery _query;
        EntityQuery _playerQuery;
        EntityQuery _hitStopQuery;
        bool _ready;

        readonly Dictionary<MonsterTier, MonsterDefinition> _defByTier = new();
        readonly Dictionary<Entity, Vis> _visuals = new();
        readonly HashSet<Entity> _alive = new();
        readonly List<Entity> _toRemove = new();

        class Vis
        {
            public GameObject go;
            public Animator anim;
            public Vector3 lastPos;
            public float atkTimer;
            public bool dead;
            public bool wasStunned;
            public MonsterDefinition def;
        }

        static readonly int P_Speed  = Animator.StringToHash("Speed");
        static readonly int P_Attack = Animator.StringToHash("Attack");
        static readonly int P_Hit    = Animator.StringToHash("Hit");
        static readonly int P_Die    = Animator.StringToHash("Die");

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;
            _query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<LeaderTag>(),
                ComponentType.ReadOnly<TierTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _playerQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<PlayerState>());
            _hitStopQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<HitStop>());

            if (_leaderDefs != null)
                foreach (var d in _leaderDefs)
                    if (d != null) _defByTier[d.tier] = d;

            _ready = true;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 playerPos = Vector3.zero;
            bool hasPlayer = !_playerQuery.IsEmpty;
            if (hasPlayer) playerPos = (Vector3)_playerQuery.GetSingleton<PlayerState>().Position;

            // 히트스톱 중엔 리더 GO 애니도 정지 — ECS는 이미 멈춰 있는데 GO만 움직이면 프리즈가 깨진다
            bool frozen = !_hitStopQuery.IsEmpty && _hitStopQuery.GetSingleton<HitStop>().Remaining > 0f;

            _alive.Clear();
            var entities = _query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                _alive.Add(e);
                var lt = _em.GetComponentData<LocalTransform>(e);
                Vector3 pos = lt.Position;

                if (!_visuals.TryGetValue(e, out var v))
                {
                    var tier = _em.GetComponentData<TierTag>(e).Value;
                    if (!_defByTier.TryGetValue(tier, out var def) || def.prefab == null) continue;

                    var go = Instantiate(def.prefab, pos, lt.Rotation);
                    go.name = "LeaderVisual_" + tier + "_" + e.Index;
                    go.transform.localScale = Vector3.one * def.scale;

                    var anim = go.GetComponentInChildren<Animator>();
                    if (anim != null && def.animOverride != null)
                    {
                        anim.runtimeAnimatorController = def.animOverride;
                        anim.applyRootMotion = false;   // 위치는 ECS가 소유 — 루트모션 끄기
                    }
                    v = new Vis { go = go, anim = anim, lastPos = pos, def = def, atkTimer = 0f };
                    _visuals[e] = v;
                }

                v.go.transform.SetPositionAndRotation(pos, lt.Rotation);   // 팔로우

                if (frozen)
                {
                    if (v.anim != null) v.anim.speed = 0f;   // 프리즈: 리더도 그 자리에 멈춤
                    v.lastPos = pos;
                    continue;
                }

                if (v.anim != null && v.anim.runtimeAnimatorController != null)
                {
                    // 이동 속도(수평) → Idle/Walk 블렌드
                    Vector3 delta = pos - v.lastPos; delta.y = 0f;
                    float moveSpeed = delta.magnitude / dt;
                    v.anim.SetFloat(P_Speed, moveSpeed);

                    // ★ 발 속도 = 이동 속도. 걷는 중에만 클립 배속을 이동속도 비례로.
                    //   (calm_walk는 원래 ~0.5m/s라 그대로 두면 심하게 미끄러진다)
                    float natural = v.def.walkClipGroundSpeed;
                    v.anim.speed = (natural > 0.01f && moveSpeed > 0.1f)
                        ? moveSpeed / natural
                        : 1f;

                    // 사망
                    bool dead = _em.IsComponentEnabled<DeadTag>(e);
                    if (dead && !v.dead) { v.anim.SetBool(P_Die, true); v.dead = true; }

                    if (!v.dead)
                    {
                        // 히트(경직 시작 엣지)
                        float stun = _em.GetComponentData<Stun>(e).Remaining;
                        if (stun > 0f && !v.wasStunned) v.anim.SetTrigger(P_Hit);
                        v.wasStunned = stun > 0f;

                        // 공격(사거리 내 & 쿨)
                        v.atkTimer -= dt;
                        if (hasPlayer && v.atkTimer <= 0f &&
                            Vector3.Distance(pos, playerPos) <= v.def.attackRange)
                        {
                            v.anim.SetTrigger(P_Attack);
                            v.atkTimer = v.def.attackCooldown;
                        }
                    }
                }
                v.lastPos = pos;
            }
            entities.Dispose();

            // 사라진(파괴된) 엔티티의 GO 정리
            _toRemove.Clear();
            foreach (var kv in _visuals)
                if (!_alive.Contains(kv.Key) || !_em.Exists(kv.Key))
                {
                    if (kv.Value.go != null) Destroy(kv.Value.go);
                    _toRemove.Add(kv.Key);
                }
            foreach (var e in _toRemove) _visuals.Remove(e);
        }
    }
}
