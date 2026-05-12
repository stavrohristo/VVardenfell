using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    internal static class CombinedCellRenderDecombineUtility
    {
        public static void DecombineIfLinked(EntityManager em, Entity logicalEntity)
        {
            if (logicalEntity == Entity.Null || !em.Exists(logicalEntity) || !em.HasBuffer<CombinedCellRenderLink>(logicalEntity))
                return;

            var links = em.GetBuffer<CombinedCellRenderLink>(logicalEntity);
            if (links.Length > 0)
            {
                uint placedRefId = em.HasComponent<PlacedRefIdentity>(logicalEntity)
                    ? em.GetComponentData<PlacedRefIdentity>(logicalEntity).Value
                    : 0u;
                throw new InvalidOperationException(
                    $"[VVardenfell][CombinedRender] Runtime mutation reached combined immutable STAT ref 0x{placedRefId:X8}; rebake required with this ref excluded from combined static collision.");
            }
        }
    }
}

