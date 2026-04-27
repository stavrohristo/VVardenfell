using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Bootstrap
{

    static partial class DirectActorPreviewBootstrap
    {
        static void ComputePreviewInfluences(
            ActorSkinMeshDef mesh,
            ActorSkinWeightDef[] skinWeights,
            int vertexCount,
            out int[,] boneIndices,
            out float[,] weights)
        {
            boneIndices = new int[vertexCount, MaxPreviewSkinInfluences];
            weights = new float[vertexCount, MaxPreviewSkinInfluences];
            for (int v = 0; v < vertexCount; v++)
                for (int i = 0; i < MaxPreviewSkinInfluences; i++)
                    boneIndices[v, i] = -1;

            int firstWeight = mesh.FirstWeightIndex;
            int weightCount = mesh.WeightCount;
            if (firstWeight >= 0 && weightCount > 0)
            {
                int end = Math.Min(skinWeights.Length, firstWeight + weightCount);
                for (int i = firstWeight; i < end; i++)
                {
                    var weight = skinWeights[i];
                    if (weight.VertexIndex >= vertexCount || weight.Weight <= 0f)
                        continue;

                    InsertPreviewInfluence(boneIndices, weights, weight.VertexIndex, weight.BoneIndex, weight.Weight);
                }
            }

            for (int v = 0; v < vertexCount; v++)
                NormalizePreviewInfluences(boneIndices, weights, v);
        }


        static void InsertPreviewInfluence(int[,] boneIndices, float[,] weights, int vertex, int boneIndex, float weight)
        {
            int replace = -1;
            float minWeight = weight;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
            {
                if (weights[vertex, i] <= 0f)
                {
                    replace = i;
                    break;
                }

                if (weights[vertex, i] < minWeight)
                {
                    minWeight = weights[vertex, i];
                    replace = i;
                }
            }

            if (replace < 0)
                return;

            boneIndices[vertex, replace] = boneIndex;
            weights[vertex, replace] = weight;
        }


        static void NormalizePreviewInfluences(int[,] boneIndices, float[,] weights, int vertex)
        {
            float sum = 0f;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
                sum += Mathf.Max(0f, weights[vertex, i]);

            if (sum <= 0.000001f)
            {
                boneIndices[vertex, 0] = 0;
                weights[vertex, 0] = 1f;
                return;
            }

            float inv = 1f / sum;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
            {
                weights[vertex, i] = Mathf.Max(0f, weights[vertex, i]) * inv;
                if (weights[vertex, i] <= 0f)
                    boneIndices[vertex, i] = -1;
            }
        }


        static Matrix4x4 ReadPackedMatrix(float[] values, int start, Matrix4x4 fallback)
        {
            if (values == null || start < 0 || start + 15 >= values.Length)
                return fallback;

            var matrix = Matrix4x4.identity;
            matrix.m00 = values[start + 0];
            matrix.m01 = values[start + 1];
            matrix.m02 = values[start + 2];
            matrix.m03 = values[start + 3];
            matrix.m10 = values[start + 4];
            matrix.m11 = values[start + 5];
            matrix.m12 = values[start + 6];
            matrix.m13 = values[start + 7];
            matrix.m20 = values[start + 8];
            matrix.m21 = values[start + 9];
            matrix.m22 = values[start + 10];
            matrix.m23 = values[start + 11];
            matrix.m30 = values[start + 12];
            matrix.m31 = values[start + 13];
            matrix.m32 = values[start + 14];
            matrix.m33 = values[start + 15];
            return matrix;
        }


        static Matrix4x4 SourceAffineToUnity(Matrix4x4 source)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = source.m00;
            matrix.m01 = source.m02;
            matrix.m02 = source.m01;
            matrix.m10 = source.m20;
            matrix.m11 = source.m22;
            matrix.m12 = source.m21;
            matrix.m20 = source.m10;
            matrix.m21 = source.m12;
            matrix.m22 = source.m11;
            Vector3 translation = SourceTranslationToUnity(new Vector3(source.m03, source.m13, source.m23));
            matrix.m03 = translation.x;
            matrix.m13 = translation.y;
            matrix.m23 = translation.z;
            return matrix;
        }


        static Vector3 SourceTranslationToUnity(Vector3 source)
            => new(source.x * WorldScale.MwUnitsToMeters, source.z * WorldScale.MwUnitsToMeters, source.y * WorldScale.MwUnitsToMeters);


        static Vector3 ReadPackedVector3(float[] values, int start, Vector3 fallback)
        {
            if (values == null || start < 0 || start + 2 >= values.Length)
                return fallback;
            return new Vector3(values[start], values[start + 1], values[start + 2]);
        }


        static Vector2 ReadPackedVector2(float[] values, int start)
        {
            if (values == null || start < 0 || start + 1 >= values.Length)
                return Vector2.zero;
            return new Vector2(values[start], values[start + 1]);
        }


        static void AppendManualPreviewBatch(List<ActorProceduralDrawBatch> batches, int drawIndex, int indexCount)
        {
            if (batches.Count > 0)
            {
                var last = batches[batches.Count - 1];
                if (last.IndexCount == indexCount && last.DrawBase + last.DrawCount == drawIndex)
                {
                    last.DrawCount++;
                    batches[batches.Count - 1] = last;
                    return;
                }
            }

            batches.Add(new ActorProceduralDrawBatch
            {
                BucketIndex = 0,
                MaterialIndex = 0,
                DrawBase = drawIndex,
                DrawCount = 1,
                IndexCount = indexCount,
            });
        }


        static ActorProceduralMatrixGpu ToProceduralGpuMatrix(Matrix4x4 matrix)
        {
            return new ActorProceduralMatrixGpu
            {
                Row0 = new float4(matrix.m00, matrix.m01, matrix.m02, matrix.m03),
                Row1 = new float4(matrix.m10, matrix.m11, matrix.m12, matrix.m13),
                Row2 = new float4(matrix.m20, matrix.m21, matrix.m22, matrix.m23),
            };
        }


        static void ApplyNpcLeftBoneMirroring(Dictionary<string, Transform> skeletonNodes)
        {
            if (skeletonNodes == null)
                return;

            ApplyMirrorScale(skeletonNodes, "Left Clavicle");
            ApplyMirrorScale(skeletonNodes, "Left Upper Arm");
            ApplyMirrorScale(skeletonNodes, "Left Forearm");
            ApplyMirrorScale(skeletonNodes, "Left Wrist");
            ApplyMirrorScale(skeletonNodes, "Left Upper Leg");
            ApplyMirrorScale(skeletonNodes, "Left Knee");
            ApplyMirrorScale(skeletonNodes, "Left Ankle");
            ApplyMirrorScale(skeletonNodes, "Left Foot");
        }


        static void ApplyMirrorScale(Dictionary<string, Transform> skeletonNodes, string boneName)
        {
            if (!skeletonNodes.TryGetValue(boneName, out var bone) || bone == null)
                return;

            Vector3 scale = bone.localScale;
            bone.localScale = new Vector3(-Mathf.Abs(scale.x), scale.y, scale.z);
        }


        static Mesh BuildUnityMesh(in NifMeshBuilder.RawBuiltMesh raw, string name)
        {
            if (raw.Vertices == null || raw.Indices == null || raw.Vertices.Length == 0 || raw.Indices.Length == 0)
                return null;

            var mesh = new Mesh { name = name };
            mesh.indexFormat = raw.VertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(raw.Vertices);
            if (raw.HasNormals)
                mesh.SetNormals(raw.Normals);
            if (raw.HasUvs)
                mesh.SetUVs(0, raw.Uvs);
            mesh.SetTriangles(raw.Indices, 0);
            if (!raw.HasNormals)
                mesh.RecalculateNormals();
            mesh.bounds = raw.LocalBounds;
            return mesh;
        }


        static void ApplySourceLocal(Transform target, Matrix4x4 sourceLocalMatrix)
        {
            DecomposeReferenceStyle(sourceLocalMatrix, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }


        static void DecomposeReferenceStyle(Matrix4x4 source, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = new Vector3(source.m03, source.m23, source.m13) * WorldScale.MwUnitsToMeters;

            float sx = new Vector3(source.m00, source.m10, source.m20).magnitude;
            float sy = new Vector3(source.m01, source.m11, source.m21).magnitude;
            float sz = new Vector3(source.m02, source.m12, source.m22).magnitude;
            float uniformScale = (sx + sy + sz) / 3f;
            if (uniformScale <= 0.000001f)
                uniformScale = 1f;
            scale = new Vector3(uniformScale, uniformScale, uniformScale);

            float invScale = 1f / uniformScale;
            float m00 = source.m00 * invScale;
            float m02 = source.m01 * invScale;
            float m01 = source.m02 * invScale;
            float m20 = source.m10 * invScale;
            float m22 = source.m11 * invScale;
            float m21 = source.m12 * invScale;
            float m10 = source.m20 * invScale;
            float m12 = source.m21 * invScale;
            float m11 = source.m22 * invScale;

            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = 0.5f * s,
                    x = (m21 - m12) * recip,
                    y = (m02 - m20) * recip,
                    z = (m10 - m01) * recip,
                };
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m21 - m12) * recip,
                    x = 0.5f * s,
                    y = (m01 + m10) * recip,
                    z = (m02 + m20) * recip,
                };
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m02 - m20) * recip,
                    x = (m01 + m10) * recip,
                    y = 0.5f * s,
                    z = (m12 + m21) * recip,
                };
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m10 - m01) * recip,
                    x = (m02 + m20) * recip,
                    y = (m12 + m21) * recip,
                    z = 0.5f * s,
                };
            }

            float rotationLengthSq = rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            if (rotationLengthSq > 0.000001f)
                rotation = rotation.normalized;
            else
                rotation = Quaternion.identity;
        }


        static void CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "PreviewGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, PreviewGroundY - 0.02f, 0f);
            ground.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
        }


        static void LayoutPreviewRootsFromRenderBounds(Transform actorRoot, Transform gpuActorRoot, Transform bakedActorRoot)
        {
            float spacing = PreviewActorSeparation;
            if (TryGetRendererBounds(actorRoot, out var bounds))
            {
                float width = Mathf.Max(bounds.size.x, 0.1f);
                float padding = Mathf.Max(PreviewActorBoundsPadding, width * 0.15f);
                spacing = Mathf.Max(PreviewActorSeparation, width + padding);
            }

            if (actorRoot != null)
                actorRoot.localPosition = new Vector3(-spacing, PreviewGroundY, 0f);
            if (gpuActorRoot != null)
                gpuActorRoot.localPosition = new Vector3(0f, PreviewGroundY, 0f);
            if (bakedActorRoot != null)
                bakedActorRoot.localPosition = new Vector3(spacing, PreviewGroundY, 0f);
        }


        static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }


        static PreviewBoundsTarget CreateBoundsTarget(Transform parent, Transform actorRoot, Transform gpuActorRoot, Transform bakedActorRoot)
        {
            if (!TryGetRendererBounds(actorRoot, out var combined))
            {
                var fallback = new GameObject("PreviewBoundsTarget").transform;
                fallback.SetParent(parent, false);
                fallback.localPosition = new Vector3(0f, 1.2f, 0f);
                return new PreviewBoundsTarget
                {
                    Target = fallback,
                    Center = fallback.position,
                    Size = Vector3.one,
                };
            }

            Bounds directBounds = combined;
            Vector3 actorPosition = actorRoot != null ? actorRoot.position : Vector3.zero;
            EncapsulateCopyBounds(ref combined, directBounds, actorPosition, gpuActorRoot);
            EncapsulateCopyBounds(ref combined, directBounds, actorPosition, bakedActorRoot);

            var target = new GameObject("PreviewBoundsTarget").transform;
            target.SetParent(parent, false);
            target.position = combined.center;
            return new PreviewBoundsTarget
            {
                Target = target,
                Center = combined.center,
                Size = combined.size,
            };
        }


        static void EncapsulateCopyBounds(ref Bounds combined, Bounds sourceBounds, Vector3 actorPosition, Transform copyRoot)
        {
            if (copyRoot == null)
                return;

            Bounds copyBounds = sourceBounds;
            copyBounds.center += copyRoot.position - actorPosition;
            combined.Encapsulate(copyBounds);
        }


        static void CreateOrAttachCamera(Transform parent, PreviewBoundsTarget boundsTarget, Dictionary<string, Transform> skeletonNodes)
        {
            Transform focus = boundsTarget.Target;
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Head", out focus);
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Neck", out focus);
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Spine2", out focus);
            if (focus == null)
                focus = parent;

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            GameObject go;
            if (camera != null)
            {
                go = camera.gameObject;
            }
            else
            {
                go = new GameObject("PreviewCamera");
                go.tag = "MainCamera";
                go.transform.SetParent(parent, false);
                camera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 4096f;
            camera.fieldOfView = 50f;

            var orbit = go.GetComponent<DirectActorPreviewOrbitCamera>();
            if (orbit == null)
                orbit = go.AddComponent<DirectActorPreviewOrbitCamera>();
            orbit.Initialize(focus, boundsTarget.Size);
        }


        struct PreviewBoundsTarget
        {
            public Transform Target;
            public Vector3 Center;
            public Vector3 Size;
        }


        sealed class DirectActorGpuPreviewLeaf
        {
            public readonly string ModelPath;
            public readonly ModelPrefabSourceNode Node;
            public readonly Transform Transform;
            public readonly Transform[] Bones;

            public DirectActorGpuPreviewLeaf(string modelPath, ModelPrefabSourceNode node, Transform transform, Transform[] bones)
            {
                ModelPath = modelPath ?? string.Empty;
                Node = node;
                Transform = transform;
                Bones = bones;
            }
        }


        static bool SourceHasSkinnedRenderLeaves(ModelPrefabSource source)
        {
            if (source?.Nodes == null)
                return false;

            for (int i = 0; i < source.Nodes.Length; i++)
            {
                var node = source.Nodes[i];
                if (node?.Kind == ModelPrefabNodeKind.RenderLeaf
                    && node.SkinBoneNames != null
                    && node.SkinBoneNames.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }


        static bool ShouldIncludeNodeForPart(
            ModelPrefabSource source,
            ModelPrefabSourceNode node,
            string[] meshFilters,
            bool filterSkinnedLeaves)
        {
            if (node == null || node.Kind != ModelPrefabNodeKind.RenderLeaf)
                return false;

            if (!filterSkinnedLeaves)
                return true;

            if (node.SkinBoneNames == null || node.SkinBoneNames.Length == 0)
                return false;

            if (meshFilters == null || meshFilters.Length == 0)
                return true;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (MatchesMeshFilter(source, node, meshFilters[i]))
                    return true;
            }

            return false;
        }


        static bool MatchesMeshFilter(ModelPrefabSource source, ModelPrefabSourceNode node, string meshFilter)
        {
            if (node == null)
                return false;

            if (MatchesMeshFilter(node.Name, meshFilter))
                return true;

            int current = node.ParentIndex;
            int guard = 0;
            while (source?.Nodes != null && (uint)current < (uint)source.Nodes.Length && guard++ < source.Nodes.Length)
            {
                var parent = source.Nodes[current];
                if (MatchesMeshFilter(parent?.Name, meshFilter))
                    return true;

                current = parent?.ParentIndex ?? -1;
            }

            return false;
        }


        static bool MatchesMeshFilter(string nodeName, string meshFilter)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(meshFilter))
                return true;

            if (nodeName.StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase))
                return true;

            return nodeName.StartsWith("tri ", StringComparison.OrdinalIgnoreCase)
                   && nodeName.Substring(4).StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase);
        }


        static string NormalizePartName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().Replace('_', ' ').Replace('-', ' ');
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            return normalized.ToLowerInvariant();
        }


        static string[] BuildMeshFilters(ActorVisualPartReference reference)
            => ActorVisualMappingPolicy.GetMeshFilters(reference);


        static string ResolveNpcSkeletonModel(bool firstPerson, bool female, bool beast)
            => ActorVisualContentRules.ResolveNpcSkeletonModel(firstPerson, female, beast);


        static string ResolveCreaturePreviewModelPath(ActorDef actor, DirectActorPreviewAssetResolver assets)
        {
            string model = NormalizeModelPath(actor.Model);
            if (string.IsNullOrEmpty(model) || assets == null)
                return model;

            string corrected = BuildPrefixedCreatureModelPath(model);
            string correctedKf = BuildCompanionKfPath(corrected);
            return assets.HasAsset(correctedKf) && assets.HasAsset(corrected)
                ? corrected
                : model;
        }


        static string BuildPrefixedCreatureModelPath(string modelPath)
            => ActorVisualContentRules.BuildPrefixedActorModelPath(modelPath);


        static string BuildCompanionKfPath(string modelPath)
            => ActorVisualContentRules.BuildCompanionKfPath(modelPath);


        static string NormalizeModelPath(string modelPath)
            => ActorVisualContentRules.NormalizeModelPath(modelPath);


        static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorVisualPartReference partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
            => ActorVisualContentRules.TryResolveNpcRaceBodyPart(bodyParts, raceId, partReference, female, firstPerson, out result);


        static int ResolveNpcRaceBodyPartScore(
            bool firstPerson,
            bool female,
            bool isFirstPersonArmPart,
            bool partFirstPerson,
            bool partFemale)
            => ActorVisualContentRules.ResolveNpcRaceBodyPartScore(
                firstPerson,
                female,
                isFirstPersonArmPart,
                partFirstPerson,
                partFemale);


        static PreviewRecords LoadPreviewRecords(string installPath, string actorId)
        {
            var races = new Dictionary<string, RaceDef>(StringComparer.OrdinalIgnoreCase);
            var bodyPartsById = new Dictionary<string, ActorBodyPartDef>(StringComparer.OrdinalIgnoreCase);
            var actorsById = new Dictionary<string, ActorDef>(StringComparer.OrdinalIgnoreCase);

            string[] sources = InstalledContentSources.ResolveGameplayRecordSources(installPath);
            if (sources == null || sources.Length == 0)
                throw new InvalidOperationException("No gameplay record sources were found.");

            for (int i = 0; i < sources.Length; i++)
            {
                using var esm = new EsmReader(sources[i]);
                while (esm.ReadRecordHeader(out var record))
                {
                    switch (record.Tag)
                    {
                        case var tag when tag == EsmFourCC.Make('R', 'A', 'C', 'E'):
                            ParseRaceRecord(esm, races);
                            break;
                        case var tag when tag == EsmFourCC.Make('B', 'O', 'D', 'Y'):
                            ParseBodyPartRecord(esm, bodyPartsById);
                            break;
                        case var tag when tag == EsmFourCC.Make('N', 'P', 'C', '_'):
                            ParseNpcRecord(esm, actorsById);
                            break;
                        case var tag when tag == EsmFourCC.Make('C', 'R', 'E', 'A'):
                            ParseCreatureRecord(esm, actorsById);
                            break;
                        default:
                            esm.SkipRecord();
                            break;
                    }
                }
            }

            if (!actorsById.TryGetValue(actorId ?? string.Empty, out var actor))
            {
                foreach (var pair in actorsById)
                {
                    actor = pair.Value;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                throw new InvalidOperationException($"Actor '{actorId}' was not found in the installed content sources.");

            RaceDef race = default;
            if (actor.Kind == ActorDefKind.Npc && !races.TryGetValue(actor.RaceId ?? string.Empty, out race))
                throw new InvalidOperationException($"Race '{actor.RaceId}' for actor '{actor.Id}' was not found.");

            return new PreviewRecords
            {
                Actor = actor,
                Race = race,
                BodyParts = Values(bodyPartsById),
                BodyPartsById = bodyPartsById,
                IsFemale = (actor.Flags & 0x1u) != 0,
                IsBeast = (race.Flags & 0x02) != 0,
            };
        }


        static ActorBodyPartDef[] Values(Dictionary<string, ActorBodyPartDef> map)
        {
            var values = new ActorBodyPartDef[map.Count];
            int index = 0;
            foreach (var pair in map)
                values[index++] = pair.Value;
            return values;
        }


        static void ParseBodyPartRecord(EsmReader esm, Dictionary<string, ActorBodyPartDef> target)
        {
            var typed = new ActorBodyPartDef();
            bool hasBydt = false;
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        typed.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        typed.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        typed.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('B', 'Y', 'D', 'T'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                        {
                            typed.Part = (ActorBodyPartMeshPart)bytes[0];
                            typed.Vampire = bytes[1];
                            typed.Female = (byte)((bytes[2] & 0x01) != 0 ? 1 : 0);
                            typed.NotPlayable = (byte)((bytes[2] & 0x02) != 0 ? 1 : 0);
                            typed.Type = (ActorBodyPartMeshType)bytes[3];
                            hasBydt = true;
                        }
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(typed.Id))
                return;

            if (deleted)
            {
                target.Remove(typed.Id);
                return;
            }

            typed.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('B', 'O', 'D', 'Y'), typed.Id);
            typed.FirstPerson = (byte)(typed.Id.EndsWith("1st", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            if (hasBydt)
                target[typed.Id] = typed;
        }


        }
    }
