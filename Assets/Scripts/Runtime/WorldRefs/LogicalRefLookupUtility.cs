using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class LogicalRefLookupUtility
    {
        public static void AddOrThrow(ref LogicalRefLookup logicalRefs, uint placedRefId, Entity logicalEntity, bool isInterior)
        {
            if (!logicalRefs.Map.IsCreated || placedRefId == 0u)
                return;

            if (logicalRefs.Map.TryAdd(placedRefId, logicalEntity))
                return;

            if (logicalRefs.Map.TryGetValue(placedRefId, out var existing) && existing != logicalEntity)
                throw new System.InvalidOperationException(
                    $"[VVardenfell][WorldRefs] duplicate logical-ref lookup for placed ref 0x{placedRefId:X8} while spawning {(isInterior ? "interior" : "exterior")} content; existing={existing.Index}:{existing.Version} new={logicalEntity.Index}:{logicalEntity.Version}.");
        }

        public static void Replace(ref LogicalRefLookup logicalRefs, uint placedRefId, Entity logicalEntity)
        {
            if (!logicalRefs.Map.IsCreated || placedRefId == 0u)
                return;

            if (logicalRefs.Map.TryAdd(placedRefId, logicalEntity))
                return;

            logicalRefs.Map.Remove(placedRefId);
            logicalRefs.Map.TryAdd(placedRefId, logicalEntity);
        }

        public static void Remove(ref LogicalRefLookup logicalRefs, uint placedRefId)
        {
            if (!logicalRefs.Map.IsCreated || placedRefId == 0u)
                return;

            logicalRefs.Map.Remove(placedRefId);
        }
    }
}
