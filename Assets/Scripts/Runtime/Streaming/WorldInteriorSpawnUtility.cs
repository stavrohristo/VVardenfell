using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldInteriorSpawnUtility
    {
        internal static void SpawnInteriorCell(World world, CellData cell, float3 worldOffset, Entity transitionEntity, ref LogicalRefLookup logicalRefs)
        {
            if (cell == null)
                return;

            var em = world.EntityManager;
            var cellSw = Stopwatch.StartNew();
            var spawnedEntities = new List<Entity>(cell.Refs?.Length + (cell.HasStaticCollider ? 1 : 0) ?? 1);
            var interiorChildEntities = cell.Refs != null ? new Entity[cell.Refs.Length] : System.Array.Empty<Entity>();
            var interiorCellId = new FixedString128Bytes(cell.CellId ?? string.Empty);

            if (cell.HasStaticCollider)
                SpawnInteriorStaticCollider(em, cell, worldOffset, spawnedEntities);

            if (cell.Refs != null)
            {
                for (int i = 0; i < cell.Refs.Length; i++)
                {
                    var entry = cell.Refs[i];
                    interiorChildEntities[i] = WorldRefSpawnUtility.SpawnInteriorRef(em, entry, worldOffset, interiorCellId, spawnedEntities);
                }
            }

            int logicalRefCount = 0;
            int proxyQueueCount = 0;
            if (cell.Refs != null && cell.Refs.Length > 0)
            {
                logicalRefCount = WorldRefSpawnUtility.BuildLogicalRefs(
                    em,
                    WorldResources.Cache?.ContentDatabase ?? RuntimeContentDatabase.Active,
                    cell.Refs,
                    interiorChildEntities,
                    true,
                    interiorCellId,
                    worldOffset,
                    ref logicalRefs,
                    spawnedEntities,
                    out proxyQueueCount);
            }

            var spawnedBuffer = em.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            for (int i = 0; i < spawnedEntities.Count; i++)
                spawnedBuffer.Add(new InteriorSpawnedEntity { Value = spawnedEntities[i] });

            cellSw.Stop();
        }

        static void SpawnInteriorStaticCollider(EntityManager em, CellData cell, float3 worldOffset, List<Entity> spawnedEntities)
        {
            var staticEntity = em.CreateEntity();
            em.SetName(staticEntity, $"InteriorStatic({cell.CellId})");
            em.AddComponentData(staticEntity, LocalTransform.FromPositionRotationScale(worldOffset, quaternion.identity, 1f));
            em.AddComponentData(staticEntity, new LocalToWorld
            {
                Value = float4x4.Translate(worldOffset)
            });
            em.AddComponent<InteriorCellMember>(staticEntity);
            em.AddComponent<Unity.Transforms.Static>(staticEntity);
            RuntimeColliderAttachmentUtility.AttachSource(
                em,
                staticEntity,
                cell.StaticColliderBlob,
                RuntimeColliderKind.StaticCell,
                active: true);
            spawnedEntities.Add(staticEntity);
        }
    }
}
