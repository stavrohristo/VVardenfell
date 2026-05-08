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
        public byte VisibilityMode;
    }

    public static class ActorRenderMeshVisibilityMode
    {
        public const byte Normal = 0;
        public const byte FirstPersonCameraHidden = 1;
        public const byte FirstPersonShadowOnly = 2;
    }
}
