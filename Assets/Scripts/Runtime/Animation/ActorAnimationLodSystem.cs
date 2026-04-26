using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationGraphSystem))]
    [UpdateBefore(typeof(ActorPoseSamplingSystem))]
    [UpdateBefore(typeof(ActorGpuAnimationRequestSystem))]
    public partial struct ActorAnimationLodSystem : ISystem
    {
        static readonly ProfilerMarker s_UpdateMarker = new("VV.ActorAnimationLod.Update");
        static readonly ProfilerMarker s_ClassifyMarker = new("VV.ActorAnimationLod.Classify");

        EntityQuery _actorQuery;
        ComponentTypeHandle<LocalToWorld> _localToWorldHandle;
        ComponentTypeHandle<ActorGpuAnimationCpuFallback> _fallbackHandle;
        ComponentTypeHandle<ActorRenderVisible> _renderVisibleHandle;
        ComponentTypeHandle<CPUAnimation> _cpuAnimationHandle;
        ComponentTypeHandle<GPUAnimation> _gpuAnimationHandle;
        ComponentTypeHandle<ActorAttachmentBoneAnimation> _attachmentAnimationHandle;

        public void OnCreate(ref SystemState state)
        {
            _actorQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<ActorGpuAnimationCpuFallback>(),
                    ComponentType.ReadOnly<ActorRenderVisible>(),
                    ComponentType.ReadWrite<CPUAnimation>(),
                    ComponentType.ReadWrite<GPUAnimation>(),
                    ComponentType.ReadWrite<ActorAttachmentBoneAnimation>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _localToWorldHandle = state.GetComponentTypeHandle<LocalToWorld>(isReadOnly: true);
            _fallbackHandle = state.GetComponentTypeHandle<ActorGpuAnimationCpuFallback>(isReadOnly: true);
            _renderVisibleHandle = state.GetComponentTypeHandle<ActorRenderVisible>(isReadOnly: true);
            _cpuAnimationHandle = state.GetComponentTypeHandle<CPUAnimation>(isReadOnly: false);
            _gpuAnimationHandle = state.GetComponentTypeHandle<GPUAnimation>(isReadOnly: false);
            _attachmentAnimationHandle = state.GetComponentTypeHandle<ActorAttachmentBoneAnimation>(isReadOnly: false);

            if (!SystemAPI.HasSingleton<ActorAnimationLodSettings>())
            {
                Entity settingsEntity = state.EntityManager.CreateEntity();
                state.EntityManager.SetName(settingsEntity, "VVardenfell.ActorAnimationLodSettings");
                state.EntityManager.AddComponentData(settingsEntity, new ActorAnimationLodSettings
                {
                    CpuNearDistance = 15f,
                    OverrideMode = ActorAnimationLodOverrideMode.None,
                    ValidationEnabled = 0,
                    ValidationActorIndex = 0,
                });
            }

            state.RequireForUpdate(_actorQuery);
            state.RequireForUpdate<ActorAnimationLodSettings>();
            state.RequireForUpdate<MainCameraSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<ActorAnimationLodSettings>();
            var cam = SystemAPI.GetSingleton<MainCameraSingleton>();
            Camera camera = cam.Camera;
            if (camera == null)
                return;

            float3 cameraPosition = camera.transform.position;
            float cpuNearDistanceSq = math.max(0f, settings.CpuNearDistance * settings.CpuNearDistance);

            _localToWorldHandle.Update(ref state);
            _fallbackHandle.Update(ref state);
            _renderVisibleHandle.Update(ref state);
            _cpuAnimationHandle.Update(ref state);
            _gpuAnimationHandle.Update(ref state);
            _attachmentAnimationHandle.Update(ref state);

            using (s_UpdateMarker.Auto())
            using (s_ClassifyMarker.Auto())
            {
                state.Dependency = new ClassifyActorAnimationLodJob
                {
                    CameraPosition = cameraPosition,
                    CpuNearDistanceSq = cpuNearDistanceSq,
                    OverrideMode = settings.OverrideMode,
                    SupportsComputeShaders = SystemInfo.supportsComputeShaders ? (byte)1 : (byte)0,
                    LocalToWorldHandle = _localToWorldHandle,
                    FallbackHandle = _fallbackHandle,
                    RenderVisibleHandle = _renderVisibleHandle,
                    CpuAnimationHandle = _cpuAnimationHandle,
                    GpuAnimationHandle = _gpuAnimationHandle,
                    AttachmentAnimationHandle = _attachmentAnimationHandle,
                }.ScheduleParallel(_actorQuery, state.Dependency);
            }
        }

        [BurstCompile]
        struct ClassifyActorAnimationLodJob : IJobChunk
        {
            [ReadOnly] public float3 CameraPosition;
            [ReadOnly] public float CpuNearDistanceSq;
            [ReadOnly] public ActorAnimationLodOverrideMode OverrideMode;
            [ReadOnly] public byte SupportsComputeShaders;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;
            [ReadOnly] public ComponentTypeHandle<ActorGpuAnimationCpuFallback> FallbackHandle;
            [ReadOnly] public ComponentTypeHandle<ActorRenderVisible> RenderVisibleHandle;
            public ComponentTypeHandle<CPUAnimation> CpuAnimationHandle;
            public ComponentTypeHandle<GPUAnimation> GpuAnimationHandle;
            public ComponentTypeHandle<ActorAttachmentBoneAnimation> AttachmentAnimationHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var localToWorlds = chunk.GetNativeArray(ref LocalToWorldHandle);
                var fallbacks = chunk.GetNativeArray(ref FallbackHandle);

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    bool targetCpuEnabled;
                    bool targetGpuEnabled;
                    bool targetAttachmentEnabled;

                    if (!chunk.IsComponentEnabled(ref RenderVisibleHandle, i))
                    {
                        targetCpuEnabled = false;
                        targetGpuEnabled = false;
                        targetAttachmentEnabled = false;
                    }
                    else if (OverrideMode == ActorAnimationLodOverrideMode.ForceCpu)
                    {
                        targetCpuEnabled = true;
                        targetGpuEnabled = false;
                        targetAttachmentEnabled = false;
                    }
                    else if (OverrideMode == ActorAnimationLodOverrideMode.ForceGpu)
                    {
                        targetGpuEnabled = SupportsComputeShaders != 0 && fallbacks[i].RequiresFullPoseSampling == 0;
                        targetCpuEnabled = !targetGpuEnabled;
                        targetAttachmentEnabled = false;
                    }
                    else
                    {
                        bool supportsGpuPath = SupportsComputeShaders != 0 && fallbacks[i].RequiresFullPoseSampling == 0;
                        bool requiresCpuConsumers = fallbacks[i].RequiresAttachments != 0 || fallbacks[i].RequiresRootMotion != 0;
                        bool isNear = math.distancesq(localToWorlds[i].Position, CameraPosition) <= CpuNearDistanceSq;

                        targetCpuEnabled = !supportsGpuPath;
                        targetGpuEnabled = supportsGpuPath;
                        targetAttachmentEnabled = supportsGpuPath && isNear && requiresCpuConsumers;
                    }

                    chunk.SetComponentEnabled(ref CpuAnimationHandle, i, targetCpuEnabled);
                    chunk.SetComponentEnabled(ref GpuAnimationHandle, i, targetGpuEnabled);
                    chunk.SetComponentEnabled(ref AttachmentAnimationHandle, i, targetAttachmentEnabled);
                }
            }
        }
    }
}
