using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup), OrderFirst = true)]
    public partial class PlayerMeleeHitSystem : SystemBase
    {
        const byte MeleeConfirmationMaxResultAgeTicks = 4;
        EntityQuery _playerQuery;
        EntityQuery _viewPoseQuery;

        struct ResolvedPendingMeleeHitConfirmation
        {
            public Entity Attacker;
            public Entity Target;
            public ContentReference WeaponContent;
            public ActorWeaponAttackType AttackType;
            public float AttackStrength;
            public uint TargetPlacedRefId;
            public float3 HitPosition;
            public byte HasHitPosition;
        }

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCharacterComponent>(),
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>());
            _viewPoseQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerPhysicsViewPose>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewPoseQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Combat] Runtime content database is not loaded.");

            Entity player = _playerQuery.GetSingletonEntity();
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindCombatRuntimeState>();
            ProcessPendingMeleeConfirmations(contentDb, player, runtimeEntity);

            Entity visualEntity = ResolveActivePlayerVisual(player, out var weaponState);
            bool stateChanged = false;

            var playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            bool hasAudioState = SystemAPI.TryGetSingletonEntity<InteractionAudioRequestState>(out Entity audioEntity);
            var audioState = hasAudioState
                ? EntityManager.GetComponentData<InteractionAudioRequestState>(audioEntity)
                : default;
            var audioEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            if (weaponState.MeleeSwingPending != 0)
            {
                EmitWeaponSwish(contentDb, player, playerTransform.Position, weaponState, ref audioState, hasAudioState, ref audioEcb);
                weaponState.MeleeSwingPending = 0;
                weaponState.MeleeSwingAttackStrength = 0f;
                weaponState.MeleeSwingWeaponContent = default;
                stateChanged = true;
            }

            if (hasAudioState)
                EntityManager.SetComponentData(audioEntity, audioState);
            audioEcb.Playback(EntityManager);
            audioEcb.Dispose();

            if (weaponState.MeleeHitPending == 0)
            {
                if (stateChanged)
                    EntityManager.SetComponentData(visualEntity, weaponState);
                return;
            }

            var hitAttackType = weaponState.MeleeHitAttackType;
            float hitAttackStrength = math.saturate(weaponState.MeleeHitAttackStrength);
            var hitWeaponContent = weaponState.MeleeHitWeaponContent;
            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                contentDb,
                hitWeaponContent,
                out bool hasWeapon,
                out _,
                out ItemEquipmentDef weapon,
                out _);

            var playerVitals = _playerQuery.GetSingleton<ActorVitalSet>();
            var playerDerived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var playerComponent = _playerQuery.GetSingleton<PlayerCharacterComponent>();

            SpendAttackFatigue(contentDb, ref playerVitals, playerDerived, hasWeapon, weapon, hitAttackStrength);
            EntityManager.SetComponentData(player, playerVitals);

            var viewPose = _viewPoseQuery.GetSingleton<PlayerPhysicsViewPose>();
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            float reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(contentDb, hasWeapon, weapon);
            TryQueueMeleeConfirmation(
                contentDb,
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
                out _,
                out _);

            weaponState.MeleeHitPending = 0;
            weaponState.MeleeHitAttackStrength = 0f;
            weaponState.MeleeHitWeaponContent = default;
            EntityManager.SetComponentData(visualEntity, weaponState);
        }

        void ProcessPendingMeleeConfirmations(RuntimeContentDatabase contentDb, Entity player, Entity runtimeEntity)
        {
            var pending = EntityManager.GetBuffer<PendingPlayerMeleeHitConfirmation>(runtimeEntity);
            if (pending.Length == 0)
                return;

            using var resolvedHits = new NativeList<ResolvedPendingMeleeHitConfirmation>(Allocator.Temp);
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            for (int i = pending.Length - 1; i >= 0; i--)
            {
                var request = pending[i];
                if (!DeferredPhysicsQueryUtility.TryGetResultBySequence(
                        EntityManager,
                        DeferredPhysicsQueryKind.MeleeConfirmation,
                        request.QuerySequence,
                        MeleeConfirmationMaxResultAgeTicks,
                        out var result))
                {
                    if (fixedTick <= request.RequestFixedTick + MeleeConfirmationMaxResultAgeTicks)
                        continue;

                    pending.RemoveAt(i);
                    continue;
                }

                if (result.Status != DeferredPhysicsQueryStatus.Miss)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (TryResolveMeleeConfirmation(
                        player,
                        request,
                        out Entity target,
                        out uint targetPlacedRefId,
                        out float3 hitPosition,
                        out byte hasHitPosition))
                {
                    resolvedHits.Add(new ResolvedPendingMeleeHitConfirmation
                    {
                        Attacker = request.Attacker,
                        Target = target,
                        WeaponContent = request.WeaponContent,
                        AttackType = request.AttackType,
                        AttackStrength = request.AttackStrength,
                        TargetPlacedRefId = targetPlacedRefId,
                        HitPosition = hitPosition,
                        HasHitPosition = hasHitPosition,
                    });
                }

                pending.RemoveAt(i);
            }

            for (int i = 0; i < resolvedHits.Length; i++)
            {
                var hit = resolvedHits[i];
                MarkAttackContact(contentDb, player, hit.Target, hit.TargetPlacedRefId);
                EmitMeleeHitEvent(
                    hit.Attacker,
                    hit.Target,
                    hit.WeaponContent,
                    hit.AttackType,
                    hit.AttackStrength,
                    hit.HitPosition,
                    hit.HasHitPosition);
            }

        }

        bool TryQueueMeleeConfirmation(
            RuntimeContentDatabase contentDb,
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
            out uint querySequence,
            out uint targetPlacedRefId)
        {
            querySequence = 0u;
            targetPlacedRefId = 0u;
            if (!TrySelectMeleeTarget(
                    contentDb,
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
                EntityManager,
                DeferredPhysicsQueryKind.MeleeConfirmation,
                player,
                target,
                player,
                viewPose.Position,
                confirmationEnd,
                InteractionCollisionLayers.LineOfSightQueryFilter);
            EntityManager.GetBuffer<PendingPlayerMeleeHitConfirmation>(runtimeEntity).Add(new PendingPlayerMeleeHitConfirmation
            {
                QuerySequence = querySequence,
                RequestFixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick,
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

        bool TrySelectMeleeTarget(
            RuntimeContentDatabase contentDb,
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
            if (TrySelectFocusedMeleeTarget(
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

            return TrySelectActorBoundsMeleeTarget(
                contentDb,
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

        bool TrySelectFocusedMeleeTarget(
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
            if (!ValidateActorMeleeTarget(player, player, focus.TargetEntity, out targetPlacedRefId))
                return false;
            if (!TryGetActorCenter(focus.TargetEntity, out confirmationEnd))
                return false;

            target = focus.TargetEntity;
            if (TryGetInteractionRayHitPosition(logicalRefLookup, target, out hitPosition))
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

        bool TrySelectActorBoundsMeleeTarget(
            RuntimeContentDatabase contentDb,
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

            float combatAngleXY = contentDb.RequireGameSettingFloat("fCombatAngleXY") / 90f;
            float combatAngleZ = contentDb.RequireGameSettingFloat("fCombatAngleZ") / 90f;
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
                if (entity == player || vitals.ValueRO.CurrentHealth <= 0f)
                    continue;
                if (EntityManager.HasComponent<PlacedRefRuntimeState>(entity)
                    && EntityManager.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0)
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

        bool TryResolveMeleeConfirmation(
            Entity player,
            in PendingPlayerMeleeHitConfirmation request,
            out Entity target,
            out uint targetPlacedRefId,
            out float3 hitPosition,
            out byte hasHitPosition)
        {
            target = Entity.Null;
            targetPlacedRefId = 0u;
            hitPosition = default;
            hasHitPosition = 0;

            Entity logicalEntity = request.Target;
            if (!ValidateActorMeleeTarget(player, request.Attacker, logicalEntity, out uint placedRefId))
                return false;
            if (request.TargetPlacedRefId != 0u && placedRefId != request.TargetPlacedRefId)
                throw new InvalidOperationException($"[VVardenfell][Combat] Pending melee target entity={logicalEntity.Index}:{logicalEntity.Version} changed placed ref id from {request.TargetPlacedRefId} to {placedRefId}.");
            if (request.Reach <= 0f)
                return false;

            target = logicalEntity;
            targetPlacedRefId = placedRefId;
            hitPosition = request.HitPosition;
            hasHitPosition = request.HasHitPosition;
            return true;
        }

        bool ValidateActorMeleeTarget(Entity player, Entity attacker, Entity logicalEntity, out uint placedRefId)
        {
            placedRefId = 0u;
            if (logicalEntity == Entity.Null || !EntityManager.Exists(logicalEntity))
                return false;
            if (logicalEntity == player || logicalEntity == attacker || EntityManager.HasComponent<PlayerTag>(logicalEntity))
                return false;
            if (!EntityManager.HasComponent<ActorSpawnSource>(logicalEntity))
                return false;
            if (EntityManager.HasComponent<PlacedRefRuntimeState>(logicalEntity)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(logicalEntity).Disabled != 0)
            {
                return false;
            }
            if (!EntityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has no PlacedRefIdentity.");
            if (!EntityManager.HasComponent<ActorVitalSet>(logicalEntity))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has no ActorVitalSet.");

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(logicalEntity);
            if (vitals.CurrentHealth <= 0f)
                return false;

            placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            if (placedRefId == 0u)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee actor entity={logicalEntity.Index}:{logicalEntity.Version} has an invalid placed ref id.");

            return true;
        }

        bool TryGetActorCenter(Entity actor, out float3 center)
        {
            center = default;
            if (!EntityManager.HasComponent<LocalTransform>(actor) || !EntityManager.HasComponent<ActorLocalBounds>(actor))
                return false;

            var transform = EntityManager.GetComponentData<LocalTransform>(actor);
            var bounds = EntityManager.GetComponentData<ActorLocalBounds>(actor);
            center = math.transform(
                float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                bounds.Center);
            return true;
        }

        bool TryGetInteractionRayHitPosition(
            in LogicalRefLookup logicalRefLookup,
            Entity target,
            out float3 hitPosition)
        {
            hitPosition = default;
            if (!SystemAPI.TryGetSingleton<PlayerInteractionRaycastHit>(out var hit) || hit.HasHit == 0)
                return false;

            if (!InteractionTargetResolver.TryResolveLogicalEntity(EntityManager, logicalRefLookup, hit.HitEntity, out Entity logicalEntity))
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

        Entity ResolveActivePlayerVisual(Entity player, out ActorWeaponAnimationState weaponState)
        {
            weaponState = default;
            var presentation = _playerQuery.GetSingleton<LocalPlayerPresentationState>();
            Entity activeVisual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (activeVisual == Entity.Null || !EntityManager.Exists(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual entity is missing.");
            if (!EntityManager.HasComponent<LocalPlayerVisual>(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is missing LocalPlayerVisual.");
            if (!EntityManager.HasComponent<ActorWeaponAnimationState>(activeVisual))
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is missing ActorWeaponAnimationState.");

            var visual = EntityManager.GetComponentData<LocalPlayerVisual>(activeVisual);
            if (visual.Player != player)
                throw new InvalidOperationException("[VVardenfell][Combat] Active local player visual is not bound to the local player.");

            weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(activeVisual);
            return activeVisual;
        }

        void EmitWeaponSwish(
            RuntimeContentDatabase contentDb,
            Entity player,
            float3 playerPosition,
            in ActorWeaponAnimationState weaponState,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb)
        {
            float strength = math.saturate(weaponState.MeleeSwingAttackStrength);
            MorrowindCombatAudioUtility.EmitRequiredSound(
                contentDb,
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
            RuntimeContentDatabase contentDb,
            ref ActorVitalSet playerVitals,
            in ActorDerivedMovementStats playerDerived,
            bool hasWeapon,
            in ItemEquipmentDef weapon,
            float attackStrength)
        {
            float fatigueLoss = MorrowindMeleeCombatMechanics.ComputeAttackFatigueLoss(
                contentDb,
                playerDerived,
                hasWeapon,
                weapon,
                attackStrength);
            playerVitals.CurrentFatigue -= fatigueLoss;
        }

        void MarkAttackContact(RuntimeContentDatabase contentDb, Entity player, Entity target, uint targetPlacedRefId)
        {
            if (!EntityManager.HasComponent<ActorScriptEventState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorScriptEventState.");
            if (!EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorSpawnSource.");

            var state = EntityManager.GetComponentData<ActorScriptEventState>(target);
            state.Attacked = 1;
            EntityManager.SetComponentData(target, state);

            uint actorPlacedRefId = EntityManager.HasComponent<PlacedRefIdentity>(target)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(target).Value
                : targetPlacedRefId;
            if (!MorrowindCombatTargetUtility.TryStartCombat(
                    contentDb,
                    EntityManager,
                    target,
                    actorPlacedRefId,
                    player,
                    0u))
            {
                throw new InvalidOperationException($"[VVardenfell][Combat] Failed to start combat for target ref={targetPlacedRefId}.");
            }
        }

        void EmitMeleeHitEvent(
            Entity attacker,
            Entity target,
            in ContentReference weaponContent,
            ActorWeaponAttackType attackType,
            float attackStrength,
            float3 hitPosition,
            byte hasHitPosition)
        {
            Entity entity = EntityManager.CreateEntity(typeof(MorrowindMeleeHitEvent));
            EntityManager.SetName(entity, "VVardenfell.MorrowindMeleeHitEvent");
            EntityManager.SetComponentData(entity, new MorrowindMeleeHitEvent
            {
                Attacker = attacker,
                Target = target,
                WeaponContent = weaponContent,
                AttackType = attackType,
                AttackStrength = attackStrength,
                HitPosition = hitPosition,
                HasHitPosition = hasHitPosition,
            });
        }

    }
}
