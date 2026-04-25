using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Nif
{
    public static class NifActorAnimationExtractor
    {
        public static ActorSkeletonDef ExtractSkeleton(NifFile nif)
        {
            var bones = new List<ActorSkeletonBoneDef>();
            foreach (int root in nif.Roots)
            {
                if (Resolve<NiAVObject>(nif, root) is NiAVObject av)
                    CollectBones(nif, av, -1, bones);
            }

            return new ActorSkeletonDef
            {
                ModelPath = nif.Path,
                AccumulationBoneIndex = FindAccumulationBone(bones),
                Bones = bones.ToArray(),
            };
        }

        public static ActorAnimationClipDef[] ExtractClips(
            NifFile nif,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys,
            List<ActorAnimationTextKeyDef> textKeys)
        {
            var clips = new List<ActorAnimationClipDef>();
            for (int i = 0; i < nif.Roots.Length; i++)
            {
                if (Resolve<NiSequenceStreamHelper>(nif, nif.Roots[i]) is NiSequenceStreamHelper helper)
                    AddSequenceStreamHelperClip(nif, helper, clips, tracks, keys, textKeys);
            }

            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is not NiSequence sequence)
                    continue;

                int firstTrack = tracks.Count;
                for (int c = 0; c < sequence.ControlledBlocks.Length; c++)
                    AddControlledBlockTracks(nif, sequence.ControlledBlocks[c], tracks, keys);

                int firstTextKey = textKeys.Count;
                if (Resolve<NiTextKeyExtraData>(nif, sequence.TextKeys) is { Keys: { } sourceKeys })
                {
                    for (int k = 0; k < sourceKeys.Length; k++)
                    {
                        textKeys.Add(new ActorAnimationTextKeyDef
                        {
                            Time = sourceKeys[k].Time,
                            Text = sourceKeys[k].Text,
                        });
                    }
                }

                clips.Add(new ActorAnimationClipDef
                {
                    SourcePath = nif.Path,
                    Name = sequence.Name ?? string.Empty,
                    AccumRootName = sequence.AccumRootName ?? string.Empty,
                    Duration = EstimateDuration(textKeys, firstTextKey),
                    FirstTrackIndex = firstTrack,
                    TrackCount = tracks.Count - firstTrack,
                    FirstTextKeyIndex = firstTextKey,
                    TextKeyCount = textKeys.Count - firstTextKey,
                });
            }

            if (clips.Count == 0)
                AddInlineClip(nif, clips, tracks, keys, textKeys);

            return clips.ToArray();
        }

        public static ActorSkinMeshDef[] ExtractSkinMeshes(
            NifFile nif,
            ActorSkeletonDef skeleton,
            int skeletonIndex,
            List<ActorSkinWeightDef> weights)
        {
            var skinMeshes = new List<ActorSkinMeshDef>();
            var boneLookup = BuildBoneLookup(skeleton);

            for (int i = 0; i < nif.Records.Length; i++)
            {
                if (nif.Records[i] is not NiGeometry geometry || !TryResolveRenderableGeometry(nif, geometry, out _))
                    continue;

                if (Resolve<NiSkinInstance>(nif, geometry.Skin) is not NiSkinInstance skinInstance
                    || Resolve<NiSkinData>(nif, skinInstance.Data) is not NiSkinData skinData)
                {
                    skinMeshes.Add(new ActorSkinMeshDef
                    {
                        ModelPath = nif.Path,
                        NodeName = geometry.Name ?? string.Empty,
                        MeshIndex = skinMeshes.Count,
                        SkeletonIndex = skeletonIndex,
                        IsRigid = 1,
                        FirstWeightIndex = -1,
                        WeightCount = 0,
                        BoneIndices = new[] { -1 },
                        BoneNames = new[] { string.Empty },
                        BindPoseMatrices = PackMatrix(Matrix4x4.identity),
                        GeometryToSkeletonMatrix = BuildGeometryToSkeletonMatrix(skeleton, geometry.Name),
                    });
                    continue;
                }

                int firstWeight = weights.Count;
                var sourceBones = skinData.Bones ?? Array.Empty<NiSkinData.BoneInfo>();
                var boneIndices = new int[sourceBones.Length];
                var boneNames = new string[sourceBones.Length];
                for (int b = 0; b < sourceBones.Length; b++)
                {
                    boneNames[b] = ResolveSkinBoneName(nif, skinInstance.Bones, b);
                    int skeletonBoneIndex = ResolveSkeletonBoneIndex(nif, skinInstance.Bones, b, boneLookup);
                    boneIndices[b] = skeletonBoneIndex;
                    var sourceWeights = sourceBones[b]?.Weights ?? Array.Empty<NiSkinData.VertexWeight>();
                    for (int w = 0; w < sourceWeights.Length; w++)
                    {
                        weights.Add(new ActorSkinWeightDef
                        {
                            VertexIndex = sourceWeights[w].Vertex,
                            BoneIndex = (ushort)b,
                            Weight = sourceWeights[w].Weight,
                        });
                    }
                }

                skinMeshes.Add(new ActorSkinMeshDef
                {
                    ModelPath = nif.Path,
                    NodeName = geometry.Name ?? string.Empty,
                    MeshIndex = skinMeshes.Count,
                    SkeletonIndex = skeletonIndex,
                    FirstWeightIndex = firstWeight,
                    WeightCount = weights.Count - firstWeight,
                    BoneIndices = boneIndices,
                    BoneNames = boneNames,
                    SkinRootName = ResolveSkinRootName(nif, skinInstance.Root),
                    BindPoseMatrices = BuildBindPoseMatrices(sourceBones),
                    GeometryToSkeletonMatrix = PackMatrix(ToUnityMatrix(skinData.Transform)),
                });
            }

            return skinMeshes.ToArray();
        }

        static void CollectBones(NifFile nif, NiAVObject obj, int parentIndex, List<ActorSkeletonBoneDef> bones)
        {
            int index = bones.Count;
            ConvertLocalTransform(obj, out Vector3 position, out Quaternion rotation, out float scale);
            bones.Add(new ActorSkeletonBoneDef
            {
                Name = obj.Name ?? string.Empty,
                ParentIndex = parentIndex,
                PosX = position.x,
                PosY = position.y,
                PosZ = position.z,
                RotX = rotation.x,
                RotY = rotation.y,
                RotZ = rotation.z,
                RotW = rotation.w,
                Scale = scale,
            });

            if (obj is not NiNode node || node.Children == null)
                return;

            for (int i = 0; i < node.Children.Length; i++)
            {
                if (Resolve<NiAVObject>(nif, node.Children[i]) is NiAVObject child)
                    CollectBones(nif, child, index, bones);
            }
        }

        static void AddControlledBlockTracks(
            NifFile nif,
            NifControlledBlock block,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            AddControllerChainTracks(nif, block.TargetName, block.Controller, tracks, keys);
        }

        static void AddSequenceStreamHelperClip(
            NifFile nif,
            NiSequenceStreamHelper helper,
            List<ActorAnimationClipDef> clips,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys,
            List<ActorAnimationTextKeyDef> textKeys)
        {
            int firstTrack = tracks.Count;
            int firstTextKey = textKeys.Count;

            var extras = ResolveExtraChain(nif, helper.ExtraData);
            if (extras.Count > 0 && extras[0] is NiTextKeyExtraData { Keys: { } sourceKeys })
            {
                for (int k = 0; k < sourceKeys.Length; k++)
                {
                    textKeys.Add(new ActorAnimationTextKeyDef
                    {
                        Time = sourceKeys[k].Time,
                        Text = sourceKeys[k].Text,
                    });
                }
            }

            int controllerIndex = helper.Controller;
            for (int i = 1; i < extras.Count && controllerIndex >= 0; i++)
            {
                if (extras[i] is NiStringExtraData stringExtra)
                    AddSingleControllerTracks(nif, stringExtra.Data, controllerIndex, tracks, keys);

                controllerIndex = Resolve<NiTimeController>(nif, controllerIndex)?.NextController ?? -1;
            }

            if (tracks.Count == firstTrack && textKeys.Count == firstTextKey)
                return;

            clips.Add(new ActorAnimationClipDef
            {
                SourcePath = nif.Path,
                Name = Path.GetFileNameWithoutExtension(nif.Path) ?? string.Empty,
                AccumRootName = string.Empty,
                Duration = EstimateDuration(tracks, firstTrack, keys, textKeys, firstTextKey),
                FirstTrackIndex = firstTrack,
                TrackCount = tracks.Count - firstTrack,
                FirstTextKeyIndex = firstTextKey,
                TextKeyCount = textKeys.Count - firstTextKey,
            });
        }

        static void AddInlineClip(
            NifFile nif,
            List<ActorAnimationClipDef> clips,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys,
            List<ActorAnimationTextKeyDef> textKeys)
        {
            int firstTrack = tracks.Count;
            int firstTextKey = textKeys.Count;
            for (int i = 0; i < nif.Roots.Length; i++)
            {
                if (Resolve<NiAVObject>(nif, nif.Roots[i]) is NiAVObject root)
                    AddInlineNode(nif, root, tracks, keys, textKeys);
            }

            if (tracks.Count == firstTrack && textKeys.Count == firstTextKey)
                return;

            clips.Add(new ActorAnimationClipDef
            {
                SourcePath = nif.Path,
                Name = Path.GetFileNameWithoutExtension(nif.Path) ?? string.Empty,
                AccumRootName = string.Empty,
                Duration = EstimateDuration(tracks, firstTrack, keys, textKeys, firstTextKey),
                FirstTrackIndex = firstTrack,
                TrackCount = tracks.Count - firstTrack,
                FirstTextKeyIndex = firstTextKey,
                TextKeyCount = textKeys.Count - firstTextKey,
            });
        }

        static void AddInlineNode(
            NifFile nif,
            NiAVObject node,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys,
            List<ActorAnimationTextKeyDef> textKeys)
        {
            AddControllerChainTracks(nif, node.Name, node.Controller, tracks, keys);
            AddTextKeys(nif, node.ExtraData, textKeys);

            if (node is not NiNode niNode || niNode.Children == null)
                return;

            for (int i = 0; i < niNode.Children.Length; i++)
            {
                if (Resolve<NiAVObject>(nif, niNode.Children[i]) is NiAVObject child)
                    AddInlineNode(nif, child, tracks, keys, textKeys);
            }
        }

        static void AddControllerChainTracks(
            NifFile nif,
            string targetName,
            int controllerIndex,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            for (int link = controllerIndex; link >= 0;)
            {
                if (Resolve<NiTimeController>(nif, link) is not NiTimeController controller)
                    return;

                if (controller is NiKeyframeController keyframeController)
                    AddKeyframeTracks(nif, targetName, keyframeController, tracks, keys);
                else if (controller is NiVisController visController)
                    AddVisibilityTrack(nif, targetName, visController, tracks, keys);

                link = controller.NextController;
            }
        }

        static void AddSingleControllerTracks(
            NifFile nif,
            string targetName,
            int controllerIndex,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            if (Resolve<NiTimeController>(nif, controllerIndex) is not NiTimeController controller)
                return;

            if (controller is NiKeyframeController keyframeController)
                AddKeyframeTracks(nif, targetName, keyframeController, tracks, keys);
            else if (controller is NiVisController visController)
                AddVisibilityTrack(nif, targetName, visController, tracks, keys);
        }

        static void AddKeyframeTracks(
            NifFile nif,
            string targetName,
            NiKeyframeController controller,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            if (Resolve<NiKeyframeData>(nif, controller.Data) is not NiKeyframeData data)
                return;

            AddTrack(targetName, ActorAnimationTrackKind.Rotation, data.Rotations, (int)data.AxisOrder, controller, tracks, keys);
            AddTrack(targetName, ActorAnimationTrackKind.XRotation, data.XRotations, (int)data.AxisOrder, controller, tracks, keys);
            AddTrack(targetName, ActorAnimationTrackKind.YRotation, data.YRotations, (int)data.AxisOrder, controller, tracks, keys);
            AddTrack(targetName, ActorAnimationTrackKind.ZRotation, data.ZRotations, (int)data.AxisOrder, controller, tracks, keys);
            AddTrack(targetName, ActorAnimationTrackKind.Translation, data.Translations, 0, controller, tracks, keys);
            AddTrack(targetName, ActorAnimationTrackKind.Scale, data.Scales, 0, controller, tracks, keys);
        }

        static void AddVisibilityTrack(
            NifFile nif,
            string targetName,
            NiVisController controller,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            if (Resolve<NiVisData>(nif, controller.Data) is not { Keys: { Length: > 0 } sourceKeys })
                return;

            int firstKey = keys.Count;
            for (int i = 0; i < sourceKeys.Length; i++)
            {
                keys.Add(new ActorAnimationKeyDef
                {
                    Time = sourceKeys[i].Time,
                    X = sourceKeys[i].Value ? 1f : 0f,
                });
            }

            tracks.Add(new ActorAnimationTrackDef
            {
                TargetName = targetName ?? string.Empty,
                Kind = ActorAnimationTrackKind.Visibility,
                Interpolation = ActorAnimationInterpolation.Constant,
                ControllerFlags = controller.Flags,
                Frequency = controller.Frequency,
                Phase = controller.Phase,
                TimeStart = controller.TimeStart,
                TimeStop = controller.TimeStop,
                FirstKeyIndex = firstKey,
                KeyCount = sourceKeys.Length,
            });
        }

        static void AddTrack(
            string targetName,
            ActorAnimationTrackKind kind,
            NifKeyGroup group,
            int axisOrder,
            NiTimeController controller,
            List<ActorAnimationTrackDef> tracks,
            List<ActorAnimationKeyDef> keys)
        {
            if (group?.Keys == null || group.Keys.Length == 0)
                return;

            int firstKey = keys.Count;
            for (int i = 0; i < group.Keys.Length; i++)
                keys.Add(ToCacheKey(group.Keys[i]));

            tracks.Add(new ActorAnimationTrackDef
            {
                TargetName = targetName ?? string.Empty,
                Kind = kind,
                Interpolation = (ActorAnimationInterpolation)(byte)group.InterpolationType,
                AxisOrder = axisOrder,
                ControllerFlags = controller.Flags,
                Frequency = controller.Frequency,
                Phase = controller.Phase,
                TimeStart = controller.TimeStart,
                TimeStop = controller.TimeStop,
                FirstKeyIndex = firstKey,
                KeyCount = group.Keys.Length,
            });
        }

        static ActorAnimationKeyDef ToCacheKey(NifAnimationKey key)
            => new()
            {
                Time = key.Time,
                X = key.Value.x,
                Y = key.Value.y,
                Z = key.Value.z,
                W = key.Value.w,
                InX = key.InTan.x,
                InY = key.InTan.y,
                InZ = key.InTan.z,
                InW = key.InTan.w,
                OutX = key.OutTan.x,
                OutY = key.OutTan.y,
                OutZ = key.OutTan.z,
                OutW = key.OutTan.w,
            };

        static float EstimateDuration(List<ActorAnimationTextKeyDef> keys, int start)
        {
            float duration = 0f;
            for (int i = start; i < keys.Count; i++)
                duration = Mathf.Max(duration, keys[i].Time);
            return duration;
        }

        static float EstimateDuration(
            List<ActorAnimationTrackDef> tracks,
            int firstTrack,
            List<ActorAnimationKeyDef> keys,
            List<ActorAnimationTextKeyDef> textKeys,
            int firstTextKey)
        {
            float duration = EstimateDuration(textKeys, firstTextKey);
            for (int i = firstTrack; i < tracks.Count; i++)
            {
                var track = tracks[i];
                duration = Mathf.Max(duration, track.TimeStop);
                int end = track.FirstKeyIndex + track.KeyCount;
                for (int k = track.FirstKeyIndex; k >= 0 && k < end && k < keys.Count; k++)
                    duration = Mathf.Max(duration, keys[k].Time);
            }
            return duration;
        }

        static void AddTextKeys(NifFile nif, int extraIndex, List<ActorAnimationTextKeyDef> textKeys)
        {
            for (int link = extraIndex; link >= 0;)
            {
                if (Resolve<Extra>(nif, link) is not Extra extra)
                    return;

                if (extra is NiTextKeyExtraData { Keys: { } sourceKeys })
                {
                    for (int i = 0; i < sourceKeys.Length; i++)
                    {
                        textKeys.Add(new ActorAnimationTextKeyDef
                        {
                            Time = sourceKeys[i].Time,
                            Text = sourceKeys[i].Text,
                        });
                    }
                }

                link = extra.NextExtra;
            }
        }

        static Dictionary<string, int> BuildBoneLookup(ActorSkeletonDef skeleton)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            for (int i = 0; i < bones.Length; i++)
            {
                string name = bones[i].Name;
                if (!string.IsNullOrEmpty(name) && !lookup.ContainsKey(name))
                    lookup[name] = i;
            }
            return lookup;
        }

        static int ResolveSkeletonBoneIndex(
            NifFile nif,
            int[] skinBones,
            int skinBoneIndex,
            Dictionary<string, int> boneLookup)
        {
            if (skinBones != null
                && (uint)skinBoneIndex < (uint)skinBones.Length
                && Resolve<NiAVObject>(nif, skinBones[skinBoneIndex]) is NiAVObject bone
                && !string.IsNullOrEmpty(bone.Name)
                && boneLookup.TryGetValue(bone.Name, out int skeletonBoneIndex))
            {
                return skeletonBoneIndex;
            }

            return -1;
        }

        static string ResolveSkinBoneName(NifFile nif, int[] skinBones, int skinBoneIndex)
        {
            if (skinBones != null
                && (uint)skinBoneIndex < (uint)skinBones.Length
                && Resolve<NiAVObject>(nif, skinBones[skinBoneIndex]) is NiAVObject bone)
            {
                return bone.Name ?? string.Empty;
            }

            return string.Empty;
        }

        static string ResolveSkinRootName(NifFile nif, int skinRoot)
        {
            return Resolve<NiAVObject>(nif, skinRoot)?.Name ?? string.Empty;
        }

        static float[] BuildBindPoseMatrices(NiSkinData.BoneInfo[] bones)
        {
            bones ??= Array.Empty<NiSkinData.BoneInfo>();
            var matrices = new float[bones.Length * 16];
            for (int i = 0; i < bones.Length; i++)
            {
                Matrix4x4 m = ToUnityMatrix(bones[i].Transform);
                int o = i * 16;
                matrices[o + 0] = m.m00; matrices[o + 1] = m.m01; matrices[o + 2] = m.m02; matrices[o + 3] = m.m03;
                matrices[o + 4] = m.m10; matrices[o + 5] = m.m11; matrices[o + 6] = m.m12; matrices[o + 7] = m.m13;
                matrices[o + 8] = m.m20; matrices[o + 9] = m.m21; matrices[o + 10] = m.m22; matrices[o + 11] = m.m23;
                matrices[o + 12] = m.m30; matrices[o + 13] = m.m31; matrices[o + 14] = m.m32; matrices[o + 15] = m.m33;
            }
            return matrices;
        }

        static float[] BuildGeometryToSkeletonMatrix(ActorSkeletonDef skeleton, string nodeName)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            var bones = skeleton?.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            if (!string.IsNullOrEmpty(nodeName))
            {
                Matrix4x4[] localToRoot = BuildBindLocalToRootMatrices(bones);
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!string.Equals(bones[i].Name, nodeName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    matrix = localToRoot[i];
                    break;
                }
            }

            return PackMatrix(matrix);
        }

        static Matrix4x4[] BuildBindLocalToRootMatrices(ActorSkeletonBoneDef[] bones)
        {
            var matrices = new Matrix4x4[bones?.Length ?? 0];
            for (int i = 0; i < matrices.Length; i++)
            {
                var bone = bones[i];
                var rotation = new Quaternion(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
                float rotationLengthSq = rotation.x * rotation.x
                    + rotation.y * rotation.y
                    + rotation.z * rotation.z
                    + rotation.w * rotation.w;
                if (rotationLengthSq <= 0.000001f)
                    rotation = Quaternion.identity;
                else
                    rotation.Normalize();

                float scale = bone.Scale <= 0f ? 1f : bone.Scale;
                var local = Matrix4x4.TRS(
                    new Vector3(bone.PosX, bone.PosY, bone.PosZ),
                    rotation,
                    new Vector3(scale, scale, scale));

                matrices[i] = bone.ParentIndex >= 0 && bone.ParentIndex < i
                    ? matrices[bone.ParentIndex] * local
                    : local;
            }
            return matrices;
        }

        static List<Extra> ResolveExtraChain(NifFile nif, int firstExtra)
        {
            var extras = new List<Extra>();
            int current = firstExtra;
            int guard = 0;
            while ((uint)current < (uint)nif.Records.Length && guard++ < nif.Records.Length)
            {
                if (nif.Records[current] is not Extra extra)
                    break;

                extras.Add(extra);
                current = extra.NextExtra;
            }
            return extras;
        }

        static float[] PackMatrix(Matrix4x4 m)
        {
            return new[]
            {
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33,
            };
        }

        static Matrix4x4 ToUnityMatrix(NiSkinData.SkinTransform transform)
        {
            var source = transform.Rotation;
            float scale = transform.Scale;
            var matrix = Matrix4x4.identity;

            // Convert from Morrowind/NIF basis to Unity basis: U = A * N * A,
            // where A swaps Y/Z. This keeps identity bind transforms as identity.
            matrix.m00 = source.m00 * scale;
            matrix.m01 = source.m02 * scale;
            matrix.m02 = source.m01 * scale;
            matrix.m10 = source.m20 * scale;
            matrix.m11 = source.m22 * scale;
            matrix.m12 = source.m21 * scale;
            matrix.m20 = source.m10 * scale;
            matrix.m21 = source.m12 * scale;
            matrix.m22 = source.m11 * scale;
            matrix.m03 = transform.Translation.x * WorldScale.MwUnitsToMeters;
            matrix.m13 = transform.Translation.z * WorldScale.MwUnitsToMeters;
            matrix.m23 = transform.Translation.y * WorldScale.MwUnitsToMeters;
            return matrix;
        }

        static int FindAccumulationBone(List<ActorSkeletonBoneDef> bones)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                string name = bones[i].Name;
                if (string.Equals(name, "bip01", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "root bone", System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        static bool TryResolveRenderableGeometry(NifFile nif, NiGeometry geometry, out NiGeometryData data)
        {
            data = geometry switch
            {
                NiTriShape => Resolve<NiTriShapeData>(nif, geometry.Data),
                NiTriStrips => Resolve<NiTriStripsData>(nif, geometry.Data),
                _ => null,
            };

            return data != null && data.Vertices != null && data.NumVertices > 0;
        }

        static void ConvertLocalTransform(NiAVObject obj, out Vector3 position, out Quaternion rotation, out float scale)
        {
            position = new Vector3(obj.Translation.x, obj.Translation.z, obj.Translation.y) * WorldScale.MwUnitsToMeters;
            Vector3 up = new(obj.Rotation.m02, obj.Rotation.m22, obj.Rotation.m12);
            Vector3 forward = new(obj.Rotation.m01, obj.Rotation.m21, obj.Rotation.m11);
            rotation = up.sqrMagnitude <= 0f || forward.sqrMagnitude <= 0f
                ? Quaternion.identity
                : Quaternion.LookRotation(forward.normalized, up.normalized);
            scale = obj.Scale;
        }

        static T Resolve<T>(NifFile nif, int link) where T : NifRecord
        {
            if (link < 0 || link >= nif.Records.Length)
                return null;
            return nif.Records[link] as T;
        }
    }
}
