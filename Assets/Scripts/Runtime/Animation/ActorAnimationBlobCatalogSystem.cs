using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup), OrderFirst = true)]
    public partial class ActorAnimationBlobCatalogSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                return;

            var catalog = WorldResources.Cache?.ActorAnimationCatalog;
            if (catalog == null)
                return;

            var blob = ActorAnimationBlobBuilder.Build(catalog);

            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ActorAnimationBlobCatalog { Blob = blob });
            ecb.Playback(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                return;

            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>();
            if (catalog.Blob.IsCreated)
                catalog.Blob.Dispose();
        }
    }
}
