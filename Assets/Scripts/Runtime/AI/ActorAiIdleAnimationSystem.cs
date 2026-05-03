using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAiPlannerSystem))]
    public partial class ActorAiIdleAnimationSystem : SystemBase
    {
        const int AiIdleOverlayPriority = 20;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAiState>();
        }

        protected override void OnUpdate()
        {
            foreach (var (aiStateRef, entity) in SystemAPI.Query<RefRW<ActorAiState>>().WithEntityAccess())
            {
                ref var aiState = ref aiStateRef.ValueRW;
                if (aiState.PendingIdleGroup != 0)
                {
                    StartIdle(entity, ref aiState);
                    continue;
                }

                if (aiState.ActiveIdleGroupHash != 0UL && !IsIdleOverlayActive(entity, aiState.ActiveIdleGroupHash))
                {
                    aiState.ActiveIdleGroupHash = 0UL;
                    if (aiState.Status == (byte)ActorAiPlannerStatus.Waiting && float.IsPositiveInfinity(aiState.WaitUntilTime))
                    {
                        aiState.WaitUntilTime = 0f;
                        aiState.Status = (byte)ActorAiPlannerStatus.Idle;
                    }
                }
            }
        }

        void StartIdle(Entity entity, ref ActorAiState aiState)
        {
            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                throw new InvalidOperationException("[VVardenfell][AI] AiWander idle has no actor animation catalog.");

            if (!EntityManager.HasComponent<ActorPresentation>(entity))
                throw new InvalidOperationException("[VVardenfell][AI] AiWander idle target has no ActorPresentation.");

            if (!EntityManager.HasBuffer<ActorAnimationOverlayState>(entity))
                throw new InvalidOperationException("[VVardenfell][AI] AiWander idle target has no ActorAnimationOverlayState buffer.");

            var group = ResolveIdleGroup(aiState.PendingIdleGroup);
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException("[VVardenfell][AI] AiWander idle has no actor animation catalog blob.");

            ref var catalog = ref catalogRef.Value;
            var presentation = EntityManager.GetComponentData<ActorPresentation>(entity);
            ulong groupHash = ActorAnimationGroupHash.Hash(group);
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out var resolvedGroup))
                throw new InvalidOperationException($"[VVardenfell][AI] AiWander idle group '{group}' is not present on target actor.");

            var overlays = EntityManager.GetBuffer<ActorAnimationOverlayState>(entity);
            RemoveAiIdleOverlays(overlays);

            ActorAnimationPlaybackState playback = default;
            ActorAnimationPlaybackUtility.Start(ref playback, resolvedGroup, requestedLoopCount: 0u);
            playback.Speed = 1f;
            overlays.Add(new ActorAnimationOverlayState
            {
                Playback = playback,
                Weight = 1f,
                Priority = AiIdleOverlayPriority,
                Mask = ActorAnimationBlendMask.All,
            });

            aiState.ActiveIdleGroupHash = groupHash;
            aiState.PendingIdleGroup = 0;
        }

        bool IsIdleOverlayActive(Entity entity, ulong groupHash)
        {
            if (!EntityManager.HasBuffer<ActorAnimationOverlayState>(entity))
                throw new InvalidOperationException("[VVardenfell][AI] AiWander idle target lost ActorAnimationOverlayState buffer.");

            var overlays = EntityManager.GetBuffer<ActorAnimationOverlayState>(entity);
            for (int i = 0; i < overlays.Length; i++)
            {
                var overlay = overlays[i];
                if (overlay.Priority == AiIdleOverlayPriority
                    && overlay.Playback.GroupHash == groupHash
                    && ActorAnimationPlaybackUtility.IsActive(overlay.Playback))
                {
                    return true;
                }
            }

            return false;
        }

        static void RemoveAiIdleOverlays(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = overlays.Length - 1; i >= 0; i--)
            {
                if (overlays[i].Priority == AiIdleOverlayPriority)
                    overlays.RemoveAt(i);
            }
        }

        static FixedString64Bytes ResolveIdleGroup(byte group)
        {
            return group switch
            {
                2 => new FixedString64Bytes("idle2"),
                3 => new FixedString64Bytes("idle3"),
                4 => new FixedString64Bytes("idle4"),
                5 => new FixedString64Bytes("idle5"),
                6 => new FixedString64Bytes("idle6"),
                7 => new FixedString64Bytes("idle7"),
                8 => new FixedString64Bytes("idle8"),
                9 => new FixedString64Bytes("idle9"),
                _ => throw new InvalidOperationException($"[VVardenfell][AI] Invalid AiWander idle group '{group}'."),
            };
        }
    }
}
