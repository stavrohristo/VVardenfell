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
        public int FirstChildIndex = -1;
        public int ChildCount;
        public int SelectedChildIndex = -1;
        public ushort Flags;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public float LocalScale = 1f;
        public NifMeshBuilder.RawBuiltMesh RenderLeaf;
        public uint MaterialFlags;
        public string TexturePath;
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

                int child = BuildNode(nif, av, syntheticRoot, state);
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

        static int BuildNode(NifFile nif, NiAVObject obj, int parentIndex, BuildState state)
        {
            if ((obj.Flags & 0x0001) != 0)
                return -1;

            var kind = ResolveKind(obj);
            if (kind == ModelPrefabNodeKind.RootCollision || kind == ModelPrefabNodeKind.Avoid)
            {
                int metadataNode = AddTransformNode(state, obj, parentIndex, kind);
                return metadataNode;
            }

            if (kind == ModelPrefabNodeKind.CollisionSwitch && (obj.Flags & 0x0020) == 0)
            {
                int switchNode = AddTransformNode(state, obj, parentIndex, kind);
                return switchNode;
            }

            if (obj is NiGeometry geometry && TryResolveRenderableGeometry(nif, geometry, out _))
                return AddRenderLeaf(nif, geometry, parentIndex, state);

            if (obj is not NiNode node)
                return AddTransformNode(state, obj, parentIndex, kind);

            int nodeIndex = AddTransformNode(state, obj, parentIndex, kind);
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

                int builtChild = BuildNode(nif, child, nodeIndex, state);
                if (builtChild >= 0)
                    pendingChildren.Add(builtChild);
            }

            state.Nodes[nodeIndex].SelectedChildIndex = selectedChild;
            return nodeIndex;
        }

        static int AddRenderLeaf(NifFile nif, NiGeometry geometry, int parentIndex, BuildState state)
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

            var node = CreateBaseNode(geometry, parentIndex, ResolveKind(geometry));
            node.MaterialFlags = materialFlags;
            node.TexturePath = FindTexture(nif, geometry, out int uvSet);
            node.RenderLeaf = BuildLocalRawMesh(geometry, data, uvSet, node.TexturePath, alphaFlags, alphaThreshold);
            return AddNode(state, node);
        }

        static ModelPrefabSourceNode CreateBaseNode(NiAVObject obj, int parentIndex, ModelPrefabNodeKind kind)
        {
            ConvertLocalTransform(obj, out Vector3 position, out Quaternion rotation, out float scale);
            return new ModelPrefabSourceNode
            {
                Kind = kind,
                Name = string.IsNullOrWhiteSpace(obj.Name) ? string.Empty : obj.Name,
                ParentIndex = parentIndex,
                Flags = obj.Flags,
                LocalPosition = position,
                LocalRotation = rotation,
                LocalScale = scale,
            };
        }

        static int AddTransformNode(BuildState state, NiAVObject obj, int parentIndex, ModelPrefabNodeKind kind)
            => AddNode(state, CreateBaseNode(obj, parentIndex, kind));

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

            data = geometry switch
            {
                NiTriShape => Resolve<NiTriShapeData>(nif, geometry.Data),
                NiTriStrips => Resolve<NiTriStripsData>(nif, geometry.Data),
                _ => null,
            };

            return data != null
                && data.Vertices != null
                && data.NumVertices > 0
                && BuildTriangleIndices(geometry, data).Length > 0;
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
