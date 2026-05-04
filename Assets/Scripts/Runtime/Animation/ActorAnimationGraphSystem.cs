#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial struct ActorAnimationControllerSystem : ISystem
    {
        const uint InfiniteLoops = uint.MaxValue;
        const float MainTransitionDuration = 0.12f;
        const float MinimumJumpAirborneTime = 0.08f;
        const float MinimumLandingGroundedTime = 0.04f;
        const float LandingVerticalVelocityEpsilon = 0.01f;
        const byte JumpPhaseNone = 0;
        const byte JumpPhaseInAir = 1;
        const byte JumpPhaseLanding = 2;
        static readonly ProfilerMarker s_Controller = new("VV.ActorAnimation.Controller");

        EntityQuery _movingPlaybackQuery;
        EntityQuery _idlePlaybackQuery;
        EntityQuery _overlayPlaybackQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();

            _movingPlaybackQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorPresentation>(),
                    ComponentType.ReadOnly<MorrowindMovementState>(),
                    ComponentType.ReadWrite<ActorAnimationState>(),
                    ComponentType.ReadWrite<ActorJumpAnimationState>(),
                }
            });
            _idlePlaybackQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorPresentation>(),
                    ComponentType.ReadWrite<ActorAnimationState>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<MorrowindMovementState>(),
                }
            });

            _overlayPlaybackQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<ActorAnimationOverlayState>()
                }
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalog.IsCreated)
                return;

            float deltaTime = SystemAPI.Time.DeltaTime;
            using (s_Controller.Auto())
            {
                state.Dependency = new PlaybackMovingJob
                {
                    Catalog = catalog,
                    DeltaTime = deltaTime,
                }.ScheduleParallel(_movingPlaybackQuery, state.Dependency);

                state.Dependency = new PlaybackIdleJob
                {
                    Catalog = catalog,
                    DeltaTime = deltaTime,
                }.ScheduleParallel(_idlePlaybackQuery, state.Dependency);

                state.Dependency = new AdvanceOverlaysJob
                {
                    DeltaTime = deltaTime,
                }.ScheduleParallel(_overlayPlaybackQuery, state.Dependency);
            }
        }

        [BurstCompile]
        partial struct PlaybackMovingJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            public float DeltaTime;

            void Execute(
                in ActorPresentation presentation,
                in MorrowindMovementState movementState,
                ref ActorAnimationState animation,
                ref ActorJumpAnimationState jumpState)
            {
                ref var catalog = ref Catalog.Value;
                ResolveMain(ref catalog, presentation, movementState, DeltaTime, ref animation, ref jumpState);
                Advance(ref animation, DeltaTime);
            }
        }

        [BurstCompile]
        partial struct PlaybackIdleJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;
            public float DeltaTime;

            void Execute(
                in ActorPresentation presentation,
                ref ActorAnimationState animation)
            {
                ref var catalog = ref Catalog.Value;
                ResolveIdle(ref catalog, presentation, sneak: false, ref animation);
                Advance(ref animation, DeltaTime);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct AdvanceOverlaysJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(DynamicBuffer<ActorAnimationOverlayState> overlays)
            {
                AdvanceOverlays(overlays, DeltaTime);
            }
        }

        static void ResolveMain(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in MorrowindMovementState movementState,
            float deltaTime,
            ref ActorAnimationState animation,
            ref ActorJumpAnimationState jumpState)
        {
            ref var playback = ref animation.Playback;
            if (playback.Speed < 0f)
                playback.Speed = 0f;

            float2 localMove = movementState.LocalMove;
            bool moving = math.lengthsq(localMove) > 0.0001f;
            if (movementState.JumpAccepted || !movementState.Grounded || jumpState.Phase == JumpPhaseInAir)
            {
                if (ResolveJump(ref catalog, presentation, movementState, moving, deltaTime, ref animation, ref jumpState))
                    return;
            }

            if (moving)
            {
                jumpState.Phase = JumpPhaseNone;
                ResolveMovementHashes(localMove, movementState.RunHeld, movementState.SneakHeld, swim: false, out ulong primary, out ulong fallback);
                if (TryResolveGroup(ref catalog, presentation, primary, out var movement)
                    || (fallback != 0UL && TryResolveGroup(ref catalog, presentation, fallback, out movement)))
                {
                    StartIfNeeded(ref animation, movement, InfiniteLoops, MainTransitionDuration);
                    animation.Playback.Speed = ResolveMovementSpeed(movement, movementState);
                    return;
                }

                ResolveIdle(ref catalog, presentation, movementState.SneakHeld, ref animation);
                return;
            }

            if (ResolveJump(ref catalog, presentation, movementState, moving: false, deltaTime, ref animation, ref jumpState))
                return;

            ResolveIdle(ref catalog, presentation, movementState.SneakHeld, ref animation);
        }

        static bool ResolveJump(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in MorrowindMovementState movementState,
            bool moving,
            float deltaTime,
            ref ActorAnimationState animation,
            ref ActorJumpAnimationState jumpState)
        {
            bool jumpPlaybackSampleAvailable = ActorAnimationPlaybackUtility.CanSample(animation.Playback)
                                               && animation.Playback.GroupHash == ActorAnimationKnownGroupHashes.Jump;
            bool jumpPlaybackPlaying = ActorAnimationPlaybackUtility.IsActive(animation.Playback)
                                       && animation.Playback.GroupHash == ActorAnimationKnownGroupHashes.Jump;
            if (!TryResolveGroup(ref catalog, presentation, ActorAnimationKnownGroupHashes.Jump, out var jump))
            {
                jumpState.Phase = JumpPhaseNone;
                return false;
            }

            bool wantsInAir = movementState.JumpAccepted || !movementState.Grounded;
            if (wantsInAir)
            {
                if (jumpState.Phase != JumpPhaseInAir || !jumpPlaybackSampleAvailable)
                {
                    StartInAirJump(ref animation, jump, MainTransitionDuration);
                    animation.Playback.Speed = 1f;
                    animation.Initialized = 1;
                    jumpState.AirborneTime = 0f;
                    jumpState.LandingGroundedTime = 0f;
                }

                if (!movementState.Grounded)
                    jumpState.AirborneTime += deltaTime;
                jumpState.LandingGroundedTime = 0f;

                jumpState.Phase = JumpPhaseInAir;
                return true;
            }

            if (jumpState.Phase == JumpPhaseInAir && jumpPlaybackSampleAvailable)
            {
                jumpState.LandingGroundedTime = movementState.Grounded
                    ? jumpState.LandingGroundedTime + deltaTime
                    : 0f;

                if (!CanStartLanding(movementState, jumpState))
                {
                    if (movementState.Grounded && jumpState.AirborneTime < MinimumJumpAirborneTime)
                    {
                        jumpState.Phase = JumpPhaseNone;
                        jumpState.AirborneTime = 0f;
                        jumpState.LandingGroundedTime = 0f;
                        return false;
                    }

                    return true;
                }

                if (moving)
                {
                    jumpState.Phase = JumpPhaseNone;
                    jumpState.AirborneTime = 0f;
                    jumpState.LandingGroundedTime = 0f;
                    return false;
                }

                StartAt(ref animation, jump, requestedLoopCount: 0u, jump.LoopStopTime, MainTransitionDuration);
                animation.Playback.Speed = 1f;
                animation.Initialized = 1;
                jumpState.Phase = JumpPhaseLanding;
                jumpState.AirborneTime = 0f;
                jumpState.LandingGroundedTime = 0f;
                return true;
            }

            if (jumpState.Phase == JumpPhaseLanding && jumpPlaybackPlaying)
                return true;

            jumpState.Phase = JumpPhaseNone;
            jumpState.AirborneTime = 0f;
            jumpState.LandingGroundedTime = 0f;
            return false;
        }

        static bool CanStartLanding(in MorrowindMovementState movementState, in ActorJumpAnimationState jumpState)
        {
            return movementState.Grounded
                   && jumpState.AirborneTime >= MinimumJumpAirborneTime
                   && jumpState.LandingGroundedTime >= MinimumLandingGroundedTime
                   && movementState.LastVelocity.y <= LandingVerticalVelocityEpsilon;
        }

        static void StartInAirJump(
            ref ActorAnimationState animation,
            ActorAnimationGroupBlob group,
            float transitionDuration)
        {
            Start(ref animation, group, requestedLoopCount: 0u, transitionDuration);
            float holdTime = group.LoopStartTime > group.StartTime ? group.LoopStartTime : group.StartTime;
            animation.Playback.LoopCount = 0u;
            animation.Playback.LoopStopTime = holdTime;
            animation.Playback.StopTime = holdTime;
            animation.Playback.HoldAtStop = 1;
        }

        static void ResolveIdle(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            bool sneak,
            ref ActorAnimationState animation)
        {
            if (sneak
                && TryResolveGroup(ref catalog, presentation, ActorAnimationKnownGroupHashes.IdleSneak, out var idleSneak))
            {
                StartIfNeeded(ref animation, idleSneak, InfiniteLoops, MainTransitionDuration);
                animation.Playback.Speed = 1f;
                return;
            }

            if (TryResolveGroup(ref catalog, presentation, ActorAnimationKnownGroupHashes.Idle, out var idle))
            {
                StartIfNeeded(ref animation, idle, InfiniteLoops, MainTransitionDuration);
                animation.Playback.Speed = 1f;
            }
            else
                Clear(ref animation);
        }

        static float ResolveMovementSpeed(ActorAnimationGroupBlob group, in MorrowindMovementState movementState)
        {
            if (group.Velocity <= 0.0001f)
                return 1f;

            float planarSpeed = math.length(new float2(movementState.LastVelocity.x, movementState.LastVelocity.z));
            if (planarSpeed <= 0.0001f)
                return 1f;

            return math.min(10f, planarSpeed / group.Velocity);
        }

        static void StartIfNeeded(
            ref ActorAnimationState animation,
            ActorAnimationGroupBlob group,
            uint requestedLoopCount,
            float transitionDuration)
        {
            if (ActorAnimationPlaybackUtility.Matches(animation.Playback, group.GroupHash, group.ClipHash))
                return;

            Start(ref animation, group, requestedLoopCount, transitionDuration);
        }

        static void Start(
            ref ActorAnimationState animation,
            ActorAnimationGroupBlob group,
            uint requestedLoopCount,
            float transitionDuration)
        {
            CaptureTransitionSource(ref animation, transitionDuration);
            ActorAnimationPlaybackUtility.Start(ref animation.Playback, group, requestedLoopCount);
            animation.Initialized = 1;
        }

        static void StartAt(
            ref ActorAnimationState animation,
            ActorAnimationGroupBlob group,
            uint requestedLoopCount,
            float startTime,
            float transitionDuration)
        {
            CaptureTransitionSource(ref animation, transitionDuration);
            ActorAnimationPlaybackUtility.StartAt(ref animation.Playback, group, requestedLoopCount, startTime);
            animation.Initialized = 1;
        }

        static void CaptureTransitionSource(ref ActorAnimationState animation, float duration)
        {
            if (duration <= 0f || !ActorAnimationPlaybackUtility.CanSample(animation.Playback))
            {
                ClearTransition(ref animation);
                return;
            }

            animation.TransitionPlayback = animation.Playback;
            animation.TransitionTime = 0f;
            animation.TransitionDuration = duration;
            animation.TransitionActive = 1;
        }

        static void Clear(ref ActorAnimationState animation)
        {
            if (animation.Playback.Playing == 0
                && animation.Playback.ClipIndex < 0
                && animation.Playback.GroupHash == 0UL
                && animation.Playback.ClipHash == 0UL)
                return;

            ActorAnimationPlaybackUtility.Clear(ref animation.Playback);
            ClearTransition(ref animation);
            animation.Initialized = 1;
        }

        static void ClearTransition(ref ActorAnimationState animation)
        {
            ActorAnimationPlaybackUtility.Clear(ref animation.TransitionPlayback);
            animation.TransitionTime = 0f;
            animation.TransitionDuration = 0f;
            animation.TransitionActive = 0;
        }

        static void Advance(ref ActorAnimationState animation, float deltaTime)
        {
            ActorAnimationPlaybackUtility.Advance(ref animation.Playback, deltaTime, InfiniteLoops);
            if (animation.TransitionActive == 0)
                return;

            ActorAnimationPlaybackUtility.Advance(ref animation.TransitionPlayback, deltaTime, InfiniteLoops);
            animation.TransitionTime += deltaTime;
            if (animation.TransitionTime >= animation.TransitionDuration)
                ClearTransition(ref animation);
        }

        static void AdvanceOverlays(DynamicBuffer<ActorAnimationOverlayState> overlays, float deltaTime)
        {
            for (int i = 0; i < overlays.Length; i++)
            {
                var overlay = overlays[i];
                if (!ActorAnimationPlaybackUtility.IsActive(overlay.Playback))
                    continue;

                ActorAnimationPlaybackUtility.Advance(ref overlay.Playback, deltaTime, InfiniteLoops);
                overlays[i] = overlay;
            }
        }

        static bool TryResolveGroup(ref ActorAnimationCatalogBlob catalog, in ActorPresentation presentation, ulong groupHash, out ActorAnimationGroupBlob group)
        {
            if (!TryGetRigFamilyAnimationIndex(ref catalog, presentation.RigFamilyIndex, out var index))
            {
                group = default;
                return false;
            }

            int first = index.FirstGroupLookupIndex;
            int count = index.GroupLookupCount;
            int end = math.min(catalog.GroupLookups.Length, first + count);
            if (first < 0 || count <= 0 || first >= end)
            {
                group = default;
                return false;
            }

            int lower = first;
            int upper = end;
            while (lower < upper)
            {
                int mid = lower + ((upper - lower) >> 1);
                if (catalog.GroupLookups[mid].GroupHash < groupHash)
                    lower = mid + 1;
                else
                    upper = mid;
            }

            int duplicateEnd = lower;
            while (duplicateEnd < end && catalog.GroupLookups[duplicateEnd].GroupHash == groupHash)
                duplicateEnd++;

            for (int i = duplicateEnd - 1; i >= lower; i--)
            {
                var lookup = catalog.GroupLookups[i];
                if ((uint)lookup.GroupIndex >= (uint)catalog.Groups.Length)
                    continue;

                group = catalog.Groups[lookup.GroupIndex];
                if ((uint)group.ClipIndex < (uint)catalog.Clips.Length)
                    return true;
            }

            group = default;
            return false;
        }

        static bool TryGetRigFamilyAnimationIndex(
            ref ActorAnimationCatalogBlob catalog,
            int rigFamilyIndex,
            out ActorRigFamilyAnimationIndexBlob index)
        {
            if ((uint)rigFamilyIndex >= (uint)catalog.RigFamilyAnimationIndexes.Length)
            {
                index = default;
                return false;
            }

            index = catalog.RigFamilyAnimationIndexes[rigFamilyIndex];
            return true;
        }

        static void ResolveMovementHashes(float2 localMove, bool run, bool sneak, bool swim, out ulong primary, out ulong fallback)
        {
            byte direction = ResolveDirection(localMove);
            if (swim)
            {
                primary = ActorAnimationKnownGroupHashes.Movement(run ? (byte)4 : (byte)3, direction);
                fallback = ActorAnimationKnownGroupHashes.Movement(run ? (byte)3 : (byte)0, direction);
                return;
            }

            if (sneak)
            {
                primary = ActorAnimationKnownGroupHashes.Movement(2, direction);
                fallback = ActorAnimationKnownGroupHashes.Movement(0, direction);
                return;
            }

            primary = ActorAnimationKnownGroupHashes.Movement(run ? (byte)1 : (byte)0, direction);
            fallback = run ? ActorAnimationKnownGroupHashes.Movement(0, direction) : 0UL;
        }

        static byte ResolveDirection(float2 localMove)
        {
            bool lateral = math.abs(localMove.x) > math.abs(localMove.y);
            if (lateral)
                return localMove.x >= 0f ? (byte)3 : (byte)2;
            return localMove.y >= 0f ? (byte)0 : (byte)1;
        }

    }
}
#else
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationStateResolveSystem))]
    public partial struct ActorAnimationGraphSystem : ISystem
    {
        const int DefaultPriority = 0;
        const int MovementPriority = 10;
        const uint InfiniteLoops = uint.MaxValue;

        static readonly ProfilerMarker k_ResolveRequestedGroups = new("VV.ActorAnimationGraph.ResolveRequestedGroups");
        static readonly ProfilerMarker k_AdvanceLayers = new("VV.ActorAnimationGraph.AdvanceLayers");

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            using (k_AdvanceLayers.Auto())
            {
                state.Dependency = new AdvanceLayersJob
                {
                    DeltaTime = deltaTime,
                }.ScheduleParallel(state.Dependency);
            }

            using (k_ResolveRequestedGroups.Auto())
            {
                state.Dependency = new ResolveRequestedGroupsJob
                {
                    Catalog = catalogRef,
                }.ScheduleParallel(state.Dependency);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct ResolveRequestedGroupsJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                ref ActorAnimationController controller,
                ref ActorIdleAnimationState idleState,
                in ActorPresentation presentation,
                DynamicBuffer<ActorAnimationLayer> layers,
                EnabledRefRW<ActorAnimationPoseDirty> poseDirty)
            {
                if (!Catalog.IsCreated)
                    return;

                if (controller.Speed <= 0f)
                    controller.Speed = 1f;

                ref var catalog = ref Catalog.Value;
                bool changed = false;
                FixedString64Bytes idle = IdleGroup();
                FixedString64Bytes activeIdle = SelectIdleGroup(ref catalog, presentation, ref idleState, layers);
                changed |= RemoveIdleLayersExcept(layers, activeIdle);

                // OpenMW keeps animation groups as active states (`mStates`) with priorities and masks.
                // We mirror that contract in flat ECS buffers: idle is the base state, movement is a
                // higher-priority full-body state. Combat/weapon layers can extend this without changing
                // the sampling formula.
                changed |= EnsureLayer(
                    layers,
                    ref catalog,
                    presentation,
                    activeIdle,
                    DefaultPriority,
                    ActorAnimationBlendMask.All,
                    idleState.CandidateCount <= 1 ? InfiniteLoops : 0u,
                    autoDisable: 0);

                FixedString64Bytes requested = controller.RequestedGroup.IsEmpty
                    ? idle
                    : controller.RequestedGroup;
                bool wantsMovement = !EqualsIgnoreCase(requested, idle);
                int movementIndex = FindMovementLayer(layers);
                if (wantsMovement)
                {
                    bool movementClipExists = TryBuildLayer(
                        ref catalog,
                        presentation,
                        requested,
                        MovementPriority,
                        ActorAnimationBlendMask.All,
                        InfiniteLoops,
                        autoDisable: 0,
                        out var movementLayer);
                    if (movementClipExists)
                    {
                        if (movementIndex >= 0)
                        {
                            var existing = layers[movementIndex];
                            if (existing.Playing != 0 && existing.ClipHash == movementLayer.ClipHash)
                                movementLayer.Time = existing.Time;

                            if (!LayerEquals(layers[movementIndex], movementLayer))
                            {
                                layers[movementIndex] = movementLayer;
                                changed = true;
                            }
                        }
                        else
                        {
                            layers.Add(movementLayer);
                            changed = true;
                        }
                    }
                    else if (movementIndex >= 0)
                    {
                        layers.RemoveAt(movementIndex);
                        changed = true;
                    }
                }
                else if (movementIndex >= 0)
                {
                    layers.RemoveAt(movementIndex);
                    changed = true;
                }

                changed |= SyncControllerToBestLayer(ref controller, layers);
                if (changed)
                    poseDirty.ValueRW = true;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct AdvanceLayersJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(
                ref ActorAnimationController controller,
                DynamicBuffer<ActorAnimationLayer> layers,
                EnabledRefRW<ActorAnimationPoseDirty> poseDirty)
            {
                if (controller.Speed <= 0f)
                    controller.Speed = 1f;

                bool changed = false;
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.Playing == 0 || layer.Weight <= 0f || layer.ClipIndex < 0)
                        continue;

                    float previousTime = layer.Time;
                    byte previousPlaying = layer.Playing;
                    float nextTime = layer.Time + DeltaTime * controller.Speed;
                    bool canLoop = layer.LoopCount > 0 && layer.LoopStopTime > layer.LoopStartTime;

                    if (canLoop && nextTime >= layer.LoopStopTime)
                    {
                        if (layer.LoopCount != InfiniteLoops)
                            layer.LoopCount--;
                        layer.Time = layer.LoopStartTime;
                    }
                    else
                    {
                        layer.Time = nextTime >= layer.StopTime ? layer.StopTime : nextTime;
                    }

                    if (!canLoop && layer.Time >= layer.StopTime)
                    {
                        layer.Playing = 0;
                        if (layer.AutoDisable != 0)
                            layer.Weight = 0f;
                    }

                    if (!MathEquals(previousTime, layer.Time) || previousPlaying != layer.Playing)
                    {
                        layers[i] = layer;
                        changed = true;
                    }
                }

                changed |= SyncControllerToBestLayer(ref controller, layers);
                if (changed)
                    poseDirty.ValueRW = true;
            }
        }

        static bool EnsureLayer(
            DynamicBuffer<ActorAnimationLayer> layers,
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            FixedString64Bytes group,
            int priority,
            ActorAnimationBlendMask mask,
            uint loopCount,
            byte autoDisable)
        {
            if (!TryBuildLayer(ref catalog, presentation, group, priority, mask, loopCount, autoDisable, out var desired))
                return false;

            int existingIndex = FindLayer(layers, group, priority);
            if (existingIndex < 0)
            {
                layers.Add(desired);
                return true;
            }

            var existing = layers[existingIndex];
            if (LayerEquals(existing, desired))
                return false;

            desired.Time = existing.Playing != 0 && existing.ClipHash == desired.ClipHash
                ? existing.Time
                : desired.Time;
            layers[existingIndex] = desired;
            return true;
        }

        static bool RemoveIdleLayersExcept(DynamicBuffer<ActorAnimationLayer> layers, FixedString64Bytes activeIdle)
        {
            bool changed = false;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer.Priority != DefaultPriority || EqualsIgnoreCase(layer.Group, activeIdle))
                    continue;

                layers.RemoveAt(i);
                changed = true;
            }

            return changed;
        }

        static FixedString64Bytes SelectIdleGroup(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorIdleAnimationState idleState,
            DynamicBuffer<ActorAnimationLayer> layers)
        {
            int candidateCount = CountIdleCandidates(ref catalog, presentation);
            idleState.CandidateCount = (byte)math.min(candidateCount, byte.MaxValue);
            if (candidateCount <= 0)
                return IdleGroup();

            int currentIndex = idleState.CurrentIndex;
            bool needsSelection = idleState.Initialized == 0
                                  || currentIndex >= candidateCount
                                  || IsIdleLayerFinished(layers);
            if (needsSelection)
            {
                uint seed = idleState.Seed == 0 ? 1u : idleState.Seed;
                seed = NextRandom(seed);
                idleState.Seed = seed;
                currentIndex = (int)(seed % (uint)candidateCount);
                idleState.CurrentIndex = (byte)math.min(currentIndex, byte.MaxValue);
                idleState.Initialized = 1;
            }

            if (ResolveIdleCandidateByOrdinal(ref catalog, presentation, currentIndex, out var group))
                return group;

            return IdleGroup();
        }

        static bool IsIdleLayerFinished(DynamicBuffer<ActorAnimationLayer> layers)
        {
            bool sawIdle = false;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.Priority != DefaultPriority)
                    continue;

                sawIdle = true;
                if (layer.Playing != 0 && layer.Weight > 0f && layer.ClipIndex >= 0)
                    return false;
            }

            return sawIdle;
        }

        static uint NextRandom(uint seed)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return seed == 0 ? 1u : seed;
        }

        static int CountIdleCandidates(ref ActorAnimationCatalogBlob catalog, in ActorPresentation presentation)
        {
            int count = 0;
            for (int i = 0; i < 9; i++)
            {
                FixedString64Bytes group = IdleVariantGroup(i);
                if (ResolveClipIndex(ref catalog, presentation, group) >= 0)
                    count++;
            }

            return count;
        }

        static bool ResolveIdleCandidateByOrdinal(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            int ordinal,
            out FixedString64Bytes group)
        {
            group = IdleGroup();
            int count = 0;
            for (int i = 0; i < 9; i++)
            {
                FixedString64Bytes candidate = IdleVariantGroup(i);
                if (ResolveClipIndex(ref catalog, presentation, candidate) < 0)
                    continue;

                if (count == ordinal)
                {
                    group = candidate;
                    return true;
                }

                count++;
            }

            return false;
        }

        static bool TryBuildLayer(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            FixedString64Bytes group,
            int priority,
            ActorAnimationBlendMask mask,
            uint loopCount,
            byte autoDisable,
            out ActorAnimationLayer layer)
        {
            layer = default;
            int clipIndex = ResolveClipIndexWithFallback(ref catalog, presentation, group, out var resolvedGroup);
            if (clipIndex < 0)
                return false;

            ResolveGroupWindow(ref catalog, clipIndex, resolvedGroup, out float startTime, out float loopStart, out float loopStop, out float stopTime);
            uint resolvedLoopCount = IsLooping(ref catalog, clipIndex, resolvedGroup) ? loopCount : 0u;
            layer = new ActorAnimationLayer
            {
                Group = resolvedGroup,
                ClipIndex = clipIndex,
                ClipHash = ResolveClipHash(ref catalog, clipIndex),
                Time = startTime,
                StartTime = startTime,
                LoopStartTime = loopStart,
                LoopStopTime = loopStop,
                StopTime = stopTime,
                Weight = 1f,
                Priority = priority,
                LowerBodyPriority = MaskContains(mask, ActorAnimationBlendMask.LowerBody) ? priority : int.MinValue,
                TorsoPriority = MaskContains(mask, ActorAnimationBlendMask.Torso) ? priority : int.MinValue,
                LeftArmPriority = MaskContains(mask, ActorAnimationBlendMask.LeftArm) ? priority : int.MinValue,
                RightArmPriority = MaskContains(mask, ActorAnimationBlendMask.RightArm) ? priority : int.MinValue,
                LoopCount = resolvedLoopCount,
                Mask = mask,
                Playing = 1,
                AutoDisable = autoDisable,
            };
            return true;
        }

        static bool SyncControllerToBestLayer(ref ActorAnimationController controller, DynamicBuffer<ActorAnimationLayer> layers)
        {
            int bestIndex = SelectBestLayer(layers);
            if (bestIndex < 0)
            {
                bool cleared = controller.Playing != 0 || !controller.CurrentGroup.IsEmpty;
                controller.CurrentGroup = default;
                controller.CurrentClipHash = 0UL;
                controller.Time = 0f;
                controller.StartTime = 0f;
                controller.LoopStartTime = 0f;
                controller.LoopStopTime = 0f;
                controller.StopTime = 0f;
                controller.LoopCount = 0u;
                controller.Playing = 0;
                controller.AutoDisable = 0;
                controller.ActiveMask = 0;
                return cleared;
            }

            var best = layers[bestIndex];
            bool changed = !controller.CurrentGroup.Equals(best.Group)
                           || controller.CurrentClipHash != best.ClipHash
                           || !MathEquals(controller.Time, best.Time)
                           || controller.Playing != best.Playing
                           || controller.ActiveMask != best.Mask;
            controller.CurrentGroup = best.Group;
            controller.CurrentClipHash = best.ClipHash;
            controller.Time = best.Time;
            controller.StartTime = best.StartTime;
            controller.LoopStartTime = best.LoopStartTime;
            controller.LoopStopTime = best.LoopStopTime;
            controller.StopTime = best.StopTime;
            controller.LoopCount = best.LoopCount;
            controller.Playing = best.Playing;
            controller.AutoDisable = best.AutoDisable;
            controller.ActiveMask = best.Mask;
            return changed;
        }

        static int SelectBestLayer(DynamicBuffer<ActorAnimationLayer> layers)
        {
            int bestIndex = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.Playing == 0 || layer.Weight <= 0f || layer.ClipIndex < 0)
                    continue;
                if (layer.Priority < bestPriority)
                    continue;

                bestPriority = layer.Priority;
                bestIndex = i;
            }

            return bestIndex;
        }

        static int FindLayer(DynamicBuffer<ActorAnimationLayer> layers, FixedString64Bytes group, int priority)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.Priority == priority && EqualsIgnoreCase(layer.Group, group))
                    return i;
            }

            return -1;
        }

        static int FindMovementLayer(DynamicBuffer<ActorAnimationLayer> layers)
        {
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].Priority == MovementPriority)
                    return i;
            return -1;
        }

        static bool LayerEquals(ActorAnimationLayer left, ActorAnimationLayer right)
        {
            return left.ClipIndex == right.ClipIndex
                   && left.ClipHash == right.ClipHash
                   && left.Priority == right.Priority
                   && left.LowerBodyPriority == right.LowerBodyPriority
                   && left.TorsoPriority == right.TorsoPriority
                   && left.LeftArmPriority == right.LeftArmPriority
                   && left.RightArmPriority == right.RightArmPriority
                   && left.Mask == right.Mask
                   && left.Playing == right.Playing
                   && left.AutoDisable == right.AutoDisable
                   && MathEquals(left.StartTime, right.StartTime)
                   && MathEquals(left.LoopStartTime, right.LoopStartTime)
                   && MathEquals(left.LoopStopTime, right.LoopStopTime)
                   && MathEquals(left.StopTime, right.StopTime)
                   && MathEquals(left.Weight, right.Weight)
                   && EqualsIgnoreCase(left.Group, right.Group);
        }

        static bool MaskContains(ActorAnimationBlendMask value, ActorAnimationBlendMask mask)
            => (value & mask) != 0;

        static int ResolveClipIndexWithFallback(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            FixedString64Bytes group,
            out FixedString64Bytes resolvedGroup)
        {
            resolvedGroup = group;
            int clipIndex = ResolveClipIndex(ref catalog, presentation, group);
            if (clipIndex >= 0)
                return clipIndex;

            if (StartsWith(group, (byte)'i', (byte)'d', (byte)'l', (byte)'e')
                && (EqualsIgnoreCase(group, IdleSwimGroup()) || EqualsIgnoreCase(group, IdleSneakGroup())))
            {
                resolvedGroup = IdleGroup();
                return ResolveClipIndex(ref catalog, presentation, resolvedGroup);
            }

            if (StartsWithSwim(group))
            {
                FixedString64Bytes withoutSwim = StripSwimPrefix(group);
                clipIndex = ResolveClipIndex(ref catalog, presentation, withoutSwim);
                if (clipIndex >= 0)
                {
                    resolvedGroup = withoutSwim;
                    return clipIndex;
                }

                if (StartsWithRun(withoutSwim))
                {
                    FixedString64Bytes walk = ReplaceRunWithWalk(withoutSwim);
                    clipIndex = ResolveClipIndex(ref catalog, presentation, walk);
                    if (clipIndex >= 0)
                        resolvedGroup = walk;
                    return clipIndex;
                }

                return -1;
            }

            if (StartsWithRun(group))
            {
                FixedString64Bytes walk = ReplaceRunWithWalk(group);
                clipIndex = ResolveClipIndex(ref catalog, presentation, walk);
                if (clipIndex >= 0)
                    resolvedGroup = walk;
                return clipIndex;
            }

            return -1;
        }

        static int ResolveClipIndex(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            FixedString64Bytes group)
        {
            if ((uint)presentation.RigFamilyIndex >= (uint)catalog.RigFamilies.Length)
                return -1;

            var rigFamily = catalog.RigFamilies[presentation.RigFamilyIndex];
            if (rigFamily.FirstClipIndex < 0 || rigFamily.ClipCount <= 0)
                return -1;

            int end = math.min(catalog.Clips.Length, rigFamily.FirstClipIndex + rigFamily.ClipCount);
            for (int i = end - 1; i >= rigFamily.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (EqualsIgnoreCase(clip.Name, group))
                    return i;
            }

            for (int i = end - 1; i >= rigFamily.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (ClipHasGroupTextKey(ref catalog, clip, group))
                    return i;
            }

            // OpenMW can fall back at the gameplay animation selection layer, but the graph state
            // itself should not silently bind to the first clip. That was the source of broad cycling.
            return -1;
        }

        static ulong ResolveClipHash(ref ActorAnimationCatalogBlob catalog, int clipIndex)
        {
            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return 0UL;
            return catalog.Clips[clipIndex].AnimationHash;
        }

        static bool ClipHasGroupTextKey(ref ActorAnimationCatalogBlob catalog, ActorAnimationClipBlob clip, FixedString64Bytes group)
        {
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
                if (EqualsIgnoreCase(catalog.TextMarkers[i].Group, group))
                    return true;
            return false;
        }

        static void ResolveGroupWindow(
            ref ActorAnimationCatalogBlob catalog,
            int clipIndex,
            FixedString64Bytes group,
            out float startTime,
            out float loopStart,
            out float loopStop,
            out float stopTime)
        {
            startTime = 0f;
            loopStart = 0f;
            loopStop = 1f;
            stopTime = 1f;

            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return;

            var clip = catalog.Clips[clipIndex];
            float duration = clip.Duration > 0f ? clip.Duration : 1f;
            loopStop = duration;
            stopTime = duration;

            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return;

            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            bool insideGroup = false;
            bool finished = false;
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = catalog.TextMarkers[i];
                bool groupMatches = EqualsIgnoreCase(marker.Group, group);
                if (groupMatches)
                    insideGroup = true;

                if (groupMatches && marker.Kind == ActorAnimationTextMarkerKind.Start)
                {
                    startTime = marker.Time;
                    loopStart = marker.Time;
                    insideGroup = true;
                    continue;
                }

                if (!insideGroup)
                    continue;

                switch (marker.Kind)
                {
                    case ActorAnimationTextMarkerKind.LoopStart:
                        if (marker.Group.IsEmpty || groupMatches)
                            loopStart = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.LoopStop:
                        if (marker.Group.IsEmpty || groupMatches)
                            loopStop = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.Stop:
                        if (marker.Group.IsEmpty || groupMatches)
                        {
                            stopTime = marker.Time;
                            finished = true;
                        }
                        break;
                    case ActorAnimationTextMarkerKind.Start:
                        if (!marker.Group.IsEmpty && !groupMatches)
                        {
                            stopTime = marker.Time;
                            finished = true;
                        }
                        break;
                }

                if (finished)
                    break;
            }

            if (stopTime <= startTime)
                stopTime = duration;
            if (loopStart < startTime)
                loopStart = startTime;
            if (loopStop <= loopStart || loopStop > stopTime)
                loopStop = stopTime;
        }

        static bool IsLooping(ref ActorAnimationCatalogBlob catalog, int clipIndex, FixedString64Bytes group)
        {
            if (IsKnownLoopingGroup(group))
                return true;

            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return false;

            var clip = catalog.Clips[clipIndex];
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = catalog.TextMarkers[i];
                if (marker.Kind == ActorAnimationTextMarkerKind.LoopStart
                    || marker.Kind == ActorAnimationTextMarkerKind.LoopStop)
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsKnownLoopingGroup(FixedString64Bytes group)
        {
            return EqualsIgnoreCase(group, IdleGroup())
                   || EqualsIgnoreCase(group, IdleSwimGroup())
                   || EqualsIgnoreCase(group, IdleSneakGroup())
                   || StartsWithWalk(group)
                   || StartsWithRun(group)
                   || StartsWithSneak(group)
                   || StartsWithSwimWalk(group)
                   || StartsWithSwimRun(group)
                   || StartsWithTurn(group)
                   || StartsWithSwimTurn(group);
        }

        static FixedString64Bytes IdleGroup()
        {
            FixedString64Bytes value = default;
            value.Append('i');
            value.Append('d');
            value.Append('l');
            value.Append('e');
            return value;
        }

        static FixedString64Bytes IdleVariantGroup(int index)
        {
            FixedString64Bytes value = IdleGroup();
            if (index <= 0)
                return value;

            value.Append((byte)('1' + index));
            return value;
        }

        static FixedString64Bytes WalkForwardGroup()
        {
            FixedString64Bytes value = default;
            value.Append('w');
            value.Append('a');
            value.Append('l');
            value.Append('k');
            value.Append('f');
            value.Append('o');
            value.Append('r');
            value.Append('w');
            value.Append('a');
            value.Append('r');
            value.Append('d');
            return value;
        }

        static FixedString64Bytes WalkBackGroup()
        {
            FixedString64Bytes value = default;
            value.Append('w');
            value.Append('a');
            value.Append('l');
            value.Append('k');
            value.Append('b');
            value.Append('a');
            value.Append('c');
            value.Append('k');
            return value;
        }

        static FixedString64Bytes WalkLeftGroup()
        {
            FixedString64Bytes value = default;
            value.Append('w');
            value.Append('a');
            value.Append('l');
            value.Append('k');
            value.Append('l');
            value.Append('e');
            value.Append('f');
            value.Append('t');
            return value;
        }

        static FixedString64Bytes WalkRightGroup()
        {
            FixedString64Bytes value = default;
            value.Append('w');
            value.Append('a');
            value.Append('l');
            value.Append('k');
            value.Append('r');
            value.Append('i');
            value.Append('g');
            value.Append('h');
            value.Append('t');
            return value;
        }

        static FixedString64Bytes IdleSwimGroup()
        {
            FixedString64Bytes value = IdleGroup();
            value.Append('s');
            value.Append('w');
            value.Append('i');
            value.Append('m');
            return value;
        }

        static FixedString64Bytes IdleSneakGroup()
        {
            FixedString64Bytes value = IdleGroup();
            value.Append('s');
            value.Append('n');
            value.Append('e');
            value.Append('a');
            value.Append('k');
            return value;
        }

        static FixedString64Bytes StripSwimPrefix(FixedString64Bytes group)
        {
            FixedString64Bytes result = default;
            for (int i = 4; i < group.Length; i++)
                result.Append(group[i]);
            return result;
        }

        static FixedString64Bytes ReplaceRunWithWalk(FixedString64Bytes group)
        {
            FixedString64Bytes result = default;
            result.Append('w');
            result.Append('a');
            result.Append('l');
            result.Append('k');
            for (int i = 3; i < group.Length; i++)
                result.Append(group[i]);
            return result;
        }

        static bool StartsWithWalk(FixedString64Bytes value)
            => StartsWith(value, (byte)'w', (byte)'a', (byte)'l', (byte)'k');

        static bool StartsWithRun(FixedString64Bytes value)
            => StartsWith(value, (byte)'r', (byte)'u', (byte)'n');

        static bool StartsWithSneak(FixedString64Bytes value)
            => StartsWith(value, (byte)'s', (byte)'n', (byte)'e', (byte)'a', (byte)'k');

        static bool StartsWithSwim(FixedString64Bytes value)
            => StartsWith(value, (byte)'s', (byte)'w', (byte)'i', (byte)'m');

        static bool StartsWithSwimWalk(FixedString64Bytes value)
            => StartsWith(value, (byte)'s', (byte)'w', (byte)'i', (byte)'m', (byte)'w', (byte)'a', (byte)'l', (byte)'k');

        static bool StartsWithSwimRun(FixedString64Bytes value)
            => StartsWith(value, (byte)'s', (byte)'w', (byte)'i', (byte)'m', (byte)'r', (byte)'u', (byte)'n');

        static bool StartsWithTurn(FixedString64Bytes value)
            => StartsWith(value, (byte)'t', (byte)'u', (byte)'r', (byte)'n');

        static bool StartsWithSwimTurn(FixedString64Bytes value)
            => StartsWith(value, (byte)'s', (byte)'w', (byte)'i', (byte)'m', (byte)'t', (byte)'u', (byte)'r', (byte)'n');

        static bool StartsWith(FixedString64Bytes value, byte a, byte b, byte c)
            => value.Length >= 3
               && ToLowerAscii(value[0]) == a
               && ToLowerAscii(value[1]) == b
               && ToLowerAscii(value[2]) == c;

        static bool StartsWith(FixedString64Bytes value, byte a, byte b, byte c, byte d)
            => value.Length >= 4
               && ToLowerAscii(value[0]) == a
               && ToLowerAscii(value[1]) == b
               && ToLowerAscii(value[2]) == c
               && ToLowerAscii(value[3]) == d;

        static bool StartsWith(FixedString64Bytes value, byte a, byte b, byte c, byte d, byte e)
            => value.Length >= 5
               && ToLowerAscii(value[0]) == a
               && ToLowerAscii(value[1]) == b
               && ToLowerAscii(value[2]) == c
               && ToLowerAscii(value[3]) == d
               && ToLowerAscii(value[4]) == e;

        static bool StartsWith(FixedString64Bytes value, byte a, byte b, byte c, byte d, byte e, byte f, byte g)
            => value.Length >= 7
               && ToLowerAscii(value[0]) == a
               && ToLowerAscii(value[1]) == b
               && ToLowerAscii(value[2]) == c
               && ToLowerAscii(value[3]) == d
               && ToLowerAscii(value[4]) == e
               && ToLowerAscii(value[5]) == f
               && ToLowerAscii(value[6]) == g;

        static bool StartsWith(FixedString64Bytes value, byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h)
            => value.Length >= 8
               && ToLowerAscii(value[0]) == a
               && ToLowerAscii(value[1]) == b
               && ToLowerAscii(value[2]) == c
               && ToLowerAscii(value[3]) == d
               && ToLowerAscii(value[4]) == e
               && ToLowerAscii(value[5]) == f
               && ToLowerAscii(value[6]) == g
               && ToLowerAscii(value[7]) == h;

        static bool EqualsIgnoreCase(FixedString64Bytes left, FixedString64Bytes right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                byte a = ToLowerAscii(left[i]);
                byte b = ToLowerAscii(right[i]);
                if (a != b)
                    return false;
            }
            return true;
        }

        static byte ToLowerAscii(byte value)
            => value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;

        static bool MathEquals(float left, float right)
            => math.abs(left - right) <= 0.0001f;
    }
}
#endif
