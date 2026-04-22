using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;

namespace VVardenfell.Runtime.Streaming
{
    public static class RefPlacementDebug
    {
        public static void DumpCurrentInteriorPlacedRef(uint placedRefId, World world = null)
        {
            world ??= World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogWarning("[VVardenfell] no active world available for placement inspection.");
                return;
            }

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            if (query.IsEmptyIgnoreFilter)
            {
                Debug.LogWarning("[VVardenfell] no interior transition state exists yet.");
                return;
            }

            var transition = query.GetSingleton<InteriorTransitionState>();
            if (transition.InteriorActive == 0 || transition.ActiveInteriorCellId.Length == 0)
            {
                Debug.LogWarning("[VVardenfell] no active interior is loaded right now.");
                return;
            }

            DumpInteriorPlacedRef(transition.ActiveInteriorCellId.ToString(), placedRefId, world);
        }

        public static void DumpInteriorPlacedRef(string interiorCellId, uint placedRefId, World world = null)
        {
            if (!WorldResources.InteriorCells.TryGetValue(interiorCellId ?? string.Empty, out var cell) || cell == null)
            {
                Debug.LogWarning($"[VVardenfell] interior '{interiorCellId}' is not loaded in WorldResources.");
                return;
            }

            DumpPlacedRef(cell, placedRefId, world ?? World.DefaultGameObjectInjectionWorld, isInterior: true, new int2(cell.GridX, cell.GridY));
        }

        public static void DumpExteriorPlacedRef(int gridX, int gridY, uint placedRefId, World world = null)
        {
            var coord = new int2(gridX, gridY);
            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell == null)
            {
                Debug.LogWarning($"[VVardenfell] exterior cell ({gridX},{gridY}) is not loaded in WorldResources.");
                return;
            }

            DumpPlacedRef(cell, placedRefId, world ?? World.DefaultGameObjectInjectionWorld, isInterior: false, coord);
        }

        static void DumpPlacedRef(CellData cell, uint placedRefId, World world, bool isInterior, int2 coord)
        {
            if (world == null)
            {
                Debug.LogWarning("[VVardenfell] no active world available for placement inspection.");
                return;
            }

            var em = world.EntityManager;
            em.CompleteDependencyBeforeRO<PlacedRefIdentity>();
            em.CompleteDependencyBeforeRO<LocalToWorld>();
            em.CompleteDependencyBeforeRO<LocalTransform>();
            em.CompleteDependencyBeforeRO<RenderBounds>();

            RefPlacementAuditEntry? bakedAudit = FindAudit(cell, placedRefId);
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlacedRefIdentity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);

            var sb = new StringBuilder(2048);
            sb.Append("[VVardenfell][RefPlacement] ");
            sb.Append(isInterior ? "interior='" : "cell='");
            sb.Append(isInterior ? cell.CellId : $"{coord.x},{coord.y}");
            sb.Append("' placedRefId=0x");
            sb.Append(placedRefId.ToString("X8"));
            sb.AppendLine();

            if (bakedAudit.HasValue)
                AppendBakedAudit(sb, bakedAudit.Value);
            else
                sb.AppendLine("Baked audit: <missing>");

            int liveCount = 0;
            int logicalDoorCount = 0;
            int logicalEntityCount = 0;
            int childEntityCount = 0;
            bool hasAggregateBounds = false;
            AABB aggregateBounds = default;

            for (int i = 0; i < entities.Length; i++)
            {
                if (identities[i].Value != placedRefId)
                    continue;

                Entity entity = entities[i];
                if (!MatchesCell(em, entity, isInterior, coord))
                    continue;

                liveCount++;
                bool isLogical = em.HasComponent<LogicalRefTag>(entity);
                bool isChild = em.HasComponent<LogicalRefParent>(entity);
                bool hasDoor = em.HasComponent<DoorInteractable>(entity);
                if (hasDoor)
                    logicalDoorCount++;
                if (isLogical)
                    logicalEntityCount++;
                if (isChild)
                    childEntityCount++;

                sb.Append("Live entity ");
                sb.Append(entity);
                sb.Append(" type=");
                sb.Append(isLogical ? "logical" : (isChild ? "child" : "legacy"));
                sb.Append(": door=");
                sb.Append(hasDoor ? "yes" : "no");

                if (isChild)
                {
                    sb.Append(" logicalParent=");
                    sb.Append(em.GetComponentData<LogicalRefParent>(entity).Value);
                }

                if (em.HasComponent<LocalTransform>(entity))
                {
                    var transform = em.GetComponentData<LocalTransform>(entity);
                    sb.Append(" pos=");
                    sb.Append(transform.Position);
                    sb.Append(" rot=");
                    sb.Append(transform.Rotation.value);
                    sb.Append(" scale=");
                    sb.Append(transform.Scale.ToString("F3"));
                }

                if (hasDoor)
                {
                    var door = em.GetComponentData<DoorInteractable>(entity);
                    sb.Append(" teleport=");
                    sb.Append(door.IsTeleport != 0 ? "yes" : "no");
                    sb.Append(" destCell='");
                    sb.Append(door.DestinationCellId.ToString());
                    sb.Append("'");
                }

                if (TryGetWorldBounds(em, entity, out var worldBounds))
                {
                    if (!hasAggregateBounds)
                    {
                        aggregateBounds = worldBounds;
                        hasAggregateBounds = true;
                    }
                    else
                    {
                        aggregateBounds = Encapsulate(aggregateBounds, worldBounds);
                    }

                    sb.Append(" boundsCenter=");
                    sb.Append(worldBounds.Center);
                    sb.Append(" boundsExtents=");
                    sb.Append(worldBounds.Extents);
                }

                sb.AppendLine();
            }

            sb.Append("Live matches: ");
            sb.Append(liveCount);
            sb.Append(", logical entities: ");
            sb.Append(logicalEntityCount);
            sb.Append(", child entities: ");
            sb.Append(childEntityCount);
            sb.Append(", logical doors: ");
            sb.Append(logicalDoorCount);
            sb.AppendLine();

            if (logicalDoorCount > 1)
                sb.AppendLine("Warning: multiple logical door entities were found for this placed ref.");

            if (bakedAudit.HasValue && hasAggregateBounds)
            {
                var audit = bakedAudit.Value;
                float3 bakedCenter = new(audit.BoundsCenterX, audit.BoundsCenterY, audit.BoundsCenterZ);
                float3 bakedExtents = new(audit.BoundsExtentsX, audit.BoundsExtentsY, audit.BoundsExtentsZ);
                float centerDelta = math.distance(bakedCenter, aggregateBounds.Center);
                float extentDelta = math.distance(bakedExtents, aggregateBounds.Extents);
                sb.Append("Bounds delta: center=");
                sb.Append(centerDelta.ToString("F4"));
                sb.Append(" extents=");
                sb.Append(extentDelta.ToString("F4"));
                sb.AppendLine();
            }

            Debug.Log(sb.ToString());
        }

        static RefPlacementAuditEntry? FindAudit(CellData cell, uint placedRefId)
        {
            var entries = cell?.PlacementAudit?.Entries;
            if (entries == null)
                return null;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].PlacedRefId == placedRefId)
                    return entries[i];
            }

            return null;
        }

        static void AppendBakedAudit(StringBuilder sb, RefPlacementAuditEntry audit)
        {
            sb.Append("Baked audit: baseId='");
            sb.Append(audit.BaseId);
            sb.Append("' flags=");
            sb.Append(audit.Flags);
            sb.Append(" sourcePos=(");
            sb.Append(audit.SourcePosX.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.SourcePosY.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.SourcePosZ.ToString("F3"));
            sb.Append(") sourceRot=(");
            sb.Append(audit.SourceRotX.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.SourceRotY.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.SourceRotZ.ToString("F3"));
            sb.Append(") unityPos=(");
            sb.Append(audit.UnityPosX.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.UnityPosY.ToString("F3"));
            sb.Append(", ");
            sb.Append(audit.UnityPosZ.ToString("F3"));
            sb.Append(") unityRot=(");
            sb.Append(audit.UnityRotX.ToString("F4"));
            sb.Append(", ");
            sb.Append(audit.UnityRotY.ToString("F4"));
            sb.Append(", ");
            sb.Append(audit.UnityRotZ.ToString("F4"));
            sb.Append(", ");
            sb.Append(audit.UnityRotW.ToString("F4"));
            sb.Append(") scale=");
            sb.Append(audit.UnityScale.ToString("F3"));
            sb.Append(" duplicateCount=");
            sb.Append(audit.DuplicatePlacedRefCount);
            sb.Append(" spawnedSubmeshes=");
            sb.Append(audit.SpawnedSubmeshCount);
            sb.AppendLine();
        }

        static bool MatchesCell(EntityManager em, Entity entity, bool isInterior, int2 coord)
        {
            if (isInterior)
                return em.HasComponent<InteriorCellMember>(entity);

            if (!em.HasComponent<CellLink>(entity))
                return false;

            return em.GetComponentData<CellLink>(entity).Value.Equals(coord);
        }

        static bool TryGetWorldBounds(EntityManager em, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!em.HasComponent<RenderBounds>(entity) || !em.HasComponent<LocalToWorld>(entity))
                return false;

            var localBounds = em.GetComponentData<RenderBounds>(entity).Value;
            float4x4 localToWorld = em.GetComponentData<LocalToWorld>(entity).Value;
            float3 center = math.transform(localToWorld, localBounds.Center);
            float3x3 rotationScale = new(localToWorld.c0.xyz, localToWorld.c1.xyz, localToWorld.c2.xyz);
            float3 extents = math.abs(rotationScale.c0) * localBounds.Extents.x
                + math.abs(rotationScale.c1) * localBounds.Extents.y
                + math.abs(rotationScale.c2) * localBounds.Extents.z;

            worldBounds = new AABB { Center = center, Extents = extents };
            return true;
        }

        static AABB Encapsulate(AABB a, AABB b)
        {
            float3 min = math.min(a.Min, b.Min);
            float3 max = math.max(a.Max, b.Max);
            return new AABB
            {
                Center = (min + max) * 0.5f,
                Extents = (max - min) * 0.5f,
            };
        }
    }
}
