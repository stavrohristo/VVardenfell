#if VVARDENFELL_ACTOR_GPU_ANIMATION
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationControllerSystem))]
    [UpdateBefore(typeof(ActorPoseSamplingSystem))]
    [UpdateBefore(typeof(ActorGpuAnimationRequestSystem))]
    public partial struct ActorAnimationLodSystem : ISystem
    {
        static readonly ProfilerMarker s_UpdateMarker = new("VV.ActorAnimationLod.Update");
        static readonly ProfilerMarker s_ClassifyMarker = new("VV.ActorAnimationLod.Classify");

        EntityQuery _actorQuery;
        ComponentTypeHandle<ActorRenderVisible> _renderVisibleHandle;
        ComponentTypeHandle<ActorShadowCasterVisible> _shadowCasterVisibleHandle;
        ComponentTypeHandle<ActorGpuAnimationState> _gpuStateHandle;
        ComponentTypeHandle<CPUAnimation> _cpuAnimationHandle;
        ComponentTypeHandle<GPUAnimation> _gpuAnimationHandle;
        ComponentTypeHandle<ActorAttachmentBoneAnimation> _attachmentAnimationHandle;

        public void OnCreate(ref SystemState state)
        {
            _actorQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorRenderVisible>(),
                    ComponentType.ReadOnly<ActorShadowCasterVisible>(),
                    ComponentType.ReadOnly<ActorGpuAnimationState>(),
                    ComponentType.ReadWrite<CPUAnimation>(),
                    ComponentType.ReadWrite<GPUAnimation>(),
                    ComponentType.ReadWrite<ActorAttachmentBoneAnimation>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _renderVisibleHandle = state.GetComponentTypeHandle<ActorRenderVisible>(isReadOnly: true);
            _shadowCasterVisibleHandle = state.GetComponentTypeHandle<ActorShadowCasterVisible>(isReadOnly: true);
            _gpuStateHandle = state.GetComponentTypeHandle<ActorGpuAnimationState>(isReadOnly: true);
            _cpuAnimationHandle = state.GetComponentTypeHandle<CPUAnimation>(isReadOnly: false);
            _gpuAnimationHandle = state.GetComponentTypeHandle<GPUAnimation>(isReadOnly: false);
            _attachmentAnimationHandle = state.GetComponentTypeHandle<ActorAttachmentBoneAnimation>(isReadOnly: false);

            if (!SystemAPI.HasSingleton<ActorAnimationLodSettings>())
            {
                Entity settingsEntity = state.EntityManager.CreateEntity();
                state.EntityManager.SetName(settingsEntity, "VVardenfell.ActorAnimationLodSettings");
                state.EntityManager.AddComponentData(settingsEntity, new ActorAnimationLodSettings
                {
                    ValidationEnabled = 0,
                    ValidationActorIndex = 0,
                });
            }

            state.RequireForUpdate(_actorQuery);
            state.RequireForUpdate<ActorAnimationLodSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _renderVisibleHandle.Update(ref state);
            _shadowCasterVisibleHandle.Update(ref state);
            _gpuStateHandle.Update(ref state);
            _cpuAnimationHandle.Update(ref state);
            _gpuAnimationHandle.Update(ref state);
            _attachmentAnimationHandle.Update(ref state);

            using (s_UpdateMarker.Auto())
            using (s_ClassifyMarker.Auto())
            {
                state.Dependency = new ClassifyActorAnimationLodJob
                {
                    SupportsComputeShaders = SystemInfo.supportsComputeShaders ? (byte)1 : (byte)0,
                    ValidationEnabled = SystemAPI.GetSingleton<ActorAnimationLodSettings>().ValidationEnabled,
                    RenderVisibleHandle = _renderVisibleHandle,
                    ShadowCasterVisibleHandle = _shadowCasterVisibleHandle,
                    GpuStateHandle = _gpuStateHandle,
                    CpuAnimationHandle = _cpuAnimationHandle,
                    GpuAnimationHandle = _gpuAnimationHandle,
                    AttachmentAnimationHandle = _attachmentAnimationHandle,
                }.ScheduleParallel(_actorQuery, state.Dependency);
            }
        }

        [BurstCompile]
        struct ClassifyActorAnimationLodJob : IJobChunk
        {
            [ReadOnly] public byte SupportsComputeShaders;
            [ReadOnly] public byte ValidationEnabled;
            [ReadOnly] public ComponentTypeHandle<ActorRenderVisible> RenderVisibleHandle;
            [ReadOnly] public ComponentTypeHandle<ActorShadowCasterVisible> ShadowCasterVisibleHandle;
            [ReadOnly] public ComponentTypeHandle<ActorGpuAnimationState> GpuStateHandle;
            public ComponentTypeHandle<CPUAnimation> CpuAnimationHandle;
            public ComponentTypeHandle<GPUAnimation> GpuAnimationHandle;
            public ComponentTypeHandle<ActorAttachmentBoneAnimation> AttachmentAnimationHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                int count = chunk.Count;
                var gpuStates = chunk.GetNativeArray(ref GpuStateHandle);
                for (int i = 0; i < count; i++)
                {
                    bool renderVisible = chunk.IsComponentEnabled(ref RenderVisibleHandle, i);
                    bool shadowVisible = chunk.IsComponentEnabled(ref ShadowCasterVisibleHandle, i);
                    bool needsPose = renderVisible || shadowVisible;
                    var gpuState = gpuStates[i];
                    bool gpuStateReady = gpuState.LayerCount > 0
                                         && gpuState.BoneMatrixCount > 0
                                         && gpuState.BoneMatrixOffset >= 0;
                    bool targetGpuEnabled = renderVisible && SupportsComputeShaders != 0;
                    bool targetCpuEnabled = needsPose && (!targetGpuEnabled || ValidationEnabled != 0 || !gpuStateReady);
                    chunk.SetComponentEnabled(ref CpuAnimationHandle, i, targetCpuEnabled);
                    chunk.SetComponentEnabled(ref GpuAnimationHandle, i, targetGpuEnabled);
                    chunk.SetComponentEnabled(ref AttachmentAnimationHandle, i, false);
                }
            }
        }
    }
}
#endif
