using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial struct DeferredPhysicsQueryBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<DeferredPhysicsQueryBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<DeferredPhysicsQueryQueueTag>())
            {
                Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
                if (!systemState.EntityManager.HasComponent<DeferredPhysicsQueryPending>(queueEntity))
                {
                    systemState.EntityManager.AddComponent<DeferredPhysicsQueryPending>(queueEntity);
                    systemState.EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, false);
                }

                RuntimeBootstrapRequestUtility.Consume<DeferredPhysicsQueryBootstrapRequest>(systemState.EntityManager);
                return;
            }

            Entity entity = systemState.EntityManager.CreateEntity(
                typeof(DeferredPhysicsQueryQueueTag),
                typeof(DeferredPhysicsQueryPending),
                typeof(DeferredPhysicsQueryRuntime));
            systemState.EntityManager.AddBuffer<DeferredPhysicsQueryRequest>(entity);
            systemState.EntityManager.AddBuffer<DeferredPhysicsQueryResult>(entity);
            systemState.EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(entity, false);
            systemState.EntityManager.SetComponentData(entity, new DeferredPhysicsQueryRuntime());
            RuntimeBootstrapRequestUtility.Consume<DeferredPhysicsQueryBootstrapRequest>(systemState.EntityManager);
        }
    }
}
