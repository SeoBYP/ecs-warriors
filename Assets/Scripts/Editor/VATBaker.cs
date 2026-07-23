#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Simulation.VAT
{
    /// <summary>
    /// VAT(Vertex Animation Texture) 베이커.
    ///
    /// 스킨드 메시 + 애니메이션 클립을 "프레임별로 구워(BakeMesh)" 각 정점의
    /// 프레임별 위치/노멀을 텍스처에 저장한다. 런타임에는 셰이더(정점 단계)가
    /// 이 텍스처를 읽어 정점을 이동시키므로 → 뼈/SkinnedMeshRenderer/스키닝 없이
    /// GPU 인스턴싱만으로 수만 마리 애니메이션이 가능하다.
    ///
    /// 산출물 3종:
    ///   _PositionMap (RGBAHalf, w=정점수, h=프레임수) : 정점의 프레임별 오브젝트공간 위치
    ///   _NormalMap   (RGBAHalf, 동일 크기)            : 정점의 프레임별 노멀(라이팅용)
    ///   _VATMesh (Mesh)                               : UV3.x 에 "정점 인덱스"를 구워둔 정적 메시
    ///
    /// 텍스처 좌표 규약:  가로(u)=정점 인덱스,  세로(v)=프레임.
    /// 셰이더는 uv3.x(정점번호)와 시간→프레임을 texel 좌표로 Load 한다.
    /// </summary>
    public static class VATBaker
    {
        public struct Result
        {
            public string positionMap;
            public string normalMap;
            public string mesh;
            public int vertexCount;
            public int frames;
            public float clipLength;
            public Bounds bounds;

            public override string ToString() =>
                $"VAT 완료: verts={vertexCount} frames={frames} len={clipLength:F2}s " +
                $"bounds={bounds.size} | {mesh}";
        }

        /// <param name="modelPrefab">SkinnedMeshRenderer + 스켈레톤을 가진 좀비 FBX(GameObject).</param>
        /// <param name="clip">구울 애니메이션 클립(별도 FBX에 내장된 것도 가능).</param>
        /// <param name="frames">세로 해상도 = 프레임 수(30~60 권장).</param>
        /// <param name="outputDir">산출물 폴더(Assets/... 상대경로).</param>
        /// <param name="outName">파일 접두어.</param>
        /// <param name="vertexRotation">정점 보정 회전(누워 구워지면 여기서 세운다). 기본 identity.</param>
        public static Result Bake(
            GameObject modelPrefab, AnimationClip clip, int frames,
            string outputDir, string outName, Quaternion vertexRotation = default)
        {
            if (modelPrefab == null) throw new System.Exception("modelPrefab 이 null");
            if (clip == null) throw new System.Exception("clip 이 null");
            if (frames < 2) frames = 2;
            if (vertexRotation.x == 0 && vertexRotation.y == 0 && vertexRotation.z == 0 && vertexRotation.w == 0)
                vertexRotation = Quaternion.identity; // default(Quaternion) 방어

            EnsureAssetFolder(outputDir);   // Assets/Data/VAT 이 없으면 AssetDatabase로 생성(첫 Bake 실패 방지)

            // 1) 모델을 씬에 인스턴스화 (에셋은 포즈를 못 잡으므로 실체가 필요)
            var inst = (GameObject)Object.Instantiate(modelPrefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) { Object.DestroyImmediate(inst); throw new System.Exception("SkinnedMeshRenderer 없음"); }

            int vCount = smr.sharedMesh.vertexCount;

            // 2) 텍스처 준비 (가로=정점, 세로=프레임). RGBAHalf = 위치를 음수/1초과까지 그대로 저장.
            var posTex = new Texture2D(vCount, frames, TextureFormat.RGBAHalf, false, true);
            var nrmTex = new Texture2D(vCount, frames, TextureFormat.RGBAHalf, false, true);
            posTex.filterMode = nrmTex.filterMode = FilterMode.Point;      // 텍셀 정확히(보간 금지)
            posTex.wrapMode = nrmTex.wrapMode = TextureWrapMode.Clamp;
            var posCols = new Color[vCount * frames];
            var nrmCols = new Color[vCount * frames];

            var baked = new Mesh();
            var min = new Vector3(1e9f, 1e9f, 1e9f);
            var max = -min;

            // 3) 프레임 루프: 클립을 시간 t로 포즈 → BakeMesh → 정점을 텍스처에 기록.
            //    t 는 [0, length) 를 균등 분할 → 마지막 프레임 다음이 곧 프레임0 (루프 이음새 없음).
            for (int f = 0; f < frames; f++)
            {
                float t = (f / (float)frames) * clip.length;
                clip.SampleAnimation(inst, t);   // 스켈레톤을 t 시점 포즈로 (레거시 아니어도 에디터에서 동작)
                smr.BakeMesh(baked, true);       // 현재 포즈의 정점을 구움(스케일 반영)

                var vs = baked.vertices;
                var ns = baked.normals;
                bool hasN = ns != null && ns.Length == vCount;
                for (int v = 0; v < vCount; v++)
                {
                    Vector3 p = vertexRotation * vs[v];
                    Vector3 n = vertexRotation * (hasN ? ns[v] : Vector3.up);
                    int idx = f * vCount + v;                  // 행우선(row-major): index = y*width + x
                    posCols[idx] = new Color(p.x, p.y, p.z, 1f);
                    nrmCols[idx] = new Color(n.x, n.y, n.z, 0f);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            posTex.SetPixels(posCols); posTex.Apply(false, false);
            nrmTex.SetPixels(nrmCols); nrmTex.Apply(false, false);

            // 4) 텍스처 저장
            string posPath = $"{outputDir}/{outName}_PositionMap.asset";
            string nrmPath = $"{outputDir}/{outName}_NormalMap.asset";
            AssetDatabase.DeleteAsset(posPath); AssetDatabase.CreateAsset(posTex, posPath);
            AssetDatabase.DeleteAsset(nrmPath); AssetDatabase.CreateAsset(nrmTex, nrmPath);

            // 5) VAT용 메시: 기존 메시 복제 + UV3.x=정점인덱스, bounds=애니 최대 범위(컬링용)
            var vatMesh = Object.Instantiate(smr.sharedMesh);
            vatMesh.name = outName + "_VATMesh";
            var uv3 = new List<Vector4>(vCount);
            for (int v = 0; v < vCount; v++) uv3.Add(new Vector4(v, 0f, 0f, 0f));
            vatMesh.SetUVs(3, uv3);
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            vatMesh.bounds = bounds;
            string meshPath = $"{outputDir}/{outName}_VATMesh.asset";
            AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(vatMesh, meshPath);

            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(inst);
            Object.DestroyImmediate(baked);

            return new Result
            {
                positionMap = posPath, normalMap = nrmPath, mesh = meshPath,
                vertexCount = vCount, frames = frames, clipLength = clip.length, bounds = bounds
            };
        }

        /// <summary>Assets/ 하위 폴더를 (없으면 상위부터) AssetDatabase로 생성.</summary>
        static void EnsureAssetFolder(string dir)
        {
            dir = dir.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir).Replace("\\", "/");
            string leaf = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>FBX 경로 + 클립 경로로 굽는 편의 래퍼(우선 첫 AnimationClip 사용).</summary>
        public static Result BakeFromPaths(
            string modelFbxPath, string clipFbxPath, int frames,
            string outputDir, string outName, Quaternion vertexRotation = default)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelFbxPath);
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clipFbxPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) { clip = c; break; }
            return Bake(model, clip, frames, outputDir, outName, vertexRotation);
        }
    }
}
#endif
