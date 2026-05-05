using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    internal static class RuntimeModelPrefabBlobBuilder
    {
        const string AttachLightNodeName = "AttachLight";

        public static BlobAssetReference<RuntimeModelPrefabBlob> Build(
            ModelPrefabCatalogData catalog,
            BlobAssetReference<Collider>[] colliderBlobs)
        {
            var records = catalog?.Records ?? Array.Empty<ModelPrefabDef>();
            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
            ref RuntimeModelPrefabBlob root = ref builder.ConstructRoot<RuntimeModelPrefabBlob>();

            var dst = builder.Allocate(ref root.Records, records.Length);
            var pathLookup = new List<RuntimeContentHashLookupBlob>(records.Length);
            var contentPathLookup = new List<RuntimeContentHashLookupBlob>(records.Length);
            var seenPathHashes = new Dictionary<ulong, string>();
            var seenContentHashes = new Dictionary<ulong, string>();

            for (int i = 0; i < records.Length; i++)
            {
                var source = records[i] ?? throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Missing model prefab record {i}.");
                string normalizedPath = ActorVisualContentRules.NormalizeModelPath(source.ModelPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                    throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab record {i} has no model path.");

                ValidateNodeRanges(source, i);
                ulong pathHash = RuntimeContentStableHash.HashPath(normalizedPath);
                ulong contentPathHash = RuntimeContentStableHash.HashPath(RemoveMeshesPrefix(normalizedPath));
                RequireUnique(seenPathHashes, pathHash, normalizedPath, "model path");
                RequireUnique(seenContentHashes, contentPathHash, normalizedPath, "content model path");

                float radius = 0f;
                if (source.CollisionIndex >= 0)
                    radius = ResolveCollisionRadius(colliderBlobs, source.CollisionIndex, normalizedPath);

                dst[i] = new RuntimeModelPrefabDefBlob
                {
                    ModelPathHash = pathHash,
                    ContentModelPathHash = contentPathHash,
                    ModelPrefabIndex = i,
                    CollisionIndex = source.CollisionIndex,
                    CollisionRadius = radius,
                    RootNodeIndex = source.RootNodeIndex,
                    Supported = 1,
                    ObjectAnimationEnabled = (byte)(source.ObjectAnimation?.IsEnabled == true ? 1 : 0),
                    EffectControllerStopTime = source.EffectControllerStopTime,
                    HasAttachLightOffset = (byte)(TryResolveAttachLightOffset(source, out float3 attachOffset) ? 1 : 0),
                    AttachLightOffset = attachOffset,
                };
                pathLookup.Add(new RuntimeContentHashLookupBlob { Hash = pathHash, HandleValue = i });
                contentPathLookup.Add(new RuntimeContentHashLookupBlob { Hash = contentPathHash, HandleValue = i });
            }

            CopySortedLookup(ref builder, ref root.ModelPathHashLookup, pathLookup);
            CopySortedLookup(ref builder, ref root.ContentModelPathHashLookup, contentPathLookup);
            return builder.CreateBlobAssetReference<RuntimeModelPrefabBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        static void ValidateNodeRanges(ModelPrefabDef source, int modelPrefabIndex)
        {
            var nodes = source.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            var childIndices = source.ChildIndices ?? Array.Empty<int>();
            if (nodes.Length <= 0)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} has no nodes.");
            if ((uint)source.RootNodeIndex >= (uint)nodes.Length)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} has invalid root node {source.RootNodeIndex}; node count {nodes.Length}.");

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i] ?? throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} node {i} is null.");
                if (node.ParentIndex >= nodes.Length)
                    throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} node {i} has invalid parent {node.ParentIndex}.");
                if (node.ChildCount < 0 || node.FirstChildIndex < -1)
                    throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} node {i} has invalid child range.");
                if (node.ChildCount > 0
                    && (node.FirstChildIndex < 0 || node.FirstChildIndex + node.ChildCount > childIndices.Length))
                    throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab {modelPrefabIndex} node {i} child range is outside child table.");
            }
        }

        static float ResolveCollisionRadius(
            BlobAssetReference<Collider>[] colliderBlobs,
            int collisionIndex,
            string normalizedPath)
        {
            if (colliderBlobs == null
                || (uint)collisionIndex >= (uint)colliderBlobs.Length
                || !colliderBlobs[collisionIndex].IsCreated)
            {
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab '{normalizedPath}' references missing collision index {collisionIndex}; rebake required.");
            }

            var body = new RigidBody
            {
                Collider = colliderBlobs[collisionIndex],
                Entity = Entity.Null,
                WorldFromBody = RigidTransform.identity,
                Scale = 1f,
            };
            Aabb aabb = body.CalculateAabb();
            float radius = math.length((aabb.Max - aabb.Min) * 0.5f) * 0.5f;
            if (radius <= 0f || !math.isfinite(radius))
                return 0f;
            return radius;
        }

        static bool TryResolveAttachLightOffset(ModelPrefabDef def, out float3 localPosition)
        {
            localPosition = default;
            var nodes = def.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            int attachNodeIndex = -1;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (string.Equals(nodes[i]?.Name, AttachLightNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    attachNodeIndex = i;
                    break;
                }
            }
            if (attachNodeIndex < 0 || attachNodeIndex == def.RootNodeIndex)
                return false;

            float4x4 rootToAttach = float4x4.identity;
            int current = attachNodeIndex;
            int guard = 0;
            while ((uint)current < (uint)nodes.Length && current != def.RootNodeIndex)
            {
                var node = nodes[current];
                rootToAttach = math.mul(BuildLocalMatrix(node), rootToAttach);
                current = node.ParentIndex;
                if (++guard > nodes.Length)
                    throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Model prefab '{def.ModelPath}' has a cyclic parent chain while resolving AttachLight.");
            }

            if (current != def.RootNodeIndex)
                return false;

            localPosition = rootToAttach.c3.xyz;
            return math.lengthsq(localPosition) > 0.000001f;
        }

        static float4x4 BuildLocalMatrix(ModelPrefabNodeDef node)
            => float4x4.TRS(
                new float3(node.PosX, node.PosY, node.PosZ),
                new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                new float3(node.Scale));

        static string RemoveMeshesPrefix(string normalizedPath)
            => normalizedPath.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring("meshes\\".Length)
                : normalizedPath;

        static void RequireUnique(Dictionary<ulong, string> seen, ulong hash, string value, string context)
        {
            if (hash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Empty {context} hash for '{value}'.");
            if (seen.TryGetValue(hash, out string existing))
                throw new InvalidOperationException($"[VVardenfell][ModelPrefabBlob] Duplicate or colliding {context} hash 0x{hash:X16}: '{existing}' and '{value}'.");
            seen.Add(hash, value);
        }

        static void CopySortedLookup(
            ref BlobBuilder builder,
            ref BlobArray<RuntimeContentHashLookupBlob> destination,
            List<RuntimeContentHashLookupBlob> values)
        {
            values.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            var dst = builder.Allocate(ref destination, values.Count);
            for (int i = 0; i < values.Count; i++)
                dst[i] = values[i];
        }
    }
}
