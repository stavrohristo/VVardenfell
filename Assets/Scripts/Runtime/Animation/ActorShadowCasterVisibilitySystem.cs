using Unity.Collections;
using Unity.Entities;
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

            var camera = SystemAPI.GetSingleton<MainCameraSingleton>().Camera;
            if (camera == null)
                return;

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
            using var entities = _query.ToEntityArray(Allocator.Temp);
            using var bounds = _query.ToComponentDataArray<ActorLocalBounds>(Allocator.Temp);
            using var localToWorlds = _query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
                EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(entities[i], false);

            if (maxShadowCasters <= 0 || maxDistance <= 0f)
                return;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entity == firstPersonVisual)
                    continue;

                float4x4 ltw = localToWorlds[i].Value;
                ActorLocalBounds localBounds = bounds[i];
                float3 center = math.transform(ltw, localBounds.Center);
                float3 extents = TransformExtents(ltw, localBounds.Extents);
                float radius = math.length(extents);
                float allowedDistance = maxDistance + radius;
                float distanceSq = math.distancesq(center, cameraPosition);
                if (distanceSq > allowedDistance * allowedDistance)
                    continue;

                InsertCandidate(new ShadowCandidate
                {
                    Entity = entity == thirdPersonVisual ? thirdPersonVisual : entity,
                    DistanceSq = distanceSq,
                }, maxShadowCasters);
            }

            for (int i = 0; i < _candidates.Length; i++)
                EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(_candidates[i].Entity, true);
        }

        static float3 TransformExtents(float4x4 matrix, float3 extents)
        {
            return math.abs(matrix.c0.xyz) * extents.x
                + math.abs(matrix.c1.xyz) * extents.y
                + math.abs(matrix.c2.xyz) * extents.z;
        }

        void InsertCandidate(ShadowCandidate candidate, int maxCount)
        {
            int count = _candidates.Length;
            if (count >= maxCount && candidate.DistanceSq >= _candidates[count - 1].DistanceSq)
                return;

            if (count < maxCount)
            {
                _candidates.Add(candidate);
                count++;
            }

            int index = count - 1;
            while (index > 0 && candidate.DistanceSq < _candidates[index - 1].DistanceSq)
            {
                _candidates[index] = _candidates[index - 1];
                index--;
            }

            _candidates[index] = candidate;
        }
    }
}
