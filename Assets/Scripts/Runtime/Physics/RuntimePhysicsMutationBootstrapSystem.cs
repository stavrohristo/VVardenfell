using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class RuntimePhysicsMutationBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimePhysicsMutationBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<RuntimePhysicsMutationQueueTag>())
            {
                RuntimeBootstrapRequestUtility.Consume<RuntimePhysicsMutationBootstrapRequest>(EntityManager);
                return;
            }

            Entity entity = EntityManager.CreateEntity(typeof(RuntimePhysicsMutationQueueTag), typeof(PhysicsFlushRequested));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.RuntimePhysicsMutationQueue"));
            EntityManager.AddBuffer<RuntimePhysicsMutationRequest>(entity);
            EntityManager.SetComponentData(entity, new PhysicsFlushRequested());
            RuntimeBootstrapRequestUtility.Consume<RuntimePhysicsMutationBootstrapRequest>(EntityManager);
        }
    }
}
