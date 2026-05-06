using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct MorrowindCombatRuntimeBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindCombatRuntimeBootstrapRequest>();
            state.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<MorrowindCombatRuntimeState>())
            {
                RuntimeBootstrapRequestUtility.Consume<MorrowindCombatRuntimeBootstrapRequest>(state.EntityManager);
                return;
            }

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                return;

            Entity entity = state.EntityManager.CreateEntity(typeof(MorrowindCombatRuntimeState));
            state.EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.MorrowindCombatRuntime"));
            state.EntityManager.SetComponentData(entity, new MorrowindCombatRuntimeState
            {
                RandomState = 0x6E624EB7u,
            });
            state.EntityManager.AddBuffer<PendingMeleeHitConfirmation>(entity);
            RuntimeBootstrapRequestUtility.Consume<MorrowindCombatRuntimeBootstrapRequest>(state.EntityManager);
        }
    }
}


