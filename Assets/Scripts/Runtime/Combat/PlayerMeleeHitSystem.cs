using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(MorrowindMeleeHitConfirmationSystem))]
    public partial struct PlayerMeleeHitSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewPoseQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCharacterComponent>(),
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>());
            _viewPoseQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerPhysicsViewPose>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_viewPoseQuery);
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Player melee hit requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            Entity player = _playerQuery.GetSingletonEntity();
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindCombatRuntimeState>();

            Entity visualEntity = ResolveActivePlayerVisual(ref systemState, player, out var weaponState);
            bool stateChanged = false;

            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            bool hasAudioState = SystemAPI.TryGetSingletonEntity<InteractionAudioRequestState>(out Entity audioEntity);
            var audioState = hasAudioState
                ? systemState.EntityManager.GetComponentData<InteractionAudioRequestState>(audioEntity)
                : default;
            var audioEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            if (weaponState.MeleeSwingPending != 0)
            {
                EmitWeaponSwish(ref content, player, playerTransform.Position, weaponState, ref audioState, hasAudioState, ref audioEcb);
                weaponState.MeleeSwingPending = 0;
                weaponState.MeleeSwingAttackStrength = 0f;
                weaponState.MeleeSwingWeaponContent = default;
                stateChanged = true;
            }

            if (hasAudioState)
                systemState.EntityManager.SetComponentData(audioEntity, audioState);
            audioEcb.Playback(systemState.EntityManager);
            audioEcb.Dispose();

            if (weaponState.MeleeHitPending == 0)
            {
                if (stateChanged)
                    systemState.EntityManager.SetComponentData(visualEntity, weaponState);
                return;
            }

            var hitAttackType = weaponState.MeleeHitAttackType;
            float hitAttackStrength = math.saturate(weaponState.MeleeHitAttackStrength);
            var hitWeaponContent = weaponState.MeleeHitWeaponContent;
            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                hitWeaponContent,
                out bool hasWeapon,
                out _,
                out ItemEquipmentDef weapon,
                out _);

            var playerVitals = _playerQuery.GetSingleton<ActorVitalSet>();
            var playerDerived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var playerComponent = _playerQuery.GetSingleton<PlayerCharacterComponent>();

            SpendAttackFatigue(ref content, ref playerVitals, playerDerived, hasWeapon, weapon, hitAttackStrength);
            systemState.EntityManager.SetComponentData(player, playerVitals);

            var viewPose = _viewPoseQuery.GetSingleton<PlayerPhysicsViewPose>();
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            float reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
            TryQueueMeleeConfirmation(ref systemState, 
                ref content,
                logicalRefLookup,
                player,
                playerTransform.Position,
                playerComponent.Radius,
                viewPose,
                reach,
                hitWeaponContent,
                hitAttackType,
                hitAttackStrength,
                runtimeEntity,
                deferredPhysicsQueueEntity,
                fixedTick,
                out _,
                out _);

            weaponState.MeleeHitPending = 0;
            weaponState.MeleeHitAttackStrength = 0f;
            weaponState.MeleeHitWeaponContent = default;
            systemState.EntityManager.SetComponentData(visualEntity, weaponState);
        }

        bool TryQueueMeleeConfirmation(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float3 playerPosition,
            float playerRadius,
            in PlayerPhysicsViewPose viewPose,
            float reach,
            in ContentReference weaponContent,
            ActorWeaponAttackType attackType,
            float attackStrength,
            Entity runtimeEntity,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            out uint querySequence,
            out uint targetPlacedRefId)
        {
            querySequence = 0u;
            targetPlacedRefId = 0u;
            if (!TrySelectMeleeTarget(ref systemState, 
                    ref content,
                    logicalRefLookup,
                    player,
                    playerPosition,
                    playerRadius,
                    viewPose,
                    reach,
                    out Entity target,
                    out targetPlacedRefId,
                    out float3 hitPosition,
                    out byte hasHitPosition,
                    out float3 confirmationEnd))
            {
                return false;
            }

            querySequence = DeferredPhysicsQueryUtility.EnqueueRay(
                systemState.EntityManager,
                deferredPhysicsQueueEntity,
                fixedTick,
                DeferredPhysicsQueryKind.MeleeConfirmation,
                player,
                target,
                player,
                viewPose.Position,
                confirmationEnd,
                InteractionCollisionLayers.LineOfSightQueryFilter);
            systemState.EntityManager.GetBuffer<PendingMeleeHitConfirmation>(runtimeEntity).Add(new PendingMeleeHitConfirmation
            {
                QuerySequence = querySequence,
                RequestFixedTick = fixedTick,
                Attacker = player,
                Target = target,
                WeaponContent = weaponContent,
                AttackType = attackType,
                AttackStrength = attackStrength,
                Reach = reach,
                TargetPlacedRefId = targetPlacedRefId,
                HitPosition = hitPosition,
                HasHitPosition = hasHitPosition,
            });
            return true;
        }

        bool TrySelectMeleeTarget(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float3 playerPosition,
            float playerRadius,
            in PlayerPhysicsViewPose viewPose,
            float reach,
            out Entity target,
            out uint targetPlacedRefId,
            out float3 hitPosition,
            out byte hasHitPosition,
            out float3 confirmationEnd)
        {
            if (TrySelectFocusedMeleeTarget(ref systemState, 
                    logicalRefLookup,
                    player,
                    reach,
                    out target,
                    out targetPlacedRefId,
                    out hitPosition,
                    out hasHitPosition,
                    out confirmationEnd))
            {
                return true;
            }

            return TrySelectActorBoundsMeleeTarget(ref systemState, 
                ref content,
                player,
                playerPosition,
                playerRadius,
                viewPose,
                reach,
                out target,
                out targetPlacedRefId,
                out hitPosition,
                out hasHitPosition,
                out confirmationEnd);
        }

        bool TrySelectFocusedMeleeTarget(ref SystemState systemState, 
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float reach,
            out Entity target,
            out uint targetPlacedRefId,
            out float3 hitPosition,
            out byte hasHitPosition,
            out float3 confirmationEnd)
        {
            target = Entity.Null;
            targetPlacedRefId = 0u;
            hitPosition = default;
            hasHitPosition = 0;
            confirmationEnd = default;

            if (!SystemAPI.TryGetSingleton<PlayerInteractionFocus>(out var focus))
                return false;
            if (focus.HasTarget == 0 || focus.TargetEntity == Entity.Null || focus.HitDistance > reach)
                return false;
            if (!ValidateActorMeleeTarget(ref systemState, player, player, focus.TargetEntity, out targetPlacedRefId))
                return false;
            if (!TryGetActorCenter(ref systemState, focus.TargetEntity, out confirmationEnd))
                return false;

            target = focus.TargetEntity;
            if (TryGetInteractionRayHitPosition(ref systemState, logicalRefLookup, target, out hitPosition))
            {
                hasHitPosition = 1;
                confirmationEnd = hitPosition;
            }
            else
            {
                hitPosition = confirmationEnd;
                hasHitPosition = 1;
            }

            return true;
        }

        bool TrySelectActorBoundsMeleeTarget(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity player,
            float3 playerPosition,
            float playerRadius,
            in PlayerPhysicsViewPose viewPose,
            float reach,
            out Entity target,
            out uint targetPlacedRefId,
            out float3 hitPosition,
            out byte hasHitPosition,
            out float3 confirmationEnd)
        {
            target = Entity.Null;
            targetPlacedRefId = 0u;
            hitPosition = default;
            hasHitPosition = 0;
            confirmationEnd = default;

            float combatAngleXY = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleXY) / 90f;
            float combatAngleZ = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleZ) / 90f;
            float3 forward = math.normalizesafe(math.rotate(viewPose.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            float3 forwardXY = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
            float actorVerticalAngle = forward.y;
            float nearestDistance = float.PositiveInfinity;

            foreach (var (placedRef, transform, bounds, vitals, entity) in
                     SystemAPI.Query<
                             RefRO<PlacedRefIdentity>,
                             RefRO<LocalTransform>,
                             RefRO<ActorLocalBounds>,
                             RefRO<ActorVitalSet>>()
                         .WithAll<ActorSpawnSource>()
                         .WithNone<PlayerTag>()
                         .WithEntityAccess())
            {
                if (entity == player || ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, entity))
                    continue;
                if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(entity)
                    && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0)
                {
                    continue;
                }

                float3 targetBase = transform.ValueRO.Position;
                float3 targetCenter = math.transform(
                    float4x4.TRS(transform.ValueRO.Position, transform.ValueRO.Rotation, new float3(transform.ValueRO.Scale)),
                    bounds.ValueRO.Center);
                float targetHeight = math.max(0.01f, bounds.ValueRO.Extents.y * 2f * math.max(0.01f, transform.ValueRO.Scale));
                float targetRadius = math.max(bounds.ValueRO.Extents.x, bounds.ValueRO.Extents.z) * math.max(0.01f, transform.ValueRO.Scale);

                if (math.abs(playerPosition.y - targetBase.y) >= reach)
                    continue;

                float distanceToBounds = math.max(0f, math.distance(ToHorizontal(playerPosition), ToHorizontal(targetBase)) - playerRadius - targetRadius);
                if (distanceToBounds >= nearestDistance || distanceToBounds >= reach)
                    continue;

                float3 toTargetXY = math.normalizesafe(ToHorizontal(targetBase - playerPosition), float3.zero);
                if (math.lengthsq(toTargetXY) <= 0f)
                    continue;
                if (math.dot(toTargetXY, forwardXY) <= 0f)
                    continue;
                if (math.abs(toTargetXY.x * forwardXY.z - toTargetXY.z * forwardXY.x) > combatAngleXY)
                    continue;

                float3 toFeet = math.normalizesafe(targetBase - viewPose.Position, float3.zero);
                float3 toHead = math.normalizesafe(targetBase + new float3(0f, targetHeight, 0f) - viewPose.Position, float3.zero);
                if (actorVerticalAngle - toHead.y > combatAngleZ || actorVerticalAngle - toFeet.y < -combatAngleZ)
                    continue;

                nearestDistance = distanceToBounds;
                target = entity;
                targetPlacedRefId = placedRef.ValueRO.Value;
                hitPosition = ComputeActorBoundsHitPosition(playerPosition, targetBase, targetRadius, targetHeight);
                hasHitPosition = 1;
                confirmationEnd = hitPosition;
            }

            return target != Entity.Null;
        }

        bool ValidateActorMeleeTarget(ref SystemState systemState, Entity player, Entity attacker, Entity logicalEntity, out uint placedRefId)
        {
            placedRefId = 0u;
            if (logicalEntity == Entity.Null || !systemState.EntityManager.Exists(logicalEntity))
                return false;
            if (logicalEntity == player || logicalEntity == attacker || systemState.EntityManager.HasComponent<PlayerTag>(logicalEntity))
                return false;
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(logicalEntity))
                return false;
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(logicalEntity)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(logicalEntity).Disabled != 0)
            {
                return false;
            }
            if (!systemState.EntityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has no PlacedRefIdentity.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(logicalEntity))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has no ActorVitalSet.");

            if (ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, logicalEntity))
                return false;

            placedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            if (placedRefId == 0u)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has an invalid placed ref id.");

            return true;
        }

        bool TryGetActorCenter(ref SystemState systemState, Entity actor, out float3 center)
        {
            center = default;
            if (!systemState.EntityManager.HasComponent<LocalTransform>(actor) || !systemState.EntityManager.HasComponent<ActorLocalBounds>(actor))
                return false;

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(actor);
            var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(actor);
            center = math.transform(
                float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                bounds.Center);
            return true;
        }

        bool TryGetInteractionRayHitPosition(ref SystemState systemState, 
            in LogicalRefLookup logicalRefLookup,
            Entity target,
            out float3 hitPosition)
        {
            hitPosition = default;
            if (!SystemAPI.TryGetSingleton<PlayerInteractionRaycastHit>(out var hit) || hit.HasHit == 0)
                return false;

            if (!InteractionTargetResolver.TryResolveLogicalEntity(systemState.EntityManager, logicalRefLookup, hit.HitEntity, out Entity logicalEntity))
                return false;
            if (logicalEntity != target)
                return false;

            hitPosition = hit.HitPosition;
            return true;
        }

        static float3 ComputeActorBoundsHitPosition(float3 playerPosition, float3 targetBase, float targetRadius, float targetHeight)
        {
            float3 directionToPlayer = math.normalizesafe(ToHorizontal(playerPosition - targetBase), new float3(0f, 0f, 1f));
            return targetBase
                   + directionToPlayer * math.max(0.01f, targetRadius)
                   + new float3(0f, targetHeight * 0.6f, 0f);
        }

        static float3 ToHorizontal(float3 value)
            => new(value.x, 0f, value.z);

        Entity ResolveActivePlayerVisual(ref SystemState systemState, Entity player, out ActorWeaponAnimationState weaponState)
        {
            weaponState = default;
            var presentation = _playerQuery.GetSingleton<LocalPlayerPresentationState>();
            Entity activeVisual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (activeVisual == Entity.Null || !systemState.EntityManager.Exists(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual entity is missing.");
            if (!systemState.EntityManager.HasComponent<LocalPlayerVisual>(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is missing LocalPlayerVisual.");
            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is missing ActorWeaponAnimationState.");

            var visual = systemState.EntityManager.GetComponentData<LocalPlayerVisual>(activeVisual);
            if (visual.Player != player)
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is not bound to the local player.");

            weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(activeVisual);
            return activeVisual;
        }

        void EmitWeaponSwish(
            ref RuntimeContentBlob content,
            Entity player,
            float3 playerPosition,
            in ActorWeaponAnimationState weaponState,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb)
        {
            float strength = math.saturate(weaponState.MeleeSwingAttackStrength);
            MorrowindCombatAudioUtility.EmitRequiredSound(
                ref content,
                "Weapon Swish",
                player,
                0u,
                playerPosition,
                0.98f + strength * 0.02f,
                0.75f + strength * 0.4f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        static void SpendAttackFatigue(
            ref RuntimeContentBlob content,
            ref ActorVitalSet playerVitals,
            in ActorDerivedMovementStats playerDerived,
            bool hasWeapon,
            in ItemEquipmentDef weapon,
            float attackStrength)
        {
            float fatigueLoss = MorrowindMeleeCombatMechanics.ComputeAttackFatigueLoss(
                ref content,
                playerDerived,
                hasWeapon,
                weapon,
                attackStrength);
            playerVitals.CurrentFatigue -= fatigueLoss;
        }

    }
}


