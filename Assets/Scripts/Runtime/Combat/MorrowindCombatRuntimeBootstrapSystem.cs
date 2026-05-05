using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class MorrowindCombatRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindCombatRuntimeBootstrapRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MorrowindCombatRuntimeState>())
            {
                RuntimeBootstrapRequestUtility.Consume<MorrowindCombatRuntimeBootstrapRequest>(EntityManager);
                return;
            }

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                return;

            Entity entity = EntityManager.CreateEntity(typeof(MorrowindCombatRuntimeState));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.MorrowindCombatRuntime"));
            EntityManager.SetComponentData(entity, new MorrowindCombatRuntimeState
            {
                RandomState = 0x6E624EB7u,
            });
            EntityManager.AddBuffer<PendingMeleeHitConfirmation>(entity);
            RuntimeBootstrapRequestUtility.Consume<MorrowindCombatRuntimeBootstrapRequest>(EntityManager);
        }
    }
}


