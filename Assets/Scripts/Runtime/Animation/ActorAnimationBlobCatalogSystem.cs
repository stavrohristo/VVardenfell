using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup), OrderFirst = true)]
    public partial struct ActorAnimationBlobCatalogSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorAnimationBlobCatalogBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
            {
                RuntimeBootstrapRequestUtility.Consume<ActorAnimationBlobCatalogBootstrapRequest>(systemState.EntityManager);
                return;
            }

            var catalog = WorldResources.Cache?.ActorAnimationCatalog;
            if (catalog == null)
                return;

            var blob = ActorAnimationBlobBuilder.Build(catalog);

            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ActorAnimationBlobCatalog { Blob = blob });
            ecb.Playback(systemState.EntityManager);
            RuntimeBootstrapRequestUtility.Consume<ActorAnimationBlobCatalogBootstrapRequest>(systemState.EntityManager);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                return;

            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>();
            if (catalog.Blob.IsCreated)
                catalog.Blob.Dispose();
        }
    }
}
