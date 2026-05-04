using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class DeferredPhysicsQueryBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<DeferredPhysicsQueryBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<DeferredPhysicsQueryQueueTag>())
            {
                Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
                if (!EntityManager.HasComponent<DeferredPhysicsQueryPending>(queueEntity))
                {
                    EntityManager.AddComponent<DeferredPhysicsQueryPending>(queueEntity);
                    EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, false);
                }

                RuntimeBootstrapRequestUtility.Consume<DeferredPhysicsQueryBootstrapRequest>(EntityManager);
                return;
            }

            Entity entity = EntityManager.CreateEntity(
                typeof(DeferredPhysicsQueryQueueTag),
                typeof(DeferredPhysicsQueryPending),
                typeof(DeferredPhysicsQueryRuntime));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.DeferredPhysicsQueryQueue"));
            EntityManager.AddBuffer<DeferredPhysicsQueryRequest>(entity);
            EntityManager.AddBuffer<DeferredPhysicsQueryResult>(entity);
            EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(entity, false);
            EntityManager.SetComponentData(entity, new DeferredPhysicsQueryRuntime());
            RuntimeBootstrapRequestUtility.Consume<DeferredPhysicsQueryBootstrapRequest>(EntityManager);
        }
    }
}
