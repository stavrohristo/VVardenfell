using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Animation
{
    public struct ObjectAnimationState : IComponentData
    {
        public int ModelPrefabIndex;
        public int ClipIndex;
        public float PreviousTime;
        public float CurrentTime;
        public byte Active;
    }

    public struct ObjectAnimationNode : IComponentData
    {
        public Entity Root;
        public int ModelPrefabIndex;
        public int NodeIndex;
        public int ParentIndex;
        public float3 BindPosition;
        public quaternion BindRotation;
        public float BindScale;
    }
}
