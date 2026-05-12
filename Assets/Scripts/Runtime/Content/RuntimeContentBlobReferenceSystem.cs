using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial struct RuntimeContentBlobReferenceSystem : ISystem
    {
        EntityQuery _materializationResourcesQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _materializationResourcesQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeMaterializationResources>());
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            if (_materializationResourcesQuery.IsEmptyIgnoreFilter)
                return;

            var blob = RuntimeMaterializationResources.Require(systemState.EntityManager).ContentBlob;
            if (!blob.IsCreated)
                return;

            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RuntimeContentBlobReference { Blob = blob });
            ecb.Playback(systemState.EntityManager);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            // RuntimeContentBlobReference is a non-owning ECS handle to
            // RuntimeMaterializationResources.ContentBlob. Cache teardown owns disposal.
        }
    }
}
