using System;
using System.Collections.Generic;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    public sealed class ModelPrefabBakery
    {
        public readonly struct Assignment
        {
            public readonly int ModelPrefabIndex;
            public readonly int CollisionIndex;

            public Assignment(int modelPrefabIndex, int collisionIndex)
            {
                ModelPrefabIndex = modelPrefabIndex;
                CollisionIndex = collisionIndex;
            }
        }

        readonly Dictionary<string, Assignment> _assignmentsByPath = new(StringComparer.OrdinalIgnoreCase);
        readonly List<ModelPrefabDef> _records = new();

        public int Count => _records.Count;
        public bool Modified { get; private set; }

        public bool TryGetAssignment(string modelPath, out Assignment assignment)
            => _assignmentsByPath.TryGetValue(modelPath ?? string.Empty, out assignment);

        public Assignment GetOrAdd(
            string modelPath,
            ModelPrefabSource source,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            CollisionBakery collisions,
            ActorAnimationBakery.Assignment actorAnimation = default,
            ModelObjectAnimationDef objectAnimation = null)
        {
            modelPath ??= string.Empty;
            if (_assignmentsByPath.TryGetValue(modelPath, out var existing))
                return existing;

            int collisionIndex = source != null && !source.Collision.IsEmpty
                ? collisions.AddOrGet(source.Collision)
                : -1;

            var record = BuildRecord(modelPath, source, meshes, materials, textures, collisions, collisionIndex, actorAnimation, objectAnimation);
            int index = _records.Count;
            _records.Add(record);

            var assignment = new Assignment(index, collisionIndex);
            _assignmentsByPath[modelPath] = assignment;
            Modified = true;
            return assignment;
        }

        public ModelPrefabCatalogData BuildCatalog()
        {
            var records = new ModelPrefabDef[_records.Count];
            for (int i = 0; i < _records.Count; i++)
                records[i] = Clone(_records[i]);

            return new ModelPrefabCatalogData
            {
                Records = records,
            };
        }

        static ModelPrefabDef BuildRecord(
            string modelPath,
            ModelPrefabSource source,
            MeshBakery meshes,
            MaterialBakery materials,
            TextureBakery textures,
            CollisionBakery collisions,
            int collisionIndex,
            ActorAnimationBakery.Assignment actorAnimation,
            ModelObjectAnimationDef objectAnimation)
        {
            if (source == null)
            {
                return new ModelPrefabDef
                {
                    ModelPath = modelPath,
                    RootNodeIndex = -1,
                    CollisionIndex = collisionIndex,
                    ActorSkeletonIndex = actorAnimation.SkeletonIndex,
                    FirstActorSkinMeshIndex = actorAnimation.FirstSkinMeshIndex,
                    ActorSkinMeshCount = actorAnimation.SkinMeshCount,
                    FirstActorClipIndex = actorAnimation.FirstClipIndex,
                    ActorClipCount = actorAnimation.ClipCount,
                    ObjectAnimation = CloneObjectAnimation(objectAnimation),
                    Nodes = Array.Empty<ModelPrefabNodeDef>(),
                    ChildIndices = Array.Empty<int>(),
                };
            }

            var nodes = new ModelPrefabNodeDef[source.Nodes.Length];
            for (int i = 0; i < source.Nodes.Length; i++)
            {
                var node = source.Nodes[i];
                int meshIndex = -1;
                int materialIndex = -1;
                int textureIndex = -1;
                int pickColliderIndex = -1;
                bool renderableLeaf = node.Kind == ModelPrefabNodeKind.RenderLeaf
                                      && node.RenderLeaf.VertexCount > 0
                                      && node.RenderLeaf.HasNormals
                                      && node.RenderLeaf.Indices != null
                                      && node.RenderLeaf.Indices.Length > 0;
                if (renderableLeaf)
                {
                    meshIndex = meshes.AddOrGet($"{modelPath}#{i}", node.RenderLeaf);
                    materialIndex = materials.AddOrGet(node.MaterialFlags);
                    textureIndex = textures.AddOrGet(node.TexturePath);
                    pickColliderIndex = AddPickCollider(collisions, node.RenderLeaf, modelPath, i);
                }

                var bounds = renderableLeaf ? node.RenderLeaf.LocalBounds : default;
                nodes[i] = new ModelPrefabNodeDef
                {
                    Kind = renderableLeaf ? node.Kind : ModelPrefabNodeKind.Transform,
                    Name = node.Name ?? string.Empty,
                    ParentIndex = node.ParentIndex,
                    FirstChildIndex = node.FirstChildIndex,
                    ChildCount = node.ChildCount,
                    SelectedChildIndex = node.SelectedChildIndex,
                    GlobalMeshIndex = meshIndex,
                    MaterialIndex = materialIndex,
                    TextureIndex = textureIndex,
                    PickColliderIndex = pickColliderIndex,
                    PosX = node.LocalPosition.x,
                    PosY = node.LocalPosition.y,
                    PosZ = node.LocalPosition.z,
                    RotX = node.LocalRotation.x,
                    RotY = node.LocalRotation.y,
                    RotZ = node.LocalRotation.z,
                    RotW = node.LocalRotation.w,
                    Scale = node.LocalScale,
                    BoundsCenterX = bounds.center.x,
                    BoundsCenterY = bounds.center.y,
                    BoundsCenterZ = bounds.center.z,
                    BoundsExtentsX = bounds.extents.x,
                    BoundsExtentsY = bounds.extents.y,
                    BoundsExtentsZ = bounds.extents.z,
                    Flags = node.Flags,
                };
            }

            var childIndices = new int[source.ChildIndices.Length];
            Array.Copy(source.ChildIndices, childIndices, childIndices.Length);
            return new ModelPrefabDef
            {
                ModelPath = modelPath,
                RootNodeIndex = 0,
                CollisionIndex = collisionIndex,
                ActorSkeletonIndex = actorAnimation.SkeletonIndex,
                FirstActorSkinMeshIndex = actorAnimation.FirstSkinMeshIndex,
                ActorSkinMeshCount = actorAnimation.SkinMeshCount,
                FirstActorClipIndex = actorAnimation.FirstClipIndex,
                ActorClipCount = actorAnimation.ClipCount,
                ObjectAnimation = CloneObjectAnimation(objectAnimation),
                Nodes = nodes,
                ChildIndices = childIndices,
            };
        }

        static ModelPrefabDef Clone(ModelPrefabDef source)
        {
            var nodes = source?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            var clonedNodes = new ModelPrefabNodeDef[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                clonedNodes[i] = new ModelPrefabNodeDef
                {
                    Kind = node.Kind,
                    Name = node.Name ?? string.Empty,
                    ParentIndex = node.ParentIndex,
                    FirstChildIndex = node.FirstChildIndex,
                    ChildCount = node.ChildCount,
                    SelectedChildIndex = node.SelectedChildIndex,
                    GlobalMeshIndex = node.GlobalMeshIndex,
                    MaterialIndex = node.MaterialIndex,
                    TextureIndex = node.TextureIndex,
                    PickColliderIndex = node.PickColliderIndex,
                    PosX = node.PosX,
                    PosY = node.PosY,
                    PosZ = node.PosZ,
                    RotX = node.RotX,
                    RotY = node.RotY,
                    RotZ = node.RotZ,
                    RotW = node.RotW,
                    Scale = node.Scale,
                    BoundsCenterX = node.BoundsCenterX,
                    BoundsCenterY = node.BoundsCenterY,
                    BoundsCenterZ = node.BoundsCenterZ,
                    BoundsExtentsX = node.BoundsExtentsX,
                    BoundsExtentsY = node.BoundsExtentsY,
                    BoundsExtentsZ = node.BoundsExtentsZ,
                    Flags = node.Flags,
                };
            }

            var childIndices = source?.ChildIndices ?? Array.Empty<int>();
            var clonedChildIndices = new int[childIndices.Length];
            Array.Copy(childIndices, clonedChildIndices, childIndices.Length);

            return new ModelPrefabDef
            {
                ModelPath = source?.ModelPath ?? string.Empty,
                RootNodeIndex = source?.RootNodeIndex ?? -1,
                CollisionIndex = source?.CollisionIndex ?? -1,
                ActorSkeletonIndex = source?.ActorSkeletonIndex ?? -1,
                FirstActorSkinMeshIndex = source?.FirstActorSkinMeshIndex ?? -1,
                ActorSkinMeshCount = source?.ActorSkinMeshCount ?? 0,
                FirstActorClipIndex = source?.FirstActorClipIndex ?? -1,
                ActorClipCount = source?.ActorClipCount ?? 0,
                ObjectAnimation = CloneObjectAnimation(source?.ObjectAnimation),
                Nodes = clonedNodes,
                ChildIndices = clonedChildIndices,
            };
        }

        static ModelObjectAnimationDef CloneObjectAnimation(ModelObjectAnimationDef source)
        {
            if (source == null)
                return null;

            var clips = source.Clips ?? Array.Empty<ModelObjectAnimationClipDef>();
            var clonedClips = new ModelObjectAnimationClipDef[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                clonedClips[i] = new ModelObjectAnimationClipDef
                {
                    Name = clip?.Name ?? string.Empty,
                    Duration = clip?.Duration ?? 0f,
                    FirstTrackIndex = clip?.FirstTrackIndex ?? -1,
                    TrackCount = clip?.TrackCount ?? 0,
                    FirstTextMarkerIndex = clip?.FirstTextMarkerIndex ?? -1,
                    TextMarkerCount = clip?.TextMarkerCount ?? 0,
                };
            }

            var tracks = source.Tracks ?? Array.Empty<ModelObjectAnimationTrackDef>();
            var clonedTracks = new ModelObjectAnimationTrackDef[tracks.Length];
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                clonedTracks[i] = new ModelObjectAnimationTrackDef
                {
                    TargetNodeIndex = track?.TargetNodeIndex ?? -1,
                    Kind = track?.Kind ?? ActorAnimationTrackKind.Translation,
                    Interpolation = track?.Interpolation ?? ActorAnimationInterpolation.Linear,
                    AxisOrder = track?.AxisOrder ?? 0,
                    ControllerFlags = track?.ControllerFlags ?? 0,
                    Frequency = track?.Frequency ?? 0f,
                    Phase = track?.Phase ?? 0f,
                    TimeStart = track?.TimeStart ?? 0f,
                    TimeStop = track?.TimeStop ?? 0f,
                    FirstKeyIndex = track?.FirstKeyIndex ?? -1,
                    KeyCount = track?.KeyCount ?? 0,
                };
            }

            var keys = source.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            var clonedKeys = new ActorAnimationKeyDef[keys.Length];
            Array.Copy(keys, clonedKeys, clonedKeys.Length);

            var markers = source.TextMarkers ?? Array.Empty<ModelObjectAnimationTextMarkerDef>();
            var clonedMarkers = new ModelObjectAnimationTextMarkerDef[markers.Length];
            Array.Copy(markers, clonedMarkers, clonedMarkers.Length);

            return new ModelObjectAnimationDef
            {
                Status = source.Status,
                DisabledReason = source.DisabledReason ?? string.Empty,
                Clips = clonedClips,
                Tracks = clonedTracks,
                Keys = clonedKeys,
                TextMarkers = clonedMarkers,
            };
        }

        static int AddPickCollider(
            CollisionBakery collisions,
            in NifMeshBuilder.RawBuiltMesh mesh,
            string modelPath,
            int nodeIndex)
        {
            if (collisions == null)
                return -1;
            if (mesh.Vertices == null || mesh.Vertices.Length == 0 || mesh.Indices == null || mesh.Indices.Length == 0)
                return -1;
            if (mesh.Indices.Length % 3 != 0)
                throw new InvalidOperationException(
                    $"Model prefab '{modelPath}' node {nodeIndex} has non-triangle render index count {mesh.Indices.Length}.");

            return collisions.AddOrGetInteractionPick(new CollisionPayload(mesh.Vertices, mesh.Indices));
        }
    }
}
