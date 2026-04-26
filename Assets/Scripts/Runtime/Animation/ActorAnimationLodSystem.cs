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
    [UpdateAfter(typeof(ActorAnimationGraphSystem))]
    [UpdateBefore(typeof(ActorPoseSamplingSystem))]
    [UpdateBefore(typeof(ActorGpuAnimationRequestSystem))]
    public partial struct ActorAnimationLodSystem : ISystem
    {
        static readonly ProfilerMarker s_UpdateMarker = new("VV.ActorAnimationLod.Update");
        static readonly ProfilerMarker s_ClassifyMarker = new("VV.ActorAnimationLod.Classify");

        EntityQuery _actorQuery;
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
                    ComponentType.ReadOnly<ActorRenderVisible>(),
                    ComponentType.ReadWrite<CPUAnimation>(),
                    ComponentType.ReadWrite<GPUAnimation>(),
                    ComponentType.ReadWrite<ActorAttachmentBoneAnimation>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

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
            _cpuAnimationHandle.Update(ref state);
            _gpuAnimationHandle.Update(ref state);
            _attachmentAnimationHandle.Update(ref state);

            using (s_UpdateMarker.Auto())
            using (s_ClassifyMarker.Auto())
            {
                state.Dependency = new ClassifyActorAnimationLodJob
                {
                    SupportsComputeShaders = SystemInfo.supportsComputeShaders ? (byte)1 : (byte)0,
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
            [ReadOnly] public byte SupportsComputeShaders;
            [ReadOnly] public ComponentTypeHandle<ActorRenderVisible> RenderVisibleHandle;
            public ComponentTypeHandle<CPUAnimation> CpuAnimationHandle;
            public ComponentTypeHandle<GPUAnimation> GpuAnimationHandle;
            public ComponentTypeHandle<ActorAttachmentBoneAnimation> AttachmentAnimationHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    bool targetGpuEnabled = SupportsComputeShaders != 0 && chunk.IsComponentEnabled(ref RenderVisibleHandle, i);
                    chunk.SetComponentEnabled(ref CpuAnimationHandle, i, false);
                    chunk.SetComponentEnabled(ref GpuAnimationHandle, i, targetGpuEnabled);
                    chunk.SetComponentEnabled(ref AttachmentAnimationHandle, i, false);
                }
            }
        }
    }
}
