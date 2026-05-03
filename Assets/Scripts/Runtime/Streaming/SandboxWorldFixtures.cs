using System;
using System.Collections.Generic;
using VVardenfell.Core;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Streaming
{
    public sealed class SandboxWorldProfile
    {
        public float3 PlayerStartPosition = WorldBootstrap.DefaultPlayerSpawnPosition();
        public quaternion PlayerStartRotation = quaternion.identity;
        public bool ClearVanillaStaticCollision = true;
        public bool QueueInitialExteriorCells = false;
        public bool SpawnLocalPlayer = true;
        public bool GenerateActorInspectionGrid = false;
        public bool IncludeCreaturesInInspectionGrid = true;
        public bool IncludeNpcsInInspectionGrid = true;
        public string ActorInspectionRepeatActorId = string.Empty;
        public int ActorInspectionRepeatActorCount = 0;
        public int ActorInspectionGridColumns = 60;
        public float ActorInspectionGridSpacing = 1.75f;
        public int2 ActorInspectionExteriorCell = new(-2, -9);
        public float2 ActorInspectionGridOrigin = new(5f, 5f);
        public bool GroundActorInspectionGrid = true;
        public float ActorInspectionGridHeight = 10f;
        public SandboxSpawnSpec[] Spawns = Array.Empty<SandboxSpawnSpec>();
    }

    public struct SandboxDoorDestination
    {
        public bool Enabled;
        public string DestinationCellId;
        public float3 Position;
        public quaternion Rotation;
    }

    public struct SandboxSpawnSpec
    {
        public string ContentId;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public bool IsInterior;
        public int2 ExteriorCell;
        public string InteriorCellId;
        public SandboxDoorDestination DoorDestination;

        public static SandboxSpawnSpec Exterior(string contentId, float3 position)
        {
            return new SandboxSpawnSpec
            {
                ContentId = contentId,
                Position = position,
                Rotation = quaternion.identity,
                Scale = 1f,
                IsInterior = false,
                ExteriorCell = WorldBootstrap.WorldPositionToCell(position),
            };
        }

        public static SandboxSpawnSpec Interior(string contentId, string cellId, float3 position)
        {
            return new SandboxSpawnSpec
            {
                ContentId = contentId,
                Position = position,
                Rotation = quaternion.identity,
                Scale = 1f,
                IsInterior = true,
                InteriorCellId = cellId ?? string.Empty,
            };
        }
    }

    public static class SandboxWorldFixtures
    {
        public static SandboxWorldProfile Active => new()
        {
            PlayerStartPosition = ExteriorCellPosition(new int2(-2, -9), 4f, 10f, 4f),
            PlayerStartRotation = quaternion.identity,
            ClearVanillaStaticCollision = true,
            QueueInitialExteriorCells = false,
            SpawnLocalPlayer = false,
            GenerateActorInspectionGrid = true,
            IncludeCreaturesInInspectionGrid = true,
            IncludeNpcsInInspectionGrid = true,
            ActorInspectionRepeatActorId = "chargen boat guard 2",
            ActorInspectionRepeatActorCount = 3000,
            ActorInspectionGridColumns = 60,
            ActorInspectionGridSpacing = 1.75f,
            ActorInspectionExteriorCell = new int2(-2, -9),
            ActorInspectionGridOrigin = new float2(5f, 5f),
            GroundActorInspectionGrid = true,
            ActorInspectionGridHeight = 10f,
            Spawns = Array.Empty<SandboxSpawnSpec>(),
        };

        internal static float3 ExteriorCellPosition(int2 cell, float localX, float y, float localZ)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            return new float3(
                cell.x * cellMeters + localX,
                y,
                cell.y * cellMeters + localZ);
        }
    }

    internal static class SandboxWorldFixtureApplier
    {
        const uint SandboxPlacedRefBase = 0x40000000u;

        public static void Apply(CacheLoader cache, WorldBootstrapPreloadResult preload, SandboxWorldProfile profile)
        {
            if (cache == null || preload == null || profile == null)
                return;

            var contentDb = cache.ContentDatabase ?? RuntimeContentDatabase.Active;
            if (contentDb == null)
            {
                Debug.LogWarning("[VVardenfell][Sandbox] runtime content database is unavailable; sandbox refs will be empty.");
                return;
            }

            var exteriorCells = BuildExteriorCellLookup(cache, preload);
            var interiorCells = BuildInteriorCellLookup(cache, preload);
            var exteriorRefs = new Dictionary<int2, List<RefEntry>>();
            var interiorRefs = new Dictionary<string, List<RefEntry>>(StringComparer.OrdinalIgnoreCase);
            var exteriorDoors = new Dictionary<int2, List<DoorRefEntry>>();
            var interiorDoors = new Dictionary<string, List<DoorRefEntry>>(StringComparer.OrdinalIgnoreCase);

            ClearVanillaRefs(preload, profile.ClearVanillaStaticCollision);

            var modelLookup = WorldModelPrefabUtility.BuildModelDescriptorLookup(cache.ModelPrefabCatalog?.Records);
            var spawns = BuildSpawnList(contentDb, profile, exteriorCells);
            for (int i = 0; i < spawns.Length; i++)
            {
                if (!TryBuildRef(cache, contentDb, modelLookup, spawns[i], i, out var entry, out var door, out bool hasDoor))
                    continue;

                if (spawns[i].IsInterior)
                {
                    string cellId = spawns[i].InteriorCellId ?? string.Empty;
                    if (!interiorCells.ContainsKey(cellId))
                    {
                        Debug.LogWarning($"[VVardenfell][Sandbox] spawn '{spawns[i].ContentId}' targets missing interior '{cellId}'.");
                        continue;
                    }

                    Add(interiorRefs, cellId, entry);
                    if (hasDoor)
                    {
                        entry.DoorMetaIndex = AddDoor(interiorDoors, cellId, door);
                        ReplaceLast(interiorRefs, cellId, entry);
                    }
                }
                else
                {
                    var coord = spawns[i].ExteriorCell;
                    if (!exteriorCells.ContainsKey(coord))
                    {
                        Debug.LogWarning($"[VVardenfell][Sandbox] spawn '{spawns[i].ContentId}' targets missing exterior cell ({coord.x},{coord.y}).");
                        continue;
                    }

                    Add(exteriorRefs, coord, entry);
                    if (hasDoor)
                    {
                        entry.DoorMetaIndex = AddDoor(exteriorDoors, coord, door);
                        ReplaceLast(exteriorRefs, coord, entry);
                    }
                }
            }

            foreach (var kv in exteriorRefs)
                exteriorCells[kv.Key].Refs = kv.Value.ToArray();
            foreach (var kv in interiorRefs)
                interiorCells[kv.Key].Refs = kv.Value.ToArray();
            foreach (var kv in exteriorDoors)
                exteriorCells[kv.Key].Doors = kv.Value.ToArray();
            foreach (var kv in interiorDoors)
                interiorCells[kv.Key].Doors = kv.Value.ToArray();
        }

        static SandboxSpawnSpec[] BuildSpawnList(
            RuntimeContentDatabase contentDb,
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells)
        {
            var authored = profile.Spawns ?? Array.Empty<SandboxSpawnSpec>();
            if (!profile.GenerateActorInspectionGrid || contentDb == null)
                return authored;

            var result = new List<SandboxSpawnSpec>(authored.Length + contentDb.ActorCount);
            result.AddRange(authored);

            if (!string.IsNullOrWhiteSpace(profile.ActorInspectionRepeatActorId))
            {
                AppendRepeatedActorInspectionGrid(contentDb, profile, exteriorCells, result);
                return result.ToArray();
            }

            int generated = 0;
            for (int i = 0; i < contentDb.ActorCount; i++)
            {
                ref readonly var actor = ref contentDb.Get(ActorDefHandle.FromIndex(i));
                if (!ShouldIncludeActor(profile, actor))
                    continue;

                int column = generated % Math.Max(1, profile.ActorInspectionGridColumns);
                int row = generated / Math.Max(1, profile.ActorInspectionGridColumns);
                float localX = profile.ActorInspectionGridOrigin.x + column * profile.ActorInspectionGridSpacing;
                float localZ = profile.ActorInspectionGridOrigin.y + row * profile.ActorInspectionGridSpacing;
                float height = ResolveInspectionGridHeight(profile, exteriorCells, localX, localZ);
                var position = SandboxWorldFixtures.ExteriorCellPosition(
                    profile.ActorInspectionExteriorCell,
                    localX,
                    height,
                    localZ);

                result.Add(new SandboxSpawnSpec
                {
                    ContentId = actor.Id ?? string.Empty,
                    Position = position,
                    Rotation = quaternion.identity,
                    Scale = 1f,
                    IsInterior = false,
                    ExteriorCell = profile.ActorInspectionExteriorCell,
                });
                generated++;
            }

            return result.ToArray();
        }

        static void AppendRepeatedActorInspectionGrid(
            RuntimeContentDatabase contentDb,
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells,
            List<SandboxSpawnSpec> result)
        {
            string actorId = profile.ActorInspectionRepeatActorId ?? string.Empty;
            if (!contentDb.TryGetActorHandle(actorId, out var actorHandle) || !actorHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Sandbox] repeated actor inspection grid requested missing actor id '{actorId}'.");

            ref readonly var actor = ref contentDb.Get(actorHandle);
            if (actor.Kind != ActorDefKind.Npc)
                throw new InvalidOperationException($"[VVardenfell][Sandbox] repeated actor inspection grid requires an NPC actor, but '{actorId}' is '{actor.Kind}'.");

            int count = Math.Max(0, profile.ActorInspectionRepeatActorCount);
            for (int generated = 0; generated < count; generated++)
            {
                int column = generated % Math.Max(1, profile.ActorInspectionGridColumns);
                int row = generated / Math.Max(1, profile.ActorInspectionGridColumns);
                float localX = profile.ActorInspectionGridOrigin.x + column * profile.ActorInspectionGridSpacing;
                float localZ = profile.ActorInspectionGridOrigin.y + row * profile.ActorInspectionGridSpacing;
                float height = ResolveInspectionGridHeight(profile, exteriorCells, localX, localZ);
                var position = SandboxWorldFixtures.ExteriorCellPosition(
                    profile.ActorInspectionExteriorCell,
                    localX,
                    height,
                    localZ);

                result.Add(new SandboxSpawnSpec
                {
                    ContentId = actor.Id ?? string.Empty,
                    Position = position,
                    Rotation = quaternion.identity,
                    Scale = 1f,
                    IsInterior = false,
                    ExteriorCell = profile.ActorInspectionExteriorCell,
                });
            }

        }

        static float ResolveInspectionGridHeight(
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells,
            float localX,
            float localZ)
        {
            if (profile.GroundActorInspectionGrid
                && exteriorCells != null
                && exteriorCells.TryGetValue(profile.ActorInspectionExteriorCell, out var cell)
                && WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cell, localX, localZ, out float terrainHeight))
            {
                return terrainHeight;
            }

            return profile.ActorInspectionGridHeight;
        }

        static bool ShouldIncludeActor(SandboxWorldProfile profile, in ActorDef actor)
        {
            return actor.Kind switch
            {
                ActorDefKind.Creature => profile.IncludeCreaturesInInspectionGrid,
                ActorDefKind.Npc => profile.IncludeNpcsInInspectionGrid,
                _ => false,
            };
        }

        static void ClearVanillaRefs(WorldBootstrapPreloadResult preload, bool clearStaticCollision)
        {
            ClearCells(preload.ExteriorCells, clearStaticCollision);
            ClearCells(preload.InteriorCells, clearStaticCollision);
        }

        static void ClearCells(CellData[] cells, bool clearStaticCollision)
        {
            if (cells == null)
                return;

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null)
                    continue;

                cell.Refs = Array.Empty<RefEntry>();
                cell.Doors = Array.Empty<DoorRefEntry>();
                cell.CapturedSouls = Array.Empty<PlacedRefSoulEntry>();
                cell.LockStates = Array.Empty<PlacedRefLockEntry>();
                cell.PlacementAudit = null;
                if (clearStaticCollision && cell.StaticColliderBlob.IsCreated)
                {
                    cell.StaticColliderBlob.Dispose();
                    cell.StaticColliderBlob = default;
                }
            }
        }

        static bool TryBuildRef(
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            SandboxSpawnSpec spec,
            int index,
            out RefEntry entry,
            out DoorRefEntry door,
            out bool hasDoor)
        {
            entry = default;
            door = default;
            hasDoor = false;

            string contentId = spec.ContentId ?? string.Empty;
            if (!contentDb.TryResolvePlaceable(contentId, out var content) || !contentDb.IsValid(content))
            {
                Debug.LogWarning($"[VVardenfell][Sandbox] unknown placeable content id '{contentId}'.");
                return false;
            }

            if (!TryGetModelPath(contentDb, content, out string modelPath, out bool modelRequired))
                return false;

            bool hasModel = false;
            var descriptor = default(WorldResources.RuntimeSpawnPrefabDescriptor);
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                hasModel = WorldModelPrefabUtility.TryResolveModelDescriptor(modelLookup, modelPath, out descriptor);
            }

            if (!hasModel && modelRequired)
            {
                Debug.LogWarning($"[VVardenfell][Sandbox] no model prefab is available for '{contentId}' using model '{modelPath}'.");
                return false;
            }

            float scale = math.max(0.0001f, spec.Scale <= 0f ? 1f : spec.Scale);
            entry = new RefEntry
            {
                ModelPrefabIndex = hasModel ? descriptor.ModelPrefabIndex : -1,
                LocalMeshIndex = -1,
                LocalMaterialIndex = -1,
                SliceIndex = -1,
                CollisionIndex = hasModel ? descriptor.CollisionIndex : -1,
                PlacedRefId = SandboxPlacedRefBase + (uint)index + 1u,
                DoorMetaIndex = -1,
                ContentHandleValue = content.HandleValue,
                ContentKind = (int)content.Kind,
                PosX = spec.Position.x,
                PosY = spec.Position.y,
                PosZ = spec.Position.z,
                RotX = spec.Rotation.value.x,
                RotY = spec.Rotation.value.y,
                RotZ = spec.Rotation.value.z,
                RotW = spec.Rotation.value.w,
                Scale = scale,
                SpawnModeRaw = (int)(hasModel ? RefSpawnMode.ModelPrefab : RefSpawnMode.LogicalOnly),
            };

            if (content.Kind == ContentReferenceKind.Door && spec.DoorDestination.Enabled)
            {
                hasDoor = true;
                door = new DoorRefEntry
                {
                    PlacedRefId = entry.PlacedRefId,
                    Flags = DoorRefEntry.FlagTeleport,
                    DestPosX = spec.DoorDestination.Position.x,
                    DestPosY = spec.DoorDestination.Position.y,
                    DestPosZ = spec.DoorDestination.Position.z,
                    DestRotX = spec.DoorDestination.Rotation.value.x,
                    DestRotY = spec.DoorDestination.Rotation.value.y,
                    DestRotZ = spec.DoorDestination.Rotation.value.z,
                    DestRotW = spec.DoorDestination.Rotation.value.w,
                    DestinationCellId = spec.DoorDestination.DestinationCellId ?? string.Empty,
                };
            }

            return true;
        }

        static bool TryGetModelPath(RuntimeContentDatabase contentDb, ContentReference content, out string modelPath, out bool modelRequired)
        {
            modelPath = string.Empty;
            modelRequired = true;
            switch (content.Kind)
            {
                case ContentReferenceKind.Actor:
                    ref readonly var actor = ref contentDb.Get(new ActorDefHandle { Value = content.HandleValue });
                    modelRequired = actor.Kind == ActorDefKind.Creature;
                    modelPath = actor.Kind == ActorDefKind.Creature ? actor.Model : string.Empty;
                    return true;

                case ContentReferenceKind.Activator:
                    modelPath = contentDb.Get(new ActivatorDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Door:
                    modelPath = contentDb.Get(new DoorDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Container:
                    modelPath = contentDb.Get(new ContainerDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Item:
                    modelPath = contentDb.Get(new ItemDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Light:
                    modelPath = contentDb.Get(new LightDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Static:
                    modelPath = contentDb.GetStatic(new GenericRecordDefHandle { Value = content.HandleValue }).Model;
                    return !string.IsNullOrWhiteSpace(modelPath);
                default:
                    Debug.LogWarning($"[VVardenfell][Sandbox] content kind '{content.Kind}' is not supported by sandbox refs.");
                    return false;
            }
        }

        static Dictionary<int2, CellData> BuildExteriorCellLookup(CacheLoader cache, WorldBootstrapPreloadResult preload)
        {
            var result = new Dictionary<int2, CellData>();
            var grid = cache.Manifest.CellGrid ?? Array.Empty<(int X, int Y)>();
            for (int i = 0; i < grid.Length && i < (preload.ExteriorCells?.Length ?? 0); i++)
            {
                var cell = preload.ExteriorCells[i];
                if (cell != null)
                    result[new int2(grid[i].X, grid[i].Y)] = cell;
            }
            return result;
        }

        static Dictionary<string, CellData> BuildInteriorCellLookup(CacheLoader cache, WorldBootstrapPreloadResult preload)
        {
            var result = new Dictionary<string, CellData>(StringComparer.OrdinalIgnoreCase);
            var ids = cache.Manifest.InteriorCellIds ?? Array.Empty<string>();
            for (int i = 0; i < ids.Length && i < (preload.InteriorCells?.Length ?? 0); i++)
            {
                var cell = preload.InteriorCells[i];
                if (cell != null)
                    result[ids[i] ?? string.Empty] = cell;
            }
            return result;
        }

        static void Add<TKey>(Dictionary<TKey, List<RefEntry>> map, TKey key, RefEntry entry)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<RefEntry>();
                map[key] = list;
            }
            list.Add(entry);
        }

        static void AddRange<TKey>(Dictionary<TKey, List<RefEntry>> map, TKey key, RefEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
                return;

            if (!map.TryGetValue(key, out var list))
            {
                list = new List<RefEntry>(entries.Length);
                map[key] = list;
            }
            list.AddRange(entries);
        }

        static void ReplaceLast<TKey>(Dictionary<TKey, List<RefEntry>> map, TKey key, RefEntry entry)
        {
            var list = map[key];
            list[list.Count - 1] = entry;
        }

        static int AddDoor<TKey>(Dictionary<TKey, List<DoorRefEntry>> map, TKey key, DoorRefEntry door)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<DoorRefEntry>();
                map[key] = list;
            }

            int index = list.Count;
            list.Add(door);
            return index;
        }
    }
}
