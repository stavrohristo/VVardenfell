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
        public static string BuildFocusPrompt(RuntimeContentDatabase contentDb, EntityManager entityManager, PlayerInteractionFocus focus)
        {
            var kind = (InteractableKind)focus.InteractKind;
            return ResolvePromptDisplayName(contentDb, entityManager, focus.TargetEntity, kind);
        }

        public static string ResolveDisplayName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity, InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => ResolveDoorName(contentDb, entityManager, entity),
                InteractableKind.LooseItem => LooseCarryableResolver.ResolveDisplayName(contentDb, entityManager, entity),
                InteractableKind.Container => ActorCorpseLootUtility.IsDeadLootableActor(entityManager, entity)
                    ? ActorCorpseLootUtility.ResolveTitle(contentDb, entityManager, entity)
                    : ResolveAuthoredDisplayName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveAuthoredDisplayName(contentDb, entityManager, entity),
                InteractableKind.Npc => ResolveActorName(contentDb, entityManager, entity),
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

        static string ResolvePromptDisplayName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity, InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => ResolveDoorPromptName(contentDb, entityManager, entity),
                InteractableKind.LooseItem => LooseCarryableResolver.ResolveDisplayName(contentDb, entityManager, entity),
                InteractableKind.Container => ActorCorpseLootUtility.IsDeadLootableActor(entityManager, entity)
                    ? ActorCorpseLootUtility.ResolveTitle(contentDb, entityManager, entity)
                    : ResolveAuthoredDisplayName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveAuthoredDisplayName(contentDb, entityManager, entity),
                InteractableKind.Npc => ResolveActorName(contentDb, entityManager, entity),
                _ => null,
            };
        }

        static string ResolveDoorPromptName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "door";

            if (entityManager.HasComponent<DoorInteractable>(entity)
                || (entityManager.HasComponent<DoorAuthoring>(entity)
                    && DoorInteractableResolver.TryResolve(entityManager, entity, out _)))
            {
                var door = entityManager.HasComponent<DoorInteractable>(entity)
                    ? entityManager.GetComponentData<DoorInteractable>(entity)
                    : DoorInteractableResolver.TryResolve(entityManager, entity, out DoorInteractable resolvedDoor)
                        ? resolvedDoor
                        : default;
                if (door.IsTeleport != 0 && door.DestinationCellId.Length > 0)
                    return ResolveInteriorDoorPromptName(door);

                if (door.IsTeleport != 0)
                    return ResolveExteriorDoorPromptName(contentDb, entityManager, entity, door);
            }

            return ResolveAuthoredDisplayName(contentDb, entityManager, entity);
        }

        static string ResolveDoorName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            return ResolveAuthoredDisplayName(contentDb, entityManager, entity);
        }

        static string ResolveInteriorDoorPromptName(in DoorInteractable door)
        {
            if (WorldResources.TryGetInteriorCell(door.DestinationCellHash, out CellData destinationCell))
                return TrimInteriorCellPromptName(destinationCell.CellId);

            string destinationCellId = WorldResources.ResolveInteriorCellId(door.DestinationCellHash);
            if (string.IsNullOrWhiteSpace(destinationCellId))
                destinationCellId = door.DestinationCellId.ToString();
            return TrimInteriorCellPromptName(destinationCellId);
        }

        static string ResolveExteriorDoorPromptName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity, in DoorInteractable door)
        {
            if (TryResolveExteriorDestinationCell(door.DestinationPosition, out int2 destinationCellCoord)
                && WorldResources.Cells.TryGetValue(destinationCellCoord, out CellData destinationCell)
                && destinationCell != null)
            {
                string regionId = destinationCell.Environment.RegionId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(regionId))
                {
                    if (contentDb != null && contentDb.TryGetRegionHandle(regionId, out var regionHandle))
                    {
                        ref readonly var region = ref contentDb.Get(regionHandle);
                        if (!string.IsNullOrWhiteSpace(region.Name))
                            return region.Name;
                        if (!string.IsNullOrWhiteSpace(region.Id))
                            return region.Id;
                    }

                    return regionId;
                }

                if (!string.IsNullOrWhiteSpace(destinationCell.CellId))
                    return destinationCell.CellId;
            }

            return ResolveAuthoredDisplayName(contentDb, entityManager, entity);
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

        static string ResolveAuthoredDisplayName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (contentDb == null || !entityManager.Exists(entity))
                return null;

            if (entityManager.HasComponent<DoorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<DoorAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveDoorDisplayName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ItemPickupAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ItemPickupAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveItemDisplayName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ContainerAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ContainerAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveContainerDisplayName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ActivatorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ActivatorAuthoring>(entity);
                return RuntimeContentMetadataResolver.ResolveActivatorDisplayName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
            {
                var actor = entityManager.GetComponentData<PassiveActorPresence>(entity);
                return ResolveActorName(contentDb, entityManager, entity, actor);
            }

            return null;
        }

        static string ResolveActorName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "npc";

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
                return ResolveActorName(contentDb, entityManager, entity, entityManager.GetComponentData<PassiveActorPresence>(entity));

            return "npc";
        }

        static string ResolveActorName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity, in PassiveActorPresence actor)
        {
            var source = entityManager.HasComponent<ActorSpawnSource>(entity)
                ? entityManager.GetComponentData<ActorSpawnSource>(entity)
                : default;
            return RuntimeContentMetadataResolver.ResolveActorDisplayName(contentDb, source.Definition, actor.CanTalk != 0 ? "npc" : "creature");
        }
    }
}
