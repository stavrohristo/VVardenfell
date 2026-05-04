using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class DeferredPhysicsQueryBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<DeferredPhysicsQueryQueueTag>())
            {
                Enabled = false;
                return;
            }

            Entity entity = EntityManager.CreateEntity(typeof(DeferredPhysicsQueryQueueTag), typeof(DeferredPhysicsQueryRuntime));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.DeferredPhysicsQueryQueue"));
            EntityManager.AddBuffer<DeferredPhysicsQueryRequest>(entity);
            EntityManager.AddBuffer<DeferredPhysicsQueryResult>(entity);
            EntityManager.SetComponentData(entity, new DeferredPhysicsQueryRuntime());
            Enabled = false;
        }
    }
}
