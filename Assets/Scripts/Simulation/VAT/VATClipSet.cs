using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Simulation.Components
{
    public class VATClipSet : ScriptableObject
    {
        public Texture2D positionMap;
        public Texture2D normalMap;
        public Mesh mesh;
        
        public int vertexCount;
        public int totalFrames;
        public ClipEntry[] clips;
    }

    [Serializable]
    public struct ClipEntry
    {
        public string name;
        public int startRow;
        public int frameCount;
        public float fps;
        public bool loop;
    }

    [MaterialProperty("_AnimParams")]
    struct VATAnimParams : IComponentData
    {
        public float4 Value;
    }
    
    [MaterialProperty("_AnimStart")]
    struct VATAnimStart : IComponentData
    {
        public float Value;
    }
}