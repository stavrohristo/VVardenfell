using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorPresentationSpawnSystem))]
    public partial struct ActorGpuAnimationRequestSystem : ISystem
    {
        const int MainPriority = 0;
        const int MainSampleKey = 1;
        const int TransitionSampleKey = 2;
        const int OverlaySampleKeyBase = 1000;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationRuntimeSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<ActorAnimationRuntimeSettings>();
            if (settings.Mode == ActorAnimationRuntimeMode.Cpu)
            {
                state.Dependency = new ClearGpuAnimationRequestsJob().ScheduleParallel(state.Dependency);
                return;
            }

            state.Dependency = new BuildGpuAnimationRequestsWithOverlaysJob().ScheduleParallel(state.Dependency);
            state.Dependency = new BuildGpuAnimationRequestsWithOverlaysWithoutMovementJob().ScheduleParallel(state.Dependency);
            state.Dependency = new BuildGpuAnimationRequestsWithoutOverlaysJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct ClearGpuAnimationRequestsJob : IJobEntity
        {
            void Execute(ref ActorGpuAnimationState gpuState, DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                gpuState.LayerOffset = 0;
                gpuState.LayerCount = 0;
                gpuState.SkinMeshOffset = 0;
                gpuState.SkinMeshCount = 0;
                gpuState.Valid = 0;
                requests.Clear();
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct BuildGpuAnimationRequestsWithOverlaysJob : IJobEntity
        {
            void Execute(
                in ActorAnimationState animation,
                in MorrowindMovementState movementState,
                in ActorAnimationMotionState motionState,
                ref ActorGpuAnimationState gpuState,
                DynamicBuffer<ActorAnimationOverlayState> overlays,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                BuildRequests(animation, overlays, ref gpuState, requests, IsMoving(movementState, motionState));
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        [WithNone(typeof(MorrowindMovementState))]
        partial struct BuildGpuAnimationRequestsWithOverlaysWithoutMovementJob : IJobEntity
        {
            void Execute(
                in ActorAnimationState animation,
                in ActorAnimationMotionState motionState,
                ref ActorGpuAnimationState gpuState,
                DynamicBuffer<ActorAnimationOverlayState> overlays,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                BuildRequests(animation, overlays, ref gpuState, requests, motionState.Moving != 0);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        [WithNone(typeof(ActorAnimationOverlayState))]
        partial struct BuildGpuAnimationRequestsWithoutOverlaysJob : IJobEntity
        {
            void Execute(
                in ActorAnimationState animation,
                ref ActorGpuAnimationState gpuState,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                requests.Clear();
                gpuState.LayerOffset = 0;
                gpuState.LayerCount = 0;
                gpuState.SkinMeshOffset = 0;
                gpuState.SkinMeshCount = 0;
                gpuState.Valid = 0;
                AddMainPlaybackRequests(requests, animation);
            }
        }

        static void BuildRequests(
            in ActorAnimationState animation,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ref ActorGpuAnimationState gpuState,
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            bool moving)
        {
            requests.Clear();
            gpuState.LayerOffset = 0;
            gpuState.LayerCount = 0;
            gpuState.SkinMeshOffset = 0;
            gpuState.SkinMeshCount = 0;
            gpuState.Valid = 0;

            AddMainPlaybackRequests(requests, animation);

            AddBestOverlayRequest(requests, overlays, ActorAnimationBlendMask.LowerBody, moving);
            AddBestOverlayRequest(requests, overlays, ActorAnimationBlendMask.Torso, moving);
            AddBestOverlayRequest(requests, overlays, ActorAnimationBlendMask.LeftArm, moving);
            AddBestOverlayRequest(requests, overlays, ActorAnimationBlendMask.RightArm, moving);
        }

        static void AddMainPlaybackRequests(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            in ActorAnimationState animation)
        {
            AddMainPlaybackRequestsForMask(requests, animation, ActorAnimationBlendMask.LowerBody);
            AddMainPlaybackRequestsForMask(requests, animation, ActorAnimationBlendMask.Torso);
            AddMainPlaybackRequestsForMask(requests, animation, ActorAnimationBlendMask.LeftArm);
            AddMainPlaybackRequestsForMask(requests, animation, ActorAnimationBlendMask.RightArm);
        }

        static void AddMainPlaybackRequestsForMask(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            in ActorAnimationState animation,
            ActorAnimationBlendMask mask)
        {
            if (!ActorAnimationPlaybackUtility.IsActive(animation.Playback))
                return;

            if (animation.TransitionActive != 0
                && ActorAnimationPlaybackUtility.CanSample(animation.TransitionPlayback)
                && animation.TransitionDuration > 0f)
            {
                float transitionWeight = math.saturate(animation.TransitionTime / animation.TransitionDuration);
                AddSampleRequest(
                    requests,
                    animation.TransitionPlayback,
                    TransitionSampleKey,
                    1f,
                    MainPriority,
                    mask,
                    hasPreviousLayer: false);
                AddPlaybackRequest(
                    requests,
                    animation.Playback,
                    MainSampleKey,
                    transitionWeight,
                    MainPriority,
                    mask,
                    hasPreviousLayer: true);
                return;
            }

            AddPlaybackRequest(
                requests,
                animation.Playback,
                MainSampleKey,
                1f,
                MainPriority,
                mask,
                hasPreviousLayer: false);
        }

        static void AddBestOverlayRequest(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ActorAnimationBlendMask mask,
            bool moving)
        {
            int selectedIndex = SelectBestOverlay(overlays, mask, moving);
            if (selectedIndex < 0)
                return;

            var overlay = overlays[selectedIndex];
            AddPlaybackRequest(
                requests,
                overlay.Playback,
                OverlaySampleKeyBase + selectedIndex,
                math.saturate(overlay.Weight),
                overlay.Priority,
                mask,
                hasPreviousLayer: true);
        }

        static int SelectBestOverlay(
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ActorAnimationBlendMask mask,
            bool moving)
        {
            int bestIndex = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < overlays.Length; i++)
            {
                var overlay = overlays[i];
                if (!ActorAnimationPlaybackUtility.IsActive(overlay.Playback)
                    || overlay.Weight <= 0f
                    || (overlay.Mask & mask) == 0)
                {
                    continue;
                }
                if (moving && overlay.SuppressWhenMoving != 0)
                    continue;
                if (moving && mask == ActorAnimationBlendMask.LowerBody && overlay.AllowMovingLowerBodyOverride == 0)
                    continue;

                if (overlay.Priority < bestPriority)
                    continue;

                bestPriority = overlay.Priority;
                bestIndex = i;
            }

            return bestIndex;
        }

        static bool IsMoving(in MorrowindMovementState movementState, in ActorAnimationMotionState motionState)
            => motionState.Moving != 0 || math.lengthsq(movementState.LocalMove) > 0.0001f;

        static void AddPlaybackRequest(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            in ActorAnimationPlaybackState playback,
            int sampleKey,
            float weight,
            int priority,
            ActorAnimationBlendMask mask,
            bool hasPreviousLayer)
        {
            if (!ActorAnimationPlaybackUtility.IsActive(playback) || weight <= 0f || mask == 0)
                return;

            AddSampleRequest(requests, playback, sampleKey, weight, priority, mask, hasPreviousLayer);
        }

        static void AddSampleRequest(
            DynamicBuffer<ActorGpuAnimationRequest> requests,
            in ActorAnimationPlaybackState playback,
            int sampleKey,
            float weight,
            int priority,
            ActorAnimationBlendMask mask,
            bool hasPreviousLayer)
        {
            if (!ActorAnimationPlaybackUtility.CanSample(playback) || weight <= 0f || mask == 0)
                return;

            requests.Add(new ActorGpuAnimationRequest
            {
                ClipIndex = playback.ClipIndex,
                ClipHash = playback.ClipHash,
                SampleKey = sampleKey,
                PreviousTime = playback.PreviousTime,
                Time = playback.Time,
                StartTime = playback.StartTime,
                LoopStartTime = playback.LoopStartTime,
                LoopStopTime = playback.LoopStopTime,
                StopTime = playback.StopTime,
                Weight = math.saturate(weight),
                Priority = priority,
                Mask = mask,
                HasPreviousLayer = hasPreviousLayer ? (byte)1 : (byte)0,
            });
        }
    }
}
