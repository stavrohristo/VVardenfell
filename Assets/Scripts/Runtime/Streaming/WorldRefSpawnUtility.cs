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
        static readonly HashSet<string> s_DroppedRefWarnings = new();
        static readonly HashSet<string> s_ActorPrefabVisualWarnings = new();
        static readonly HashSet<int> s_DisabledObjectAnimationWarnings = new();

        static readonly ProfilerMarker k_LogicalRefs = new("VV.Spawn.LogicalRefs");
        static readonly ProfilerMarker k_LogicalRefCreate = new("VV.Spawn.LogicalRefs.Create");
        static readonly ProfilerMarker k_LogicalRefLink = new("VV.Spawn.LogicalRefs.LinkChildren");
        static readonly ProfilerMarker k_LogicalRefLookup = new("VV.Spawn.LogicalRefs.Lookup");

        internal static Entity SpawnExteriorRef(EntityManager em, RefEntry entry, int2 coord)
        {
            string cellLabel = $"exterior ({coord.x},{coord.y})";
            return SpawnRef(em, entry, false, coord, default, float3.zero, null, cellLabel);
        }

        internal static void SpawnExteriorRefs(
            EntityManager em,
            RefEntry[] refs,
            int2 coord,
            Entity[] spawnedRefEntities)
        {
            if (refs == null || spawnedRefEntities == null)
                return;

            string cellLabel = $"exterior ({coord.x},{coord.y})";
            for (int i = 0; i < refs.Length && i < spawnedRefEntities.Length; i++)
                spawnedRefEntities[i] = SpawnRef(em, refs[i], false, coord, default, float3.zero, null, cellLabel);
        }

        internal static Entity SpawnInteriorRef(EntityManager em, RefEntry entry, float3 worldOffset, FixedString128Bytes interiorCellId, List<Entity> spawnedEntities)
        {
            string cellLabel = $"interior '{interiorCellId}'";
            return SpawnRef(em, entry, true, default, interiorCellId, worldOffset, spawnedEntities, cellLabel);
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
                                contentDb,
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
                    if ((!logicalOnlyRef || contentReference.Kind == ContentReferenceKind.Actor)
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
            if (!IsLogicalOnlyRef(entry))
                return false;

            return contentReference.Kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Light
                or ContentReferenceKind.LeveledItem
                or ContentReferenceKind.LeveledCreature
                or ContentReferenceKind.Actor;
        }

        static bool IsLogicalOnlyRef(in RefEntry entry)
        {
            if ((RefSpawnMode)entry.SpawnModeRaw != RefSpawnMode.LogicalOnly)
                return false;

            if (entry.ModelPrefabIndex >= 0)
                return false;

            ContentReferenceKind kind = (ContentReferenceKind)entry.ContentKind;
            return kind is ContentReferenceKind.Activator
                or ContentReferenceKind.Light
                or ContentReferenceKind.LeveledItem
                or ContentReferenceKind.LeveledCreature
                or ContentReferenceKind.Actor;
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

        static Entity SpawnRef(
            EntityManager em,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            List<Entity> spawnedEntities,
            string cellLabel)
        {
            switch ((RefSpawnMode)entry.SpawnModeRaw)
            {
                case RefSpawnMode.ModelPrefab:
                    return SpawnModelPrefabRef(em, entry, isInterior, exteriorCell, interiorCellId, worldOffset, spawnedEntities, cellLabel);
                case RefSpawnMode.LogicalOnly:
                    return Entity.Null;
                default:
                    WarnUnsupportedSpawnMode(entry, cellLabel);
                    WarnDroppedRef(entry, cellLabel, $"unsupported spawn mode {entry.SpawnModeRaw}");
                    return Entity.Null;
            }
        }

        static Entity SpawnModelPrefabRef(
            EntityManager em,
            RefEntry entry,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            float3 worldOffset,
            List<Entity> spawnedEntities = null,
            string cellLabel = null)
        {
            var prefabs = WorldResources.ModelPrefabs;
            if (prefabs == null || (uint)entry.ModelPrefabIndex >= (uint)prefabs.Length)
            {
                WarnDroppedRef(entry, cellLabel, DescribeModelPrefabFailure(entry, prefabs));
                return Entity.Null;
            }

            if (!WorldBootstrap.EnsureModelPrefabBuilt(em, WorldResources.Cache, entry.ModelPrefabIndex))
            {
                WarnDroppedRef(entry, cellLabel, $"model prefab {entry.ModelPrefabIndex} could not be built");
                return Entity.Null;
            }

            prefabs = WorldResources.ModelPrefabs;
            if (prefabs == null || (uint)entry.ModelPrefabIndex >= (uint)prefabs.Length || prefabs[entry.ModelPrefabIndex] == Entity.Null)
            {
                WarnDroppedRef(entry, cellLabel, $"model prefab {entry.ModelPrefabIndex} was not available after build");
                return Entity.Null;
            }

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
            if (isInterior)
                EnsureRenderEnabled(em, root);

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
                        EnsureRenderEnabled(em, linkedEntity);
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

        static void EnsureRenderEnabled(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<MaterialMeshInfo>(entity))
                return;

            em.SetComponentEnabled<MaterialMeshInfo>(entity, true);
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
            if (entry.SpawnModeRaw == (int)RefSpawnMode.LogicalOnly)
                return;
            if (entry.SpawnModeRaw == (int)RefSpawnMode.ModelPrefab)
                return;

            string key = $"{cellLabel}:{entry.PlacedRefId}:{entry.SpawnModeRaw}";
            if (!s_UnsupportedSpawnModeWarnings.Add(key))
                return;

            string mode = FormatRefSpawnMode(entry.SpawnModeRaw);
            Debug.LogWarning($"[VVardenfell] {cellLabel} ref {entry.PlacedRefId:X8} uses unsupported world spawn mode {mode}; rebuild cache pipeline {CacheFormat.WorldBakePipelineVersion} with model-prefab refs.");
        }

        static void WarnDroppedRef(RefEntry entry, string cellLabel, string reason)
        {
            cellLabel = string.IsNullOrEmpty(cellLabel) ? "unknown cell" : cellLabel;
            reason = string.IsNullOrEmpty(reason) ? "unknown spawn failure" : reason;
            string key = $"{cellLabel}:{entry.PlacedRefId:X8}:{entry.SpawnModeRaw}:{entry.ModelPrefabIndex}:{reason}";
            if (!s_DroppedRefWarnings.Add(key))
                return;

            string mode = FormatRefSpawnMode(entry.SpawnModeRaw);
            string kind = FormatContentReferenceKind(entry.ContentKind);
            Debug.LogWarning(
                $"[VVardenfell][DroppedRef] {cellLabel} ref {entry.PlacedRefId:X8} was not spawned: {reason}. "
                + $"spawnMode={mode} modelPrefab={entry.ModelPrefabIndex} "
                + $"mesh={entry.LocalMeshIndex} material={entry.LocalMaterialIndex} content={kind}:{entry.ContentHandleValue} "
                + $"pos=({entry.PosX:F3}, {entry.PosY:F3}, {entry.PosZ:F3}).");
        }

        static string FormatRefSpawnMode(int raw)
        {
            var mode = (RefSpawnMode)raw;
            return System.Enum.IsDefined(typeof(RefSpawnMode), mode)
                ? mode.ToString()
                : $"unknown({raw})";
        }

        static string FormatContentReferenceKind(int raw)
        {
            if ((uint)raw > byte.MaxValue)
                return $"unknown({raw})";

            var kind = (ContentReferenceKind)(byte)raw;
            return System.Enum.IsDefined(typeof(ContentReferenceKind), kind)
                ? kind.ToString()
                : $"unknown({raw})";
        }

        static string DescribeModelPrefabFailure(in RefEntry entry, Entity[] prefabs)
        {
            if (entry.SpawnModeRaw != (int)RefSpawnMode.ModelPrefab)
                return $"unsupported spawn mode {entry.SpawnModeRaw} routed to model-prefab spawn";
            if (entry.ModelPrefabIndex < 0)
                return "missing model-prefab index";
            if (prefabs == null)
                return "model-prefab table is null";
            if ((uint)entry.ModelPrefabIndex >= (uint)prefabs.Length)
                return $"model-prefab index {entry.ModelPrefabIndex} is outside prefab table length {prefabs.Length}";
            if (prefabs[entry.ModelPrefabIndex] == Entity.Null)
                return $"model-prefab {entry.ModelPrefabIndex} is null";

            return "model-prefab lookup failed";
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
            RuntimeContentDatabase contentDb,
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

            FixedString64Bytes capturedSoulId = default;
            int capturedSoulActorHandleValue = 0;
            if (TryResolveCapturedSoul(contentDb, placedRefId, exteriorCell, isInterior, interiorCellId, out string soulId, out ActorDefHandle actorHandle))
            {
                capturedSoulId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(soulId);
                capturedSoulActorHandleValue = actorHandle.Value;
            }

            bool hasLockState = TryResolveLockState(placedRefId, exteriorCell, isInterior, interiorCellId, out var lockState);

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
                CapturedSoulId = capturedSoulId,
                CapturedSoulActorHandleValue = capturedSoulActorHandleValue,
                HasLockState = hasLockState,
                LockState = lockState,
            };
        }

        static bool TryResolveLockState(
            uint placedRefId,
            int2 exteriorCell,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            out PlacedRefLockState lockState)
        {
            lockState = default;
            if (placedRefId == 0u)
                return false;

            CellData cell = null;
            if (isInterior)
                WorldResources.TryGetInteriorCell(InteriorCellIdHash.Hash(interiorCellId), out cell);
            else
                WorldResources.Cells.TryGetValue(exteriorCell, out cell);

            if (cell?.LockStates == null)
                return false;

            for (int i = 0; i < cell.LockStates.Length; i++)
            {
                var entry = cell.LockStates[i];
                if (entry.PlacedRefId != placedRefId)
                    continue;

                lockState = new PlacedRefLockState
                {
                    LockLevel = entry.LockLevel,
                    Locked = entry.Locked,
                    KeyId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(entry.KeyId),
                    TrapId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(entry.TrapId),
                };
                return true;
            }

            return false;
        }

        static bool TryResolveCapturedSoul(
            RuntimeContentDatabase contentDb,
            uint placedRefId,
            int2 exteriorCell,
            bool isInterior,
            FixedString128Bytes interiorCellId,
            out string soulId,
            out ActorDefHandle actorHandle)
        {
            soulId = null;
            actorHandle = default;
            if (contentDb == null || placedRefId == 0u)
                return false;

            CellData cell = null;
            if (isInterior)
                WorldResources.TryGetInteriorCell(InteriorCellIdHash.Hash(interiorCellId), out cell);
            else
                WorldResources.Cells.TryGetValue(exteriorCell, out cell);

            if (cell?.CapturedSouls == null)
                return false;

            for (int i = 0; i < cell.CapturedSouls.Length; i++)
            {
                var entry = cell.CapturedSouls[i];
                if (entry.PlacedRefId != placedRefId || string.IsNullOrWhiteSpace(entry.SoulId))
                    continue;

                if (!contentDb.TryGetActorHandle(entry.SoulId, out actorHandle) || !actorHandle.IsValid)
                    return false;

                soulId = entry.SoulId;
                return true;
            }

            return false;
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

    }
}
