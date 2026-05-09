using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(PlayerMeleeHitSystem))]
    [UpdateBefore(typeof(MorrowindMeleeDamageRollSystem))]
    public partial struct ActorMeleeHitSystem : ISystem
    {
        EntityQuery _combatActorQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _combatActorQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorActiveCombatTarget>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<ActorLocalBounds>(),
                    ComponentType.ReadWrite<ActorWeaponAnimationState>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<LocalPlayerVisual>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            systemState.RequireForUpdate(_combatActorQuery);
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Actor melee hit requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            bool hasAudioState = SystemAPI.TryGetSingletonEntity<InteractionAudioRequestState>(out Entity audioEntity);
            var audioState = hasAudioState
                ? systemState.EntityManager.GetComponentData<InteractionAudioRequestState>(audioEntity)
                : default;
            var audioEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindCombatRuntimeState>();
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;

            foreach (var (combat, transform, actorBounds, weaponState, entity) in
                         SystemAPI.Query<
                                 RefRO<ActorActiveCombatTarget>,
                                 RefRO<LocalTransform>,
                                 RefRO<ActorLocalBounds>,
                                 RefRW<ActorWeaponAnimationState>>()
                             .WithNone<PlayerTag, LocalPlayerVisual>()
                             .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                             .WithEntityAccess())
            {
                ref var weapon = ref weaponState.ValueRW;
                if (!SystemAPI.IsComponentEnabled<ActorActiveCombatTarget>(entity))
                {
                    ClearPendingMeleeEvents(ref weapon);
                    continue;
                }

                if (weapon.MeleeSwingPending != 0)
                {
                    EmitWeaponSwish(ref systemState, ref content, entity, transform.ValueRO.Position, weapon, ref audioState, hasAudioState, ref audioEcb);
                    weapon.MeleeSwingPending = 0;
                    weapon.MeleeSwingAttackStrength = 0f;
                    weapon.MeleeSwingWeaponContent = default;
                }

                if (weapon.MeleeHitPending == 0)
                    continue;

                Entity target = combat.ValueRO.TargetEntity;
                if (TryQueueActorMeleeHit(ref systemState, 
                        ref content,
                        entity,
                        transform.ValueRO,
                        actorBounds.ValueRO,
                        target,
                        weapon,
                        runtimeEntity,
                        deferredPhysicsQueueEntity,
                        fixedTick))
                {
                    SpendAttackFatigue(ref systemState, ref content, entity, weapon.MeleeHitWeaponContent, math.saturate(weapon.MeleeHitAttackStrength));
                }

                weapon.MeleeHitPending = 0;
                weapon.MeleeHitAttackStrength = 0f;
                weapon.MeleeHitWeaponContent = default;
            }

            if (hasAudioState)
                systemState.EntityManager.SetComponentData(audioEntity, audioState);
            audioEcb.Playback(systemState.EntityManager);
            audioEcb.Dispose();
        }

        static void ClearPendingMeleeEvents(ref ActorWeaponAnimationState weapon)
        {
            weapon.MeleeSwingPending = 0;
            weapon.MeleeSwingAttackStrength = 0f;
            weapon.MeleeSwingWeaponContent = default;
            weapon.MeleeHitPending = 0;
            weapon.MeleeHitAttackStrength = 0f;
            weapon.MeleeHitWeaponContent = default;
        }

        bool TryQueueActorMeleeHit(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity attacker,
            in LocalTransform attackerTransform,
            in ActorLocalBounds attackerBounds,
            Entity target,
            in ActorWeaponAnimationState weaponState,
            Entity runtimeEntity,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick)
        {
            if (!ValidateAttacker(ref systemState, attacker))
                return false;
            if (!TryResolveTargetBounds(ref systemState, target, out uint targetPlacedRefId, out float3 targetBase, out float targetRadius, out float targetHeight, out float3 targetCenter))
                return false;

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                weaponState.MeleeHitWeaponContent,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            float reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
            float attackerRadius = math.max(attackerBounds.Extents.x, attackerBounds.Extents.z) * math.max(0.01f, attackerTransform.Scale);
            float distanceToBounds = math.max(0f, math.distance(ToHorizontal(attackerTransform.Position), ToHorizontal(targetBase)) - attackerRadius - targetRadius);
            if (distanceToBounds > reach)
                return false;
            if (!PassesCombatCone(ref content, attackerTransform, targetBase, targetHeight))
                return false;

            float3 source = math.transform(
                float4x4.TRS(attackerTransform.Position, attackerTransform.Rotation, new float3(attackerTransform.Scale)),
                attackerBounds.Center);
            float3 hitPosition = ComputeHitPosition(attackerTransform.Position, targetBase, targetRadius, targetHeight);
            uint sequence = DeferredPhysicsQueryUtility.EnqueueRay(
                systemState.EntityManager,
                deferredPhysicsQueueEntity,
                fixedTick,
                DeferredPhysicsQueryKind.MeleeConfirmation,
                attacker,
                target,
                attacker,
                source,
                targetCenter,
                InteractionCollisionLayers.LineOfSightQueryFilter);
            systemState.EntityManager.GetBuffer<PendingMeleeHitConfirmation>(runtimeEntity).Add(new PendingMeleeHitConfirmation
            {
                QuerySequence = sequence,
                RequestFixedTick = fixedTick,
                Attacker = attacker,
                Target = target,
                WeaponContent = weaponState.MeleeHitWeaponContent,
                AttackType = weaponState.MeleeHitAttackType,
                AttackStrength = math.saturate(weaponState.MeleeHitAttackStrength),
                Reach = reach,
                TargetPlacedRefId = targetPlacedRefId,
                HitPosition = hitPosition,
                HasHitPosition = 1,
            });
            return true;
        }

        bool ValidateAttacker(ref SystemState systemState, Entity attacker)
        {
            if (attacker == Entity.Null || !systemState.EntityManager.Exists(attacker))
                return false;
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(attacker))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Attacker ref={PlacedRefId(ref systemState, attacker)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(attacker))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Attacker ref={PlacedRefId(ref systemState, attacker)} has no ActorHitAftermathState.");

            return ActorHitAftermathStateUtility.IsAlive(systemState.EntityManager, attacker);
        }

        bool TryResolveTargetBounds(ref SystemState systemState, 
            Entity target,
            out uint targetPlacedRefId,
            out float3 targetBase,
            out float targetRadius,
            out float targetHeight,
            out float3 targetCenter)
        {
            targetPlacedRefId = 0u;
            targetBase = default;
            targetRadius = 0f;
            targetHeight = 0f;
            targetCenter = default;
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                return false;
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                return false;
            }
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorHitAftermathState.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no LocalTransform.");

            if (ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, target))
                return false;

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            targetBase = transform.Position;
            if (systemState.EntityManager.HasComponent<ActorLocalBounds>(target))
            {
                var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(target);
                float scale = math.max(0.01f, transform.Scale);
                targetRadius = math.max(bounds.Extents.x, bounds.Extents.z) * scale;
                targetHeight = math.max(0.01f, bounds.Extents.y * 2f * scale);
                targetCenter = math.transform(float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)), bounds.Center);
                if (systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                    targetPlacedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
                return true;
            }

            if (systemState.EntityManager.HasComponent<PlayerCharacterComponent>(target))
            {
                var player = systemState.EntityManager.GetComponentData<PlayerCharacterComponent>(target);
                targetRadius = math.max(0.01f, player.Radius);
                targetHeight = math.max(0.01f, player.StandingHeight);
                targetCenter = targetBase + new float3(0f, targetHeight * 0.5f, 0f);
                return true;
            }

            throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
        }

        void SpendAttackFatigue(ref SystemState systemState, ref RuntimeContentBlob content, Entity attacker, in ContentReference weaponContent, float attackStrength)
        {
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(attacker))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Attacker ref={PlacedRefId(ref systemState, attacker)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorDerivedMovementStats>(attacker))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Attacker ref={PlacedRefId(ref systemState, attacker)} has no ActorDerivedMovementStats.");

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                weaponContent,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(attacker);
            var derived = systemState.EntityManager.GetComponentData<ActorDerivedMovementStats>(attacker);
            vitals.CurrentFatigue -= MorrowindMeleeCombatMechanics.ComputeAttackFatigueLoss(ref content, derived, hasWeapon, weapon, attackStrength);
            systemState.EntityManager.SetComponentData(attacker, vitals);
        }

        void EmitWeaponSwish(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity actor,
            float3 actorPosition,
            in ActorWeaponAnimationState weaponState,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb)
        {
            float strength = math.saturate(weaponState.MeleeSwingAttackStrength);
            MorrowindCombatAudioUtility.EmitRequiredSound(
                ref content,
                "Weapon Swish",
                actor,
                PlacedRefId(ref systemState, actor),
                actorPosition,
                0.98f + strength * 0.02f,
                0.75f + strength * 0.4f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        static bool PassesCombatCone(ref RuntimeContentBlob content, in LocalTransform actorTransform, float3 targetBase, float targetHeight)
        {
            float combatAngleXY = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleXY) / 90f;
            float combatAngleZ = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleZ) / 90f;
            float3 forward = math.normalizesafe(math.rotate(actorTransform.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            float3 forwardXY = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
            float3 toTargetXY = math.normalizesafe(ToHorizontal(targetBase - actorTransform.Position), float3.zero);
            if (math.lengthsq(toTargetXY) <= 0f)
                return false;
            if (math.dot(toTargetXY, forwardXY) <= 0f)
                return false;
            if (math.abs(toTargetXY.x * forwardXY.z - toTargetXY.z * forwardXY.x) > combatAngleXY)
                return false;

            float actorVerticalAngle = forward.y;
            float3 toFeet = math.normalizesafe(targetBase - actorTransform.Position, float3.zero);
            float3 toHead = math.normalizesafe(targetBase + new float3(0f, targetHeight, 0f) - actorTransform.Position, float3.zero);
            return actorVerticalAngle - toHead.y <= combatAngleZ && actorVerticalAngle - toFeet.y >= -combatAngleZ;
        }

        static float3 ComputeHitPosition(float3 attackerPosition, float3 targetBase, float targetRadius, float targetHeight)
        {
            float3 directionToAttacker = math.normalizesafe(ToHorizontal(attackerPosition - targetBase), new float3(0f, 0f, 1f));
            return targetBase
                   + directionToAttacker * math.max(0.01f, targetRadius)
                   + new float3(0f, targetHeight * 0.6f, 0f);
        }

        static float3 ToHorizontal(float3 value)
            => new(value.x, 0f, value.z);

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => entity != Entity.Null
               && systemState.EntityManager.Exists(entity)
               && systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}

