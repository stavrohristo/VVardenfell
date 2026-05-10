using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup), OrderFirst = true)]
    public partial struct MorrowindMeleeHitConfirmationSystem : ISystem
    {
        const byte MeleeConfirmationMaxResultAgeTicks = 4;

        struct ResolvedMeleeHitConfirmation
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

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<PendingMeleeHitConfirmation>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindCombatRuntimeState>();
            var pending = systemState.EntityManager.GetBuffer<PendingMeleeHitConfirmation>(runtimeEntity);
            if (pending.Length == 0)
                return;

            using var resolvedHits = new NativeList<ResolvedMeleeHitConfirmation>(Allocator.Temp);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            for (int i = pending.Length - 1; i >= 0; i--)
            {
                var request = pending[i];
                if (!DeferredPhysicsQueryUtility.TryGetResultBySequence(
                        systemState.EntityManager,
                        deferredPhysicsQueueEntity,
                        fixedTick,
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

                if (TryResolveMeleeConfirmation(ref systemState, 
                        request,
                        out Entity target,
                        out uint targetPlacedRefId,
                        out float3 hitPosition,
                        out byte hasHitPosition))
                {
                    resolvedHits.Add(new ResolvedMeleeHitConfirmation
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

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < resolvedHits.Length; i++)
            {
                var hit = resolvedHits[i];
                MarkAttackContact(ref systemState, hit.Target, hit.TargetPlacedRefId);
                EmitMeleeHitEvent(ref ecb, hit);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        bool TryResolveMeleeConfirmation(ref SystemState systemState, 
            in PendingMeleeHitConfirmation request,
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
            if (!ValidateActorMeleeTarget(ref systemState, request.Attacker, logicalEntity, request.TargetPlacedRefId, out uint placedRefId))
                return false;
            if (request.Reach <= 0f)
                return false;
            if (!TryResolveCurrentMeleeContact(ref systemState, request.Attacker, logicalEntity, request.Reach, out hitPosition))
                return false;

            target = logicalEntity;
            targetPlacedRefId = placedRefId;
            hasHitPosition = 1;
            return true;
        }

        bool ValidateActorMeleeTarget(ref SystemState systemState, Entity attacker, Entity target, uint expectedPlacedRefId, out uint placedRefId)
        {
            placedRefId = 0u;
            if (attacker == Entity.Null || !systemState.EntityManager.Exists(attacker))
                return false;
            if (systemState.EntityManager.HasComponent<ActorDead>(attacker)
                && systemState.EntityManager.IsComponentEnabled<ActorDead>(attacker))
            {
                return false;
            }
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                return false;
            if (target == attacker)
                return false;
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                return false;
            }
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorHitAftermathState.");

            if (ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, target))
                return false;

            if (systemState.EntityManager.HasComponent<PlayerTag>(target))
            {
                if (expectedPlacedRefId != 0u)
                    throw new InvalidOperationException($"[VVardenfell][Combat] Player melee target unexpectedly carried placed ref id 0x{expectedPlacedRefId:X8}.");
                return true;
            }

            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorSpawnSource.");
            if (!systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no PlacedRefIdentity.");

            placedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (placedRefId == 0u)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has an invalid placed ref id.");
            if (expectedPlacedRefId != 0u && placedRefId != expectedPlacedRefId)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][Combat] Pending melee target entity={target.Index}:{target.Version} changed placed ref id from 0x{expectedPlacedRefId:X8} to 0x{placedRefId:X8}.");
            }

            return true;
        }

        bool TryResolveCurrentMeleeContact(ref SystemState systemState, Entity attacker, Entity target, float reach, out float3 hitPosition)
        {
            hitPosition = default;
            if (!systemState.EntityManager.HasComponent<LocalTransform>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee attacker entity={attacker.Index}:{attacker.Version} has no LocalTransform.");
            if (!systemState.EntityManager.HasComponent<ActorLocalBounds>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee attacker entity={attacker.Index}:{attacker.Version} has no ActorLocalBounds.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no LocalTransform.");

            var attackerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(attacker);
            var attackerBounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(attacker);
            float attackerRadius = math.max(attackerBounds.Extents.x, attackerBounds.Extents.z) * math.max(0.01f, attackerTransform.Scale);

            if (!TryResolveCurrentTargetContact(ref systemState, target, out float3 targetBase, out float targetRadius, out float targetHeight))
                return false;

            if (!MorrowindMeleeCombatMechanics.IsInMeleeReach(attackerTransform.Position, attackerRadius, targetBase, targetRadius, reach))
                return false;

            hitPosition = ComputeHitPosition(attackerTransform.Position, targetBase, targetRadius, targetHeight);
            return true;
        }

        bool TryResolveCurrentTargetContact(ref SystemState systemState, Entity target, out float3 targetBase, out float targetRadius, out float targetHeight)
        {
            targetBase = default;
            targetRadius = 0f;
            targetHeight = 0f;

            var targetTransform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            targetBase = targetTransform.Position;
            if (systemState.EntityManager.HasComponent<ActorLocalBounds>(target))
            {
                var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(target);
                float scale = math.max(0.01f, targetTransform.Scale);
                targetRadius = math.max(bounds.Extents.x, bounds.Extents.z) * scale;
                targetHeight = math.max(0.01f, bounds.Extents.y * 2f * scale);
                return true;
            }

            if (systemState.EntityManager.HasComponent<PlayerCharacterComponent>(target))
            {
                var player = systemState.EntityManager.GetComponentData<PlayerCharacterComponent>(target);
                targetRadius = math.max(0.01f, player.Radius);
                targetHeight = math.max(0.01f, player.StandingHeight);
                return true;
            }

            throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
        }

        void MarkAttackContact(ref SystemState systemState, Entity target, uint targetPlacedRefId)
        {
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorScriptEventState.");
            if (!systemState.EntityManager.HasComponent<ActorFriendlyHitState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorFriendlyHitState.");
            if (!systemState.EntityManager.HasComponent<ActorCrimeState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorCrimeState.");

            var state = systemState.EntityManager.GetComponentData<ActorScriptEventState>(target);
            state.Attacked = 1;
            systemState.EntityManager.SetComponentData(target, state);
        }

        static void EmitMeleeHitEvent(ref EntityCommandBuffer ecb, in ResolvedMeleeHitConfirmation hit)
        {
            Entity entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new MorrowindMeleeHitEvent
            {
                Attacker = hit.Attacker,
                Target = hit.Target,
                WeaponContent = hit.WeaponContent,
                AttackType = hit.AttackType,
                AttackStrength = hit.AttackStrength,
                HitPosition = hit.HitPosition,
                HasHitPosition = hit.HasHitPosition,
            });
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => entity != Entity.Null
               && systemState.EntityManager.Exists(entity)
               && systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        static float3 ComputeHitPosition(float3 attackerPosition, float3 targetBase, float targetRadius, float targetHeight)
        {
            float3 directionToAttacker = math.normalizesafe(ToHorizontal(attackerPosition - targetBase), new float3(0f, 0f, 1f));
            return targetBase
                   + directionToAttacker * math.max(0.01f, targetRadius)
                   + new float3(0f, targetHeight * 0.6f, 0f);
        }

        static float3 ToHorizontal(float3 value)
            => new(value.x, 0f, value.z);
    }
}
