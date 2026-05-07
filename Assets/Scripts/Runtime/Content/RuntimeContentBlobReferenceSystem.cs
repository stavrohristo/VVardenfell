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
        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            var blob = WorldResources.Cache?.ContentBlob ?? default;
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
            // WorldResources.Cache.ContentBlob. WorldResources.Reset owns disposal.
        }
    }
}
