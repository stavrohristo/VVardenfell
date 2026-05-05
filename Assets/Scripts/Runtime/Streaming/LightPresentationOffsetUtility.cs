using System;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Streaming
{
    internal static class LightPresentationOffsetUtility
    {
        const string AttachLightNodeName = "AttachLight";

        public static bool TryResolveAttachLightOffset(
            RuntimeContentDatabase contentDb,
            LightDefHandle handle,
            out float3 localPosition)
        {
            localPosition = default;
            if (contentDb == null || !handle.IsValid)
                return false;

            ref readonly var light = ref contentDb.Get(handle);
            if (string.IsNullOrWhiteSpace(light.Model))
                return false;

            var modelDefs = WorldResources.Cache?.ModelPrefabCatalog?.Records;
            if (modelDefs == null)
                return false;

            string normalizedModelPath = WorldModelPrefabUtility.NormalizeContentModelPath(light.Model);
            for (int i = 0; i < modelDefs.Length; i++)
            {
                var def = modelDefs[i];
                if (def == null
                    || !string.Equals(WorldModelPrefabUtility.NormalizeContentModelPath(def.ModelPath), normalizedModelPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryResolveAttachLightOffset(def, out localPosition);
            }

            return false;
        }

        static bool TryResolveAttachLightOffset(ModelPrefabDef def, out float3 localPosition)
        {
            localPosition = default;
            if (def?.Nodes == null || def.Nodes.Length == 0)
                return false;

            int attachNodeIndex = -1;
            for (int i = 0; i < def.Nodes.Length; i++)
            {
                if (string.Equals(def.Nodes[i]?.Name, AttachLightNodeName, StringComparison.OrdinalIgnoreCase))
                {
                    attachNodeIndex = i;
                    break;
                }
            }

            if (attachNodeIndex < 0)
                return false;

            int rootIndex = math.clamp(def.RootNodeIndex, 0, def.Nodes.Length - 1);
            if (attachNodeIndex == rootIndex)
                return false;

            float4x4 rootToAttach = float4x4.identity;
            int current = attachNodeIndex;
            int guard = 0;
            while (current >= 0 && current < def.Nodes.Length && current != rootIndex)
            {
                var node = def.Nodes[current];
                rootToAttach = math.mul(BuildLocalMatrix(node), rootToAttach);
                current = node.ParentIndex;
                guard++;
                if (guard > def.Nodes.Length)
                    throw new InvalidOperationException($"[VVardenfell][Lighting] Model prefab '{def.ModelPath}' has a cyclic parent chain while resolving AttachLight.");
            }

            if (current != rootIndex)
                return false;

            localPosition = rootToAttach.c3.xyz;
            return math.lengthsq(localPosition) > 0.000001f;
        }

        static float4x4 BuildLocalMatrix(ModelPrefabNodeDef node)
            => float4x4.TRS(
                new float3(node.PosX, node.PosY, node.PosZ),
                new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                new float3(node.Scale));
    }
}
