using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldRefSpawnUtility
    {
        const int RefGatherBatchSize = 512;

        static readonly HashSet<string> s_UnsupportedSpawnModeWarnings = new();

        static readonly ProfilerMarker k_LogicalRefs = new("VV.Spawn.LogicalRefs");
        static readonly ProfilerMarker k_LogicalRefCreate = new("VV.Spawn.LogicalRefs.Create");
        static readonly ProfilerMarker k_LogicalRefLink = new("VV.Spawn.LogicalRefs.LinkChildren");
        static readonly ProfilerMarker k_LogicalRefLookup = new("VV.Spawn.LogicalRefs.Lookup");

        internal static Entity SpawnExteriorRef(EntityManager em, RefEntry entry, int2 coord, RenderShardRecord[] shardCatalog)
        {
            WarnUnsupportedSpawnMode(entry, $"exterior ({coord.x},{coord.y})");
            return (RefSpawnMode)entry.SpawnModeRaw == RefSpawnMode.ModelPrefab
                ? SpawnModelPrefabRef(em, entry, false, coord, default, float3.zero)
                : SpawnRenderShardRef(em, entry, false, coord, default, float3.zero, shardCatalog);
        }

        internal static Entity SpawnInteriorRef(EntityManager em, RefEntry entry, float3 worldOffset, FixedString128Bytes interiorCellId, List<Entity> spawnedEntities)
        {
            WarnUnsupportedSpawnMode(entry, $"interior '{interiorCellId}'");
            Entity root;
            if ((RefSpawnMode)entry.SpawnModeRaw == RefSpawnMode.ModelPrefab)
            {
                root = SpawnModelPrefabRef(em, entry, true, default, interiorCellId, worldOffset, spawnedEntities);
            }
            else
            {
                root = SpawnRenderShardRef(em, entry, true, default, interiorCellId, worldOffset, WorldResources.Cache?.RenderShardCatalog?.Records);
                AppendSpawnedEntities(em, root, spawnedEntities);
            }

            return root;
        }

        internal static int BuildLogicalRefs(
            EntityManager em,
            RuntimeContentDatabase contentDb,
            NativeArray<RefEntry> refs,
            NativeArray<int2> coords,
            Entity[] childEntities,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            ref LogicalRefLookup logicalRefs,
            RuntimeLoadProgress progress,
            out int proxyQueueCount)
        {
            using var _ = k_LogicalRefs.Auto();
            proxyQueueCount = 0;
            if (contentDb == null || childEntities == null)
                return 0;

            Entity[][] childSnapshots = SnapshotLogicalChildGroups(em, childEntities);
            var logicalByPlacedRef = new Dictionary<uint, Entity>();
            int logicalRefCount = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                if (i >= childEntities.Length)
                    break;

                Entity child = childEntities[i];
                if (child == Entity.Null || !em.Exists(child))
                    continue;

                RefEntry entry = refs[i];
                if (!TryGetContentReference(entry, out var contentReference) || !contentDb.IsValid(contentReference))
                    continue;

                uint placedRefId = entry.PlacedRefId;
                if (placedRefId == 0u)
                    continue;

                if (!logicalByPlacedRef.TryGetValue(placedRefId, out Entity logicalEntity))
                {
                    k_LogicalRefCreate.Begin();
                    try
                    {
                        logicalEntity = CreateLogicalRefEntity(
                            em,
                            contentDb,
                            contentReference,
                            placedRefId,
                            entry,
                            coords[i],
                            isInterior,
                            interiorCellId,
                            worldOffset);
                    }
                    finally
                    {
                        k_LogicalRefCreate.End();
                    }
                    logicalByPlacedRef.Add(placedRefId, logicalEntity);
                    logicalRefCount++;
                    k_LogicalRefLookup.Begin();
                    try
                    {
                        TryAddLogicalLookup(ref logicalRefs, placedRefId, logicalEntity, isInterior);
                    }
                    finally
                    {
                        k_LogicalRefLookup.End();
                    }
                }

                k_LogicalRefLink.Begin();
                try
                {
                    AppendLogicalChildren(em, logicalEntity, childSnapshots[i]);
                    if (logicalByPlacedRef[placedRefId] == logicalEntity
                        && !em.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity)
                        && InteractionActivationProxyBuildUtility.EnsureQueued(em, logicalEntity))
                        proxyQueueCount++;
                }
                finally
                {
                    k_LogicalRefLink.End();
                }

                if (((i + 1) % RefGatherBatchSize) == 0)
                    progress?.Report($"Creating logical placed refs {i + 1}/{refs.Length}", i + 1, refs.Length);
            }

            progress?.Report($"Creating logical placed refs {refs.Length}/{refs.Length}", refs.Length, refs.Length);
            return logicalRefCount;
        }

        internal static int BuildLogicalRefs(
            EntityManager em,
            RuntimeContentDatabase contentDb,
            RefEntry[] refs,
            Entity[] childEntities,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            ref LogicalRefLookup logicalRefs,
            List<Entity> spawnedEntities,
            out int proxyQueueCount)
        {
            using var _ = k_LogicalRefs.Auto();
            proxyQueueCount = 0;
            if (contentDb == null || refs == null || childEntities == null)
                return 0;

            Entity[][] childSnapshots = SnapshotLogicalChildGroups(em, childEntities);
            var logicalByPlacedRef = new Dictionary<uint, Entity>();
            int logicalRefCount = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                if (i >= childEntities.Length)
                    break;

                Entity child = childEntities[i];
                if (child == Entity.Null || !em.Exists(child))
                    continue;

                RefEntry entry = refs[i];
                if (!TryGetContentReference(entry, out var contentReference) || !contentDb.IsValid(contentReference))
                    continue;

                uint placedRefId = entry.PlacedRefId;
                if (placedRefId == 0u)
                    continue;

                if (!logicalByPlacedRef.TryGetValue(placedRefId, out Entity logicalEntity))
                {
                    k_LogicalRefCreate.Begin();
                    try
                    {
                        logicalEntity = CreateLogicalRefEntity(
                            em,
                            contentDb,
                            contentReference,
                            placedRefId,
                            entry,
                            default,
                            isInterior,
                            interiorCellId,
                            worldOffset);
                    }
                    finally
                    {
                        k_LogicalRefCreate.End();
                    }
                    logicalByPlacedRef.Add(placedRefId, logicalEntity);
                    logicalRefCount++;
                    k_LogicalRefLookup.Begin();
                    try
                    {
                        TryAddLogicalLookup(ref logicalRefs, placedRefId, logicalEntity, isInterior);
                    }
                    finally
                    {
                        k_LogicalRefLookup.End();
                    }
                    spawnedEntities?.Add(logicalEntity);
                }

                k_LogicalRefLink.Begin();
                try
                {
                    AppendLogicalChildren(em, logicalEntity, childSnapshots[i]);
                    if (logicalByPlacedRef[placedRefId] == logicalEntity
                        && !em.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity)
                        && InteractionActivationProxyBuildUtility.EnsureQueued(em, logicalEntity))
                        proxyQueueCount++;
                }
                finally
                {
                    k_LogicalRefLink.End();
                }
            }

            return logicalRefCount;
        }

        static Entity SpawnRenderShardRef(
            EntityManager em,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            RenderShardRecord[] shardCatalog)
        {
            var prefabs = WorldResources.RefPrefabs;
            if (prefabs == null || (uint)entry.RenderShardIndex >= (uint)prefabs.Length)
                return Entity.Null;

            var entity = em.Instantiate(prefabs[entry.RenderShardIndex]);
            float3 position = new(entry.PosX, entry.PosY, entry.PosZ);
            position += worldOffset;
            quaternion rotation = new(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, rotation, entry.Scale));
            em.SetComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(entry.Scale))
            });
            em.SetComponentData(entity, MaterialMeshInfo.FromRenderMeshArrayIndices(entry.LocalMaterialIndex, entry.LocalMeshIndex));

            int textureSlice = entry.SliceIndex < 0 ? WorldResources.FallbackBucketSlice.y : WorldResources.TexBucketInfo[entry.SliceIndex].y;
            em.SetComponentData(entity, new TextureSlice { Value = textureSlice });

            int globalMeshIndex = TryResolveGlobalMeshIndex(shardCatalog, entry.RenderShardIndex, entry.LocalMeshIndex);
            var aabb = (uint)globalMeshIndex < (uint)WorldResources.MeshBounds.Length
                ? WorldResources.MeshBounds[globalMeshIndex]
                : new AABB { Center = float3.zero, Extents = new float3(1f) };
            em.SetComponentData(entity, new RenderBounds { Value = aabb });

            ApplyRefRootMetadata(em, entity, entry, isInterior, exteriorCell, interiorCellId);
            return entity;
        }

        static Entity SpawnModelPrefabRef(
            EntityManager em,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            List<Entity> spawnedEntities = null)
        {
            var prefabs = WorldResources.ModelPrefabs;
            if (prefabs == null || (uint)entry.ModelPrefabIndex >= (uint)prefabs.Length)
                return Entity.Null;

            if (!WorldBootstrap.EnsureModelPrefabBuilt(em, WorldResources.Cache, entry.ModelPrefabIndex))
                return Entity.Null;

            prefabs = WorldResources.ModelPrefabs;
            var root = em.Instantiate(prefabs[entry.ModelPrefabIndex]);
            Entity[] linkedEntities = SnapshotLinkedEntityGroup(em, root);
            float3 position = new(entry.PosX, entry.PosY, entry.PosZ);
            position += worldOffset;
            quaternion rotation = new(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);

            em.SetComponentData(root, LocalTransform.FromPositionRotationScale(position, rotation, entry.Scale));
            em.SetComponentData(root, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(entry.Scale))
            });

            ApplyRefRootMetadata(em, root, entry, isInterior, exteriorCell, interiorCellId);

            if (linkedEntities != null)
            {
                for (int i = 0; i < linkedEntities.Length; i++)
                {
                    Entity linkedEntity = linkedEntities[i];
                    if (linkedEntity == root || !em.Exists(linkedEntity))
                        continue;

                    if (isInterior)
                    {
                        if (!em.HasComponent<InteriorCellMember>(linkedEntity))
                            em.AddComponent<InteriorCellMember>(linkedEntity);
                    }
                    else
                    {
                        if (em.HasComponent<CellLink>(linkedEntity))
                            em.SetComponentData(linkedEntity, new CellLink { Value = exteriorCell });
                        else
                            em.AddComponentData(linkedEntity, new CellLink { Value = exteriorCell });
                    }
                }
            }

            AppendSpawnedEntities(root, spawnedEntities, linkedEntities);
            return root;
        }

        static void ApplyRefRootMetadata(
            EntityManager em,
            Entity entity,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId)
        {
            if (entry.PlacedRefId != 0u)
            {
                if (em.HasComponent<PlacedRefIdentity>(entity))
                    em.SetComponentData(entity, new PlacedRefIdentity { Value = entry.PlacedRefId });
                else
                    em.AddComponentData(entity, new PlacedRefIdentity { Value = entry.PlacedRefId });
            }

            if (isInterior)
            {
                if (!em.HasComponent<InteriorCellMember>(entity))
                    em.AddComponent<InteriorCellMember>(entity);
                if (em.HasComponent<CellLink>(entity))
                    em.RemoveComponent<CellLink>(entity);
            }
            else
            {
                if (em.HasComponent<CellLink>(entity))
                    em.SetComponentData(entity, new CellLink { Value = exteriorCell });
                else
                    em.AddComponentData(entity, new CellLink { Value = exteriorCell });
            }

            var colliderBlobs = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Collider>>();
            if ((uint)entry.CollisionIndex < (uint)colliderBlobs.Length && colliderBlobs[entry.CollisionIndex].IsCreated)
            {
                var colliderBlob = colliderBlobs[entry.CollisionIndex];
                RuntimeColliderAttachmentUtility.AttachSource(
                    em,
                    entity,
                    colliderBlob,
                    RuntimeColliderKind.PlacedRef,
                    active: true);
            }
        }

        static void AppendSpawnedEntities(EntityManager em, Entity root, List<Entity> spawnedEntities)
        {
            if (root == Entity.Null || spawnedEntities == null)
                return;

            if (em.Exists(root))
                spawnedEntities.Add(root);
        }

        static void AppendSpawnedEntities(Entity root, List<Entity> spawnedEntities, Entity[] linkedEntities)
        {
            if (root == Entity.Null || spawnedEntities == null)
                return;

            if (linkedEntities == null)
            {
                spawnedEntities.Add(root);
                return;
            }

            for (int i = 0; i < linkedEntities.Length; i++)
            {
                Entity linkedEntity = linkedEntities[i];
                if (linkedEntity != Entity.Null)
                    spawnedEntities.Add(linkedEntity);
            }
        }

        static void WarnUnsupportedSpawnMode(RefEntry entry, string cellLabel)
        {
            if (entry.SpawnModeRaw == (int)RefSpawnMode.RenderShard)
                return;

            string key = $"{cellLabel}:{entry.PlacedRefId}:{entry.SpawnModeRaw}";
            if (!s_UnsupportedSpawnModeWarnings.Add(key))
                return;

            string mode = System.Enum.IsDefined(typeof(RefSpawnMode), entry.SpawnModeRaw)
                ? ((RefSpawnMode)entry.SpawnModeRaw).ToString()
                : $"unknown({entry.SpawnModeRaw})";
            Debug.LogWarning($"[VVardenfell] {cellLabel} ref {entry.PlacedRefId:X8} uses unsupported world spawn mode {mode}; rebuild cache pipeline {CacheFormat.WorldBakePipelineVersion} with render-shard refs.");
        }

        static Entity CreateLogicalRefEntity(
            EntityManager em,
            RuntimeContentDatabase contentDb,
            ContentReference contentReference,
            uint placedRefId,
            RefEntry entry,
            int2 exteriorCell,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            float3 worldOffset)
        {
            Entity logicalEntity = em.CreateEntity();
            //cant do this for now, we're exceeding that max allowed entity names, they maybe can share a name? Probably not
            //em.SetName(logicalEntity, $"LogicalRef({placedRefId:X8})");
            em.AddComponentData(logicalEntity, new LogicalRefTag());
            em.AddComponentData(logicalEntity, new PlacedRefIdentity { Value = placedRefId });
            em.AddComponentData(logicalEntity, new LogicalRefContentRef { Value = contentReference });
            em.AddComponentData(logicalEntity, new LogicalRefLocation
            {
                ExteriorCell = exteriorCell,
                InteriorCellId = interiorCellId,
                IsInterior = (byte)(isInterior ? 1 : 0),
            });
            em.AddBuffer<LogicalRefChild>(logicalEntity);
            float3 position = new float3(entry.PosX, entry.PosY, entry.PosZ) + worldOffset;
            quaternion rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);
            em.AddComponentData(logicalEntity, LocalTransform.FromPositionRotationScale(
                position,
                rotation,
                entry.Scale));
            em.AddComponentData(logicalEntity, new LocalToWorld
            {
                Value = float4x4.TRS(
                    position,
                    rotation,
                    new float3(entry.Scale))
            });

            if (isInterior)
                em.AddComponent<InteriorCellMember>(logicalEntity);
            else
                em.AddComponentData(logicalEntity, new CellLink { Value = exteriorCell });

            AttachLogicalAuthoring(em, logicalEntity, contentDb, contentReference, entry, exteriorCell, isInterior, interiorCellId);
            return logicalEntity;
        }

        static Entity[][] SnapshotLogicalChildGroups(EntityManager em, Entity[] rootChildren)
        {
            var snapshots = new Entity[rootChildren.Length][];
            for (int i = 0; i < rootChildren.Length; i++)
            {
                Entity rootChild = rootChildren[i];
                if (rootChild == Entity.Null || !em.Exists(rootChild))
                    continue;

                snapshots[i] = SnapshotLinkedEntityGroup(em, rootChild) ?? new[] { rootChild };
            }

            return snapshots;
        }

        static Entity[] SnapshotLinkedEntityGroup(EntityManager em, Entity root)
        {
            if (root == Entity.Null || !em.Exists(root))
                return null;

            if (!em.HasBuffer<LinkedEntityGroup>(root))
                return null;

            var linked = em.GetBuffer<LinkedEntityGroup>(root);
            var linkedEntities = new Entity[linked.Length];
            for (int i = 0; i < linked.Length; i++)
                linkedEntities[i] = linked[i].Value;

            return linkedEntities;
        }

        static void AppendLogicalChildren(EntityManager em, Entity logicalEntity, Entity[] children)
        {
            if (children == null)
                return;

            for (int i = 0; i < children.Length; i++)
                LinkLogicalChild(em, logicalEntity, children[i]);
        }

        static void LinkLogicalChild(EntityManager em, Entity logicalEntity, Entity child)
        {
            if (child == Entity.Null || !em.Exists(child))
                return;

            if (em.HasComponent<LogicalRefParent>(child))
                em.SetComponentData(child, new LogicalRefParent { Value = logicalEntity });
            else
                em.AddComponentData(child, new LogicalRefParent { Value = logicalEntity });

            em.GetBuffer<LogicalRefChild>(logicalEntity).Add(new LogicalRefChild { Value = child });
        }

        static void AttachLogicalAuthoring(
            EntityManager em,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            ContentReference contentReference,
            RefEntry entry,
            int2 exteriorCell,
            bool isInterior,
            FixedString128Bytes interiorCellId)
        {
            bool attachDoor = false;
            DoorInteractable door = default;
            if (contentReference.Kind == ContentReferenceKind.Door)
            {
                attachDoor = !isInterior
                    ? TryResolveDoorInteractable(exteriorCell, entry.PlacedRefId, out door)
                    : TryResolveInteriorDoorInteractable(entry.PlacedRefId, interiorCellId, out door);
            }

            LogicalRefAuthoringUtility.TryAttach(em, logicalEntity, contentDb, contentReference, attachDoor, door);
        }

        static bool TryGetContentReference(RefEntry entry, out ContentReference contentReference)
        {
            contentReference = new ContentReference
            {
                Kind = (ContentReferenceKind)entry.ContentKind,
                HandleValue = entry.ContentHandleValue,
            };
            return contentReference.IsValid;
        }

        static void TryAddLogicalLookup(ref LogicalRefLookup logicalRefs, uint placedRefId, Entity logicalEntity, bool isInterior)
        {
            if (!logicalRefs.Map.IsCreated || placedRefId == 0u)
                return;

            if (logicalRefs.Map.TryAdd(placedRefId, logicalEntity))
                return;

            if (logicalRefs.Map.TryGetValue(placedRefId, out var existing) && existing != logicalEntity)
            {
                Debug.LogWarning(
                    $"[VVardenfell] duplicate logical-ref lookup for placed ref 0x{placedRefId:X8} while spawning {(isInterior ? "interior" : "exterior")} content.");
            }
        }

        static bool TryResolveDoorInteractable(int2 coord, uint placedRefId, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (placedRefId == 0u)
                return false;
            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell == null)
                return false;
            return TryResolveDoorInteractable(cell, placedRefId, out doorInteractable);
        }

        static bool TryResolveInteriorDoorInteractable(uint placedRefId, FixedString128Bytes interiorCellId, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (placedRefId == 0u)
                return false;
            if (!WorldResources.InteriorCells.TryGetValue(interiorCellId.ToString(), out var cell) || cell == null)
                return false;
            return TryResolveDoorInteractable(cell, placedRefId, out doorInteractable);
        }

        static bool TryResolveDoorInteractable(CellData cell, uint placedRefId, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (cell?.Refs == null || cell.Doors == null)
                return false;

            for (int i = 0; i < cell.Refs.Length; i++)
            {
                var entry = cell.Refs[i];
                if (entry.PlacedRefId != placedRefId || entry.DoorMetaIndex < 0 || entry.DoorMetaIndex >= cell.Doors.Length)
                    continue;

                doorInteractable = BuildDoorInteractable(cell.Doors[entry.DoorMetaIndex]);
                return true;
            }

            return false;
        }

        static DoorInteractable BuildDoorInteractable(in DoorRefEntry door)
        {
            return new DoorInteractable
            {
                IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
            };
        }

        static int TryResolveGlobalMeshIndex(RenderShardRecord[] shardCatalog, int renderShardIndex, int localMeshIndex)
        {
            if (shardCatalog == null || (uint)renderShardIndex >= (uint)shardCatalog.Length)
                return -1;

            var globalMeshIndices = shardCatalog[renderShardIndex]?.GlobalMeshIndices;
            return globalMeshIndices != null && (uint)localMeshIndex < (uint)globalMeshIndices.Length
                ? globalMeshIndices[localMeshIndex]
                : -1;
        }

    }
}
