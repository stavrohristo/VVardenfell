using Unity.Burst;
#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorPresentationSpawnSystem))]
    [UpdateBefore(typeof(ActorGpuAnimationRequestSystem))]
    public partial struct InventoryAvatarPreviewCombatPoseSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<InventoryAvatarPreviewTag>();
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
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
                state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref contentBlob, equipment, out var weaponContent);
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
