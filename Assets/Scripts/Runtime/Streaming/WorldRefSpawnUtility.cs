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
using VVardenfell.Runtime;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.WorldRefs;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldRefSpawnUtility
    {
        const int RefGatherBatchSize = 512;

        static readonly HashSet<string> s_UnsupportedSpawnModeWarnings = new();
        static readonly HashSet<string> s_ActorPrefabVisualWarnings = new();
        static readonly HashSet<int> s_DisabledObjectAnimationWarnings = new();

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

        internal static void SpawnExteriorRefs(
            EntityManager em,
            RefEntry[] refs,
            int2 coord,
            RenderShardRecord[] shardCatalog,
            Entity[] spawnedRefEntities)
        {
            if (refs == null || spawnedRefEntities == null)
                return;

            string cellLabel = $"exterior ({coord.x},{coord.y})";
            Dictionary<int, List<int>> renderShardGroups = null;
            for (int i = 0; i < refs.Length && i < spawnedRefEntities.Length; i++)
            {
                RefEntry entry = refs[i];
                WarnUnsupportedSpawnMode(entry, cellLabel);
                if ((RefSpawnMode)entry.SpawnModeRaw == RefSpawnMode.ModelPrefab)
                {
                    spawnedRefEntities[i] = SpawnModelPrefabRef(em, entry, false, coord, default, float3.zero);
                    continue;
                }

                if (!TryGetRenderShardPrefab(entry, out _))
                {
                    spawnedRefEntities[i] = Entity.Null;
                    continue;
                }

                renderShardGroups ??= new Dictionary<int, List<int>>();
                if (!renderShardGroups.TryGetValue(entry.RenderShardIndex, out var indices))
                {
                    indices = new List<int>();
                    renderShardGroups.Add(entry.RenderShardIndex, indices);
                }

                indices.Add(i);
            }

            if (renderShardGroups == null)
                return;

            foreach (var pair in renderShardGroups)
            {
                if (!TryGetRenderShardPrefab(refs[pair.Value[0]], out Entity prefab))
                    continue;

                using var instances = new NativeArray<Entity>(pair.Value.Count, Allocator.Temp);
                em.Instantiate(prefab, instances);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int refIndex = pair.Value[i];
                    Entity entity = instances[i];
                    spawnedRefEntities[refIndex] = entity;
                    ApplyRenderShardRefData(
                        em,
                        entity,
                        refs[refIndex],
                        false,
                        coord,
                        default,
                        float3.zero,
                        shardCatalog);
                }
            }
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
            return BuildLogicalRefsCore(
                em,
                contentDb,
                new NativeLogicalRefBuildSource(refs, coords),
                childEntities,
                isInterior,
                interiorCellId,
                worldOffset,
                ref logicalRefs,
                progress,
                null,
                out proxyQueueCount);
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
            if (refs == null)
            {
                proxyQueueCount = 0;
                return 0;
            }

            return BuildLogicalRefsCore(
                em,
                contentDb,
                new ManagedLogicalRefBuildSource(refs),
                childEntities,
                isInterior,
                interiorCellId,
                worldOffset,
                ref logicalRefs,
                null,
                spawnedEntities,
                out proxyQueueCount);
        }

        static int BuildLogicalRefsCore<TSource>(
            EntityManager em,
            RuntimeContentDatabase contentDb,
            TSource source,
            Entity[] childEntities,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            ref LogicalRefLookup logicalRefs,
            RuntimeLoadProgress progress,
            List<Entity> spawnedEntities,
            out int proxyQueueCount)
            where TSource : struct, ILogicalRefBuildSource
        {
            using var _ = k_LogicalRefs.Auto();
            proxyQueueCount = 0;
            if (contentDb == null || childEntities == null)
                return 0;

            Entity[][] childSnapshots = LogicalRefChildUtility.SnapshotLogicalChildGroups(em, childEntities);
            var logicalByPlacedRef = new Dictionary<uint, Entity>();
            var placedRefsToResolve = new List<uint>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            int logicalRefCount = 0;
            int refCount = source.Length;
            for (int i = 0; i < refCount; i++)
            {
                if (i >= childEntities.Length)
                    break;

                RefEntry entry = source.GetRef(i);
                if (!TryGetContentReference(entry, out var contentReference) || !contentDb.IsValid(contentReference))
                    continue;

                Entity child = childEntities[i];
                bool hasChild = child != Entity.Null && em.Exists(child);
                bool logicalOnlyRef = IsLogicalOnlyRef(entry, contentReference);
                if (!hasChild && contentReference.Kind != ContentReferenceKind.Actor && !logicalOnlyRef)
                    continue;

                uint placedRefId = entry.PlacedRefId;
                if (placedRefId == 0u)
                    continue;

                if (!logicalByPlacedRef.TryGetValue(placedRefId, out Entity logicalEntity))
                {
                    k_LogicalRefCreate.Begin();
                    try
                    {
                        logicalEntity = LogicalRefEntityFactory.QueueCreate(
                            em,
                            ref ecb,
                            contentDb,
                            BuildLogicalRefDescriptor(
                                contentReference,
                                placedRefId,
                                entry,
                                source.GetExteriorCell(i),
                                isInterior,
                                interiorCellId,
                                worldOffset));
                    }
                    finally
                    {
                        k_LogicalRefCreate.End();
                    }
                    logicalByPlacedRef.Add(placedRefId, logicalEntity);
                    placedRefsToResolve.Add(placedRefId);
                    logicalRefCount++;
                    if (!logicalOnlyRef
                        && LogicalRefEntityFactory.QueueEnsureInteractionProxyQueued(em, ref ecb, logicalEntity, assumeNewEntity: true))
                    {
                        proxyQueueCount++;
                    }
                }

                if (hasChild)
                {
                    k_LogicalRefLink.Begin();
                    try
                    {
                        DisableActorModelPrefabChildren(em, ref ecb, contentReference, childSnapshots[i]);
                        LogicalRefChildUtility.QueueAppendChildren(em, ref ecb, logicalEntity, childSnapshots[i]);
                    }
                    finally
                    {
                        k_LogicalRefLink.End();
                    }
                }

                if (((i + 1) % RefGatherBatchSize) == 0)
                    progress?.Report($"Creating logical placed refs {i + 1}/{refCount}", i + 1, refCount);
            }

            ecb.Playback(em);
            ecb.Dispose();
            ResolveQueuedLogicalRefs(em, placedRefsToResolve, isInterior, ref logicalRefs, spawnedEntities);
            progress?.Report($"Creating logical placed refs {refCount}/{refCount}", refCount, refCount);
            return logicalRefCount;
        }

        static bool IsLogicalOnlyRef(in RefEntry entry, ContentReference contentReference)
        {
            if ((RefSpawnMode)entry.SpawnModeRaw != RefSpawnMode.RenderShard)
                return false;

            if (entry.RenderShardIndex >= 0 || entry.ModelPrefabIndex >= 0)
                return false;

            return contentReference.Kind == ContentReferenceKind.Activator;
        }

        interface ILogicalRefBuildSource
        {
            int Length { get; }
            RefEntry GetRef(int index);
            int2 GetExteriorCell(int index);
        }

        readonly struct NativeLogicalRefBuildSource : ILogicalRefBuildSource
        {
            readonly NativeArray<RefEntry> _refs;
            readonly NativeArray<int2> _coords;

            public NativeLogicalRefBuildSource(NativeArray<RefEntry> refs, NativeArray<int2> coords)
            {
                _refs = refs;
                _coords = coords;
            }

            public int Length => _refs.Length;

            public RefEntry GetRef(int index)
            {
                return _refs[index];
            }

            public int2 GetExteriorCell(int index)
            {
                return _coords[index];
            }
        }

        readonly struct ManagedLogicalRefBuildSource : ILogicalRefBuildSource
        {
            readonly RefEntry[] _refs;

            public ManagedLogicalRefBuildSource(RefEntry[] refs)
            {
                _refs = refs;
            }

            public int Length => _refs.Length;

            public RefEntry GetRef(int index)
            {
                return _refs[index];
            }

            public int2 GetExteriorCell(int index)
            {
                return default;
            }
        }

        static void DisableActorModelPrefabChildren(
            EntityManager em,
            ref EntityCommandBuffer ecb,
            ContentReference contentReference,
            Entity[] children)
        {
            if (contentReference.Kind != ContentReferenceKind.Actor || children == null)
                return;

            int enabledRenderChildren = 0;
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i];
                if (child == Entity.Null || !em.Exists(child))
                    continue;

                if (em.HasComponent<MaterialMeshInfo>(child))
                {
                    if (BootstrapRuntimeModeUtility.IsSandboxMode(WorldResources.RuntimeMode)
                        && em.IsComponentEnabled<MaterialMeshInfo>(child))
                    {
                        enabledRenderChildren++;
                    }

                    ecb.SetComponentEnabled<MaterialMeshInfo>(child, false);
                }

                RuntimeColliderAttachmentUtility.QueueDisablePhysics(em, ref ecb, child);
            }

            if (enabledRenderChildren > 0 && BootstrapRuntimeModeUtility.IsSandboxMode(WorldResources.RuntimeMode))
            {
                string key = $"{contentReference.Kind}:{contentReference.HandleValue}:{enabledRenderChildren}";
                if (s_ActorPrefabVisualWarnings.Add(key))
                {
                    Debug.LogWarning(
                        $"[VVardenfell][ActorDuplicateVisualGuard] actor content handle {contentReference.HandleValue} " +
                        $"had {enabledRenderChildren} enabled model-prefab render children; suppressing them so actor GPU animation rendering is the only visual path.");
                }
            }
        }

        static void ResolveQueuedLogicalRefs(
            EntityManager em,
            List<uint> placedRefsToResolve,
            bool isInterior,
            ref LogicalRefLookup logicalRefs,
            List<Entity> spawnedEntities)
        {
            k_LogicalRefLookup.Begin();
            try
            {
                for (int i = 0; i < placedRefsToResolve.Count; i++)
                {
                    uint placedRefId = placedRefsToResolve[i];
                    Entity logicalEntity = FindLogicalRefByPlacedRef(em, placedRefId);
                    if (logicalEntity == Entity.Null)
                        continue;

                    LogicalRefLookupUtility.AddWithDuplicateWarning(ref logicalRefs, placedRefId, logicalEntity, isInterior);
                    spawnedEntities?.Add(logicalEntity);
                }
            }
            finally
            {
                k_LogicalRefLookup.End();
            }
        }

        static Entity FindLogicalRefByPlacedRef(EntityManager em, uint placedRefId)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (identities[i].Value == placedRefId)
                    return entities[i];
            }

            return Entity.Null;
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
            if (!TryGetRenderShardPrefab(entry, out Entity prefab))
                return Entity.Null;

            var entity = em.Instantiate(prefab);
            ApplyRenderShardRefData(
                em,
                entity,
                entry,
                isInterior,
                exteriorCell,
                interiorCellId,
                worldOffset,
                shardCatalog);
            return entity;
        }

        static bool TryGetRenderShardPrefab(RefEntry entry, out Entity prefab)
        {
            prefab = Entity.Null;
            if (entry.RenderShardIndex < 0 || entry.LocalMeshIndex < 0 || entry.LocalMaterialIndex < 0)
                return false;

            var prefabs = WorldResources.RefPrefabs;
            if (prefabs == null || (uint)entry.RenderShardIndex >= (uint)prefabs.Length)
                return false;

            prefab = prefabs[entry.RenderShardIndex];
            return prefab != Entity.Null;
        }

        static void ApplyRenderShardRefData(
            EntityManager em,
            Entity entity,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            RenderShardRecord[] shardCatalog)
        {
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
            Entity[] linkedEntities = LogicalRefChildUtility.SnapshotLinkedEntityGroup(em, root);
            float3 position = new(entry.PosX, entry.PosY, entry.PosZ);
            position += worldOffset;
            quaternion rotation = new(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);

            em.SetComponentData(root, LocalTransform.FromPositionRotationScale(position, rotation, entry.Scale));
            em.SetComponentData(root, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(entry.Scale))
            });

            ApplyRefRootMetadata(em, root, entry, isInterior, exteriorCell, interiorCellId);
            ApplyObjectAnimationState(em, root, entry);

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
                        WorldResources.RegisterExteriorCellEntity(exteriorCell, linkedEntity);
                    }
                }
            }

            AppendSpawnedEntities(root, spawnedEntities, linkedEntities);
            return root;
        }

        static void ApplyObjectAnimationState(EntityManager em, Entity root, RefEntry entry)
        {
            if (root == Entity.Null || !em.Exists(root) || !IsObjectAnimationRuntimeEligible((ContentReferenceKind)entry.ContentKind))
                return;

            var modelDefs = WorldResources.Cache?.ModelPrefabCatalog?.Records;
            if (modelDefs == null || (uint)entry.ModelPrefabIndex >= (uint)modelDefs.Length)
                return;

            var animation = modelDefs[entry.ModelPrefabIndex]?.ObjectAnimation;
            if (animation == null || animation.Status == ModelObjectAnimationStatus.None)
                return;

            if (!animation.IsEnabled)
            {
                WarnDisabledObjectAnimation(entry.ModelPrefabIndex, animation.DisabledReason);
                return;
            }

            if (!em.HasComponent<ObjectAnimationState>(root))
            {
                em.AddComponentData(root, new ObjectAnimationState
                {
                    ModelPrefabIndex = entry.ModelPrefabIndex,
                    ClipIndex = 0,
                    PreviousTime = 0f,
                    CurrentTime = 0f,
                    Active = 0,
                });
            }
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
                WorldResources.RegisterExteriorCellEntity(exteriorCell, entity);
            }

            var location = new LogicalRefLocation
            {
                ExteriorCell = exteriorCell,
                InteriorCellId = isInterior ? interiorCellId : default,
                InteriorCellHash = isInterior ? InteriorCellIdHash.Hash(interiorCellId) : 0UL,
                IsInterior = (byte)(isInterior ? 1 : 0),
            };
            if (em.HasComponent<LogicalRefLocation>(entity))
                em.SetComponentData(entity, location);
            else
                em.AddComponentData(entity, location);

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
            if (entry.SpawnModeRaw == (int)RefSpawnMode.ModelPrefab
                && IsObjectAnimationRuntimeEligible((ContentReferenceKind)entry.ContentKind))
            {
                return;
            }
            if (entry.SpawnModeRaw == (int)RefSpawnMode.ModelPrefab
                && BootstrapRuntimeModeUtility.IsSandboxMode(WorldResources.RuntimeMode))
                return;

            string key = $"{cellLabel}:{entry.PlacedRefId}:{entry.SpawnModeRaw}";
            if (!s_UnsupportedSpawnModeWarnings.Add(key))
                return;

            string mode = System.Enum.IsDefined(typeof(RefSpawnMode), entry.SpawnModeRaw)
                ? ((RefSpawnMode)entry.SpawnModeRaw).ToString()
                : $"unknown({entry.SpawnModeRaw})";
            Debug.LogWarning($"[VVardenfell] {cellLabel} ref {entry.PlacedRefId:X8} uses unsupported world spawn mode {mode}; rebuild cache pipeline {CacheFormat.WorldBakePipelineVersion} with render-shard refs.");
        }

        static bool IsObjectAnimationRuntimeEligible(ContentReferenceKind kind)
        {
            return kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Door
                or ContentReferenceKind.Container
                or ContentReferenceKind.Light;
        }

        static void WarnDisabledObjectAnimation(int modelPrefabIndex, string reason)
        {
            if (!s_DisabledObjectAnimationWarnings.Add(modelPrefabIndex))
                return;

            Debug.LogWarning(
                $"[VVardenfell][ObjectAnimation] model prefab {modelPrefabIndex} has baked object animation metadata but animation is disabled: {reason}");
        }

        static LogicalRefEntityDescriptor BuildLogicalRefDescriptor(
            ContentReference contentReference,
            uint placedRefId,
            RefEntry entry,
            int2 exteriorCell,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            float3 worldOffset)
        {
            float3 position = new float3(entry.PosX, entry.PosY, entry.PosZ) + worldOffset;
            quaternion rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW);
            bool attachDoor = false;
            DoorInteractable door = default;
            if (contentReference.Kind == ContentReferenceKind.Door)
            {
                attachDoor = !isInterior
                    ? TryResolveDoorInteractable(exteriorCell, entry.PlacedRefId, out door)
                    : TryResolveInteriorDoorInteractable(entry.PlacedRefId, InteriorCellIdHash.Hash(interiorCellId), out door);
            }

            return new LogicalRefEntityDescriptor
            {
                ContentReference = contentReference,
                PlacedRefId = placedRefId,
                Position = position,
                Rotation = rotation,
                Scale = entry.Scale,
                IsInterior = isInterior,
                ExteriorCell = exteriorCell,
                InteriorCellId = interiorCellId,
                AttachDoorInteractable = attachDoor,
                DoorInteractable = door,
            };
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

        static bool TryResolveDoorInteractable(int2 coord, uint placedRefId, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (placedRefId == 0u)
                return false;
            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell == null)
                return false;
            return TryResolveDoorInteractable(cell, placedRefId, out doorInteractable);
        }

        static bool TryResolveInteriorDoorInteractable(uint placedRefId, ulong interiorCellHash, out DoorInteractable doorInteractable)
        {
            doorInteractable = default;
            if (placedRefId == 0u)
                return false;
            if (!WorldResources.TryGetInteriorCell(interiorCellHash, out var cell))
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
                DestinationCellHash = InteriorCellIdHash.Hash(door.DestinationCellId),
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
