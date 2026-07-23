#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Simulation.Components;   // VATClipSet, ClipEntry
using UnityEditor;
using UnityEngine;

namespace Simulation.VAT
{
    /// <summary>
    /// VAT(Vertex Animation Texture) 베이커.
    ///
    /// 스킨드 메시 + 애니 클립을 프레임별로 구워(BakeMesh) 각 정점의 프레임별 위치/노멀을
    /// 텍스처에 저장한다. 런타임엔 셰이더가 이 텍스처를 읽어 정점을 옮기므로
    /// 뼈/SkinnedMeshRenderer 없이 GPU 인스턴싱으로 수만 마리 애니 재생.
    ///
    ///   Bake()      : 클립 1개 → _PositionMap/_NormalMap/_VATMesh (별도 파일)
    ///   BakeMulti() : 클립 여러 개를 한 텍스처에 세로로 스택 → VATClipSet(SO, 서브에셋 포함)
    ///
    /// 텍스처 규약: 가로(u)=정점 인덱스(메시 UV3.x), 세로(v)=프레임(멀티는 전 클립 스택).
    /// </summary>
    public static class VATBaker
    {
        public struct Result
        {
            public string positionMap, normalMap, mesh;
            public int vertexCount, frames;
            public float clipLength;
            public Bounds bounds;

            public override string ToString() =>
                $"VAT 완료: verts={vertexCount} frames={frames} len={clipLength:F2}s bounds={bounds.size} | {mesh}";
        }

        // ────────────────────────────────────────────────────────────────
        // 단일 클립 (별도 파일 3종). 기존 워크플로 호환용.
        // ────────────────────────────────────────────────────────────────
        public static Result Bake(
            GameObject modelPrefab, AnimationClip clip, int frames,
            string outputDir, string outName, Quaternion vertexRotation = default)
        {
            if (modelPrefab == null) throw new System.Exception("modelPrefab 이 null");
            if (clip == null) throw new System.Exception("clip 이 null");
            if (frames < 2) frames = 2;
            vertexRotation = Normalize(vertexRotation);
            EnsureAssetFolder(outputDir);

            var inst = InstantiateModel(modelPrefab, out var smr);
            int vCount = smr.sharedMesh.vertexCount;

            var posTex = MakeTex(vCount, frames);
            var nrmTex = MakeTex(vCount, frames);
            var posCols = new Color[vCount * frames];
            var nrmCols = new Color[vCount * frames];
            var baked = new Mesh();
            var min = new Vector3(1e9f, 1e9f, 1e9f);
            var max = -min;

            SampleClip(inst, smr, baked, clip, frames, 0, vCount, vertexRotation, posCols, nrmCols, ref min, ref max);

            posTex.SetPixels(posCols); posTex.Apply(false, false);
            nrmTex.SetPixels(nrmCols); nrmTex.Apply(false, false);

            string posPath = $"{outputDir}/{outName}_PositionMap.asset";
            string nrmPath = $"{outputDir}/{outName}_NormalMap.asset";
            AssetDatabase.DeleteAsset(posPath); AssetDatabase.CreateAsset(posTex, posPath);
            AssetDatabase.DeleteAsset(nrmPath); AssetDatabase.CreateAsset(nrmTex, nrmPath);

            var vatMesh = BuildVATMesh(smr.sharedMesh, vCount, outName, min, max);
            string meshPath = $"{outputDir}/{outName}_VATMesh.asset";
            AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(vatMesh, meshPath);

            AssetDatabase.SaveAssets();
            Object.DestroyImmediate(inst); Object.DestroyImmediate(baked);

            return new Result {
                positionMap = posPath, normalMap = nrmPath, mesh = meshPath,
                vertexCount = vCount, frames = frames, clipLength = clip.length, bounds = vatMesh.bounds
            };
        }

        // ────────────────────────────────────────────────────────────────
        // 멀티 클립 스택 → VATClipSet (텍스처/메시를 서브에셋으로 편입, 파일 하나).
        // ────────────────────────────────────────────────────────────────
        public static VATClipSet BakeMulti(
            GameObject modelPrefab, (AnimationClip clip, bool loop)[] clips, int framesPerClip,
            string outputDir, string outName, Quaternion vertexRotation = default)
        {
            if (modelPrefab == null) throw new System.Exception("modelPrefab 이 null");
            if (clips == null || clips.Length == 0) throw new System.Exception("clips 가 비었음");
            if (framesPerClip < 2) framesPerClip = 2;
            vertexRotation = Normalize(vertexRotation);
            EnsureAssetFolder(outputDir);

            var inst = InstantiateModel(modelPrefab, out var smr);
            int vCount = smr.sharedMesh.vertexCount;
            int total = clips.Length * framesPerClip;   // 세로 = 클립수 × 프레임

            var posTex = MakeTex(vCount, total);
            var nrmTex = MakeTex(vCount, total);
            var posCols = new Color[vCount * total];
            var nrmCols = new Color[vCount * total];
            var baked = new Mesh();
            var min = new Vector3(1e9f, 1e9f, 1e9f);
            var max = -min;

            var entries = new ClipEntry[clips.Length];
            int rowCursor = 0;
            for (int c = 0; c < clips.Length; c++)
            {
                var clip = clips[c].clip;
                if (clip == null) { Object.DestroyImmediate(inst); Object.DestroyImmediate(baked); throw new System.Exception($"clips[{c}].clip 이 null"); }

                SampleClip(inst, smr, baked, clip, framesPerClip, rowCursor, vCount, vertexRotation, posCols, nrmCols, ref min, ref max);
                entries[c] = new ClipEntry {
                    name = clip.name,
                    startRow = rowCursor,
                    frameCount = framesPerClip,
                    fps = framesPerClip / clip.length,   // 원래 속도
                    loop = clips[c].loop
                };
                rowCursor += framesPerClip;
            }

            posTex.SetPixels(posCols); posTex.Apply(false, false);
            nrmTex.SetPixels(nrmCols); nrmTex.Apply(false, false);
            posTex.name = outName + "_PositionMap";
            nrmTex.name = outName + "_NormalMap";

            var vatMesh = BuildVATMesh(smr.sharedMesh, vCount, outName, min, max);

            // VATClipSet 에셋 + 텍스처/메시를 서브에셋으로
            var set = ScriptableObject.CreateInstance<VATClipSet>();
            set.vertexCount = vCount;
            set.totalFrames = total;
            set.clips = entries;
            string setPath = $"{outputDir}/{outName}_ClipSet.asset";
            AssetDatabase.DeleteAsset(setPath);
            AssetDatabase.CreateAsset(set, setPath);
            AssetDatabase.AddObjectToAsset(posTex, set);
            AssetDatabase.AddObjectToAsset(nrmTex, set);
            AssetDatabase.AddObjectToAsset(vatMesh, set);
            set.positionMap = posTex;
            set.normalMap = nrmTex;
            set.mesh = vatMesh;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(setPath);

            Object.DestroyImmediate(inst); Object.DestroyImmediate(baked);
            return set;
        }

        // ── 한 클립을 rowStart 행부터 framesPerClip 행에 기록 (min/max 갱신) ──
        static void SampleClip(
            GameObject inst, SkinnedMeshRenderer smr, Mesh baked,
            AnimationClip clip, int framesPerClip, int rowStart, int vCount, Quaternion rot,
            Color[] posCols, Color[] nrmCols, ref Vector3 min, ref Vector3 max)
        {
            for (int f = 0; f < framesPerClip; f++)
            {
                float t = (f / (float)framesPerClip) * clip.length;   // [0,len) 균등 → 루프 이음새 없음
                clip.SampleAnimation(inst, t);
                smr.BakeMesh(baked, true);

                var vs = baked.vertices;
                var ns = baked.normals;
                bool hasN = ns != null && ns.Length == vCount;
                int row = rowStart + f;
                for (int v = 0; v < vCount; v++)
                {
                    Vector3 p = rot * vs[v];
                    Vector3 n = rot * (hasN ? ns[v] : Vector3.up);
                    int idx = row * vCount + v;               // 행우선: index = y*width + x
                    posCols[idx] = new Color(p.x, p.y, p.z, 1f);
                    nrmCols[idx] = new Color(n.x, n.y, n.z, 0f);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }

        // ── 소소한 헬퍼들 ──────────────────────────────────
        static Quaternion Normalize(Quaternion q) =>
            (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) ? Quaternion.identity : q;

        static Texture2D MakeTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            t.filterMode = FilterMode.Point;      // 텍셀 정확히(보간 금지)
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        static GameObject InstantiateModel(GameObject modelPrefab, out SkinnedMeshRenderer smr)
        {
            var inst = (GameObject)Object.Instantiate(modelPrefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) { Object.DestroyImmediate(inst); throw new System.Exception("SkinnedMeshRenderer 없음"); }
            return inst;
        }

        static Mesh BuildVATMesh(Mesh source, int vCount, string outName, Vector3 min, Vector3 max)
        {
            var vatMesh = Object.Instantiate(source);
            vatMesh.name = outName + "_VATMesh";
            var uv3 = new List<Vector4>(vCount);
            for (int v = 0; v < vCount; v++) uv3.Add(new Vector4(v, 0f, 0f, 0f));   // UV3.x = 정점 인덱스
            vatMesh.SetUVs(3, uv3);
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            vatMesh.bounds = bounds;   // 애니 최대 범위(컬링용)
            return vatMesh;
        }

        static void EnsureAssetFolder(string dir)
        {
            dir = dir.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir).Replace("\\", "/");
            string leaf = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>FBX(또는 .anim)에서 첫 AnimationClip 로드.</summary>
        public static AnimationClip LoadFirstClip(string clipPath)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(clipPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            return null;
        }

        public static Result BakeFromPaths(
            string modelFbxPath, string clipFbxPath, int frames,
            string outputDir, string outName, Quaternion vertexRotation = default)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelFbxPath);
            return Bake(model, LoadFirstClip(clipFbxPath), frames, outputDir, outName, vertexRotation);
        }
    }
}
#endif
