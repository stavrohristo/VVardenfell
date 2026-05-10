using System;
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
        public static void Apply(CacheLoader cache, SandboxWorldProfile profile, WorldBootstrapPreloadResult preload = null)
        {
            if (cache == null || profile == null)
                return;

            Debug.LogWarning("[VVardenfell][Sandbox] sandbox cell mutation is disabled because runtime cells now stream from DOTS section cache. Sandbox-specific refs need a dedicated runtime spawn request path.");
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

    }
}
