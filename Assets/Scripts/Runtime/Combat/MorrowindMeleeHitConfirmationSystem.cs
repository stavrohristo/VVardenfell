using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup), OrderFirst = true)]
    public partial class MorrowindMeleeHitConfirmationSystem : SystemBase
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

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<PendingMeleeHitConfirmation>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindCombatRuntimeState>();
            var pending = EntityManager.GetBuffer<PendingMeleeHitConfirmation>(runtimeEntity);
            if (pending.Length == 0)
                return;

            using var resolvedHits = new NativeList<ResolvedMeleeHitConfirmation>(Allocator.Temp);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            for (int i = pending.Length - 1; i >= 0; i--)
            {
                var request = pending[i];
                if (!DeferredPhysicsQueryUtility.TryGetResultBySequence(
                        EntityManager,
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

                if (TryResolveMeleeConfirmation(
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
                MarkAttackContact(hit.Target, hit.TargetPlacedRefId);
                EmitMeleeHitEvent(ref ecb, hit);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        bool TryResolveMeleeConfirmation(
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
            if (!ValidateActorMeleeTarget(request.Attacker, logicalEntity, request.TargetPlacedRefId, out uint placedRefId))
                return false;
            if (request.Reach <= 0f)
                return false;

            target = logicalEntity;
            targetPlacedRefId = placedRefId;
            hitPosition = request.HitPosition;
            hasHitPosition = request.HasHitPosition;
            return true;
        }

        bool ValidateActorMeleeTarget(Entity attacker, Entity target, uint expectedPlacedRefId, out uint placedRefId)
        {
            placedRefId = 0u;
            if (attacker == Entity.Null || !EntityManager.Exists(attacker))
                return false;
            if (target == Entity.Null || !EntityManager.Exists(target))
                return false;
            if (target == attacker)
                return false;
            if (EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                return false;
            }
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorHitAftermathState.");

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);
            if (aftermath.Dead != 0 && vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target ref={PlacedRefId(target)} is marked dead but still has positive health.");
            if (vitals.CurrentHealth <= 0f || aftermath.Dead != 0)
                return false;

            if (EntityManager.HasComponent<PlayerTag>(target))
            {
                if (expectedPlacedRefId != 0u)
                    throw new InvalidOperationException($"[VVardenfell][Combat] Player melee target unexpectedly carried placed ref id 0x{expectedPlacedRefId:X8}.");
                return true;
            }

            if (!EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no ActorSpawnSource.");
            if (!EntityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has no PlacedRefIdentity.");

            placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (placedRefId == 0u)
                throw new InvalidOperationException($"[VVardenfell][Combat] Melee target entity={target.Index}:{target.Version} has an invalid placed ref id.");
            if (expectedPlacedRefId != 0u && placedRefId != expectedPlacedRefId)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][Combat] Pending melee target entity={target.Index}:{target.Version} changed placed ref id from 0x{expectedPlacedRefId:X8} to 0x{placedRefId:X8}.");
            }

            return true;
        }

        void MarkAttackContact(Entity target, uint targetPlacedRefId)
        {
            if (!EntityManager.HasComponent<ActorScriptEventState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorScriptEventState.");
            if (!EntityManager.HasComponent<ActorFriendlyHitState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorFriendlyHitState.");
            if (!EntityManager.HasComponent<ActorCrimeState>(target))
                throw new InvalidOperationException($"[VVardenfell][Combat] Target ref={targetPlacedRefId} has no ActorCrimeState.");

            var state = EntityManager.GetComponentData<ActorScriptEventState>(target);
            state.Attacked = 1;
            EntityManager.SetComponentData(target, state);
        }

        static void EmitMeleeHitEvent(ref EntityCommandBuffer ecb, in ResolvedMeleeHitConfirmation hit)
        {
            Entity entity = ecb.CreateEntity();
            ecb.SetName(entity, "VVardenfell.MorrowindMeleeHitEvent");
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

        uint PlacedRefId(Entity entity)
            => entity != Entity.Null
               && EntityManager.Exists(entity)
               && EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
