using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(RuntimeContentBlobReferenceSystem))]
    public partial struct RuntimeModelPrefabBlobReferenceSystem : ISystem
    {
        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimeModelPrefabBlobReference>())
                return;

            var cache = WorldResources.Cache;
            if (cache?.ModelPrefabCatalog?.Records == null || WorldResources.ColliderBlobs == null)
                return;

            var blob = RuntimeModelPrefabBlobBuilder.Build(cache.ModelPrefabCatalog, WorldResources.ColliderBlobs);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RuntimeModelPrefabBlobReference { Blob = blob });
            ecb.Playback(systemState.EntityManager);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (!SystemAPI.HasSingleton<RuntimeModelPrefabBlobReference>())
                return;

            var reference = SystemAPI.GetSingleton<RuntimeModelPrefabBlobReference>();
            if (reference.Blob.IsCreated)
                reference.Blob.Dispose();
        }
    }
}
