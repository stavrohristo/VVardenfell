using System;
using System.Collections.Generic;
using VVardenfell.Core;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.Streaming
{
    public sealed class SandboxWorldProfile
    {
        public float3 PlayerStartPosition = WorldBootstrap.DefaultPlayerSpawnPosition();
        public quaternion PlayerStartRotation = quaternion.identity;
        public bool ClearVanillaStaticCollision = true;
        public bool QueueInitialExteriorCells = false;
        public int PreloadExteriorCellRadius = 0;
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
        public bool GenerateCombatFactionTeams = false;
        public string CombatFactionAId = string.Empty;
        public string CombatFactionBId = string.Empty;
        public int CombatTeamSize = 0;
        public int2 CombatExteriorCell = new(-2, -9);
        public float2 CombatTeamAOrigin = new(24f, 24f);
        public float2 CombatTeamBOrigin = new(24f, 34f);
        public int CombatTeamColumns = 1;
        public float CombatTeamSpacing = 3f;
        public bool GroundCombatTeams = true;
        public float CombatTeamHeight = 10f;
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
        public static SandboxWorldProfile Active => Get(BootstrapRuntimeMode.Sandbox);

        public static SandboxWorldProfile Get(BootstrapRuntimeMode mode)
        {
            return mode switch
            {
                BootstrapRuntimeMode.Sandbox => BuildActorInspectionSandbox(),
                BootstrapRuntimeMode.CombatSandbox => BuildCombatSandbox(new BattleSimulatorBootSelection(new int2(-6, -1))),
                _ => null,
            };
        }

        public static SandboxWorldProfile Get(BootstrapRuntimeMode mode, BattleSimulatorBootSelection selection)
        {
            return mode switch
            {
                BootstrapRuntimeMode.Sandbox => BuildActorInspectionSandbox(),
                BootstrapRuntimeMode.CombatSandbox => BuildCombatSandbox(selection),
                _ => null,
            };
        }

        static SandboxWorldProfile BuildActorInspectionSandbox()
        {
            return new SandboxWorldProfile
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
        }

        static SandboxWorldProfile BuildCombatSandbox(BattleSimulatorBootSelection selection)
        {
            var cell = selection.ExteriorCell;
            return new SandboxWorldProfile
            {
                PlayerStartPosition = ExteriorCellPosition(cell, 40f, 58f, 62f),
                PlayerStartRotation = quaternion.LookRotationSafe(new float3(0f, 0f, 1f), math.up()),
                ClearVanillaStaticCollision = true,
                QueueInitialExteriorCells = true,
                PreloadExteriorCellRadius = 1,
                SpawnLocalPlayer = false,
                GenerateActorInspectionGrid = false,
                GenerateCombatFactionTeams = false,
                CombatExteriorCell = cell,
                Spawns = Array.Empty<SandboxSpawnSpec>(),
            };
        }

        internal static float3 ExteriorCellPosition(int2 cell, float localX, float y, float localZ)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            return new float3(
                cell.x * cellMeters + localX,
                y,
                cell.y * cellMeters + localZ);
        }

        internal static bool CanSpawnBattleSimulatorActor(ref RuntimeContentBlob content, ref RuntimeActorDefBlob actor)
        {
            if (actor.ScriptIdHash != 0UL)
                return false;
            if (actor.Kind == ActorDefKind.Creature)
                return true;
            return actor.Kind == ActorDefKind.Npc
                   && SandboxWorldFixtureApplier.CanSpawnCombatSandboxNpc(ref content, ref actor);
        }
    }

    internal static class SandboxWorldFixtureApplier
    {
        const uint SandboxPlacedRefBase = 0x40000000u;

        public static void Apply(CacheLoader cache, WorldBootstrapPreloadResult preload, SandboxWorldProfile profile)
        {
            if (cache == null || preload == null || profile == null)
                return;

            if (!cache.ContentBlob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Sandbox] runtime content blob is unavailable; sandbox refs cannot be built.");
            ref RuntimeContentBlob content = ref cache.ContentBlob.Value;

            var exteriorCells = BuildExteriorCellLookup(cache, preload);
            var interiorCells = BuildInteriorCellLookup(cache, preload);
            var exteriorRefs = new Dictionary<int2, List<RefEntry>>();
            var interiorRefs = new Dictionary<string, List<RefEntry>>(StringComparer.OrdinalIgnoreCase);
            var exteriorDoors = new Dictionary<int2, List<DoorRefEntry>>();
            var interiorDoors = new Dictionary<string, List<DoorRefEntry>>(StringComparer.OrdinalIgnoreCase);

            ClearVanillaRefs(preload, profile.ClearVanillaStaticCollision);

            var modelLookup = WorldModelPrefabUtility.BuildModelDescriptorLookup(cache.ModelPrefabCatalog?.Records);
            var spawns = BuildSpawnList(ref content, profile, exteriorCells);
            for (int i = 0; i < spawns.Length; i++)
            {
                if (!TryBuildRef(cache, ref content, modelLookup, spawns[i], i, out var entry, out var door, out bool hasDoor))
                    throw new InvalidOperationException($"[VVardenfell][Sandbox] failed to build sandbox ref for '{spawns[i].ContentId}'.");

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
            ref RuntimeContentBlob content,
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells)
        {
            var authored = profile.Spawns ?? Array.Empty<SandboxSpawnSpec>();
            if (!profile.GenerateActorInspectionGrid && !profile.GenerateCombatFactionTeams)
                return authored;

            int generatedCapacity = content.Actors.Length;
            if (profile.GenerateCombatFactionTeams)
                generatedCapacity = Math.Max(generatedCapacity, profile.CombatTeamSize * 2);
            var result = new List<SandboxSpawnSpec>(authored.Length + generatedCapacity);
            result.AddRange(authored);

            if (profile.GenerateCombatFactionTeams)
                AppendCombatFactionTeams(ref content, profile, exteriorCells, result);

            if (!profile.GenerateActorInspectionGrid)
                return result.ToArray();

            if (!string.IsNullOrWhiteSpace(profile.ActorInspectionRepeatActorId))
            {
                AppendRepeatedActorInspectionGrid(ref content, profile, exteriorCells, result);
                return result.ToArray();
            }

            int generated = 0;
            for (int i = 0; i < content.Actors.Length; i++)
            {
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, ActorDefHandle.FromIndex(i));
                if (!ShouldIncludeActor(profile, ref actor))
                    continue;

                int column = generated % Math.Max(1, profile.ActorInspectionGridColumns);
                int row = generated / Math.Max(1, profile.ActorInspectionGridColumns);
                float localX = profile.ActorInspectionGridOrigin.x + column * profile.ActorInspectionGridSpacing;
                float localZ = profile.ActorInspectionGridOrigin.y + row * profile.ActorInspectionGridSpacing;
                float height = ResolveGroundedExteriorHeight(
                    exteriorCells,
                    profile.ActorInspectionExteriorCell,
                    localX,
                    localZ,
                    profile.GroundActorInspectionGrid,
                    profile.ActorInspectionGridHeight);
                var position = SandboxWorldFixtures.ExteriorCellPosition(
                    profile.ActorInspectionExteriorCell,
                    localX,
                    height,
                    localZ);

                result.Add(new SandboxSpawnSpec
                {
                    ContentId = actor.Id.ToString(),
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
            ref RuntimeContentBlob content,
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells,
            List<SandboxSpawnSpec> result)
        {
            string actorId = profile.ActorInspectionRepeatActorId ?? string.Empty;
            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref content, RuntimeContentStableHash.HashId(actorId), out var actorHandle) || !actorHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Sandbox] repeated actor inspection grid requested missing actor id '{actorId}'.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            if (actor.Kind != ActorDefKind.Npc)
                throw new InvalidOperationException($"[VVardenfell][Sandbox] repeated actor inspection grid requires an NPC actor, but '{actorId}' is '{actor.Kind}'.");

            int count = Math.Max(0, profile.ActorInspectionRepeatActorCount);
            for (int generated = 0; generated < count; generated++)
            {
                int column = generated % Math.Max(1, profile.ActorInspectionGridColumns);
                int row = generated / Math.Max(1, profile.ActorInspectionGridColumns);
                float localX = profile.ActorInspectionGridOrigin.x + column * profile.ActorInspectionGridSpacing;
                float localZ = profile.ActorInspectionGridOrigin.y + row * profile.ActorInspectionGridSpacing;
                float height = ResolveGroundedExteriorHeight(
                    exteriorCells,
                    profile.ActorInspectionExteriorCell,
                    localX,
                    localZ,
                    profile.GroundActorInspectionGrid,
                    profile.ActorInspectionGridHeight);
                var position = SandboxWorldFixtures.ExteriorCellPosition(
                    profile.ActorInspectionExteriorCell,
                    localX,
                    height,
                    localZ);

                result.Add(new SandboxSpawnSpec
                {
                    ContentId = actor.Id.ToString(),
                    Position = position,
                    Rotation = quaternion.identity,
                    Scale = 1f,
                    IsInterior = false,
                    ExteriorCell = profile.ActorInspectionExteriorCell,
                });
            }

        }

        static void AppendCombatFactionTeams(
            ref RuntimeContentBlob content,
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells,
            List<SandboxSpawnSpec> result)
        {
            if (profile.CombatTeamSize <= 0)
                throw new InvalidOperationException("[VVardenfell][CombatSandbox] combat team size must be positive.");
            if (profile.CombatTeamColumns <= 0)
                throw new InvalidOperationException("[VVardenfell][CombatSandbox] combat team columns must be positive.");

            var factionA = ResolveFaction(ref content, profile.CombatFactionAId);
            var factionB = ResolveFaction(ref content, profile.CombatFactionBId);
            SelectMutuallyHostileCombatSandboxTeams(
                ref content,
                profile,
                factionA.Index,
                factionB.Index,
                out var teamA,
                out var teamB);
            var rotationA = quaternion.LookRotationSafe(new float3(0f, 0f, 1f), math.up());
            var rotationB = quaternion.LookRotationSafe(new float3(0f, 0f, -1f), math.up());

            for (int i = 0; i < profile.CombatTeamSize; i++)
            {
                result.Add(BuildCombatTeamSpawn(
                    profile,
                    exteriorCells,
                    teamA[i],
                    profile.CombatTeamAOrigin,
                    i,
                    rotationA));
                result.Add(BuildCombatTeamSpawn(
                    profile,
                    exteriorCells,
                    teamB[i],
                    profile.CombatTeamBOrigin,
                    i,
                    rotationB));
            }
        }

        static GenericRecordDefHandle ResolveFaction(ref RuntimeContentBlob content, string factionId)
        {
            if (string.IsNullOrWhiteSpace(factionId))
                throw new InvalidOperationException("[VVardenfell][CombatSandbox] combat faction id is empty.");

            if (!RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref content, RuntimeContentStableHash.HashId(factionId), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CombatSandbox] unknown faction '{factionId}'.");

            return handle;
        }

        static void SelectMutuallyHostileCombatSandboxTeams(
            ref RuntimeContentBlob content,
            SandboxWorldProfile profile,
            int factionAIndex,
            int factionBIndex,
            out string[] teamA,
            out string[] teamB)
        {
            var candidatesA = CollectSpawnableNpcActorIdsForFaction(ref content, factionAIndex, profile.CombatFactionAId);
            var candidatesB = CollectSpawnableNpcActorIdsForFaction(ref content, factionBIndex, profile.CombatFactionBId);

            float distanceMw = ResolveNearestCombatFormationDistanceMw(profile);

            var selectedA = new List<string>(Math.Min(candidatesA.Count, profile.CombatTeamSize));
            var selectedB = new List<string>(Math.Min(candidatesB.Count, profile.CombatTeamSize));
            var usedB = new bool[candidatesB.Count];
            for (int i = 0; i < candidatesA.Count; i++)
            {
                ref RuntimeActorDefBlob actorA = ref ResolveActorById(ref content, candidatesA[i]);
                for (int j = 0; j < candidatesB.Count; j++)
                {
                    if (usedB[j])
                        continue;

                    ref RuntimeActorDefBlob actorB = ref ResolveActorById(ref content, candidatesB[j]);
                    if (!WouldVanillaAggress(ref content, ref actorA, factionAIndex, factionBIndex, distanceMw)
                        || !WouldVanillaAggress(ref content, ref actorB, factionBIndex, factionAIndex, distanceMw))
                    {
                        continue;
                    }

                    selectedA.Add(candidatesA[i]);
                    selectedB.Add(candidatesB[j]);
                    usedB[j] = true;
                    break;
                }
            }

            if (selectedA.Count == 0)
                throw new InvalidOperationException($"[VVardenfell][CombatSandbox] factions '{profile.CombatFactionAId}' and '{profile.CombatFactionBId}' yielded no mutually hostile spawnable NPC pairs.");

            teamA = new string[profile.CombatTeamSize];
            teamB = new string[profile.CombatTeamSize];
            for (int i = 0; i < profile.CombatTeamSize; i++)
            {
                teamA[i] = selectedA[i % selectedA.Count];
                teamB[i] = selectedB[i % selectedB.Count];
            }
        }

        static List<string> CollectSpawnableNpcActorIdsForFaction(
            ref RuntimeContentBlob content,
            int factionIndex,
            string factionId)
        {
            ref RuntimeFactionDefBlob faction = ref content.Factions[factionIndex];
            var actorIds = new List<string>();
            for (int i = 0; i < content.Actors.Length; i++)
            {
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, ActorDefHandle.FromIndex(i));
                if (actor.Kind != ActorDefKind.Npc || actor.FactionIdHash != faction.IdHash)
                    continue;
                if (actor.ScriptIdHash != 0UL)
                    continue;
                if (!CanSpawnCombatSandboxNpc(ref content, ref actor))
                    continue;

                string actorId = actor.Id.ToString();
                if (!string.IsNullOrWhiteSpace(actorId))
                    actorIds.Add(actorId);
            }

            if (actorIds.Count == 0)
                throw new InvalidOperationException($"[VVardenfell][CombatSandbox] faction '{factionId}' has no spawnable NPCs.");

            return actorIds;
        }

        static ref RuntimeActorDefBlob ResolveActorById(ref RuntimeContentBlob content, string actorId)
        {
            ulong idHash = RuntimeContentStableHash.HashId(actorId);
            for (int i = 0; i < content.Actors.Length; i++)
            {
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, ActorDefHandle.FromIndex(i));
                if (actor.IdHash == idHash)
                    return ref actor;
            }

            throw new InvalidOperationException($"[VVardenfell][CombatSandbox] selected actor '{actorId}' was not found.");
        }

        static bool WouldVanillaAggress(
            ref RuntimeContentBlob content,
            ref RuntimeActorDefBlob actor,
            int sourceFactionIndex,
            int targetFactionIndex,
            float distanceMw)
        {
            int disposition = math.clamp(actor.Disposition + ResolveFactionReaction(ref content, sourceFactionIndex, targetFactionIndex), 0, 100);
            float fightTerm = actor.AiData.Fight
                              + RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightDistanceBase)
                              - RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDistanceMultiplier) * distanceMw
                              + (50f - disposition) * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDispMult);
            return fightTerm >= 100f;
        }

        static float ResolveNearestCombatFormationDistanceMw(SandboxWorldProfile profile)
        {
            int columns = Math.Max(1, profile.CombatTeamColumns);
            int rows = Math.Max(1, (profile.CombatTeamSize + columns - 1) / columns);
            float spacing = math.max(0.1f, profile.CombatTeamSpacing);

            float2 teamAMin = profile.CombatTeamAOrigin;
            float2 teamAMax = profile.CombatTeamAOrigin + new float2((columns - 1) * spacing, (rows - 1) * spacing);
            float2 teamBMin = profile.CombatTeamBOrigin;
            float2 teamBMax = profile.CombatTeamBOrigin + new float2((columns - 1) * spacing, (rows - 1) * spacing);

            float dx = math.max(0f, math.max(teamAMin.x - teamBMax.x, teamBMin.x - teamAMax.x));
            float dz = math.max(0f, math.max(teamAMin.y - teamBMax.y, teamBMin.y - teamAMax.y));
            return math.length(new float2(dx, dz)) / WorldScale.MwUnitsToMeters;
        }

        static int ResolveFactionReaction(ref RuntimeContentBlob content, int sourceFactionIndex, int targetFactionIndex)
        {
            RuntimeContentBlobUtility.RequireRange(sourceFactionIndex, 1, content.Factions.Length, "source faction");
            ref RuntimeFactionDefBlob source = ref content.Factions[sourceFactionIndex];
            ulong targetFactionIdHash = content.Factions[targetFactionIndex].IdHash;
            RuntimeContentBlobUtility.RequireRange(source.FirstReactionIndex, source.ReactionCount, content.FactionReactions.Length, "faction reaction");
            for (int i = 0; i < source.ReactionCount; i++)
            {
                ref RuntimeFactionReactionDefBlob reaction = ref content.FactionReactions[source.FirstReactionIndex + i];
                if (reaction.FactionIdHash == targetFactionIdHash)
                    return reaction.Reaction;
            }

            return 0;
        }

        internal static bool CanSpawnCombatSandboxNpc(ref RuntimeContentBlob content, ref RuntimeActorDefBlob actor)
        {
            RuntimeContentBlobUtility.RequireRange(actor.FirstInventoryIndex, actor.InventoryCount, content.ActorInventoryItems.Length, "actor inventory");
            bool isBeastNpc = IsBeastRace(ref content, actor.RaceIdHash);
            for (int i = 0; i < actor.InventoryCount; i++)
            {
                ref RuntimeContainerItemDefBlob authored = ref content.ActorInventoryItems[actor.FirstInventoryIndex + i];
                if (authored.Count == 0 || authored.ItemIdHash == 0UL)
                    continue;
                int visibleCount = math.abs(authored.Count);
                if (!MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(ref content, authored.ItemIdHash, out var itemContent))
                {
                    if (visibleCount != 1 && RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref content, authored.ItemIdHash, out var leveledHandle) && leveledHandle.IsValid)
                        return false;

                    continue;
                }
                if (itemContent.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = itemContent.HandleValue };
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref content, itemHandle, out var itemEquipment))
                    continue;
                if (!CanAutoEquipCombatSandboxItem(ref content, itemEquipment, isBeastNpc))
                    continue;
                if (itemEquipment.Health > 0 && visibleCount != 1)
                    return false;
            }

            return true;
        }

        static bool CanAutoEquipCombatSandboxItem(ref RuntimeContentBlob content, in ItemEquipmentDef equipment, bool isBeastNpc)
        {
            if (equipment.Slot == ItemEquipmentSlot.None)
                return false;
            if (equipment.Kind == ItemEquipmentKind.Armor && equipment.Health == 0)
                return false;
            if (isBeastNpc && HasBeastForbiddenPart(ref content, equipment))
                return false;

            return equipment.Kind == ItemEquipmentKind.Weapon
                   || equipment.Kind == ItemEquipmentKind.Armor
                   || equipment.Kind == ItemEquipmentKind.Clothing;
        }

        static bool HasBeastForbiddenPart(ref RuntimeContentBlob content, in ItemEquipmentDef equipment)
        {
            RuntimeContentBlobUtility.RequireRange(equipment.FirstBodyPartIndex, equipment.BodyPartCount, content.ItemEquipmentBodyParts.Length, "item equipment body part");
            for (int i = 0; i < equipment.BodyPartCount; i++)
            {
                var part = content.ItemEquipmentBodyParts[equipment.FirstBodyPartIndex + i].Part;
                if (part == ItemEquipmentPartReference.Head
                    || part == ItemEquipmentPartReference.LeftFoot
                    || part == ItemEquipmentPartReference.RightFoot)
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsBeastRace(ref RuntimeContentBlob content, ulong raceIdHash)
        {
            if (raceIdHash == 0UL)
                return false;
            if (!RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref content, raceIdHash, out var raceHandle) || !raceHandle.IsValid)
                return false;

            ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
            return ActorVisualContentRules.IsBeastRaceFlags(race.Flags);
        }

        static SandboxSpawnSpec BuildCombatTeamSpawn(
            SandboxWorldProfile profile,
            Dictionary<int2, CellData> exteriorCells,
            string actorId,
            float2 origin,
            int index,
            quaternion rotation)
        {
            int columns = Math.Max(1, profile.CombatTeamColumns);
            int column = index % columns;
            int row = index / columns;
            float spacing = math.max(0.1f, profile.CombatTeamSpacing);
            float localX = origin.x + column * spacing;
            float localZ = origin.y + row * spacing;
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float worldX = profile.CombatExteriorCell.x * cellMeters + localX;
            float worldZ = profile.CombatExteriorCell.y * cellMeters + localZ;
            var position = new float3(worldX, profile.CombatTeamHeight, worldZ);
            int2 exteriorCell = WorldBootstrap.WorldPositionToCell(position);
            float sampleLocalX = worldX - exteriorCell.x * cellMeters;
            float sampleLocalZ = worldZ - exteriorCell.y * cellMeters;
            float height = ResolveGroundedExteriorHeight(
                exteriorCells,
                exteriorCell,
                sampleLocalX,
                sampleLocalZ,
                profile.GroundCombatTeams,
                profile.CombatTeamHeight);
            position.y = height;

            return new SandboxSpawnSpec
            {
                ContentId = actorId,
                Position = position,
                Rotation = rotation,
                Scale = 1f,
                IsInterior = false,
                ExteriorCell = exteriorCell,
            };
        }

        static float ResolveGroundedExteriorHeight(
            Dictionary<int2, CellData> exteriorCells,
            int2 exteriorCell,
            float localX,
            float localZ,
            bool ground,
            float fallbackHeight)
        {
            if (ground
                && exteriorCells != null
                && exteriorCells.TryGetValue(exteriorCell, out var cell)
                && WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cell, localX, localZ, out float terrainHeight))
            {
                return terrainHeight;
            }

            return fallbackHeight;
        }

        static bool ShouldIncludeActor(SandboxWorldProfile profile, ref RuntimeActorDefBlob actor)
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
            ref RuntimeContentBlob content,
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
            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, RuntimeContentStableHash.HashId(contentId), out var contentRef) || !RuntimeContentBlobUtility.IsValid(ref content, contentRef))
            {
                throw new InvalidOperationException($"[VVardenfell][Sandbox] unknown placeable content id '{contentId}'.");
            }

            if (!TryGetModelPath(ref content, contentRef, out string modelPath, out bool modelRequired))
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
                ContentHandleValue = contentRef.HandleValue,
                ContentKind = (int)contentRef.Kind,
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

            if (contentRef.Kind == ContentReferenceKind.Door && spec.DoorDestination.Enabled)
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

        static bool TryGetModelPath(ref RuntimeContentBlob contentBlob, ContentReference content, out string modelPath, out bool modelRequired)
        {
            modelPath = string.Empty;
            modelRequired = true;
            switch (content.Kind)
            {
                case ContentReferenceKind.Actor:
                    ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, new ActorDefHandle { Value = content.HandleValue });
                    modelRequired = actor.Kind == ActorDefKind.Creature;
                    modelPath = actor.Kind == ActorDefKind.Creature ? actor.Model.ToString() : string.Empty;
                    return true;

                case ContentReferenceKind.Activator:
                    modelPath = RuntimeContentBlobUtility.Get(ref contentBlob, new ActivatorDefHandle { Value = content.HandleValue }).Model.ToString();
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Door:
                    modelPath = RuntimeContentBlobUtility.Get(ref contentBlob, new DoorDefHandle { Value = content.HandleValue }).Model.ToString();
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Container:
                    modelPath = RuntimeContentBlobUtility.Get(ref contentBlob, new ContainerDefHandle { Value = content.HandleValue }).Model.ToString();
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Item:
                    modelPath = RuntimeContentBlobUtility.Get(ref contentBlob, new ItemDefHandle { Value = content.HandleValue }).Model.ToString();
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Light:
                    modelPath = RuntimeContentBlobUtility.Get(ref contentBlob, new LightDefHandle { Value = content.HandleValue }).Model.ToString();
                    return !string.IsNullOrWhiteSpace(modelPath);
                case ContentReferenceKind.Static:
                    modelPath = RuntimeContentBlobUtility.GetStatic(ref contentBlob, new GenericRecordDefHandle { Value = content.HandleValue }).Model.ToString();
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
