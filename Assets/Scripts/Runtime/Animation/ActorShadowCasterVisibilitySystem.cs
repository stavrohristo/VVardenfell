using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial class ActorShadowCasterVisibilitySystem : SystemBase
    {
        static readonly ProfilerMarker k_Update = new("VV.ActorShadowCasterVisibility.Update");

        struct ShadowCandidate
        {
            public Entity Entity;
            public float DistanceSq;
        }

        EntityQuery _query;
        NativeList<ShadowCandidate> _candidates;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorShadowCasterVisible>(),
                    ComponentType.ReadOnly<ActorLocalBounds>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _candidates = new NativeList<ShadowCandidate>(Allocator.Persistent);
            RequireForUpdate(_query);
            RequireForUpdate<MainCameraSingleton>();
        }

        protected override void OnDestroy()
        {
            if (_candidates.IsCreated)
                _candidates.Dispose();
        }

        protected override void OnUpdate()
        {
            using var _ = k_Update.Auto();

            var camera = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            float3 cameraPosition = camera.transform.position;
            float shadowDistance = math.max(0f, WorldResources.ActorShadowCasterDistance);
            float padding = math.max(0f, WorldResources.ActorShadowCasterPadding);
            float maxDistance = shadowDistance + padding;
            int maxShadowCasters = math.max(0, WorldResources.MaxActorShadowCasters);

            Entity firstPersonVisual = Entity.Null;
            Entity thirdPersonVisual = Entity.Null;
            if (SystemAPI.HasSingleton<LocalPlayerPresentationState>())
            {
                var state = SystemAPI.GetSingleton<LocalPlayerPresentationState>();
                firstPersonVisual = state.FirstPersonVisual;
                thirdPersonVisual = state.ThirdPersonVisual;
            }

            _candidates.Clear();

            EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(_query, false);

            if (maxShadowCasters <= 0 || maxDistance <= 0f)
                return;

            int queryCount = _query.CalculateEntityCount();
            if (_candidates.Capacity < queryCount)
                _candidates.Capacity = queryCount;

            var gatherJob = new GatherShadowCandidatesJob
            {
                EntityHandle = GetEntityTypeHandle(),
                BoundsHandle = GetComponentTypeHandle<ActorLocalBounds>(isReadOnly: true),
                LocalToWorldHandle = GetComponentTypeHandle<LocalToWorld>(isReadOnly: true),
                CameraPosition = cameraPosition,
                MaxDistance = maxDistance,
                FirstPersonVisual = firstPersonVisual,
                ThirdPersonVisual = thirdPersonVisual,
                Candidates = _candidates.AsParallelWriter(),
            };
            Dependency = gatherJob.ScheduleParallel(_query, Dependency);
            Dependency.Complete();
            Dependency = default;

            _candidates.Sort(new ShadowCandidateDistanceComparer());

            int limit = math.min(maxShadowCasters, _candidates.Length);
            for (int i = 0; i < limit; i++)
                EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(_candidates[i].Entity, true);
        }

        struct ShadowCandidateDistanceComparer : IComparer<ShadowCandidate>
        {
            public int Compare(ShadowCandidate x, ShadowCandidate y)
            {
                int distance = x.DistanceSq.CompareTo(y.DistanceSq);
                return distance != 0 ? distance : x.Entity.Index.CompareTo(y.Entity.Index);
            }
        }

        [BurstCompile]
        struct GatherShadowCandidatesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActorLocalBounds> BoundsHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;
            public float3 CameraPosition;
            public float MaxDistance;
            public Entity FirstPersonVisual;
            public Entity ThirdPersonVisual;
            public NativeList<ShadowCandidate>.ParallelWriter Candidates;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var bounds = chunk.GetNativeArray(ref BoundsHandle);
                var localToWorlds = chunk.GetNativeArray(ref LocalToWorldHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    Entity entity = entities[i];
                    if (entity == FirstPersonVisual)
                        continue;

                    float4x4 ltw = localToWorlds[i].Value;
                    ActorLocalBounds localBounds = bounds[i];
                    float3 center = math.transform(ltw, localBounds.Center);
                    float3 extents = TransformExtents(ltw, localBounds.Extents);
                    float radius = math.length(extents);
                    float allowedDistance = MaxDistance + radius;
                    float distanceSq = math.distancesq(center, CameraPosition);
                    if (distanceSq > allowedDistance * allowedDistance)
                        continue;

                    Candidates.AddNoResize(new ShadowCandidate
                    {
                        Entity = entity == ThirdPersonVisual ? ThirdPersonVisual : entity,
                        DistanceSq = distanceSq,
                    });
                }
            }
        }

        static float3 TransformExtents(float4x4 matrix, float3 extents)
        {
            return math.abs(matrix.c0.xyz) * extents.x
                + math.abs(matrix.c1.xyz) * extents.y
                + math.abs(matrix.c2.xyz) * extents.z;
        }
    }
}
