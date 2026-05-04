using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial class RuntimePhysicsMutationBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<RuntimePhysicsMutationQueueTag>())
            {
                Enabled = false;
                return;
            }

            Entity entity = EntityManager.CreateEntity(typeof(RuntimePhysicsMutationQueueTag), typeof(PhysicsFlushRequested));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.RuntimePhysicsMutationQueue"));
            EntityManager.AddBuffer<RuntimePhysicsMutationRequest>(entity);
            EntityManager.SetComponentData(entity, new PhysicsFlushRequested());
            Enabled = false;
        }
    }
}
