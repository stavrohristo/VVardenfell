using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageFeedbackSystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathAnimationSystem))]
    public partial class MorrowindBlockAnimationSystem : SystemBase
    {
        const int BlockOverlayPriority = 60;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorBlockState>();
            RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Block] Block animation has no actor animation catalog blob.");

            ref var catalog = ref catalogRef.Value;
            foreach (var (block, entity) in
                     SystemAPI.Query<RefRW<ActorBlockState>>()
                         .WithEntityAccess())
            {
                if (block.ValueRO.Active == 0)
                    continue;

                RequireAnimationComposition(entity);
                var presentation = EntityManager.GetComponentData<ActorPresentation>(entity);
                var overlays = EntityManager.GetBuffer<ActorAnimationOverlayState>(entity);
                ApplyBlockAnimation(entity, block, ref catalog, presentation, overlays);
            }
        }

        void ApplyBlockAnimation(
            Entity entity,
            RefRW<ActorBlockState> block,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            int overlayIndex = FindBlockOverlay(overlays);
            if (block.ValueRO.AnimatedSequence == block.ValueRO.Sequence)
            {
                if (overlayIndex < 0 || IsPlaybackComplete(overlays[overlayIndex].Playback))
                {
                    RemoveBlockOverlay(overlays);
                    block.ValueRW.Active = 0;
                    return;
                }

                return;
            }

            FixedString64Bytes groupName = new("shield");
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(
                    ref catalog,
                    presentation,
                    ActorAnimationGroupHash.Hash(groupName),
                    out var group))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(entity)} is missing required shield animation group.");
            }

            if (!ActorAnimationMarkerWindowUtility.TryResolveWindow(
                    ref catalog,
                    group,
                    new FixedString64Bytes("block start"),
                    new FixedString64Bytes("block stop"),
                    out float startTime,
                    out float stopTime))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(entity)} shield animation is missing block start/stop markers.");
            }

            RemoveBlockOverlay(overlays);
            ActorAnimationPlaybackState playback = default;
            ActorAnimationPlaybackUtility.StartWindow(ref playback, group, startTime, stopTime, holdAtStop: false);
            playback.Speed = 1f;
            overlays.Add(new ActorAnimationOverlayState
            {
                Playback = playback,
                Weight = 1f,
                Priority = BlockOverlayPriority,
                Mask = ActorAnimationBlendMask.UpperBody,
            });
            block.ValueRW.AnimatedSequence = block.ValueRO.Sequence;
        }

        static bool IsPlaybackComplete(in ActorAnimationPlaybackState playback)
            => !ActorAnimationPlaybackUtility.IsActive(playback) || playback.Time >= playback.StopTime;

        static int FindBlockOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = 0; i < overlays.Length; i++)
                if (overlays[i].Priority == BlockOverlayPriority)
                    return i;

            return -1;
        }

        static void RemoveBlockOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = overlays.Length - 1; i >= 0; i--)
                if (overlays[i].Priority == BlockOverlayPriority)
                    overlays.RemoveAt(i);
        }

        void RequireAnimationComposition(Entity entity)
        {
            if (!EntityManager.HasComponent<ActorPresentation>(entity))
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(entity)} has no ActorPresentation.");
            if (!EntityManager.HasComponent<ActorAnimationState>(entity))
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(entity)} has no ActorAnimationState.");
            if (!EntityManager.HasBuffer<ActorAnimationOverlayState>(entity))
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(entity)} has no ActorAnimationOverlayState buffer.");
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
