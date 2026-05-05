using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class RuntimeContentBlobReferenceSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            var blob = WorldResources.Cache?.ContentBlob ?? default;
            if (!blob.IsCreated)
                return;

            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RuntimeContentBlobReference { Blob = blob });
            ecb.Playback(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (!SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            var reference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (reference.Blob.IsCreated)
                reference.Blob.Dispose();
        }
    }
}
