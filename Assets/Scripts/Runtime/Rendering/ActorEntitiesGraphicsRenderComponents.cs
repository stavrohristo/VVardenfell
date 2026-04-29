using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Rendering
{
    [MaterialProperty("_ActorDeformedMeshIndex")]
    public struct ActorDeformedMeshIndex : IComponentData
    {
        public float Value;
    }

    public struct ActorRenderMeshInstance : IComponentData
    {
        public Entity Actor;
        public int SkinMeshIndex;
    }
}
