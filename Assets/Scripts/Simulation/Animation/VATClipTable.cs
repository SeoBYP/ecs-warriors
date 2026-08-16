using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Simulation.Components
{
    // 상태. 순서 = ClipSet에 구운 클립 순서(0 Idle … 4 Death)와 일치.
    public enum ZAnim : byte
    {
        Idle = 0,
        Walk = 1,
        Attack = 2,
        Damage = 3,
        Death = 4,
        None=255
    }

    // 상태별 클립 파라미터 테이블. Params[ZAnim] = (startRow, frameCount, fps, loop).
    public struct VATClipBlob
    {
        public BlobArray<float4> Params;
    }
        
    // 씬에 하나(싱글톤). ZombieAnimSystem이 상태→파라미터 조회에 씀.
    public struct VATClipTable : IComponentData
    {
        public BlobAssetReference<VATClipBlob> Blob;
    }
    
    // VATClipSet(SO) → ECS blob 테이블로 굽는 authoring.
    public class VATClipTableAuthoring : MonoBehaviour
    {
        public VATClipSet clipSet;
    }

    public struct ZombieAnim : IComponentData { public byte Current; } 
    
    class VATClipTableBaker : Baker<VATClipTableAuthoring>
    {
        public override void Bake(VATClipTableAuthoring authoring)
        {
            if(authoring.clipSet == null || authoring.clipSet.clips == null || authoring.clipSet.clips.Length == 0)
                throw new System.Exception("clipSet is null or empty");
            
            var entity = GetEntity(TransformUsageFlags.None);
            
            // 1) BlobBuilder로 5개 상태 슬롯 구성
            var builder = new BlobBuilder(Allocator.Temp);
            ref VATClipBlob root  = ref builder.ConstructRoot<VATClipBlob>();
            int count = System.Enum.GetValues(typeof(ZAnim)).Length;
            BlobBuilderArray<float4> arr = builder.Allocate(ref root.Params, count);

            // 없는 상태는 idle(0행)로 폴백
            for (int i = 0; i < count; i++) arr[i] = new float4(0, 30, 30, 1);

            // 2) 각 클립을 "이름"으로 상태에 매핑
            foreach (var c in authoring.clipSet.clips)
            {
                int s = MapNameToState(c.name);
                if (s >= 0) arr[s] = new float4(c.startRow, c.frameCount, c.fps, c.loop ? 1f : 0f);
            }
            
            // 3) blob 생성 → Baker가 수명 관리(수동 Dispose 불필요)
            var blob = builder.CreateBlobAssetReference<VATClipBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref blob, out _);

            AddComponent(entity, new VATClipTable { Blob = blob });
        }
        
        // death/damage/attack을 idle/walk보다 먼저 검사 → "death_idle" 같은 오탐 방지
        static int MapNameToState(string clipName)
        {
            string n = clipName.ToLowerInvariant();
            if (n.Contains("death"))  return (int)ZAnim.Death;
            if (n.Contains("damage")) return (int)ZAnim.Damage;
            if (n.Contains("attack") || n.Contains("bite")) return (int)ZAnim.Attack;
            if (n.Contains("walk") || n.Contains("run"))    return (int)ZAnim.Walk;
            if (n.Contains("idle"))   return (int)ZAnim.Idle;
            return -1;
        }
    }
}