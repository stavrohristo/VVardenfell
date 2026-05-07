using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    internal static class CombinedCellRenderSpawnUtility
    {
        public static Entity[] SpawnChunks(EntityManager em, int2 coord, CellData data, bool active)
        {
            var chunks = data?.CombinedRenderChunks;
            if (chunks == null || chunks.Length == 0)
                return System.Array.Empty<Entity>();

            var result = new Entity[chunks.Length];
            var managed = WorldResources.LoadedManaged.TryGetValue(coord, out var existing)
                ? existing
                : new WorldResources.PerCellManaged();
            managed.CombinedRenderMeshes ??= new List<Mesh>(chunks.Length);
            managed.CombinedRenderRmas ??= new List<RenderMeshArray>(chunks.Length);

            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                Mesh mesh = CombinedCellRenderMeshUploadUtility.Upload(chunk, $"CombinedCellRender({coord.x},{coord.y})#{i}");
                RenderMeshArray rma = CreateRenderMeshArray(chunk, mesh);
                managed.CombinedRenderMeshes.Add(mesh);
                managed.CombinedRenderRmas.Add(rma);
                result[i] = CreateChunkEntity(em, coord, chunk, rma, active, i);
            }

            WorldResources.LoadedManaged[coord] = managed;
            return result;
        }

        public static void AttachMembershipLinks(
            EntityManager em,
            CombinedCellRenderChunkDef[] chunkDefs,
            Entity[] chunkEntities,
            ref LogicalRefLookup logicalRefs)
        {
            if (chunkDefs == null || chunkEntities == null || !logicalRefs.Map.IsCreated)
                return;

            for (int i = 0; i < chunkDefs.Length && i < chunkEntities.Length; i++)
            {
                Entity chunkEntity = chunkEntities[i];
                if (chunkEntity == Entity.Null || !em.Exists(chunkEntity))
                    continue;

                var chunk = em.GetComponentData<CombinedCellRenderChunk>(chunkEntity);
                var pendingMembers = new List<CombinedCellRenderChunkMember>();
                var members = chunkDefs[i].Members ?? System.Array.Empty<CombinedCellRenderChunkMemberDef>();
                for (int m = 0; m < members.Length; m++)
                {
                    var member = members[m];
                    if (member == null)
                        continue;

                    uint placedRefId = member.PlacedRefId;
                    if (!logicalRefs.Map.TryGetValue(placedRefId, out Entity logicalEntity)
                        || logicalEntity == Entity.Null
                        || !em.Exists(logicalEntity))
                    {
                        continue;
                    }

                    if (!em.HasComponent<LogicalRefContent>(logicalEntity))
                        throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Static chunk member 0x{placedRefId:X8} has no logical content component.");
                    var content = em.GetComponentData<LogicalRefContent>(logicalEntity);
                    if (content.Value.Kind != ContentReferenceKind.Static)
                        throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Non-static ref 0x{placedRefId:X8} was baked into a static render chunk.");

                    Entity[] children = LogicalRefChildUtility.SnapshotChildBuffer(em, logicalEntity);
                    for (int c = 0; c < children.Length; c++)
                    {
                        Entity child = children[c];
                        if (child == Entity.Null || !em.Exists(child) || !em.HasComponent<MaterialMeshInfo>(child))
                            continue;
                        if (!em.HasComponent<ModelPrefabRenderLeaf>(child))
                            continue;

                        var leaf = em.GetComponentData<ModelPrefabRenderLeaf>(child);
                        if (leaf.NodeIndex != member.NodeIndex)
                            continue;
                        int leafMaterialIndex = ResolveCombinedMaterialIndex(leaf.MaterialIndex);
                        if (leafMaterialIndex != chunk.MaterialIndex)
                            throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Static chunk member 0x{placedRefId:X8} node {member.NodeIndex} material variant mismatch.");
                        int leafBucketKey = ResolveTextureBucketKey(leaf.TextureIndex);
                        if (leafBucketKey != chunk.TextureBucketKey)
                            throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Static chunk member 0x{placedRefId:X8} node {member.NodeIndex} texture bucket mismatch.");

                        AttachLogicalChunkLink(em, logicalEntity, chunkEntity);
                        if (!em.HasComponent<CombinedCellRenderSuppressed>(child))
                            em.AddComponent<CombinedCellRenderSuppressed>(child);
                        em.SetComponentEnabled<MaterialMeshInfo>(child, false);
                        pendingMembers.Add(new CombinedCellRenderChunkMember
                        {
                            RenderEntity = child,
                            LogicalRefEntity = logicalEntity,
                            PlacedRefId = placedRefId,
                            NodeIndex = member.NodeIndex,
                        });
                        break;
                    }
                }

                if (pendingMembers.Count == 0)
                    continue;

                DynamicBuffer<CombinedCellRenderChunkMember> memberBuffer = em.GetBuffer<CombinedCellRenderChunkMember>(chunkEntity);
                for (int memberIndex = 0; memberIndex < pendingMembers.Count; memberIndex++)
                    memberBuffer.Add(pendingMembers[memberIndex]);
            }
        }

        static RenderMeshArray CreateRenderMeshArray(CombinedCellRenderChunkDef chunk, Mesh mesh)
        {
            var cache = WorldResources.Cache;
            if (cache == null || cache.CombinedMaterials == null)
                throw new System.InvalidOperationException("[VVardenfell][CombinedRender] Runtime combined materials are not loaded.");

            if (WorldResources.RefBucketIndexByKey == null
                || !WorldResources.RefBucketIndexByKey.TryGetValue(chunk.TextureBucketKey, out int bucketIndex))
            {
                throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Missing texture bucket key 0x{chunk.TextureBucketKey:X8} for combined chunk.");
            }

            var materials = new Material[WorldResources.CombinedRenderVariantCount];
            int materialStart = bucketIndex * WorldResources.CombinedRenderVariantCount;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = cache.CombinedMaterials[materialStart + i];

            return new RenderMeshArray(materials, new[] { mesh });
        }

        static Entity CreateChunkEntity(
            EntityManager em,
            int2 coord,
            CombinedCellRenderChunkDef chunk,
            RenderMeshArray rma,
            bool active,
            int index)
        {
            Entity entity = em.CreateEntity();
            em.SetName(entity, $"CombinedCellRender({coord.x},{coord.y})#{index}");
            RenderMeshUtility.AddComponents(
                entity,
                em,
                WorldResources.Desc,
                rma,
                MaterialMeshInfo.FromRenderMeshArrayIndices(math.max(0, chunk.MaterialIndex), 0));
            em.SetComponentEnabled<MaterialMeshInfo>(entity, active);

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float3 cellOrigin = new(coord.x * cellMeters, 0f, coord.y * cellMeters);
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(cellOrigin, quaternion.identity, 1f));
            em.AddComponentData(entity, new LocalToWorld { Value = float4x4.Translate(cellOrigin) });
            em.AddComponentData(entity, new RenderBounds
            {
                Value = new AABB
                {
                    Center = new float3(chunk.BoundsCenterX, chunk.BoundsCenterY, chunk.BoundsCenterZ),
                    Extents = new float3(chunk.BoundsExtentsX, chunk.BoundsExtentsY, chunk.BoundsExtentsZ),
                }
            });
            em.AddComponentData(entity, new CellLink { Value = coord });
            em.AddComponentData(entity, new CombinedCellRenderChunk
            {
                Cell = coord,
                TileX = chunk.TileX,
                TileY = chunk.TileY,
                MaterialIndex = chunk.MaterialIndex,
                TextureBucketKey = chunk.TextureBucketKey,
                Disabled = 0,
            });
            em.AddBuffer<CombinedCellRenderChunkMember>(entity);
            em.AddComponent<Unity.Transforms.Static>(entity);
            WorldResources.RegisterExteriorCellEntity(coord, entity);
            return entity;
        }

        static void AttachLogicalChunkLink(EntityManager em, Entity logicalEntity, Entity chunkEntity)
        {
            DynamicBuffer<CombinedCellRenderLink> links = em.HasBuffer<CombinedCellRenderLink>(logicalEntity)
                ? em.GetBuffer<CombinedCellRenderLink>(logicalEntity)
                : em.AddBuffer<CombinedCellRenderLink>(logicalEntity);

            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].Chunk == chunkEntity)
                    return;
            }

            links.Add(new CombinedCellRenderLink { Chunk = chunkEntity });
        }

        static int ResolveTextureBucketKey(int textureIndex)
        {
            int bucketIndex;
            if (textureIndex < 0)
            {
                bucketIndex = WorldResources.FallbackBucketSlice.x;
            }
            else
            {
                if (!WorldResources.TexBucketInfo.IsCreated)
                    throw new System.InvalidOperationException("[VVardenfell][CombinedRender] Texture buckets are not loaded.");
                if ((uint)textureIndex >= (uint)WorldResources.TexBucketInfo.Length)
                    throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Missing texture index {textureIndex}.");
                bucketIndex = WorldResources.TexBucketInfo[textureIndex].x;
            }

            if (WorldResources.RefBucketKeys == null || (uint)bucketIndex >= (uint)WorldResources.RefBucketKeys.Length)
                throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Missing runtime bucket {bucketIndex}.");

            return WorldResources.RefBucketKeys[bucketIndex];
        }

        static int ResolveCombinedMaterialIndex(int materialIndex)
        {
            var records = WorldResources.Cache?.MaterialRecords;
            if (records == null || (uint)materialIndex >= (uint)records.Length)
                throw new System.InvalidOperationException($"[VVardenfell][CombinedRender] Missing material index {materialIndex}.");

            uint flags = records[materialIndex].Flags;
            return (flags & CacheFormat.MatFlagAlphaBlend) != 0 ? 1 : 0;
        }
    }
}
