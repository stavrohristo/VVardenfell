using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorPoseSamplingSystem))]
    public partial struct ActorSkinnedMeshRenderSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorSkinMesh>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Actor GPU animation upload now reads static ActorSkinMesh data directly.
            // Keep this system slot for ordering, but avoid rebuilding a transient draw buffer every frame.
        }
    }
}
