using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial struct RuntimePhysicsMutationBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimePhysicsMutationBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<RuntimePhysicsMutationQueueTag>())
            {
                RuntimeBootstrapRequestUtility.Consume<RuntimePhysicsMutationBootstrapRequest>(systemState.EntityManager);
                return;
            }

            Entity entity = systemState.EntityManager.CreateEntity(typeof(RuntimePhysicsMutationQueueTag), typeof(PhysicsFlushRequested));
            systemState.EntityManager.AddBuffer<RuntimePhysicsMutationRequest>(entity);
            systemState.EntityManager.SetComponentData(entity, new PhysicsFlushRequested());
            RuntimeBootstrapRequestUtility.Consume<RuntimePhysicsMutationBootstrapRequest>(systemState.EntityManager);
        }
    }
}
