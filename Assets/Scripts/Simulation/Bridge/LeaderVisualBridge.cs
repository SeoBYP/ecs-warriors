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

            public Transform bar;        // 머리 위 HP 바(월드공간 캔버스, 루트 오브젝트)
            public Transform barFill;    // 좌측 피벗 — localScale.x = 남은 비율
            public float barHeight;      // 머리 위 오프셋(모델 높이에서 산출)
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
                    BuildHealthBar(v, tier);
                    _visuals[e] = v;
                }

                v.go.transform.SetPositionAndRotation(pos, lt.Rotation);   // 팔로우
                UpdateHealthBar(v, e, pos);                                // 머리 위 HP 바

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
                    if (kv.Value.bar != null) Destroy(kv.Value.bar.gameObject);
                    _toRemove.Add(kv.Key);
                }
            foreach (var e in _toRemove) _visuals.Remove(e);
        }

        /// <summary>머리 위 월드공간 HP 바 생성. 리더 GO의 스케일을 안 물려받도록 루트로 둔다.</summary>
        void BuildHealthBar(Vis v, MonsterTier tier)
        {
            // 모델 높이에서 바 높이 산출 — 파츠가 여러 개라 전 렌더러를 합친 바운즈를 써야 한다
            // (첫 렌더러만 보면 머리 같은 작은 조각이 잡혀 바가 가슴에 걸린다)
            var rends = v.go.GetComponentsInChildren<Renderer>();
            float top = 0f;
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                top = b.max.y - v.go.transform.position.y;   // 발밑 기준 모델 높이
            }
            v.barHeight = (top > 0.1f ? top : 2f) + 0.4f;

            bool boss = tier == MonsterTier.Boss;
            float width = boss ? 2.2f : 1.4f;
            float height = boss ? 0.22f : 0.16f;

            var root = new GameObject("HPBar_" + tier);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(width, height);
            rt.localScale = Vector3.one;

            // 배경(어두운 판)
            var bg = new GameObject("BG", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            bg.transform.SetParent(root.transform, false);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bg.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.65f);

            // 채움(좌측 피벗 → localScale.x 로 줄인다)
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fill.transform.SetParent(root.transform, false);
            var fRt = (RectTransform)fill.transform;
            fRt.pivot = new Vector2(0f, 0.5f);
            fRt.anchorMin = new Vector2(0f, 0f); fRt.anchorMax = new Vector2(0f, 1f);
            fRt.offsetMin = new Vector2(0f, 0.02f); fRt.offsetMax = new Vector2(0f, -0.02f);
            fRt.sizeDelta = new Vector2(width - 0.04f, fRt.sizeDelta.y);
            fRt.anchoredPosition = new Vector2(0.02f, 0f);
            fill.GetComponent<UnityEngine.UI.Image>().color = boss
                ? new Color(0.85f, 0.15f, 0.15f)     // 보스 = 붉은색
                : new Color(0.95f, 0.55f, 0.10f);    // 엘리트 = 주황

            v.bar = root.transform;
            v.barFill = fill.transform;
        }

        /// <summary>HP 비율 반영 + 머리 위 배치 + 카메라 빌보드.</summary>
        void UpdateHealthBar(Vis v, Entity e, Vector3 pos)
        {
            if (v.bar == null) return;

            float ratio = 0f;
            if (_em.HasComponent<Health>(e) && v.def.hp > 0)
                ratio = Mathf.Clamp01(_em.GetComponentData<Health>(e).Value / (float)v.def.hp);

            var s = v.barFill.localScale; s.x = ratio; v.barFill.localScale = s;

            v.bar.position = pos + Vector3.up * v.barHeight;
            var cam = Camera.main;
            if (cam != null) v.bar.rotation = cam.transform.rotation;   // 빌보드
        }
    }
}
