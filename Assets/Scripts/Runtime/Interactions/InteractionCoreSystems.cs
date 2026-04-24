using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{

    static class InteractionCollisionLayers
    {
        public const uint Player = 1u << 1;
        public const uint ActivationProxy = 1u << 2;
        public const uint ActivationQuery = 1u << 3;
        public const uint SolidQuery = 1u << 4;

        public static CollisionFilter ActivationProxyFilter => new()
        {
            BelongsTo = ActivationProxy,
            CollidesWith = ActivationQuery,
            GroupIndex = 0,
        };

        public static CollisionFilter ActivationQueryFilter => new()
        {
            BelongsTo = ActivationQuery,
            CollidesWith = ActivationProxy,
            GroupIndex = 0,
        };

        public static CollisionFilter SolidQueryFilter => new()
        {
            BelongsTo = SolidQuery,
            CollidesWith = ~(Player | ActivationProxy),
            GroupIndex = 0,
        };

        public static CollisionFilter PlayerBodyFilter => new()
        {
            BelongsTo = Player,
            CollidesWith = ~(Player | ActivationProxy),
            GroupIndex = 0,
        };
    }

    readonly struct ResolvedInteractionTarget
    {
        public readonly Entity TargetEntity;
        public readonly uint PlacedRefId;
        public readonly InteractableKind Kind;
        public readonly float HitDistance;

        public ResolvedInteractionTarget(Entity targetEntity, uint placedRefId, InteractableKind kind, float hitDistance)
        {
            TargetEntity = targetEntity;
            PlacedRefId = placedRefId;
            Kind = kind;
            HitDistance = hitDistance;
        }
    }

    static class LooseCarryableResolver
    {
        public static bool TryResolveContent(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity logicalEntity,
            out ContentReference content)
        {
            return TryResolveContent(contentDb, entityManager, logicalEntity, out content, out _);
        }

        public static bool TryResolveContent(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity logicalEntity,
            out ContentReference content,
            out string diagnostic)
        {
            content = default;
            diagnostic = null;

            if (contentDb == null || !entityManager.Exists(logicalEntity))
                return false;

            if (entityManager.HasComponent<ItemPickupAuthoring>(logicalEntity))
            {
                var authoring = entityManager.GetComponentData<ItemPickupAuthoring>(logicalEntity);
                content = ContainerLootUtility.ToContentReference(authoring.Definition);
                return content.IsValid;
            }

            if (entityManager.HasComponent<LightSourceAuthoring>(logicalEntity))
            {
                if (!entityManager.HasComponent<LightInstanceFlags>(logicalEntity))
                    return false;

                var flags = entityManager.GetComponentData<LightInstanceFlags>(logicalEntity);
                if (flags.Carry == 0)
                    return false;

                var authoring = entityManager.GetComponentData<LightSourceAuthoring>(logicalEntity);
                content = ContainerLootUtility.ToContentReference(authoring.Definition);
                return content.IsValid;
            }

            if (entityManager.HasComponent<LeveledItemAuthoring>(logicalEntity))
            {
                if (!entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                    return false;

                uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
                if (placedRefId == 0u)
                    return false;

                var authoring = entityManager.GetComponentData<LeveledItemAuthoring>(logicalEntity);
                return ContainerLootUtility.TryResolveLooseLeveledCarryable(
                    contentDb,
                    authoring.Definition,
                    placedRefId,
                    out content,
                    out diagnostic)
                    && content.IsValid;
            }

            return false;
        }

        public static bool TryResolveMetadata(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity logicalEntity,
            out CarryableMetadata metadata)
        {
            metadata = default;
            return TryResolveContent(contentDb, entityManager, logicalEntity, out ContentReference content)
                && InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, content, out metadata);
        }

        public static string ResolveDisplayName(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity logicalEntity,
            string fallback = "item")
        {
            return TryResolveMetadata(contentDb, entityManager, logicalEntity, out CarryableMetadata metadata)
                ? metadata.DisplayName
                : fallback;
        }
    }

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
                InteractableKind.Container => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveBaseName(contentDb, entityManager, entity),
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
                InteractableKind.Container => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Npc => ResolveActorName(contentDb, entityManager, entity),
                _ => null,
            };
        }

        static string ResolveDoorPromptName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "door";

            if (entityManager.HasComponent<DoorInteractable>(entity)
                || (entityManager.HasComponent<DoorAuthoring>(entity) && DoorInteractableResolver.TryHydrate(entityManager, entity)))
            {
                var door = entityManager.GetComponentData<DoorInteractable>(entity);
                if (door.IsTeleport != 0 && door.DestinationCellId.Length > 0)
                    return ResolveInteriorDoorPromptName(door);

                if (door.IsTeleport != 0)
                    return ResolveExteriorDoorPromptName(contentDb, entityManager, entity, door);
            }

            return ResolveBaseName(contentDb, entityManager, entity);
        }

        static string ResolveDoorName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            return ResolveBaseName(contentDb, entityManager, entity);
        }

        static string ResolveInteriorDoorPromptName(in DoorInteractable door)
        {
            string destinationCellId = door.DestinationCellId.ToString();
            if (WorldResources.InteriorCells.TryGetValue(destinationCellId, out CellData destinationCell) && destinationCell != null)
                return TrimInteriorCellPromptName(destinationCell.CellId);

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

            return ResolveBaseName(contentDb, entityManager, entity);
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

        static string ResolveBaseName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (contentDb == null || !entityManager.Exists(entity))
                return null;

            if (entityManager.HasComponent<DoorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<DoorAuthoring>(entity);
                return ResolveBaseName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ItemPickupAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ItemPickupAuthoring>(entity);
                return ResolveBaseName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ContainerAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ContainerAuthoring>(entity);
                return ResolveBaseName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<ActivatorAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<ActivatorAuthoring>(entity);
                return ResolveBaseName(contentDb, authoring.Definition);
            }

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
            {
                var actor = entityManager.GetComponentData<PassiveActorPresence>(entity);
                return ResolveActorName(contentDb, actor);
            }

            return null;
        }

        static string ResolveBaseName(RuntimeContentDatabase contentDb, DoorDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "door");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ItemDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "item");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ContainerDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "container");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ActivatorDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "activator");
        static string ResolveActorName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return "npc";

            if (entityManager.HasComponent<PassiveActorPresence>(entity))
                return ResolveActorName(contentDb, entityManager.GetComponentData<PassiveActorPresence>(entity));

            if (entityManager.HasComponent<DialogueSpeakerAuthoring>(entity))
            {
                var authoring = entityManager.GetComponentData<DialogueSpeakerAuthoring>(entity);
                return ResolveActorName(contentDb, authoring.Definition, "npc");
            }

            return "npc";
        }

        static string ResolveActorName(RuntimeContentDatabase contentDb, in PassiveActorPresence actor)
        {
            if (actor.DisplayName.Length > 0)
                return actor.DisplayName.ToString();

            return ResolveActorName(contentDb, actor.Definition, actor.CanTalk != 0 ? "npc" : "creature");
        }

        static string ResolveActorName(RuntimeContentDatabase contentDb, ActorDefHandle handle, string fallback)
        {
            if (contentDb == null || !handle.IsValid)
                return fallback;

            ref readonly var def = ref contentDb.Get(handle);
            if (!string.IsNullOrWhiteSpace(def.Name))
                return def.Name.Trim();
            if (!string.IsNullOrWhiteSpace(def.Id))
                return def.Id.Trim();
            return fallback;
        }

        static string ResolveBaseName(RuntimeContentDatabase contentDb, in BaseDef def, string fallback)
        {
            if (contentDb == null)
                return fallback;
            if (!string.IsNullOrWhiteSpace(def.Name))
                return def.Name;
            if (!string.IsNullOrWhiteSpace(def.Id))
                return def.Id;
            return fallback;
        }
    }

    static class InteractionEntityDestroyUtility
    {
        public static void DestroyLogicalRef(
            EntityManager entityManager,
            Entity logicalEntity,
            ref LogicalRefLookup logicalRefLookup,
            bool preserveRuntimeSpawnRegistration = false)
        {
            if (!entityManager.Exists(logicalEntity))
                return;

            if (entityManager.HasComponent<RuntimeSpawnedRefIdentity>(logicalEntity))
            {
                uint runtimeRefId = entityManager.GetComponentData<RuntimeSpawnedRefIdentity>(logicalEntity).RuntimeRefId;
                if (preserveRuntimeSpawnRegistration)
                {
                    RuntimeSpawnProjectionUtility.MarkUnloaded(entityManager, runtimeRefId);
                }
                else if (RuntimeSpawnProjectionUtility.MarkDestroyed(entityManager, runtimeRefId))
                {
                    WorldJournalUtility.AppendRuntimeDestroyed(entityManager, runtimeRefId);
                }
            }

            if (entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
                if (placedRefId != 0u && logicalRefLookup.Map.IsCreated)
                    logicalRefLookup.Map.Remove(placedRefId);
            }

            var children = SnapshotChildren(entityManager, logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i];
                if (entityManager.Exists(child)
                    && entityManager.HasComponent<InteractionActivationProxyTag>(child)
                    && entityManager.HasComponent<RuntimeColliderSource>(child))
                {
                    var source = entityManager.GetComponentData<RuntimeColliderSource>(child);
                    if (source.Temporary != 0)
                        RuntimeColliderBlobLifetime.DeferGeneratedBlobDisposal(entityManager, source.Value);
                }

                if (entityManager.Exists(child))
                    entityManager.DestroyEntity(child);
            }

            if (entityManager.Exists(logicalEntity))
                entityManager.DestroyEntity(logicalEntity);
        }

        static Entity[] SnapshotChildren(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return System.Array.Empty<Entity>();

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            if (children.Length == 0)
                return System.Array.Empty<Entity>();

            var snapshot = new Entity[children.Length];
            for (int i = 0; i < children.Length; i++)
                snapshot[i] = children[i].Value;

            return snapshot;
        }
    }


    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class InteractionRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.InteractionRuntime");
            }

            EnsureComponent(runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteractionRuntimeState());
            EnsureComponent(runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteractionActivationResult());
            EnsureComponent(runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            });
            EnsureComponent(runtimeEntity, new InteractionDiagnosticsState());
            EnsureComponent(runtimeEntity, new InteractionDiagnosticsSnapshot());
            EnsureComponent(runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteriorTransitionState());
            EnsureBuffer<InteriorSpawnedEntity>(runtimeEntity);
            EnsureBuffer<PlayerInventoryItem>(runtimeEntity);
            EnsureBuffer<PickedItemRecord>(runtimeEntity);
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }

        void EnsureBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity))
                EntityManager.AddBuffer<T>(entity);
        }
    }


    [UpdateInGroup(typeof(MorrowindInteractionPresentationSystemGroup))]
    public partial class InteractionPresentationStateSystem : SystemBase
    {
        const float NotificationLifetimeSeconds = 2.25f;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerInteractionFocus>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionPresentationState>();
        }

        protected override void OnUpdate()
        {
            ref var presentation = ref SystemAPI.GetSingletonRW<InteractionPresentationState>().ValueRW;
            presentation.ShowCrosshair = 1;
            presentation.ShowFocus = 0;
            presentation.FocusText = default;

            var focus = SystemAPI.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget != 0 && EntityManager.Exists(focus.TargetEntity))
            {
                string prompt = BuildFocusPrompt(RuntimeContentDatabase.Active, focus);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    presentation.ShowFocus = 1;
                    presentation.FocusText = ToFixedString(prompt);
                }
            }

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            if (result.PendingNotification != 0 && result.Success != 0 && result.NotificationText.Length > 0)
            {
                presentation.NotificationText = result.NotificationText;
                presentation.NotificationSecondsRemaining = NotificationLifetimeSeconds;
                presentation.ShowNotification = 1;
                result.PendingNotification = 0;
            }
            else if (presentation.ShowNotification != 0)
            {
                presentation.NotificationSecondsRemaining -= SystemAPI.Time.DeltaTime;
                if (presentation.NotificationSecondsRemaining <= 0f)
                {
                    presentation.NotificationSecondsRemaining = 0f;
                    presentation.NotificationText = default;
                    presentation.ShowNotification = 0;
                }
            }
        }

        string BuildFocusPrompt(RuntimeContentDatabase contentDb, PlayerInteractionFocus focus)
        {
            return InteractionMetadataResolver.BuildFocusPrompt(contentDb, EntityManager, focus);
        }

        static FixedString128Bytes ToFixedString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            if (value.Length > 127)
                value = value.Substring(0, 127);

            return new FixedString128Bytes(value);
        }
    }
}
