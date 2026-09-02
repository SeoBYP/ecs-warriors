using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Simulation.Components
{
    /// <summary>
    /// 일반 좀비 머리 위 HP 바.
    ///
    /// **왜 Canvas가 아닌가**: 리더는 11마리라 월드 캔버스로 충분하지만(LeaderVisualBridge),
    /// 병사는 1,300마리다. 캔버스는 하나당 배치가 갈리므로 그대로 붙이면 드로우콜이 1,300개 늘어난다.
    /// 여기선 쿼드 하나를 인스턴싱으로 그린다 — 몇 마리든 배경/채움 2패스뿐.
    /// </summary>
    public class EnemyHealthBarBridge : MonoBehaviour
    {
        [Header("치수 (m)")]
        [Tooltip("머리 끝에서 띄울 간격. 높이 자체는 엔티티 렌더 바운즈에서 읽으므로 스케일이 달라도 따라간다.")]
        [SerializeField] float _margin = 0.18f;
        [Tooltip("렌더 바운즈가 부모에 없는 좀비(멀티 서브메시 → 자식 렌더 엔티티)의 머리 높이 폴백. 실측 1.42~1.44m.")]
        [SerializeField] float _fallbackTop = 1.43f;
        [SerializeField] float _width = 0.45f;
        [SerializeField] float _thickness = 0.06f;

        [Tooltip("이 거리 밖의 적은 그리지 않는다. 멀리선 픽셀 한 점이라 비용만 든다.")]
        [SerializeField] float _maxDistance = 18f;

        [Tooltip("체력이 가득 찬 적은 숨긴다(무쌍은 대부분 한 방이라 화면이 바 밭이 되는 걸 막는다).")]
        [SerializeField] bool _hideWhenFull = false;

        [SerializeField] Color _bgColor = new(0.06f, 0.06f, 0.06f);
        [SerializeField] Color _fillColor = new(0.90f, 0.25f, 0.20f);

        const int Batch = 511;   // DrawMeshInstanced 상한 1023 이내

        EntityManager _em;
        EntityQuery _qBounded;   // 부모에 렌더 바운즈가 있는 좀비
        EntityQuery _qPlain;     // 없는 좀비(자식 렌더 엔티티로 쪼개진 멀티 서브메시) → 폴백 높이
        bool _ready;

        // 프레임당 한 번만 구하는 카메라 값 — Append에 인자로 끌고 다니지 않으려고 필드로 둔다
        Quaternion _rot; Vector3 _camPos, _toCam; float _maxSq;

        Mesh _quad;
        Material _bgMat, _fillMat;
        readonly Matrix4x4[] _bg = new Matrix4x4[Batch];
        readonly Matrix4x4[] _fill = new Matrix4x4[Batch];

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em = world.EntityManager;

            // 리더는 제외(자기 캔버스 바가 있다), 죽은 적도 제외.
            // ★ 쿼리를 둘로 나눈 이유: 멀티 서브메시 좀비는 Entities Graphics가 렌더러를 자식 엔티티로
            //   쪼개서 부모에 WorldRenderBounds가 없다. WithAll로 요구하면 그 40%가 통째로 빠진다.
            //   자식을 매 프레임 뒤지는 대신, 없는 쪽은 폴백 높이로 일괄 처리한다.
            _qBounded = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Enemy, LocalTransform, Health, WorldRenderBounds>()
                .WithNone<LeaderTag>()
                .WithDisabled<DeadTag>()
                .Build(_em);
            _qPlain = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Enemy, LocalTransform, Health>()
                .WithNone<LeaderTag, WorldRenderBounds>()
                .WithDisabled<DeadTag>()
                .Build(_em);

            _quad = BuildQuad();
            _bgMat = BuildMat(_bgColor);
            _fillMat = BuildMat(_fillColor);
            _ready = _bgMat != null && _fillMat != null;
        }

        void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
            if (_bgMat != null) Destroy(_bgMat);
            if (_fillMat != null) Destroy(_fillMat);
        }

        void LateUpdate()
        {
            if (!_ready) return;
            var cam = Camera.main;
            if (cam == null) return;

            _rot = cam.transform.rotation;                    // 빌보드
            _camPos = cam.transform.position;
            _toCam = -cam.transform.forward * 0.01f;          // 채움을 배경 앞으로(z-fighting 방지)
            _maxSq = _maxDistance * _maxDistance;

            int n = 0;
            n = Append(_qBounded, true, n);
            n = Append(_qPlain, false, n);
            Flush(n);
        }

        /// <summary>한 쿼리분을 인스턴스 배열에 채운다. 가득 차면 그 자리에서 그린다. 반환값은 다음 인덱스.</summary>
        int Append(EntityQuery q, bool useBounds, int n)
        {
            var lts = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            var bnds = useBounds ? q.ToComponentDataArray<WorldRenderBounds>(Allocator.Temp)
                                 : default(NativeArray<WorldRenderBounds>);

            for (int i = 0; i < lts.Length; i++)
            {
                var h = hps[i];
                if (h.Max <= 0) continue;
                float ratio = Mathf.Clamp01(h.Value / (float)h.Max);
                if (_hideWhenFull && ratio >= 0.999f) continue;

                Vector3 p = (Vector3)lts[i].Position;
                if (useBounds)
                {
                    var aabb = bnds[i].Value;
                    p.y = aabb.Center.y + aabb.Extents.y + _margin;   // 실제 머리 끝 기준
                }
                else
                {
                    p.y += _fallbackTop * lts[i].Scale + _margin;     // 바운즈가 부모에 없는 좀비
                }
                if ((p - _camPos).sqrMagnitude > _maxSq) continue;

                _bg[n] = Matrix4x4.TRS(p, _rot, new Vector3(_width, _thickness, 1f));
                // 좌측 정렬: 폭을 ratio로 줄이고 중심을 줄어든 만큼 왼쪽으로 민다
                Vector3 fillPos = p + _toCam + _rot * (Vector3.left * (_width * (1f - ratio) * 0.5f));
                _fill[n] = Matrix4x4.TRS(fillPos, _rot, new Vector3(_width * ratio, _thickness * 0.7f, 1f));

                if (++n == Batch) { Flush(n); n = 0; }
            }

            lts.Dispose();
            hps.Dispose();
            if (useBounds) bnds.Dispose();
            return n;
        }

        void Flush(int n)
        {
            if (n <= 0) return;
            Graphics.DrawMeshInstanced(_quad, 0, _bgMat, _bg, n, null, ShadowCastingMode.Off, false);
            Graphics.DrawMeshInstanced(_quad, 0, _fillMat, _fill, n, null, ShadowCastingMode.Off, false);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "HPBarQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            m.RecalculateBounds();
            return m;
        }

        static Material BuildMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) { Debug.LogError("[HPBar] URP Unlit 셰이더를 못 찾았습니다."); return null; }
            var m = new Material(sh) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", 0f);   // 빌보드가 뒤집혀도 보이게 양면
            return m;
        }
    }
}
