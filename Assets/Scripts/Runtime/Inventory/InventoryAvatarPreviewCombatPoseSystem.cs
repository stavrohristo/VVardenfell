#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorPresentationSpawnSystem))]
    [UpdateBefore(typeof(ActorGpuAnimationRequestSystem))]
    public partial class InventoryAvatarPreviewCombatPoseSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InventoryAvatarPreviewTag>();
            RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            ref var catalog = ref catalogRef.Value;
            foreach (var (presentation, movementState, weaponState, equipment, overlays) in
                     SystemAPI.Query<
                         RefRO<ActorPresentation>,
                         RefRO<MorrowindMovementState>,
                         RefRW<ActorWeaponAnimationState>,
                         DynamicBuffer<ActorEquipmentSlot>,
                         DynamicBuffer<ActorAnimationOverlayState>>()
                         .WithAll<InventoryAvatarPreviewTag>())
            {
                ref var state = ref weaponState.ValueRW;
                state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(contentDb, equipment, out var weaponContent);
                state.WeaponContent = weaponContent;
                state.Drawn = 1;
                state.Phase = ActorWeaponAnimationPhase.Equipped;
                state.AttackHeld = 0;
                state.AttackPressed = 0;
                state.AttackReleased = 0;
                state.ReleaseQueued = 0;

                ActorWeaponAnimationSystem.UpdateWeaponAnimation(
                    ref catalog,
                    presentation.ValueRO,
                    movementState.ValueRO,
                    ref state,
                    overlays);
            }
        }
    }
}
#endif
