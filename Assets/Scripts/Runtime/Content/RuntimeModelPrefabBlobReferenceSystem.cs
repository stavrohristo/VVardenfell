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
        EntityQuery _materializationResourcesQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _materializationResourcesQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeMaterializationResources>());
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimeModelPrefabBlobReference>())
                return;

            if (_materializationResourcesQuery.IsEmptyIgnoreFilter)
                return;

            var resources = RuntimeMaterializationResources.Require(systemState.EntityManager);
            var cache = resources.Cache;
            if (cache?.ModelPrefabCatalog?.Records == null || resources.ColliderBlobs == null)
                return;

            var blob = RuntimeModelPrefabBlobBuilder.Build(cache.ModelPrefabCatalog, resources.ColliderBlobs);
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
