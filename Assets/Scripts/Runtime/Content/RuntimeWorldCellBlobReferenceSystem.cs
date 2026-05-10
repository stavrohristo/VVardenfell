using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(RuntimeModelPrefabBlobReferenceSystem))]
    public partial struct RuntimeWorldCellBlobReferenceSystem : ISystem
    {
        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimeWorldCellBlobReference>())
                return;
            if (WorldResources.Cache == null)
                return;

            var blob = RuntimeWorldCellBlobBuilder.Build(WorldResources.Cache);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RuntimeWorldCellBlobReference { Blob = blob });
            ecb.Playback(systemState.EntityManager);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (!SystemAPI.HasSingleton<RuntimeWorldCellBlobReference>())
                return;

            var reference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (reference.Blob.IsCreated)
                reference.Blob.Dispose();
        }
    }
}
