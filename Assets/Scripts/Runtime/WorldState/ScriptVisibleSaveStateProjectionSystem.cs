using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct ScriptVisibleSaveStateProjectionSystem : ISystem
    {
        static readonly ProfilerCounterValue<int> k_CandidateCount = new(ProfilerCategory.Scripts, "VV.Overlay.ProjectCandidateCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_HitCount = new(ProfilerCategory.Scripts, "VV.Overlay.ProjectHitCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_MissCount = new(ProfilerCategory.Scripts, "VV.Overlay.ProjectMissCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_StructuralCount = new(ProfilerCategory.Scripts, "VV.Overlay.ProjectStructuralCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_StateCount = new(ProfilerCategory.Scripts, "VV.Overlay.StateCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_ScriptInstanceCount = new(ProfilerCategory.Scripts, "VV.Overlay.ScriptInstanceCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_ActorInventoryCount = new(ProfilerCategory.Scripts, "VV.Overlay.ActorInventoryItemCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_ContainerItemCount = new(ProfilerCategory.Scripts, "VV.Overlay.ContainerItemCount", ProfilerMarkerDataUnit.Count);

        EntityQuery _pendingQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _pendingQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.Exclude<PlacedRefOverlayProjectionApplied>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate(_pendingQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (!ScriptVisibleSaveStateUtility.TryPrepareProjection(
                    systemState.EntityManager,
                    out Entity runtimeEntity,
                    out var contentBlob,
                    out var overlayIndex,
                    out string error))
            {
                throw new InvalidOperationException(error);
            }

            if (runtimeEntity == Entity.Null)
                return;

            ref RuntimeContentBlob content = ref contentBlob.Value;
            var stats = default(ScriptVisibleSaveStateUtility.ProjectionStats);
            var structuralEcb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (identity, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>>()
                         .WithAll<LogicalRefTag>()
                         .WithNone<PlacedRefOverlayProjectionApplied>()
                         .WithEntityAccess())
            {
                if (!ScriptVisibleSaveStateUtility.TryProjectLiveRef(
                        systemState.EntityManager,
                        ref structuralEcb,
                        runtimeEntity,
                        entity,
                        identity.ValueRO.Value,
                        ref content,
                        overlayIndex,
                        ref stats,
                        out error))
                {
                    structuralEcb.Dispose();
                    throw new InvalidOperationException(error);
                }
            }

            structuralEcb.Playback(systemState.EntityManager);
            structuralEcb.Dispose();
            ScriptVisibleSaveStateUtility.ResolveGlobalScriptTargetsForProjection(systemState.EntityManager);

            k_CandidateCount.Value = stats.CandidateCount;
            k_HitCount.Value = stats.OverlayHitCount;
            k_MissCount.Value = stats.OverlayMissCount;
            k_StructuralCount.Value = stats.ProjectionMarkerStructuralCount;
            k_StateCount.Value = SystemAPI.GetBuffer<PlacedRefOverlayState>(runtimeEntity).Length;
            k_ScriptInstanceCount.Value = SystemAPI.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity).Length;
            k_ActorInventoryCount.Value = SystemAPI.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity).Length;
            k_ContainerItemCount.Value = SystemAPI.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity).Length;
        }
    }
}
