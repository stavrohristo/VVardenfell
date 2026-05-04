using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup), OrderFirst = true)]
    public partial class ObjectAnimationBlobCatalogSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ObjectAnimationBlobCatalogBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
            {
                RuntimeBootstrapRequestUtility.Consume<ObjectAnimationBlobCatalogBootstrapRequest>(EntityManager);
                return;
            }

            var catalog = WorldResources.Cache?.ModelPrefabCatalog;
            if (catalog?.Records == null)
                return;

            var blob = ObjectAnimationBlobBuilder.Build(catalog);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ObjectAnimationBlobCatalog { Blob = blob });
            ecb.Playback(EntityManager);
            RuntimeBootstrapRequestUtility.Consume<ObjectAnimationBlobCatalogBootstrapRequest>(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (!SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
                return;

            var catalog = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>();
            if (catalog.Blob.IsCreated)
                catalog.Blob.Dispose();
        }
    }
}
