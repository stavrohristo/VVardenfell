using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Vfx
{
    public struct MorrowindVfxSpawnRequest : IComponentData
    {
        public FixedString512Bytes ModelPath;
        public FixedString512Bytes TextureOverridePath;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public Entity FollowEntity;
        public FixedString128Bytes FollowNodeName;
        public byte Loop;
        public uint EffectId;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct MorrowindVfxRemoveRequest : IComponentData
    {
        public Entity Owner;
        public uint EffectId;
    }

    public struct MorrowindVfxRuntimeState : IComponentData
    {
    }
}
