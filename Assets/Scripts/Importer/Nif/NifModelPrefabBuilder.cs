using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Nif
{
    public sealed class ModelPrefabSource
    {
        public string ModelPath;
        public CollisionPayload Collision;
        public ModelPrefabSourceNode[] Nodes = System.Array.Empty<ModelPrefabSourceNode>();
        public int[] ChildIndices = System.Array.Empty<int>();
    }

    public sealed class ModelPrefabSourceNode
    {
        public ModelPrefabNodeKind Kind;
        public string Name;
        public int ParentIndex = -1;
        public int SourceRecordIndex = -1;
        public int FirstChildIndex = -1;
        public int ChildCount;
        public int SelectedChildIndex = -1;
        public ushort Flags;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public float LocalScale = 1f;
        public Matrix4x4 SourceLocalMatrix = Matrix4x4.identity;
        public NifMeshBuilder.RawBuiltMesh RenderLeaf;
        public uint MaterialFlags;
        public string TexturePath;
        public string SkinRootName;
        public int SkinRootSourceRecordIndex = -1;
        public string[] SkinBoneNames = System.Array.Empty<string>();
        public BoneWeight[] SkinBoneWeights;
        public Matrix4x4[] SkinBindPoses;
    }

    public static class NifModelPrefabBuilder
    {
        sealed class BuildState
        {
            public readonly List<ModelPrefabSourceNode> Nodes = new();
            public readonly List<int> ChildIndices = new();
            public readonly List<List<int>> PendingChildren = new();
        }

        public static ModelPrefabSource Build(NifFile nif)
        {
            var state = new BuildState();
            bool hasEditorMarkers = HasRootEditorMarker(nif);
            int syntheticRoot = AddNode(state, new ModelPrefabSourceNode
            {
                Kind = ModelPrefabNodeKind.SyntheticRoot,
                Name = "root",
                ParentIndex = -1,
            });

            for (int i = 0; i < nif.Roots.Length; i++)
            {
                int rootIndex = nif.Roots[i];
                if (rootIndex < 0 || rootIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[rootIndex] is not NiAVObject av)
                    continue;

                int child = BuildNode(nif, rootIndex, av, syntheticRoot, hasEditorMarkers, state);
                if (child >= 0)
                    state.PendingChildren[syntheticRoot].Add(child);
            }

            FinalizeChildren(state);
            return new ModelPrefabSource
            {
                ModelPath = nif.Path,
                Collision = NifCollisionExtractor.TryExtract(nif, out var collision) ? collision : default,
                Nodes = state.Nodes.ToArray(),
                ChildIndices = state.ChildIndices.ToArray(),
            };
        }

        static int BuildNode(NifFile nif, int recordIndex, NiAVObject obj, int parentIndex, bool hasEditorMarkers, BuildState state)
        {
            var kind = ResolveKind(obj);
            if (kind == ModelPrefabNodeKind.RootCollision || kind == ModelPrefabNodeKind.Avoid)
            {
                int metadataNode = AddTransformNode(state, recordIndex, obj, parentIndex, kind);
                return metadataNode;
            }

            if (kind == ModelPrefabNodeKind.CollisionSwitch && (obj.Flags & 0x0020) == 0)
            {
                int switchNode = AddTransformNode(state, recordIndex, obj, parentIndex, kind);
                return switchNode;
            }

            if (obj is NiGeometry geometry
                && TryResolveRenderableGeometry(nif, geometry, out _)
                && !ShouldSkipMorrowindRenderGeometry(geometry.Name, hasEditorMarkers))
            {
                return AddRenderLeaf(nif, recordIndex, geometry, parentIndex, state);
            }

            if (obj is not NiNode node)
                return AddTransformNode(state, recordIndex, obj, parentIndex, kind);

            int nodeIndex = AddTransformNode(state, recordIndex, obj, parentIndex, kind);
            var pendingChildren = state.PendingChildren[nodeIndex];
            if (node.Children == null || node.Children.Length == 0)
                return nodeIndex;

            int selectedChild = ResolveSelectedChildIndex(node);
            for (int i = 0; i < node.Children.Length; i++)
            {
                if (!ShouldTraverseChild(kind, i, selectedChild))
                    continue;

                int childIndex = node.Children[i];
                if (childIndex < 0 || childIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[childIndex] is not NiAVObject child)
                    continue;

                int builtChild = BuildNode(nif, childIndex, child, nodeIndex, hasEditorMarkers, state);
                if (builtChild >= 0)
                    pendingChildren.Add(builtChild);
            }

            state.Nodes[nodeIndex].SelectedChildIndex = selectedChild;
            return nodeIndex;
        }

        static int AddRenderLeaf(NifFile nif, int recordIndex, NiGeometry geometry, int parentIndex, BuildState state)
        {
            if (!TryResolveRenderableGeometry(nif, geometry, out var data))
                return -1;

            FindAlpha(nif, geometry, out ushort alphaFlags, out byte alphaThreshold);
            uint materialFlags = 0;
            if ((alphaFlags & 0x0001) != 0)
                materialFlags |= CacheFormat.MatFlagAlphaBlend;
            if ((alphaFlags & 0x0200) != 0)
                materialFlags |= CacheFormat.MatFlagAlphaClip;
            materialFlags = CacheFormat.PackAlphaThreshold(materialFlags, alphaThreshold);

            var node = CreateBaseNode(geometry, recordIndex, parentIndex, ResolveKind(geometry));
            node.MaterialFlags = materialFlags;
            node.TexturePath = FindTexture(nif, geometry, out int uvSet);
            node.RenderLeaf = BuildLocalRawMesh(geometry, data, uvSet, node.TexturePath, alphaFlags, alphaThreshold);
            PopulateSkinPayload(nif, geometry, data, node);
            return AddNode(state, node);
        }

        static void PopulateSkinPayload(NifFile nif, NiGeometry geometry, NiGeometryData data, ModelPrefabSourceNode node)
        {
            if (Resolve<NiSkinInstance>(nif, geometry.Skin) is not NiSkinInstance skinInstance
                || Resolve<NiSkinData>(nif, skinInstance.Data) is not NiSkinData skinData
                || data == null
                || data.NumVertices <= 0)
            {
                return;
            }

            var sourceBones = skinData.Bones ?? System.Array.Empty<NiSkinData.BoneInfo>();
            var boneNames = new string[sourceBones.Length];
            for (int b = 0; b < boneNames.Length; b++)
                boneNames[b] = ResolveSkinBoneName(nif, skinInstance.Bones, b);

            var boneWeights = new BoneWeight[data.NumVertices];
            for (int b = 0; b < sourceBones.Length; b++)
            {
                var weights = sourceBones[b]?.Weights ?? System.Array.Empty<NiSkinData.VertexWeight>();
                for (int w = 0; w < weights.Length; w++)
                {
                    int vertex = weights[w].Vertex;
                    if ((uint)vertex >= (uint)boneWeights.Length)
                        continue;

                    AddBoneWeight(ref boneWeights[vertex], b, weights[w].Weight);
                }
            }

            NormalizeBoneWeights(boneWeights);
            node.SkinRootName = Resolve<NiAVObject>(nif, skinInstance.Root)?.Name ?? string.Empty;
            node.SkinRootSourceRecordIndex = skinInstance.Root;
            node.SkinBoneNames = boneNames;
            node.SkinBoneWeights = boneWeights;
            node.SkinBindPoses = BuildUnitySkinBindPoses(skinData);
        }

        static Matrix4x4[] BuildUnitySkinBindPoses(NiSkinData skinData)
        {
            var sourceBones = skinData.Bones ?? System.Array.Empty<NiSkinData.BoneInfo>();
            var bindPoses = new Matrix4x4[sourceBones.Length];
            Matrix4x4 skinTransform = BuildUnitySkinTransform(skinData.Transform);
            for (int i = 0; i < bindPoses.Length; i++)
                bindPoses[i] = skinTransform * BuildUnitySkinTransform(sourceBones[i].Transform);

            return bindPoses;
        }

        static Matrix4x4 BuildUnitySkinTransform(NiSkinData.SkinTransform transform)
        {
            Vector3 position = new(
                transform.Translation.x,
                transform.Translation.z,
                transform.Translation.y);
            position *= WorldScale.MwUnitsToMeters;

            Quaternion rotation = BuildUnityRotation(transform.Rotation);
            return Matrix4x4.TRS(position, rotation, Vector3.one * transform.Scale);
        }

        static Quaternion BuildUnityRotation(Matrix4x4 source)
        {
            float m00 = source.m00;
            float m02 = source.m01;
            float m01 = source.m02;
            float m20 = source.m10;
            float m22 = source.m11;
            float m21 = source.m12;
            float m10 = source.m20;
            float m12 = source.m21;
            float m11 = source.m22;

            float trace = m00 + m11 + m22;
            Quaternion rotation;
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

            float lengthSq = rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            return lengthSq > 0.000001f ? rotation.normalized : Quaternion.identity;
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

        static void AddBoneWeight(ref BoneWeight target, int boneIndex, float weight)
        {
            if (weight <= 0f)
                return;

            if (target.weight0 <= 0f)
            {
                target.boneIndex0 = boneIndex;
                target.weight0 = weight;
                return;
            }

            if (target.weight1 <= 0f)
            {
                target.boneIndex1 = boneIndex;
                target.weight1 = weight;
                return;
            }

            if (target.weight2 <= 0f)
            {
                target.boneIndex2 = boneIndex;
                target.weight2 = weight;
                return;
            }

            if (target.weight3 <= 0f)
            {
                target.boneIndex3 = boneIndex;
                target.weight3 = weight;
                return;
            }

            float weakest = target.weight0;
            int slot = 0;
            if (target.weight1 < weakest) { weakest = target.weight1; slot = 1; }
            if (target.weight2 < weakest) { weakest = target.weight2; slot = 2; }
            if (target.weight3 < weakest) { weakest = target.weight3; slot = 3; }
            if (weight <= weakest)
                return;

            switch (slot)
            {
                case 0:
                    target.boneIndex0 = boneIndex;
                    target.weight0 = weight;
                    break;
                case 1:
                    target.boneIndex1 = boneIndex;
                    target.weight1 = weight;
                    break;
                case 2:
                    target.boneIndex2 = boneIndex;
                    target.weight2 = weight;
                    break;
                default:
                    target.boneIndex3 = boneIndex;
                    target.weight3 = weight;
                    break;
            }
        }

        static void NormalizeBoneWeights(BoneWeight[] boneWeights)
        {
            if (boneWeights == null)
                return;

            for (int i = 0; i < boneWeights.Length; i++)
            {
                float sum = boneWeights[i].weight0 + boneWeights[i].weight1 + boneWeights[i].weight2 + boneWeights[i].weight3;
                if (sum <= 0.000001f)
                {
                    boneWeights[i].boneIndex0 = 0;
                    boneWeights[i].weight0 = 1f;
                    continue;
                }

                float inv = 1f / sum;
                boneWeights[i].weight0 *= inv;
                boneWeights[i].weight1 *= inv;
                boneWeights[i].weight2 *= inv;
                boneWeights[i].weight3 *= inv;
            }
        }

        static ModelPrefabSourceNode CreateBaseNode(NiAVObject obj, int recordIndex, int parentIndex, ModelPrefabNodeKind kind)
        {
            ConvertLocalTransform(obj, out Vector3 position, out Quaternion rotation, out float scale);
            return new ModelPrefabSourceNode
            {
                Kind = kind,
                Name = string.IsNullOrWhiteSpace(obj.Name) ? string.Empty : obj.Name,
                ParentIndex = parentIndex,
                SourceRecordIndex = recordIndex,
                Flags = obj.Flags,
                LocalPosition = position,
                LocalRotation = rotation,
                LocalScale = scale,
                SourceLocalMatrix = BuildSourceLocalMatrix(obj),
            };
        }

        static int AddTransformNode(BuildState state, int recordIndex, NiAVObject obj, int parentIndex, ModelPrefabNodeKind kind)
            => AddNode(state, CreateBaseNode(obj, recordIndex, parentIndex, kind));

        static int AddNode(BuildState state, ModelPrefabSourceNode node)
        {
            int index = state.Nodes.Count;
            state.Nodes.Add(node);
            state.PendingChildren.Add(new List<int>());
            return index;
        }

        static void FinalizeChildren(BuildState state)
        {
            for (int i = 0; i < state.Nodes.Count; i++)
            {
                var children = state.PendingChildren[i];
                if (children.Count == 0)
                {
                    state.Nodes[i].FirstChildIndex = -1;
                    state.Nodes[i].ChildCount = 0;
                    continue;
                }

                state.Nodes[i].FirstChildIndex = state.ChildIndices.Count;
                state.Nodes[i].ChildCount = children.Count;
                for (int c = 0; c < children.Count; c++)
                    state.ChildIndices.Add(children[c]);
            }
        }

        static bool ShouldTraverseChild(ModelPrefabNodeKind kind, int childIndex, int selectedChild)
        {
            return kind switch
            {
                ModelPrefabNodeKind.Switch => childIndex == selectedChild,
                ModelPrefabNodeKind.Lod => childIndex == selectedChild,
                ModelPrefabNodeKind.FltAnimation => childIndex == selectedChild,
                _ => true,
            };
        }

        static int ResolveSelectedChildIndex(NiNode node)
        {
            if (node.Children == null || node.Children.Length == 0)
                return -1;

            return node switch
            {
                NiLODNode => 0,
                NiFltAnimationNode => 0,
                NiSwitchNode switchNode => switchNode.InitialIndex >= node.Children.Length ? 0 : (int)switchNode.InitialIndex,
                _ => -1,
            };
        }

        static ModelPrefabNodeKind ResolveKind(NiAVObject obj)
        {
            return obj switch
            {
                NiBillboardNode => ModelPrefabNodeKind.Billboard,
                RootCollisionNode => ModelPrefabNodeKind.RootCollision,
                AvoidNode => ModelPrefabNodeKind.Avoid,
                NiLODNode => ModelPrefabNodeKind.Lod,
                NiFltAnimationNode => ModelPrefabNodeKind.FltAnimation,
                NiSwitchNode => ModelPrefabNodeKind.Switch,
                NiBSAnimationNode => ModelPrefabNodeKind.BsAnimation,
                NiBSParticleNode => ModelPrefabNodeKind.BsParticle,
                NiCollisionSwitch => ModelPrefabNodeKind.CollisionSwitch,
                NiTriShape => ModelPrefabNodeKind.RenderLeaf,
                NiTriStrips => ModelPrefabNodeKind.RenderLeaf,
                _ => ModelPrefabNodeKind.Transform,
            };
        }

        static void ConvertLocalTransform(NiAVObject obj, out Vector3 position, out Quaternion rotation, out float scale)
        {
            position = new Vector3(
                obj.Translation.x,
                obj.Translation.z,
                obj.Translation.y) * WorldScale.MwUnitsToMeters;

            Vector3 right = new(obj.Rotation.m00, obj.Rotation.m20, obj.Rotation.m10);
            Vector3 up = new(obj.Rotation.m02, obj.Rotation.m22, obj.Rotation.m12);
            Vector3 forward = new(obj.Rotation.m01, obj.Rotation.m21, obj.Rotation.m11);
            if (right.sqrMagnitude <= 0f || up.sqrMagnitude <= 0f || forward.sqrMagnitude <= 0f)
            {
                rotation = Quaternion.identity;
            }
            else
            {
                rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            }

            scale = obj.Scale;
        }

        static Matrix4x4 BuildSourceLocalMatrix(NiAVObject obj)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = obj.Rotation.m00 * obj.Scale;
            matrix.m01 = obj.Rotation.m01 * obj.Scale;
            matrix.m02 = obj.Rotation.m02 * obj.Scale;
            matrix.m10 = obj.Rotation.m10 * obj.Scale;
            matrix.m11 = obj.Rotation.m11 * obj.Scale;
            matrix.m12 = obj.Rotation.m12 * obj.Scale;
            matrix.m20 = obj.Rotation.m20 * obj.Scale;
            matrix.m21 = obj.Rotation.m21 * obj.Scale;
            matrix.m22 = obj.Rotation.m22 * obj.Scale;
            matrix.m03 = obj.Translation.x;
            matrix.m13 = obj.Translation.y;
            matrix.m23 = obj.Translation.z;
            return matrix;
        }

        static NifMeshBuilder.RawBuiltMesh BuildLocalRawMesh(
            NiGeometry geometry,
            NiGeometryData data,
            int uvSet,
            string texturePath,
            ushort alphaFlags,
            byte alphaThreshold)
        {
            int vcount = data.NumVertices;
            var verts = new Vector3[vcount];
            var normals = data.Normals != null ? new Vector3[vcount] : null;
            var uvs = ResolveUvSet(data, uvSet, vcount);

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < vcount; i++)
            {
                Vector3 pU = new(data.Vertices[i].x, data.Vertices[i].z, data.Vertices[i].y);
                pU *= WorldScale.MwUnitsToMeters;
                verts[i] = pU;
                min = Vector3.Min(min, pU);
                max = Vector3.Max(max, pU);

                if (normals != null)
                {
                    Vector3 n = data.Normals[i];
                    normals[i] = new Vector3(n.x, n.z, n.y).normalized;
                }
            }

            int[] indices = BuildTriangleIndices(geometry, data);
            if (indices.Length == 0)
                return default;

            var bounds = new Bounds((min + max) * 0.5f, max - min);
            return new NifMeshBuilder.RawBuiltMesh(
                verts,
                normals,
                uvs,
                indices,
                texturePath,
                geometry.Name ?? string.Empty,
                bounds,
                alphaFlags,
                alphaThreshold);
        }

        static void FindAlpha(NifFile nif, NiGeometry geometry, out ushort flags, out byte threshold)
        {
            flags = 0;
            threshold = 0;
            if (geometry.PropertyLinks == null)
                return;

            for (int i = 0; i < geometry.PropertyLinks.Length; i++)
            {
                int propertyIndex = geometry.PropertyLinks[i];
                if (propertyIndex < 0 || propertyIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[propertyIndex] is not NiAlphaProperty alpha)
                    continue;

                flags = alpha.Flags;
                threshold = alpha.Threshold;
                return;
            }
        }

        static string FindTexture(NifFile nif, NiGeometry geometry, out int uvSet)
        {
            uvSet = 0;
            if (geometry.PropertyLinks == null)
                return null;

            for (int i = 0; i < geometry.PropertyLinks.Length; i++)
            {
                int propertyIndex = geometry.PropertyLinks[i];
                if (propertyIndex < 0 || propertyIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[propertyIndex] is not NiTexturingProperty textureProperty
                    || textureProperty.Textures == null
                    || textureProperty.Textures.Length == 0
                    || textureProperty.Textures[0] == null
                    || !textureProperty.Textures[0].Enabled)
                    continue;

                var source = Resolve<NiSourceTexture>(nif, textureProperty.Textures[0].SourceTexture);
                if (source != null && source.External)
                {
                    uvSet = (int)textureProperty.Textures[0].UVSet;
                    return source.FileName;
                }
            }

            return null;
        }

        static bool TryResolveRenderableGeometry(NifFile nif, NiGeometry geometry, out NiGeometryData data)
        {
            data = null;
            if (geometry == null)
                return false;
            if ((geometry.Flags & 0x0001) != 0)
                return false;
            if (IsOpenMwCreatureHelperGeometry(geometry.Name))
                return false;

            data = geometry switch
            {
                NiTriShape => Resolve<NiTriShapeData>(nif, geometry.Data),
                NiTriStrips => Resolve<NiTriStripsData>(nif, geometry.Data),
                _ => null,
            };

            return data != null
                && data.Vertices != null
                && data.Normals != null
                && data.NumVertices > 0
                && data.Normals.Length == data.NumVertices
                && BuildTriangleIndices(geometry, data).Length > 0;
        }

        static bool IsOpenMwCreatureHelperGeometry(string name)
            => !string.IsNullOrWhiteSpace(name)
               && name.TrimStart().StartsWith("tri bip", System.StringComparison.OrdinalIgnoreCase);

        static bool ShouldSkipMorrowindRenderGeometry(string name, bool hasEditorMarkers)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string trimmed = name.TrimStart();
            return (hasEditorMarkers && trimmed.StartsWith("tri editormarker", System.StringComparison.OrdinalIgnoreCase))
                || trimmed.StartsWith("shadow", System.StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("tri shadow", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool HasRootEditorMarker(NifFile nif)
        {
            for (int i = 0; i < nif.Roots.Length; i++)
            {
                int rootIndex = nif.Roots[i];
                if (rootIndex < 0 || rootIndex >= nif.Records.Length)
                    continue;
                if (nif.Records[rootIndex] is NiObjectNET root && HasStringExtra(nif, root, "MRK"))
                    return true;
            }

            return false;
        }

        static bool HasStringExtra(NifFile nif, NiObjectNET obj, string value)
        {
            int extraIndex = obj.ExtraData;
            int guard = 0;
            while (extraIndex >= 0 && extraIndex < nif.Records.Length && guard++ < nif.Records.Length)
            {
                if (nif.Records[extraIndex] is NiStringExtraData stringExtra
                    && string.Equals(stringExtra.Data, value, System.StringComparison.Ordinal))
                {
                    return true;
                }

                extraIndex = nif.Records[extraIndex] is Extra extra ? extra.NextExtra : -1;
            }

            return false;
        }

        static Vector2[] ResolveUvSet(NiGeometryData data, int uvSet, int vertexCount)
        {
            if (data?.UvSets == null || data.UvSets.Length == 0)
                return null;

            if (uvSet < 0 || uvSet >= data.UvSets.Length || data.UvSets[uvSet] == null || data.UvSets[uvSet].Length != vertexCount)
                uvSet = 0;

            return data.UvSets[uvSet] != null && data.UvSets[uvSet].Length == vertexCount
                ? (Vector2[])data.UvSets[uvSet].Clone()
                : null;
        }

        static int[] BuildTriangleIndices(NiGeometry geometry, NiGeometryData data)
        {
            switch (geometry)
            {
                case NiTriShape when data is NiTriShapeData triShapeData:
                {
                    if (triShapeData.Triangles == null || triShapeData.Triangles.Length == 0)
                        return System.Array.Empty<int>();

                    int[] indices = new int[triShapeData.Triangles.Length];
                    for (int i = 0; i < triShapeData.Triangles.Length; i += 3)
                    {
                        indices[i + 0] = triShapeData.Triangles[i + 0];
                        indices[i + 1] = triShapeData.Triangles[i + 2];
                        indices[i + 2] = triShapeData.Triangles[i + 1];
                    }
                    return indices;
                }
                case NiTriStrips when data is NiTriStripsData triStripsData:
                    return ConvertTriangleStrips(triStripsData.Strips);
                default:
                    return System.Array.Empty<int>();
            }
        }

        static int[] ConvertTriangleStrips(ushort[][] strips)
        {
            if (strips == null || strips.Length == 0)
                return System.Array.Empty<int>();

            var triangles = new List<int>();
            for (int s = 0; s < strips.Length; s++)
            {
                var strip = strips[s];
                if (strip == null || strip.Length < 3)
                    continue;

                ushort b = strip[0];
                ushort c = strip[1];
                for (int i = 2; i < strip.Length; i++)
                {
                    ushort a = b;
                    b = c;
                    c = strip[i];
                    if (a == b || b == c || a == c)
                        continue;

                    if ((i & 1) == 0)
                    {
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                    }
                }
            }

            return triangles.ToArray();
        }

        static T Resolve<T>(NifFile nif, int link) where T : NifRecord
        {
            if (link < 0 || link >= nif.Records.Length)
                return null;
            return nif.Records[link] as T;
        }
    }
}
