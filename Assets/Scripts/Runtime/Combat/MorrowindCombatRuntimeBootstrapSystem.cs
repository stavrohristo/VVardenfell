using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class MorrowindCombatRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MorrowindCombatRuntimeState>())
                return;

            if (RuntimeContentDatabase.Active == null)
                return;

            Entity entity = EntityManager.CreateEntity(typeof(MorrowindCombatRuntimeState));
            EntityManager.SetName(entity, new FixedString64Bytes("VVardenfell.MorrowindCombatRuntime"));
            EntityManager.SetComponentData(entity, new MorrowindCombatRuntimeState
            {
                RandomState = 0x6E624EB7u,
            });
            EntityManager.AddBuffer<PendingPlayerMeleeHitConfirmation>(entity);
        }
    }
}
