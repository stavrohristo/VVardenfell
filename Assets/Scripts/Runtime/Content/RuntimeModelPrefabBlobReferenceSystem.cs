using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(RuntimeContentBlobReferenceSystem))]
    public partial class RuntimeModelPrefabBlobReferenceSystem : SystemBase
    {
        protected override void OnUpdate()
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
            ecb.Playback(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (!SystemAPI.HasSingleton<RuntimeModelPrefabBlobReference>())
                return;

            var reference = SystemAPI.GetSingleton<RuntimeModelPrefabBlobReference>();
            if (reference.Blob.IsCreated)
                reference.Blob.Dispose();
        }
    }
}
