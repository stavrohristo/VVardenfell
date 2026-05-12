using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using Object = UnityEngine.Object;

namespace VVardenfell.Importer.Bake
{
    internal static class RuntimeWorldCellBlobBakery
    {
        public static void Write(string path, BakeManifest.BakedCellState[] cellStates)
        {
            using var blob = Build(cellStates);
            RuntimeWorldCellBlobFile.Write(path, blob);
        }

        static BlobAssetReference<RuntimeWorldCellBlob> Build(BakeManifest.BakedCellState[] cellStates)
        {
            var sources = LoadSectionSources(cellStates);
            return BuildBlob(sources);
        }

        static List<CellSource> LoadSectionSources(BakeManifest.BakedCellState[] cellStates)
        {
            if (cellStates == null || cellStates.Length == 0)
                throw new InvalidDataException("[VVardenfell][WorldCellBlob] cannot bake without cell states.");

            var states = new List<BakeManifest.BakedCellState>(cellStates.Length);
            for (int i = 0; i < cellStates.Length; i++)
            {
                if (cellStates[i] != null)
                    states.Add(cellStates[i]);
            }

            states.Sort(CompareCellStates);
            var result = new List<CellSource>(states.Count);
            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                string path = ResolveCellSectionPath(state);
                result.Add(ReadSection(path, state.IsInterior, state.InteriorId ?? string.Empty));
            }

            return result;
        }

        static int CompareCellStates(BakeManifest.BakedCellState a, BakeManifest.BakedCellState b)
        {
            int interiorCompare = a.IsInterior.CompareTo(b.IsInterior);
            if (interiorCompare != 0)
                return interiorCompare;
            if (!a.IsInterior)
            {
                int x = a.GridX.CompareTo(b.GridX);
                return x != 0 ? x : a.GridY.CompareTo(b.GridY);
            }
            return string.Compare(a.InteriorId ?? string.Empty, b.InteriorId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        static CellSource ReadSection(string path, bool isInterior, string cellId)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] missing runtime cell section '{path}'; rebake required.");

            using var world = new World($"VV.WorldCellBlobRead({Path.GetFileName(path)})");
            RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorld(path, out var renderReferences, out var bytes);
            if (renderReferences == null || renderReferences.Length != 0)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' contains serialized Unity render object references; rebake required for direct runtime render IDs.");
            var unityObjects = CreateValidationObjects(renderReferences);
            try
            {
                unsafe
                {
                    fixed (byte* ptr = bytes)
                    {
                        using var reader = new MemoryBinaryReader(ptr, bytes.Length);
                        var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                        SerializeUtility.DeserializeWorld(tx, reader, unityObjects);
                        world.EntityManager.EndExclusiveEntityTransaction();
                    }
                }
            }
            finally
            {
                DestroyValidationObjects(unityObjects);
            }

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' must contain exactly one header.");
            Entity entity = query.GetSingletonEntity();
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' pipeline {header.PipelineVersion} does not match {CacheFormat.WorldBakePipelineVersion}; rebake required.");
            if ((header.IsInterior != 0) != isInterior)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section '{path}' interior flag mismatch.");

            return new CellSource
            {
                Header = header,
                Refs = CopyRefs(em, entity),
                Doors = CopyDoors(em, entity),
                LockStates = CopyLockStates(em, entity),
                CapturedSouls = CopyCapturedSouls(em, entity),
                TerrainHeights = CopyTerrainHeights(em, entity, header.Flags),
                WorldMapSamples = CopyWorldMapSamples(em, entity, header.Flags),
            };
        }

        static BlobAssetReference<RuntimeWorldCellBlob> BuildBlob(List<CellSource> sources)
        {
            int refCount = 0;
            int doorCount = 0;
            int lockStateCount = 0;
            int capturedSoulCount = 0;
            int terrainHeightCount = 0;
            int worldMapSampleCount = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                refCount += sources[i].Refs.Length;
                doorCount += sources[i].Doors.Length;
                lockStateCount += sources[i].LockStates.Length;
                capturedSoulCount += sources[i].CapturedSouls.Length;
                terrainHeightCount += sources[i].TerrainHeights.Length;
                worldMapSampleCount += sources[i].WorldMapSamples.Length;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref RuntimeWorldCellBlob root = ref builder.ConstructRoot<RuntimeWorldCellBlob>();
                var cells = builder.Allocate(ref root.Cells, sources.Count);
                var refs = builder.Allocate(ref root.Refs, refCount);
                var doors = builder.Allocate(ref root.Doors, doorCount);
                var lockStates = builder.Allocate(ref root.LockStates, lockStateCount);
                var capturedSouls = builder.Allocate(ref root.CapturedSouls, capturedSoulCount);
                var terrainHeights = builder.Allocate(ref root.TerrainHeights, terrainHeightCount);
                var worldMapSamples = builder.Allocate(ref root.WorldMapSamples, worldMapSampleCount);
                var exteriorLookup = new List<RuntimeWorldCellExteriorLookupBlob>();
                var interiorLookup = new List<RuntimeContentHashLookupBlob>();

                int refCursor = 0;
                int doorCursor = 0;
                int lockCursor = 0;
                int soulCursor = 0;
                int heightCursor = 0;
                int mapCursor = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    int firstRef = refCursor;
                    int firstDoor = doorCursor;
                    int firstLock = lockCursor;
                    int firstSoul = soulCursor;
                    int firstHeight = heightCursor;
                    int firstMap = mapCursor;

                    for (int r = 0; r < source.Refs.Length; r++)
                        refs[refCursor++] = source.Refs[r];
                    for (int d = 0; d < source.Doors.Length; d++)
                        doors[doorCursor++] = source.Doors[d];
                    for (int l = 0; l < source.LockStates.Length; l++)
                        lockStates[lockCursor++] = source.LockStates[l];
                    for (int s = 0; s < source.CapturedSouls.Length; s++)
                        capturedSouls[soulCursor++] = source.CapturedSouls[s];
                    for (int h = 0; h < source.TerrainHeights.Length; h++)
                        terrainHeights[heightCursor++] = source.TerrainHeights[h];
                    for (int m = 0; m < source.WorldMapSamples.Length; m++)
                        worldMapSamples[mapCursor++] = source.WorldMapSamples[m];

                    bool isInterior = source.Header.IsInterior != 0;
                    var coord = new int2(source.Header.GridX, source.Header.GridY);
                    cells[i] = new RuntimeWorldCellDefBlob
                    {
                        ExteriorCoord = coord,
                        CellId = source.Header.CellId,
                        InteriorCellId = isInterior ? source.Header.CellId : default,
                        InteriorCellHash = source.Header.InteriorCellHash,
                        IsInterior = (byte)(isInterior ? 1 : 0),
                        HasTerrain = (byte)((source.Header.Flags & CacheFormat.CellFlagHasTerrain) != 0 ? 1 : 0),
                        HasWorldMap = (byte)((source.Header.Flags & CacheFormat.CellFlagHasWorldMap) != 0 ? 1 : 0),
                        HasStaticCollider = (byte)((source.Header.Flags & CacheFormat.CellFlagHasStaticCollision) != 0 ? 1 : 0),
                        HasTerrainCollider = (byte)((source.Header.Flags & CacheFormat.CellFlagHasTerrain) != 0 ? 1 : 0),
                        FirstRefIndex = firstRef,
                        RefCount = source.Refs.Length,
                        FirstDoorIndex = firstDoor,
                        DoorCount = source.Doors.Length,
                        FirstLockStateIndex = firstLock,
                        LockStateCount = source.LockStates.Length,
                        FirstCapturedSoulIndex = firstSoul,
                        CapturedSoulCount = source.CapturedSouls.Length,
                        FirstTerrainHeightIndex = firstHeight,
                        TerrainHeightCount = source.TerrainHeights.Length,
                        FirstWorldMapSampleIndex = firstMap,
                        WorldMapSampleCount = source.WorldMapSamples.Length,
                        Environment = CopyEnvironment(source.Header.Environment),
                    };

                    if (isInterior)
                        interiorLookup.Add(new RuntimeContentHashLookupBlob { Hash = source.Header.InteriorCellHash, HandleValue = i });
                    else
                        exteriorLookup.Add(new RuntimeWorldCellExteriorLookupBlob { Coord = coord, CellIndex = i });
                }

                CopyExteriorLookup(ref builder, ref root.ExteriorCellLookup, exteriorLookup);
                CopyInteriorLookup(ref builder, ref root.InteriorCellHashLookup, interiorLookup);
                return builder.CreateBlobAssetReference<RuntimeWorldCellBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        static RefEntry[] CopyRefs(EntityManager em, Entity entity)
        {
            var doorsByPlacedRef = BuildDoorIndexByPlacedRef(em, entity);
            var logicals = CopyOrderedLogicalRefs(em, entity);
            var result = new RefEntry[logicals.Length];
            for (int i = 0; i < result.Length; i++)
            {
                Entity logical = logicals[i];
                var content = em.GetComponentData<LogicalRefContent>(logical).Value;
                var identity = em.GetComponentData<PlacedRefIdentity>(logical);
                var transform = em.GetComponentData<PlacedRefInitialTransform>(logical);
                int modelPrefabIndex = ResolveModelPrefabIndex(em, logical);
                int collisionIndex = ResolveCollisionIndex(em, logical);
                result[i] = new RefEntry
                {
                    ModelPrefabIndex = modelPrefabIndex,
                    LocalMeshIndex = -1,
                    LocalMaterialIndex = -1,
                    SliceIndex = -1,
                    CollisionIndex = collisionIndex,
                    PlacedRefId = identity.Value,
                    DoorMetaIndex = doorsByPlacedRef.TryGetValue(identity.Value, out int doorIndex) ? doorIndex : -1,
                    ContentHandleValue = content.HandleValue,
                    ContentKind = (int)content.Kind,
                    PosX = transform.Position.x,
                    PosY = transform.Position.y,
                    PosZ = transform.Position.z,
                    RotX = transform.Rotation.value.x,
                    RotY = transform.Rotation.value.y,
                    RotZ = transform.Rotation.value.z,
                    RotW = transform.Rotation.value.w,
                    Scale = transform.Scale,
                    SpawnModeRaw = modelPrefabIndex >= 0 ? (int)RefSpawnMode.ModelPrefab : (int)RefSpawnMode.LogicalOnly,
                };
            }
            return result;
        }

        static RuntimeWorldDoorRefDefBlob[] CopyDoors(EntityManager em, Entity entity)
        {
            var logicals = CopyOrderedLogicalRefs(em, entity);
            var doors = new List<RuntimeWorldDoorRefDefBlob>();
            for (int i = 0; i < logicals.Length; i++)
            {
                Entity logical = logicals[i];
                if (!em.HasComponent<RuntimeCellSectionDoorMetadata>(logical))
                    continue;
                var door = em.GetComponentData<RuntimeCellSectionDoorMetadata>(logical);
                uint placedRefId = em.GetComponentData<PlacedRefIdentity>(logical).Value;
                doors.Add(new RuntimeWorldDoorRefDefBlob
                {
                    PlacedRefId = placedRefId,
                    Flags = door.Flags,
                    DestPosX = door.DestinationPosition.x,
                    DestPosY = door.DestinationPosition.y,
                    DestPosZ = door.DestinationPosition.z,
                    DestRotX = door.DestinationRotation.value.x,
                    DestRotY = door.DestinationRotation.value.y,
                    DestRotZ = door.DestinationRotation.value.z,
                    DestRotW = door.DestinationRotation.value.w,
                    DestinationCellId = door.DestinationCellId,
                    DestinationCellHash = string.IsNullOrWhiteSpace(door.DestinationCellId.ToString()) ? 0UL : RuntimeContentStableHash.HashInteriorCellId(door.DestinationCellId.ToString()),
                });
            }
            return doors.ToArray();
        }

        static RuntimeWorldPlacedRefLockStateBlob[] CopyLockStates(EntityManager em, Entity entity)
        {
            var logicals = CopyOrderedLogicalRefs(em, entity);
            var states = new List<RuntimeWorldPlacedRefLockStateBlob>();
            for (int i = 0; i < logicals.Length; i++)
            {
                Entity logical = logicals[i];
                if (!em.HasComponent<PlacedRefLockState>(logical))
                    continue;
                var item = em.GetComponentData<PlacedRefLockState>(logical);
                states.Add(new RuntimeWorldPlacedRefLockStateBlob
                {
                    PlacedRefId = em.GetComponentData<PlacedRefIdentity>(logical).Value,
                    LockLevel = item.LockLevel,
                    Locked = item.Locked,
                    KeyId = item.KeyId,
                    TrapId = item.TrapId,
                });
            }
            return states.ToArray();
        }

        static RuntimeWorldPlacedRefCapturedSoulBlob[] CopyCapturedSouls(EntityManager em, Entity entity)
        {
            var logicals = CopyOrderedLogicalRefs(em, entity);
            var souls = new List<RuntimeWorldPlacedRefCapturedSoulBlob>();
            for (int i = 0; i < logicals.Length; i++)
            {
                Entity logical = logicals[i];
                if (!em.HasComponent<PlacedRefCapturedSoul>(logical))
                    continue;
                var item = em.GetComponentData<PlacedRefCapturedSoul>(logical);
                souls.Add(new RuntimeWorldPlacedRefCapturedSoulBlob
                {
                    PlacedRefId = em.GetComponentData<PlacedRefIdentity>(logical).Value,
                    SoulId = item.SoulId,
                    SoulIdHash = RuntimeContentStableHash.HashId(item.SoulId.ToString()),
                });
            }
            return souls.ToArray();
        }

        static float[] CopyTerrainHeights(EntityManager em, Entity entity, uint flags)
        {
            if ((flags & CacheFormat.CellFlagHasTerrain) == 0)
                return Array.Empty<float>();
            Entity terrain = RequireSectionEntity<RuntimeCellSectionTerrainTag>(em, entity, "terrain");
            var buffer = em.GetBuffer<RuntimeCellSectionTerrainHeight>(terrain);
            var result = new float[buffer.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = buffer[i].Value;
            return result;
        }

        static sbyte[] CopyWorldMapSamples(EntityManager em, Entity entity, uint flags)
        {
            if ((flags & CacheFormat.CellFlagHasWorldMap) == 0)
                return Array.Empty<sbyte>();
            Entity terrain = RequireSectionEntity<RuntimeCellSectionTerrainTag>(em, entity, "terrain");
            var buffer = em.GetBuffer<RuntimeCellSectionWorldMapSample>(terrain);
            var result = new sbyte[buffer.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = buffer[i].Value;
            return result;
        }

        static Dictionary<uint, int> BuildDoorIndexByPlacedRef(EntityManager em, Entity section)
        {
            var logicals = CopyOrderedLogicalRefs(em, section);
            var result = new Dictionary<uint, int>();
            int index = 0;
            for (int i = 0; i < logicals.Length; i++)
            {
                Entity logical = logicals[i];
                if (!em.HasComponent<RuntimeCellSectionDoorMetadata>(logical))
                    continue;
                uint placedRefId = em.GetComponentData<PlacedRefIdentity>(logical).Value;
                result[placedRefId] = index++;
            }
            return result;
        }

        static Entity[] CopyOrderedLogicalRefs(EntityManager em, Entity section)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>(),
                ComponentType.ReadOnly<RuntimeCellSectionRefOrder>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var members = query.ToComponentDataArray<RuntimeCellSectionMember>(Allocator.Temp);
            using var orders = query.ToComponentDataArray<RuntimeCellSectionRefOrder>(Allocator.Temp);
            var entries = new List<(Entity Entity, int Order)>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                if (members[i].Section == section)
                    entries.Add((entities[i], orders[i].Value));
            }
            entries.Sort((a, b) => a.Order.CompareTo(b.Order));
            var result = new Entity[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                result[i] = entries[i].Entity;
            return result;
        }

        static Entity RequireSectionEntity<T>(EntityManager em, Entity section, string label)
            where T : unmanaged, IComponentData
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var members = query.ToComponentDataArray<RuntimeCellSectionMember>(Allocator.Temp);
            Entity match = Entity.Null;
            for (int i = 0; i < entities.Length; i++)
            {
                if (members[i].Section != section)
                    continue;
                if (match != Entity.Null)
                    throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section has multiple {label} entities.");
                match = entities[i];
            }
            if (match == Entity.Null)
                throw new InvalidDataException($"[VVardenfell][WorldCellBlob] section is missing {label} entity.");
            return match;
        }

        static int ResolveModelPrefabIndex(EntityManager em, Entity logical)
        {
            if (!em.HasBuffer<LogicalRefChild>(logical))
                return -1;
            var children = em.GetBuffer<LogicalRefChild>(logical);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child != Entity.Null && em.Exists(child) && em.HasComponent<RuntimeCellSectionRenderRoot>(child))
                    return em.GetComponentData<RuntimeCellSectionRenderRoot>(child).ModelPrefabIndex;
            }
            return -1;
        }

        static int ResolveCollisionIndex(EntityManager em, Entity logical)
        {
            if (!em.HasBuffer<LogicalRefChild>(logical))
                return -1;
            var children = em.GetBuffer<LogicalRefChild>(logical);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child != Entity.Null && em.Exists(child) && em.HasComponent<RuntimeCellSectionRenderRoot>(child))
                    return em.GetComponentData<RuntimeCellSectionRenderRoot>(child).CollisionIndex;
            }
            return -1;
        }

        static RuntimeWorldCellEnvironmentDefBlob CopyEnvironment(in CellEnvironmentDataBlob source)
            => new RuntimeWorldCellEnvironmentDefBlob
            {
                HasMood = source.HasMood,
                HasWater = source.HasWater,
                AmbientColorRgba = source.AmbientColorRgba,
                DirectionalColorRgba = source.DirectionalColorRgba,
                FogColorRgba = source.FogColorRgba,
                FogDensity = source.FogDensity,
                WaterHeight = source.WaterHeight,
                RegionIdHash = RuntimeContentStableHash.HashId(source.RegionId.ToString()),
            };

        static string ResolveCellSectionPath(BakeManifest.BakedCellState state)
            => !string.IsNullOrWhiteSpace(state?.SectionPath)
                ? state.SectionPath
                : throw new InvalidDataException("[VVardenfell][WorldCellBlob] cell state has no section path; rebake required.");

        static Object[] CreateValidationObjects(RuntimeRenderObjectReference[] references)
        {
            var objects = new Object[references?.Length ?? 0];
            if (objects.Length == 0)
                return objects;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidDataException("[VVardenfell][WorldCellBlob] URP/Lit shader is required to read runtime section render references.");
            for (int i = 0; i < objects.Length; i++)
            {
                var reference = references[i];
                switch (reference.Kind)
                {
                    case RuntimeRenderObjectReferenceKind.Mesh:
                        var mesh = new Mesh { name = $"VV:WorldCellBlobReadMesh[{reference.Index}]" };
                        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                        mesh.triangles = new[] { 0, 1, 2 };
                        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                        objects[i] = mesh;
                        break;
                    case RuntimeRenderObjectReferenceKind.RefMaterial:
                    case RuntimeRenderObjectReferenceKind.CombinedMaterial:
                    case RuntimeRenderObjectReferenceKind.TerrainMaterial:
                        objects[i] = new Material(shader)
                        {
                            name = $"VV:WorldCellBlobReadMaterial[{reference.Kind}:{reference.Index}]",
                            enableInstancing = true,
                        };
                        break;
                    default:
                        throw new InvalidDataException($"[VVardenfell][WorldCellBlob] Unsupported render object reference kind {(int)reference.Kind}.");
                }
            }
            return objects;
        }

        static void DestroyValidationObjects(Object[] objects)
        {
            if (objects == null)
                return;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                    Object.DestroyImmediate(objects[i]);
            }
        }

        static void CopyExteriorLookup(ref BlobBuilder builder, ref BlobArray<RuntimeWorldCellExteriorLookupBlob> destination, List<RuntimeWorldCellExteriorLookupBlob> source)
        {
            source.Sort((a, b) => PackCoord(a.Coord).CompareTo(PackCoord(b.Coord)));
            var dst = builder.Allocate(ref destination, source.Count);
            long previous = long.MinValue;
            for (int i = 0; i < source.Count; i++)
            {
                long key = PackCoord(source[i].Coord);
                if (i > 0 && key == previous)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Duplicate exterior cell coord {source[i].Coord.x},{source[i].Coord.y}.");
                previous = key;
                dst[i] = source[i];
            }
        }

        static void CopyInteriorLookup(ref BlobBuilder builder, ref BlobArray<RuntimeContentHashLookupBlob> destination, List<RuntimeContentHashLookupBlob> source)
        {
            source.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            var dst = builder.Allocate(ref destination, source.Count);
            ulong previous = 0UL;
            for (int i = 0; i < source.Count; i++)
            {
                if (i > 0 && source[i].Hash == previous)
                    throw new InvalidOperationException($"[VVardenfell][WorldCellBlob] Duplicate interior cell hash 0x{source[i].Hash:X16}.");
                previous = source[i].Hash;
                dst[i] = source[i];
            }
        }

        static long PackCoord(int2 coord)
            => ((long)coord.x << 32) ^ (uint)coord.y;

        struct CellSource
        {
            public RuntimeCellSectionHeader Header;
            public RefEntry[] Refs;
            public RuntimeWorldDoorRefDefBlob[] Doors;
            public RuntimeWorldPlacedRefLockStateBlob[] LockStates;
            public RuntimeWorldPlacedRefCapturedSoulBlob[] CapturedSouls;
            public float[] TerrainHeights;
            public sbyte[] WorldMapSamples;
        }
    }
}
