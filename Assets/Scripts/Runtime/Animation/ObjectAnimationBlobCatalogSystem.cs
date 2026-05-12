using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup), OrderFirst = true)]
    public partial struct ObjectAnimationBlobCatalogSystem : ISystem
    {
        EntityQuery _materializationResourcesQuery;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ObjectAnimationBlobCatalogBootstrapRequest>();
            _materializationResourcesQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeMaterializationResources>());
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
            {
                RuntimeBootstrapRequestUtility.Consume<ObjectAnimationBlobCatalogBootstrapRequest>(systemState.EntityManager);
                return;
            }

            if (_materializationResourcesQuery.IsEmptyIgnoreFilter)
                return;

            var catalog = RuntimeMaterializationResources.Require(systemState.EntityManager).Cache?.ModelPrefabCatalog;
            if (catalog?.Records == null)
                return;

            var blob = ObjectAnimationBlobBuilder.Build(catalog);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ObjectAnimationBlobCatalog { Blob = blob });
            ecb.Playback(systemState.EntityManager);
            RuntimeBootstrapRequestUtility.Consume<ObjectAnimationBlobCatalogBootstrapRequest>(systemState.EntityManager);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (!SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
                return;

            var catalog = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>();
            if (catalog.Blob.IsCreated)
                catalog.Blob.Dispose();
        }
    }
}
