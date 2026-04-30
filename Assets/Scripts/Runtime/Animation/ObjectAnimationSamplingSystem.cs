using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ObjectAnimationPlaybackSystem))]
    public partial struct ObjectAnimationSamplingSystem : ISystem
    {
        ComponentLookup<ObjectAnimationState> _stateLookup;

        public void OnCreate(ref SystemState state)
        {
            _stateLookup = state.GetComponentLookup<ObjectAnimationState>(isReadOnly: true);
            state.RequireForUpdate<ObjectAnimationBlobCatalog>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalogRef = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            _stateLookup.Update(ref state);
            state.Dependency = new SampleObjectAnimationTransformsJob
            {
                Catalog = catalogRef,
                States = _stateLookup,
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new SampleObjectAnimationVisibilityJob
            {
                Catalog = catalogRef,
                States = _stateLookup,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct SampleObjectAnimationTransformsJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ObjectAnimationCatalogBlob> Catalog;
            [ReadOnly] public ComponentLookup<ObjectAnimationState> States;

            void Execute(Entity entity, in ObjectAnimationNode node, ref LocalTransform transform)
            {
                if (entity == node.Root)
                    return;

                if (!TryGetClip(node, out var clip, out var time))
                    return;

                float3 position = node.BindPosition;
                quaternion rotation = SafeNormalize(node.BindRotation);
                float scale = node.BindScale <= 0f ? 1f : node.BindScale;
                float3 axisRotation = default;
                int axisFlags = 0;
                int axisOrder = 0;

                ref var catalog = ref Catalog.Value;
                int trackEnd = math.min(catalog.Tracks.Length, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < trackEnd; trackIndex++)
                {
                    var track = catalog.Tracks[trackIndex];
                    if (track.TargetNodeIndex != node.NodeIndex || track.KeyCount <= 0 || track.FirstKeyIndex < 0)
                        continue;

                    float trackTime = MapTrackTime(time, track);
                    switch (track.Kind)
                    {
                        case ActorAnimationTrackKind.Translation:
                            position = ActorAnimationSpaceConversion.SourceTranslationToUnity(SampleValue(ref catalog, track, trackTime).xyz);
                            break;
                        case ActorAnimationTrackKind.Rotation:
                            rotation = SampleSourceRotation(ref catalog, track, trackTime);
                            break;
                        case ActorAnimationTrackKind.Scale:
                            scale = math.max(0.0001f, SampleValue(ref catalog, track, trackTime).x);
                            break;
                        case ActorAnimationTrackKind.XRotation:
                            axisRotation.x = SampleValue(ref catalog, track, trackTime).x;
                            axisFlags |= 1;
                            axisOrder = track.AxisOrder;
                            break;
                        case ActorAnimationTrackKind.YRotation:
                            axisRotation.y = SampleValue(ref catalog, track, trackTime).x;
                            axisFlags |= 2;
                            axisOrder = track.AxisOrder;
                            break;
                        case ActorAnimationTrackKind.ZRotation:
                            axisRotation.z = SampleValue(ref catalog, track, trackTime).x;
                            axisFlags |= 4;
                            axisOrder = track.AxisOrder;
                            break;
                    }
                }

                if (axisFlags != 0)
                    rotation = ComposeSourceXyzRotation(axisRotation, axisOrder);

                transform.Position = position;
                transform.Rotation = rotation;
                transform.Scale = scale;
            }

            bool TryGetClip(in ObjectAnimationNode node, out ObjectAnimationClipBlob clip, out float time)
            {
                clip = default;
                time = 0f;
                if (!Catalog.IsCreated || !States.HasComponent(node.Root))
                    return false;

                var state = States[node.Root];
                ref var catalog = ref Catalog.Value;
                if ((uint)state.ModelPrefabIndex >= (uint)catalog.Models.Length)
                    return false;

                var model = catalog.Models[state.ModelPrefabIndex];
                if (model.Enabled == 0 || model.ClipCount <= 0)
                    return false;

                int clipIndex = model.FirstClipIndex + math.clamp(state.ClipIndex, 0, model.ClipCount - 1);
                if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                    return false;

                clip = catalog.Clips[clipIndex];
                time = state.CurrentTime;
                return clip.FirstTrackIndex >= 0 && clip.TrackCount > 0;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ModelPrefabRenderLeaf))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        partial struct SampleObjectAnimationVisibilityJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ObjectAnimationCatalogBlob> Catalog;
            [ReadOnly] public ComponentLookup<ObjectAnimationState> States;

            void Execute(in ObjectAnimationNode node, EnabledRefRW<MaterialMeshInfo> materialEnabled)
            {
                materialEnabled.ValueRW = TryGetClip(node, out var state, out var clip)
                    && state.Active != 0
                    && IsNodeVisible(node, clip, state.CurrentTime);
            }

            bool TryGetClip(in ObjectAnimationNode node, out ObjectAnimationState state, out ObjectAnimationClipBlob clip)
            {
                state = default;
                clip = default;
                if (!Catalog.IsCreated || !States.HasComponent(node.Root))
                    return false;

                state = States[node.Root];
                ref var catalog = ref Catalog.Value;
                if ((uint)state.ModelPrefabIndex >= (uint)catalog.Models.Length)
                    return false;

                var model = catalog.Models[state.ModelPrefabIndex];
                if (model.Enabled == 0 || model.ClipCount <= 0)
                    return false;

                int clipIndex = model.FirstClipIndex + math.clamp(state.ClipIndex, 0, model.ClipCount - 1);
                if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                    return false;

                clip = catalog.Clips[clipIndex];
                return true;
            }

            bool IsNodeVisible(in ObjectAnimationNode node, ObjectAnimationClipBlob clip, float time)
            {
                ref var catalog = ref Catalog.Value;
                int modelIndex = node.ModelPrefabIndex;
                if ((uint)modelIndex >= (uint)catalog.Models.Length)
                    return false;

                var model = catalog.Models[modelIndex];
                int current = node.NodeIndex;
                int guard = 0;
                while ((uint)current < (uint)model.NodeCount && guard++ < model.NodeCount)
                {
                    if (!SampleNodeVisibility(ref catalog, clip, current, time))
                        return false;

                    int globalNodeIndex = model.FirstNodeIndex + current;
                    if ((uint)globalNodeIndex >= (uint)catalog.Nodes.Length)
                        break;

                    current = catalog.Nodes[globalNodeIndex].ParentIndex;
                }

                return true;
            }

            static bool SampleNodeVisibility(ref ObjectAnimationCatalogBlob catalog, ObjectAnimationClipBlob clip, int nodeIndex, float time)
            {
                if (clip.FirstTrackIndex < 0 || clip.TrackCount <= 0)
                    return true;

                int trackEnd = math.min(catalog.Tracks.Length, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < trackEnd; trackIndex++)
                {
                    var track = catalog.Tracks[trackIndex];
                    if (track.TargetNodeIndex != nodeIndex || track.Kind != ActorAnimationTrackKind.Visibility || track.KeyCount <= 0)
                        continue;

                    float value = SampleValue(ref catalog, track, MapTrackTime(time, track)).x;
                    if (value <= 0.5f)
                        return false;
                }

                return true;
            }
        }

        static float MapTrackTime(float layerTime, ObjectAnimationTrackBlob track)
        {
            float frequency = track.Frequency == 0f ? 1f : track.Frequency;
            float time = layerTime * frequency + track.Phase;
            if (track.TimeStop <= track.TimeStart)
                return time;

            if (time >= track.TimeStart && time <= track.TimeStop)
                return time;

            const ushort ExtrapolationMask = 0x6;
            const ushort ExtrapolationReverse = 0x2;
            const ushort ExtrapolationConstant = 0x4;
            ushort extrapolation = (ushort)(track.ControllerFlags & ExtrapolationMask);
            if (extrapolation == ExtrapolationReverse)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                float cycleFloor = math.floor(cycles);
                float remainder = (cycles - cycleFloor) * duration;
                return ((int)math.abs(cycleFloor) & 1) == 0
                    ? track.TimeStart + remainder
                    : track.TimeStop - remainder;
            }

            if (extrapolation != ExtrapolationConstant)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                return track.TimeStart + (cycles - math.floor(cycles)) * duration;
            }

            return time < track.TimeStart ? track.TimeStart : track.TimeStop;
        }

        static float4 SampleValue(ref ObjectAnimationCatalogBlob catalog, ObjectAnimationTrackBlob track, float time)
        {
            int first = track.FirstKeyIndex;
            int count = track.KeyCount;
            if (count <= 0 || first < 0 || first >= catalog.Keys.Length)
                return default;

            int last = math.min(catalog.Keys.Length - 1, first + count - 1);
            if (first >= last || time <= catalog.Keys[first].Time)
                return catalog.Keys[first].Value;
            if (time >= catalog.Keys[last].Time)
                return catalog.Keys[last].Value;

            int right = first + 1;
            while (right <= last && catalog.Keys[right].Time < time)
                right++;

            int left = math.max(first, right - 1);
            var a = catalog.Keys[left];
            var b = catalog.Keys[right];
            float span = math.max(0.00001f, b.Time - a.Time);
            float t = math.saturate((time - a.Time) / span);

            return track.Interpolation switch
            {
                ActorAnimationInterpolation.Constant => a.Value,
                ActorAnimationInterpolation.Quadratic => Hermite(a, b, t, span),
                _ => math.lerp(a.Value, b.Value, t),
            };
        }

        static quaternion SampleSourceRotation(ref ObjectAnimationCatalogBlob catalog, ObjectAnimationTrackBlob track, float time)
        {
            int first = track.FirstKeyIndex;
            int count = track.KeyCount;
            if (count <= 0 || first < 0 || first >= catalog.Keys.Length)
                return quaternion.identity;

            int last = math.min(catalog.Keys.Length - 1, first + count - 1);
            if (first >= last || time <= catalog.Keys[first].Time)
                return KeyRotation(catalog.Keys[first]);
            if (time >= catalog.Keys[last].Time)
                return KeyRotation(catalog.Keys[last]);

            int right = first + 1;
            while (right <= last && catalog.Keys[right].Time < time)
                right++;

            int left = math.max(first, right - 1);
            var a = catalog.Keys[left];
            var b = catalog.Keys[right];
            if (track.Interpolation == ActorAnimationInterpolation.Constant)
                return KeyRotation(a);

            float span = math.max(0.00001f, b.Time - a.Time);
            float t = math.saturate((time - a.Time) / span);
            return SafeNormalize(math.slerp(KeyRotation(a), KeyRotation(b), t));
        }

        static quaternion KeyRotation(ActorAnimationKeyBlob key)
            => ActorAnimationSpaceConversion.SourceQuaternionToUnity(new quaternion(key.Value.x, key.Value.y, key.Value.z, key.Value.w));

        static float4 Hermite(ActorAnimationKeyBlob a, ActorAnimationKeyBlob b, float t, float span)
        {
            float4 p0 = a.Value;
            float4 p1 = b.Value;
            float4 m0 = a.OutTangent * span;
            float4 m1 = b.InTangent * span;
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0
                   + (t3 - 2f * t2 + t) * m0
                   + (-2f * t3 + 3f * t2) * p1
                   + (t3 - t2) * m1;
        }

        static quaternion ComposeSourceXyzRotation(float3 angles, int axisOrder)
        {
            quaternion x = quaternion.AxisAngle(new float3(1f, 0f, 0f), angles.x);
            quaternion y = quaternion.AxisAngle(new float3(0f, 1f, 0f), angles.y);
            quaternion z = quaternion.AxisAngle(new float3(0f, 0f, 1f), angles.z);

            quaternion raw = axisOrder switch
            {
                1 => SafeNormalize(math.mul(math.mul(x, z), y)),
                2 => SafeNormalize(math.mul(math.mul(y, z), x)),
                3 => SafeNormalize(math.mul(math.mul(y, x), z)),
                4 => SafeNormalize(math.mul(math.mul(z, x), y)),
                5 => SafeNormalize(math.mul(math.mul(z, y), x)),
                _ => SafeNormalize(math.mul(math.mul(x, y), z)),
            };
            return ActorAnimationSpaceConversion.SourceQuaternionToUnity(raw);
        }

        static quaternion SafeNormalize(quaternion value)
        {
            return math.lengthsq(value.value) > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;
        }
    }
}
