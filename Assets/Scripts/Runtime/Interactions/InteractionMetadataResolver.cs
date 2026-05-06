using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionMetadataResolver
    {
        public static string BuildFocusPrompt(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, PlayerInteractionFocus focus)
        {
            var kind = (InteractableKind)focus.InteractKind;
            return ResolvePromptDisplayName(ref contentBlob, ref worldCells, entityManager, focus.TargetEntity, kind);
        }

        public static string ResolveDisplayName(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity entity, InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => ResolveDoorName(ref contentBlob, entityManager, entity),
                InteractableKind.LooseItem => LooseCarryableResolver.ResolveDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Container => ActorCorpseLootUtility.IsDeadLootableActor(entityManager, entity)
                    ? ActorCorpseLootUtility.ResolveTitle(ref contentBlob, entityManager, entity)
                    : ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Activator => ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Npc => ResolveActorName(ref contentBlob, entityManager, entity),
                _ => null,
            };
        }

        public static string ResolveKindLabel(InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => "door",
                InteractableKind.LooseItem => "item",
                InteractableKind.Container => "container",
                InteractableKind.Activator => "activator",
                InteractableKind.Npc => "npc",
                _ => "interactable",
            };
        }

        static string ResolvePromptDisplayName(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity entity, InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => ResolveDoorPromptName(ref contentBlob, ref worldCells, entityManager, entity),
                InteractableKind.LooseItem => LooseCarryableResolver.ResolveDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Container => ActorCorpseLootUtility.IsDeadLootableActor(entityManager, entity)
                    ? ActorCorpseLootUtility.ResolveTitle(ref contentBlob, entityManager, entity)
                    : ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Activator => ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity),
                InteractableKind.Npc => ResolveActorName(ref contentBlob, entityManager, entity),
                _ => null,
            };
        }

        static string ResolveDoorPromptName(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "door";

            if (entityManager.HasComponent<DoorInteractable>(entity)
                || (entityManager.HasComponent<DoorAuthoring>(entity)
                    && DoorInteractableResolver.TryResolve(entityManager, ref worldCells, entity, out _)))
            {
                var door = entityManager.HasComponent<DoorInteractable>(entity)
                    ? entityManager.GetComponentData<DoorInteractable>(entity)
                    : DoorInteractableResolver.TryResolve(entityManager, ref worldCells, entity, out DoorInteractable resolvedDoor)
                        ? resolvedDoor
                        : default;
                if (door.IsTeleport != 0 && door.DestinationCellId.Length > 0)
                    return ResolveInteriorDoorPromptName(ref worldCells, door);

                if (door.IsTeleport != 0)
                    return ResolveExteriorDoorPromptName(ref contentBlob, ref worldCells, entityManager, entity, door);
            }

            return ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity);
        }

        static string ResolveDoorName(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity entity)
        {
            return ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity);
        }

        static string ResolveInteriorDoorPromptName(ref RuntimeWorldCellBlob worldCells, in DoorInteractable door)
        {
            if (RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, door.DestinationCellHash, out int cellIndex))
            {
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                FixedString128Bytes cellId = !cell.InteriorCellId.IsEmpty ? cell.InteriorCellId : cell.CellId;
                return TrimInteriorCellPromptName(cellId.ToString());
            }

            if (door.DestinationCellHash != 0UL)
                throw new System.InvalidOperationException($"[VVardenfell][Interaction] teleport destination interior hash 0x{door.DestinationCellHash:X16} is missing from the world cell blob.");

            string destinationCellId = door.DestinationCellId.ToString();
            return TrimInteriorCellPromptName(destinationCellId);
        }

        static string ResolveExteriorDoorPromptName(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity entity, in DoorInteractable door)
        {
            if (TryResolveExteriorDestinationCell(door.DestinationPosition, out int2 destinationCellCoord)
                && RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, destinationCellCoord, out int cellIndex))
            {
                ref RuntimeWorldCellDefBlob destinationCell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                if (destinationCell.Environment.RegionIdHash != 0UL)
                {
                    if (!RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref contentBlob, destinationCell.Environment.RegionIdHash, out var regionHandle) || !regionHandle.IsValid)
                        throw new System.InvalidOperationException($"[VVardenfell][Interaction] exterior door destination region hash 0x{destinationCell.Environment.RegionIdHash:X16} does not resolve.");

                    ref RuntimeRegionDefBlob region = ref RuntimeContentBlobUtility.Get(ref contentBlob, regionHandle);
                    string name = region.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                    string id = region.Id.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }

                if (!destinationCell.CellId.IsEmpty)
                    return destinationCell.CellId.ToString();
            }

            return ResolveAuthoredDisplayName(ref contentBlob, entityManager, entity);
        }

        static bool TryResolveExteriorDestinationCell(float3 destinationPosition, out int2 cellCoord)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            if (cellMeters <= 0f)
            {
                cellCoord = default;
                return false;
            }

            cellCoord = new int2(
                (int)math.floor(destinationPosition.x / cellMeters),
                (int)math.floor(destinationPosition.z / cellMeters));
            return true;
        }

        static string TrimInteriorCellPromptName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            int commaIndex = value.LastIndexOf(',');
            if (commaIndex < 0 || commaIndex >= value.Length - 1)
                return value.Trim();

            return value.Substring(commaIndex + 1).Trim();
        }

        static string ResolveAuthoredDisplayName(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return null;

            if (entityManager.HasComponent<DoorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<DoorAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveDoorDisplayName(ref contentBlob, authoring.Definition);
            }

            if (entityManager.HasComponent<ItemPickupAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ItemPickupAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveItemDisplayName(ref contentBlob, authoring.Definition);
            }

            if (entityManager.HasComponent<ContainerAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ContainerAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveContainerDisplayName(ref contentBlob, authoring.Definition);
            }

            if (entityManager.HasComponent<ActivatorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ActivatorAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveActivatorDisplayName(ref contentBlob, authoring.Definition);
            }

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
            {
                var actor = entityManager.GetComponentData<PassiveActorPresence>(entity);
                return ResolveActorName(ref contentBlob, entityManager, entity, actor);
            }

            return null;
        }

        static string ResolveActorName(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "npc";

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
                return ResolveActorName(ref contentBlob, entityManager, entity, entityManager.GetComponentData<PassiveActorPresence>(entity));

            return "npc";
        }

        static string ResolveActorName(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity entity, in PassiveActorPresence actor)
        {
            var source = entityManager.HasComponent<ActorSpawnSource>(entity)
                ? entityManager.GetComponentData<ActorSpawnSource>(entity)
                : default;
            return RuntimeContentMetadataResolver.ResolveActorDisplayName(ref contentBlob, source.Definition, actor.CanTalk != 0 ? "npc" : "creature");
        }
    }
}
