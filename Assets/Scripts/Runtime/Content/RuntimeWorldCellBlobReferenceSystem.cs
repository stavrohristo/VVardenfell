using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Content
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(RuntimeModelPrefabBlobReferenceSystem))]
    public partial class RuntimeWorldCellBlobReferenceSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<RuntimeWorldCellBlobReference>())
                return;
            if (!WorldResources.HasAnyPreloadedCells())
                return;

            var blob = RuntimeWorldCellBlobBuilder.Build();
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new RuntimeWorldCellBlobReference { Blob = blob });
            ecb.Playback(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (!SystemAPI.HasSingleton<RuntimeWorldCellBlobReference>())
                return;

            var reference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (reference.Blob.IsCreated)
                reference.Blob.Dispose();
        }
    }
}
