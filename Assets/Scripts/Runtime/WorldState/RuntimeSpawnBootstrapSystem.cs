using Unity.Entities;
using Unity.Collections;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial class RuntimeSpawnBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            bool created = false;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<RuntimeSpawnState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            }
            else
            {
                runtimeEntity = ecb.CreateEntity();
                ecb.SetName(runtimeEntity, new FixedString64Bytes("VVardenfell.RuntimeSpawn"));
                created = true;
            }

            EnsureComponent(runtimeEntity, new RuntimeSpawnState(), ref ecb, created);
            EnsureComponent(runtimeEntity, new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            }, ref ecb, created);
            EnsureBuffer<RuntimeSpawnRequest>(runtimeEntity, ref ecb, created);
            EnsureBuffer<RuntimeSpawnedRef>(runtimeEntity, ref ecb, created);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value, ref EntityCommandBuffer ecb, bool created)
            where T : unmanaged, IComponentData
        {
            if (created || !EntityManager.HasComponent<T>(entity))
                ecb.AddComponent(entity, value);
        }

        void EnsureBuffer<T>(Entity entity, ref EntityCommandBuffer ecb, bool created)
            where T : unmanaged, IBufferElementData
        {
            if (created || !EntityManager.HasBuffer<T>(entity))
                ecb.AddBuffer<T>(entity);
        }
    }
}
