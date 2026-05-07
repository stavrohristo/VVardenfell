using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageFeedbackSystem))]
    public partial struct MorrowindHitAftermathAnimationSystem : ISystem
    {
        public const int HitRecoveryOverlayPriority = 50;
        public const int KnockdownOverlayPriority = 90;
        public const int DeathOverlayPriority = 120;
        const int MaxHitReactionVariants = 16;
        const int MaxDeathVariants = 5;
        const uint InfiniteLoops = uint.MaxValue;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorHitAftermathState>();
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Aftermath] Hit aftermath animation has no actor animation catalog blob.");

            var combatState = SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>();
            var random = new Unity.Mathematics.Random(combatState.ValueRO.RandomState == 0u ? 0x6E624EB7u : combatState.ValueRO.RandomState);
            ref var catalog = ref catalogRef.Value;

            foreach (var (aftermath, entity) in
                     SystemAPI.Query<RefRW<ActorHitAftermathState>>()
                         .WithEntityAccess())
            {
                if (!HasAnyAftermath(aftermath.ValueRO))
                    continue;

                RequireAnimationComposition(ref systemState, entity);
                var presentation = systemState.EntityManager.GetComponentData<ActorPresentation>(entity);
                var overlays = systemState.EntityManager.GetBuffer<ActorAnimationOverlayState>(entity);
                ApplyAnimation(ref systemState, entity, aftermath, ref catalog, presentation, overlays, ref random);
            }

            combatState.ValueRW.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        void ApplyAnimation(ref SystemState systemState, 
            Entity entity,
            RefRW<ActorHitAftermathState> aftermath,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ref Unity.Mathematics.Random random)
        {
            if (aftermath.ValueRO.Dead != 0)
            {
                ApplyDeathAnimation(ref systemState, entity, aftermath, ref catalog, presentation, overlays, ref random);
                return;
            }

            if (aftermath.ValueRO.KnockedOut != 0)
            {
                if (!systemState.EntityManager.HasComponent<ActorVitalSet>(entity))
                    throw new InvalidOperationException($"[VVardenfell][Aftermath] Knockout actor ref={PlacedRefId(ref systemState, entity)} has no ActorVitalSet.");

                var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(entity);
                if (vitals.CurrentFatigue >= 0f && vitals.ModifiedFatigueBase > 0f)
                {
                    aftermath.ValueRW.KnockedOut = 0;
                    aftermath.ValueRW.KnockedDown = 0;
                    aftermath.ValueRW.KnockedDownOneFrame = 0;
                    aftermath.ValueRW.KnockedDownOverOneFrame = 0;
                    RemoveAftermathOverlays(overlays);
                    return;
                }

                FixedString64Bytes groupName = default;
                groupName.Append("knockout");
                StartOrKeepAnimation(ref systemState, 
                    entity,
                    aftermath,
                    ref catalog,
                    presentation,
                    overlays,
                    groupName,
                    KnockdownOverlayPriority,
                    requestedLoopCount: InfiniteLoops,
                    holdAtStop: true);
                return;
            }

            if (aftermath.ValueRO.KnockedDown != 0)
            {
                int overlayIndex = FindAftermathOverlay(overlays, KnockdownOverlayPriority);
                if (aftermath.ValueRO.AnimatedSequence == aftermath.ValueRO.Sequence)
                {
                    if (overlayIndex < 0 || IsPlaybackComplete(overlays[overlayIndex].Playback))
                    {
                        RemoveAftermathOverlays(overlays);
                        aftermath.ValueRW.KnockedDown = 0;
                        aftermath.ValueRW.KnockedDownOneFrame = 0;
                        aftermath.ValueRW.KnockedDownOverOneFrame = 0;
                        return;
                    }
                }

                FixedString64Bytes groupName = default;
                groupName.Append("knockdown");
                StartOrKeepAnimation(ref systemState, 
                    entity,
                    aftermath,
                    ref catalog,
                    presentation,
                    overlays,
                    groupName,
                    KnockdownOverlayPriority,
                    requestedLoopCount: 0u,
                    holdAtStop: false);
                return;
            }

            if (aftermath.ValueRO.HitRecovery != 0)
            {
                int overlayIndex = FindAftermathOverlay(overlays, HitRecoveryOverlayPriority);
                if (aftermath.ValueRO.AnimatedSequence == aftermath.ValueRO.Sequence)
                {
                    if (overlayIndex < 0 || IsPlaybackComplete(overlays[overlayIndex].Playback))
                    {
                        RemoveAftermathOverlays(overlays);
                        aftermath.ValueRW.HitRecovery = 0;
                        return;
                    }

                    return;
                }

                FixedString64Bytes groupName = ResolveHitRecoveryGroup(ref systemState, entity, ref catalog, presentation, ref random);
                StartOrKeepAnimation(ref systemState, 
                    entity,
                    aftermath,
                    ref catalog,
                    presentation,
                    overlays,
                    groupName,
                    HitRecoveryOverlayPriority,
                    requestedLoopCount: 0u,
                    holdAtStop: false);
            }
        }

        void ApplyDeathAnimation(ref SystemState systemState, 
            Entity entity,
            RefRW<ActorHitAftermathState> aftermath,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ref Unity.Mathematics.Random random)
        {
            FixedString64Bytes deathGroup = aftermath.ValueRO.DeathAnimationGroup;
            if (deathGroup.IsEmpty)
            {
                deathGroup = ResolveDeathGroup(ref systemState, entity, ref catalog, presentation, aftermath.ValueRO, ref random);
                aftermath.ValueRW.DeathAnimationGroup = deathGroup;
            }

            int overlayIndex = FindAftermathOverlay(overlays, DeathOverlayPriority);
            if (aftermath.ValueRO.AnimatedSequence == aftermath.ValueRO.Sequence
                && overlayIndex >= 0
                && overlays[overlayIndex].Playback.Time >= overlays[overlayIndex].Playback.StopTime)
            {
                aftermath.ValueRW.DeathAnimationFinished = 1;
                return;
            }

            StartOrKeepAnimation(ref systemState, 
                entity,
                aftermath,
                ref catalog,
                presentation,
                overlays,
                deathGroup,
                DeathOverlayPriority,
                requestedLoopCount: 0u,
                holdAtStop: true);
        }

        void StartOrKeepAnimation(ref SystemState systemState, 
            Entity entity,
            RefRW<ActorHitAftermathState> aftermath,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            FixedString64Bytes groupName,
            int priority,
            uint requestedLoopCount,
            bool holdAtStop)
        {
            if (groupName.IsEmpty)
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} requested an empty aftermath animation group.");

            ulong groupHash = ActorAnimationGroupHash.Hash(groupName);
            int overlayIndex = FindAftermathOverlay(overlays, priority);
            if (aftermath.ValueRO.AnimatedSequence == aftermath.ValueRO.Sequence
                && overlayIndex >= 0
                && overlays[overlayIndex].Playback.GroupHash == groupHash
                && ActorAnimationPlaybackUtility.IsActive(overlays[overlayIndex].Playback))
            {
                return;
            }

            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out var group))
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} is missing required aftermath animation group '{groupName}'.");
            }

            RemoveAftermathOverlays(overlays);
            ActorAnimationPlaybackState playback = default;
            ActorAnimationPlaybackUtility.Start(ref playback, group, requestedLoopCount);
            playback.Speed = 1f;
            playback.HoldAtStop = holdAtStop ? (byte)1 : (byte)0;

            overlays.Add(new ActorAnimationOverlayState
            {
                Playback = playback,
                Weight = 1f,
                Priority = priority,
                Mask = ActorAnimationBlendMask.All,
                AllowMovingLowerBodyOverride = 1,
            });
            aftermath.ValueRW.AnimatedSequence = aftermath.ValueRO.Sequence;
        }

        FixedString64Bytes ResolveHitRecoveryGroup(ref SystemState systemState, 
            Entity entity,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref Unity.Mathematics.Random random)
        {
            Span<int> variants = stackalloc int[MaxHitReactionVariants];
            int count = 0;
            for (int variant = 1; variant <= MaxHitReactionVariants; variant++)
            {
                FixedString64Bytes candidate = default;
                candidate.Append("hit");
                candidate.Append(variant);
                if (ActorAnimationGroupLookupUtility.TryResolveGroup(
                        ref catalog,
                        presentation,
                        ActorAnimationGroupHash.Hash(candidate),
                        out _))
                {
                    variants[count++] = variant;
                }
            }

            if (count == 0)
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no required hit recovery animation variants.");

            int selected = variants[random.NextInt(count)];
            FixedString64Bytes groupName = default;
            groupName.Append("hit");
            groupName.Append(selected);
            return groupName;
        }

        FixedString64Bytes ResolveDeathGroup(ref SystemState systemState, 
            Entity entity,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in ActorHitAftermathState aftermath,
            ref Unity.Mathematics.Random random)
        {
            if (aftermath.KnockedOut != 0 && TryGroup(ref catalog, presentation, "deathknockout", out var knockout))
                return knockout;
            if (aftermath.KnockedDown != 0 && TryGroup(ref catalog, presentation, "deathknockdown", out var knockdown))
                return knockdown;

            Span<int> variants = stackalloc int[MaxDeathVariants];
            int count = 0;
            for (int variant = 1; variant <= MaxDeathVariants; variant++)
            {
                FixedString64Bytes candidate = default;
                candidate.Append("death");
                candidate.Append(variant);
                if (ActorAnimationGroupLookupUtility.TryResolveGroup(
                        ref catalog,
                        presentation,
                        ActorAnimationGroupHash.Hash(candidate),
                        out _))
                {
                    variants[count++] = variant;
                }
            }

            if (count == 0)
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no required death animation variants.");

            int selected = variants[random.NextInt(count)];
            FixedString64Bytes groupName = default;
            groupName.Append("death");
            groupName.Append(selected);
            return groupName;
        }

        static bool TryGroup(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            string group,
            out FixedString64Bytes groupName)
        {
            groupName = default;
            groupName.Append(group);
            return ActorAnimationGroupLookupUtility.TryResolveGroup(
                ref catalog,
                presentation,
                ActorAnimationGroupHash.Hash(groupName),
                out _);
        }

        static bool HasAnyAftermath(in ActorHitAftermathState aftermath)
            => aftermath.Dead != 0
               || aftermath.KnockedOut != 0
               || aftermath.KnockedDown != 0
               || aftermath.HitRecovery != 0;

        static bool IsPlaybackComplete(in ActorAnimationPlaybackState playback)
            => !ActorAnimationPlaybackUtility.IsActive(playback) || playback.Time >= playback.StopTime;

        static int FindAftermathOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays, int priority)
        {
            for (int i = 0; i < overlays.Length; i++)
            {
                if (overlays[i].Priority == priority)
                    return i;
            }

            return -1;
        }

        public static void RemoveAftermathOverlays(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = overlays.Length - 1; i >= 0; i--)
            {
                int priority = overlays[i].Priority;
                if (priority == HitRecoveryOverlayPriority
                    || priority == KnockdownOverlayPriority
                    || priority == DeathOverlayPriority)
                {
                    overlays.RemoveAt(i);
                }
            }
        }

        void RequireAnimationComposition(ref SystemState systemState, Entity entity)
        {
            if (!systemState.EntityManager.HasComponent<ActorPresentation>(entity))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no ActorPresentation.");
            if (!systemState.EntityManager.HasComponent<ActorAnimationState>(entity))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no ActorAnimationState.");
            if (!systemState.EntityManager.HasBuffer<ActorAnimationOverlayState>(entity))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no ActorAnimationOverlayState buffer.");
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
