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

namespace VVardenfell.Runtime.Streaming
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

    static class DoorInteractableResolver
    {
        public static bool TryHydrate(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<DoorAuthoring>(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity)
                || !entityManager.HasComponent<LogicalRefLocation>(logicalEntity))
            {
                return false;
            }

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            var location = entityManager.GetComponentData<LogicalRefLocation>(logicalEntity);
            if (!TryBuild(location, placedRefId, out DoorInteractable interactable))
                return false;

            entityManager.AddComponentData(logicalEntity, interactable);
            Debug.Log($"[VVardenfell][Door] hydrated DoorInteractable for placedRef=0x{placedRefId:X8}.");
            return true;
        }

        static bool TryBuild(in LogicalRefLocation location, uint placedRefId, out DoorInteractable interactable)
        {
            interactable = default;

            CellData cell = null;
            if (location.IsInterior != 0)
            {
                string interiorCellId = location.InteriorCellId.ToString();
                if (!WorldResources.InteriorCells.TryGetValue(interiorCellId, out cell) || cell == null)
                    return false;
            }
            else
            {
                if (!WorldResources.Cells.TryGetValue(location.ExteriorCell, out cell) || cell == null)
                    return false;
            }

            var refs = cell.Refs;
            var doors = cell.Doors;
            if (refs == null || doors == null)
                return false;

            for (int i = 0; i < refs.Length; i++)
            {
                ref readonly var entry = ref refs[i];
                if (entry.PlacedRefId != placedRefId || entry.DoorMetaIndex < 0 || entry.DoorMetaIndex >= doors.Length)
                    continue;

                var door = doors[entry.DoorMetaIndex];
                interactable = new DoorInteractable
                {
                    IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                    DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                    DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                    DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
                };
                return true;
            }

            return false;
        }
    }

    static class InteractionProxyBoundsUtility
    {
        static readonly float3 MinExtents = new(0.08f, 0.08f, 0.08f);

        public static bool TryBuildAggregateWorldBounds(EntityManager entityManager, Entity logicalEntity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.Exists(logicalEntity))
                return false;

            bool hasBounds = false;
            if (TryGetWorldBounds(entityManager, logicalEntity, out AABB logicalBounds))
            {
                worldBounds = logicalBounds;
                hasBounds = true;
            }

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return hasBounds;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (!entityManager.Exists(child) || entityManager.HasComponent<InteractionActivationProxyTag>(child))
                    continue;

                if (!TryGetWorldBounds(entityManager, child, out AABB childBounds))
                    continue;

                worldBounds = hasBounds ? Encapsulate(worldBounds, childBounds) : childBounds;
                hasBounds = true;
            }

            if (!hasBounds)
                return false;

            worldBounds.Extents = math.max(worldBounds.Extents, MinExtents);
            return true;
        }

        static bool TryGetWorldBounds(EntityManager entityManager, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.HasComponent<RenderBounds>(entity) || !entityManager.HasComponent<LocalToWorld>(entity))
                return false;

            var localBounds = entityManager.GetComponentData<RenderBounds>(entity).Value;
            float4x4 localToWorld = entityManager.GetComponentData<LocalToWorld>(entity).Value;
            float3 center = math.transform(localToWorld, localBounds.Center);
            float3x3 rotationScale = new(localToWorld.c0.xyz, localToWorld.c1.xyz, localToWorld.c2.xyz);
            float3 extents = math.abs(rotationScale.c0) * localBounds.Extents.x
                + math.abs(rotationScale.c1) * localBounds.Extents.y
                + math.abs(rotationScale.c2) * localBounds.Extents.z;

            worldBounds = new AABB
            {
                Center = center,
                Extents = extents,
            };
            return true;
        }

        static AABB Encapsulate(AABB a, AABB b)
        {
            float3 min = math.min(a.Min, b.Min);
            float3 max = math.max(a.Max, b.Max);
            return new AABB
            {
                Center = (min + max) * 0.5f,
                Extents = (max - min) * 0.5f,
            };
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
                InteractableKind.LooseItem => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Container => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveBaseName(contentDb, entityManager, entity),
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
                _ => "interactable",
            };
        }

        static string ResolvePromptDisplayName(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity entity, InteractableKind kind)
        {
            return kind switch
            {
                InteractableKind.Door => ResolveDoorPromptName(contentDb, entityManager, entity),
                InteractableKind.LooseItem => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Container => ResolveBaseName(contentDb, entityManager, entity),
                InteractableKind.Activator => ResolveBaseName(contentDb, entityManager, entity),
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

            return null;
        }

        static string ResolveBaseName(RuntimeContentDatabase contentDb, DoorDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "door");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ItemDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "item");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ContainerDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "container");
        static string ResolveBaseName(RuntimeContentDatabase contentDb, ActivatorDefHandle handle) => ResolveBaseName(contentDb, contentDb.Get(handle), "activator");

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
        public static void DestroyLogicalRef(EntityManager entityManager, Entity logicalEntity, ref LogicalRefLookup logicalRefLookup)
        {
            if (!entityManager.Exists(logicalEntity))
                return;

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

    static class InteractionTargetResolver
    {
        public const float MaxInteractDistance = 2.25f;

        public static bool TryResolveFromViewRay(
            EntityManager entityManager,
            in PhysicsWorldSingleton physicsWorld,
            in LogicalRefLookup logicalRefLookup,
            in LocalToWorld viewTransform,
            out Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            hitEntity = Entity.Null;
            resolved = default;

            float3 origin = viewTransform.Value.c3.xyz;
            float3 forward = math.normalizesafe(viewTransform.Value.c2.xyz, new float3(0f, 0f, 1f));
            var activationInput = new RaycastInput
            {
                Start = origin,
                End = origin + forward * MaxInteractDistance,
                Filter = InteractionCollisionLayers.ActivationQueryFilter,
            };

            var solidInput = new RaycastInput
            {
                Start = origin,
                End = activationInput.End,
                Filter = InteractionCollisionLayers.SolidQueryFilter,
            };

            bool hasProxyHit = physicsWorld.CastRay(activationInput, out Unity.Physics.RaycastHit proxyHit);
            bool hasSolidHit = physicsWorld.CastRay(solidInput, out Unity.Physics.RaycastHit solidHit);

            if (hasProxyHit && TryResolveEntity(entityManager, logicalRefLookup, proxyHit.Entity, out ResolvedInteractionTarget proxyResolved))
            {
                if (hasSolidHit && SolidHitBlocksProxy(entityManager, logicalRefLookup, solidHit, proxyResolved.TargetEntity, proxyHit.Fraction))
                {
                    hitEntity = solidHit.Entity;
                    if (TryResolveEntity(entityManager, logicalRefLookup, solidHit.Entity, out resolved))
                    {
                        resolved = new ResolvedInteractionTarget(
                            resolved.TargetEntity,
                            resolved.PlacedRefId,
                            resolved.Kind,
                            solidHit.Fraction * MaxInteractDistance);
                        return true;
                    }

                    return false;
                }

                hitEntity = proxyHit.Entity;
                resolved = new ResolvedInteractionTarget(
                    proxyResolved.TargetEntity,
                    proxyResolved.PlacedRefId,
                    proxyResolved.Kind,
                    proxyHit.Fraction * MaxInteractDistance);
                return true;
            }

            if (!hasSolidHit)
                return false;

            hitEntity = solidHit.Entity;
            if (!TryResolveEntity(entityManager, logicalRefLookup, solidHit.Entity, out resolved))
                return false;

            resolved = new ResolvedInteractionTarget(
                resolved.TargetEntity,
                resolved.PlacedRefId,
                resolved.Kind,
                solidHit.Fraction * MaxInteractDistance);
            return true;
        }

        public static bool TryResolveSupportedKind(EntityManager entityManager, Entity logicalEntity, out InteractableKind kind)
        {
            kind = InteractableKind.None;

            if (entityManager.HasComponent<DoorInteractable>(logicalEntity)
                || (entityManager.HasComponent<DoorAuthoring>(logicalEntity) && DoorInteractableResolver.TryHydrate(entityManager, logicalEntity)))
            {
                kind = InteractableKind.Door;
                return true;
            }

            if (entityManager.HasComponent<ItemPickupAuthoring>(logicalEntity))
            {
                kind = InteractableKind.LooseItem;
                return true;
            }

            if (entityManager.HasComponent<ContainerAuthoring>(logicalEntity))
            {
                kind = InteractableKind.Container;
                return true;
            }

            if (entityManager.HasComponent<ActivatorAuthoring>(logicalEntity))
            {
                kind = InteractableKind.Activator;
                return true;
            }

            return false;
        }

        static bool SolidHitBlocksProxy(
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            in Unity.Physics.RaycastHit solidHit,
            Entity proxyTargetEntity,
            float proxyFraction)
        {
            const float FractionEpsilon = 0.0005f;
            if (solidHit.Fraction + FractionEpsilon >= proxyFraction)
                return false;

            if (TryResolveLogicalEntity(entityManager, logicalRefLookup, solidHit.Entity, out Entity solidLogicalEntity)
                && solidLogicalEntity == proxyTargetEntity)
            {
                return false;
            }

            return true;
        }

        public static bool TryResolveLogicalEntity(
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity hitEntity,
            out Entity logicalEntity)
        {
            logicalEntity = Entity.Null;

            if (!entityManager.Exists(hitEntity))
                return false;

            if (entityManager.HasComponent<LogicalRefParent>(hitEntity))
            {
                logicalEntity = entityManager.GetComponentData<LogicalRefParent>(hitEntity).Value;
                return entityManager.Exists(logicalEntity);
            }

            if (entityManager.HasComponent<LogicalRefTag>(hitEntity))
            {
                logicalEntity = hitEntity;
                return true;
            }

            if (!entityManager.HasComponent<PlacedRefIdentity>(hitEntity))
                return false;

            uint childPlacedRefId = entityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
            if (childPlacedRefId == 0u || !logicalRefLookup.Map.IsCreated)
                return false;

            return logicalRefLookup.Map.TryGetValue(childPlacedRefId, out logicalEntity) && entityManager.Exists(logicalEntity);
        }

        public static bool TryResolveEntity(
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            resolved = default;

            if (!TryResolveLogicalEntity(entityManager, logicalRefLookup, hitEntity, out Entity logicalEntity))
                return false;

            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<LogicalRefTag>(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                return false;
            }

            if (!TryResolveSupportedKind(entityManager, logicalEntity, out InteractableKind kind))
                return false;

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            if (placedRefId == 0u)
                return false;

            resolved = new ResolvedInteractionTarget(logicalEntity, placedRefId, kind, 0f);
            return true;
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
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

    readonly struct ActivationProxyBuildRequest
    {
        public readonly Entity LogicalEntity;
        public readonly AABB WorldBounds;

        public ActivationProxyBuildRequest(Entity logicalEntity, AABB worldBounds)
        {
            LogicalEntity = logicalEntity;
            WorldBounds = worldBounds;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(PlayerInteractionFocusSystem))]
    public partial class InteractionActivationProxySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var pendingBuilds = new List<ActivationProxyBuildRequest>();
            foreach (var (_, entity) in SystemAPI
                         .Query<DynamicBuffer<LogicalRefChild>>()
                         .WithAll<LogicalRefTag, PlacedRefIdentity>()
                         .WithEntityAccess())
            {
                if (!InteractionTargetResolver.TryResolveSupportedKind(EntityManager, entity, out _))
                    continue;

                if (HasLiveProxy(entity))
                    continue;

                if (!InteractionProxyBoundsUtility.TryBuildAggregateWorldBounds(EntityManager, entity, out AABB worldBounds))
                    continue;

                pendingBuilds.Add(new ActivationProxyBuildRequest(entity, worldBounds));
            }

            for (int i = 0; i < pendingBuilds.Count; i++)
            {
                var request = pendingBuilds[i];
                if (!EntityManager.Exists(request.LogicalEntity) || HasLiveProxy(request.LogicalEntity))
                    continue;

                Entity proxyEntity = CreateProxyEntity(request.LogicalEntity, request.WorldBounds);
                if (EntityManager.HasComponent<InteractionActivationProxyState>(request.LogicalEntity))
                {
                    EntityManager.SetComponentData(request.LogicalEntity, new InteractionActivationProxyState
                    {
                        ProxyEntity = proxyEntity,
                    });
                }
                else
                {
                    EntityManager.AddComponentData(request.LogicalEntity, new InteractionActivationProxyState
                    {
                        ProxyEntity = proxyEntity,
                    });
                }

                if (EntityManager.HasBuffer<LogicalRefChild>(request.LogicalEntity))
                    EntityManager.GetBuffer<LogicalRefChild>(request.LogicalEntity).Add(new LogicalRefChild { Value = proxyEntity });
            }
        }

        bool HasLiveProxy(Entity logicalEntity)
        {
            if (!EntityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
                return false;

            Entity proxyEntity = EntityManager.GetComponentData<InteractionActivationProxyState>(logicalEntity).ProxyEntity;
            return proxyEntity != Entity.Null && EntityManager.Exists(proxyEntity);
        }

        Entity CreateProxyEntity(Entity logicalEntity, in AABB worldBounds)
        {
            float3 size = math.max(worldBounds.Extents * 2f, new float3(0.16f));
            BlobAssetReference<Collider> collider = BoxCollider.Create(
                new BoxGeometry
                {
                    Center = float3.zero,
                    Orientation = quaternion.identity,
                    Size = size,
                    BevelRadius = 0f,
                },
                InteractionCollisionLayers.ActivationProxyFilter);

            Entity proxyEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(proxyEntity, LocalTransform.FromPositionRotationScale(worldBounds.Center, quaternion.identity, 1f));
            EntityManager.AddComponentData(proxyEntity, new LocalToWorld
            {
                Value = float4x4.TRS(worldBounds.Center, quaternion.identity, new float3(1f)),
            });
            EntityManager.AddComponentData(proxyEntity, new PhysicsCollider { Value = collider });
            EntityManager.AddComponentData(proxyEntity, new LogicalRefParent { Value = logicalEntity });
            EntityManager.AddComponent<InteractionActivationProxyTag>(proxyEntity);
            EntityManager.AddComponent<Unity.Transforms.Static>(proxyEntity);
            EntityManager.AddSharedComponent(proxyEntity, new PhysicsWorldIndex { Value = 0 });

            if (EntityManager.HasComponent<CellLink>(logicalEntity))
                EntityManager.AddComponentData(proxyEntity, EntityManager.GetComponentData<CellLink>(logicalEntity));
            if (EntityManager.HasComponent<InteriorCellMember>(logicalEntity))
                EntityManager.AddComponent<InteriorCellMember>(proxyEntity);

            return proxyEntity;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class PlayerInteractionFocusSystem : SystemBase
    {
        EntityQuery _viewQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            RequireForUpdate(_viewQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            ref var focus = ref focusRef.ValueRW;
            focus.TargetEntity = Entity.Null;
            focus.PlacedRefId = 0u;
            focus.InteractKind = (byte)InteractableKind.None;
            focus.HitDistance = 0f;
            focus.HasTarget = 0;

            var viewTransform = _viewQuery.GetSingleton<LocalToWorld>();
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();

            if (!InteractionTargetResolver.TryResolveFromViewRay(
                    EntityManager,
                    physicsWorld,
                    logicalRefLookup,
                    viewTransform,
                    out _,
                    out ResolvedInteractionTarget resolved))
            {
                return;
            }

            focus.TargetEntity = resolved.TargetEntity;
            focus.PlacedRefId = resolved.PlacedRefId;
            focus.InteractKind = (byte)resolved.Kind;
            focus.HitDistance = resolved.HitDistance;
            focus.HasTarget = 1;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionFocusSystem))]
    public partial class PlayerInteractionActivationSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _focusQuery;
        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCharacterControl>());
            _focusQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionFocus>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending != 0)
                return;

            var transition = _transitionQuery.GetSingleton<InteriorTransitionState>();
            if (transition.TransitionInProgress != 0)
                return;

            var control = _playerQuery.GetSingleton<PlayerCharacterControl>();
            if (!control.InteractPressed)
                return;

            bool hasTarget = false;
            Entity hitEntity = Entity.Null;
            ResolvedInteractionTarget resolved = default;

            var focus = _focusQuery.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget != 0 && EntityManager.Exists(focus.TargetEntity))
            {
                resolved = new ResolvedInteractionTarget(
                    focus.TargetEntity,
                    focus.PlacedRefId,
                    (InteractableKind)focus.InteractKind,
                    focus.HitDistance);
                hasTarget = true;
            }

            if (!hasTarget)
            {
                EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
                var viewTransform = _viewQuery.GetSingleton<LocalToWorld>();
                var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
                var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
                hasTarget = InteractionTargetResolver.TryResolveFromViewRay(
                    EntityManager,
                    physicsWorld,
                    logicalRefLookup,
                    viewTransform,
                    out hitEntity,
                    out resolved);
            }

            if (!hasTarget)
            {
                if (hitEntity != Entity.Null)
                {
                    Debug.Log(
                        $"[VVardenfell][Interaction] interact ray hit entity {hitEntity}, but it did not resolve to a supported interactable.");
                }
                else
                {
                    Debug.Log("[VVardenfell][Interaction] interact pressed but no supported target was hit within range.");
                }

                return;
            }

            ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            uint sequence = runtimeState.NextActivationSequence + 1u;
            runtimeState.NextActivationSequence = sequence;

            request = new InteractionActivationRequest
            {
                Pending = 1,
                Sequence = sequence,
                Kind = (byte)resolved.Kind,
                TargetEntity = resolved.TargetEntity,
                TargetPlacedRefId = resolved.PlacedRefId,
            };

            Debug.Log(
                $"[VVardenfell][Interaction] queued activation: seq={sequence}, kind={resolved.Kind}, placedRef=0x{resolved.PlacedRefId:X8}, entity={resolved.TargetEntity}.");
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionActivationSystem))]
    public partial class TeleportDoorTransitionSystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = new(0f, 0f, 200000f);
        static readonly ProfilerMarker k_Transition = new("VV.Streaming.TeleportDoorTransition");

        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _focusQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadWrite<InteriorTransitionState>(), ComponentType.ReadWrite<InteriorSpawnedEntity>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            _streamingQuery = GetEntityQuery(
                ComponentType.ReadWrite<StreamingConfig>(),
                ComponentType.ReadWrite<LogicalRefLookup>(),
                ComponentType.ReadOnly<AvailableCells>(),
                ComponentType.ReadWrite<LoadedCellsMap>());
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_streamingQuery);
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_Transition.Auto();

            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Door)
                return;

            CompleteDependency();

            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transitionRef = _transitionQuery.GetSingletonRW<InteriorTransitionState>();
            ref var transition = ref transitionRef.ValueRW;
            transition.TransitionInProgress = 1;

            Entity target = request.TargetEntity;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<DoorInteractable>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] door activation request resolved to a missing or non-door logical entity.");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            var door = EntityManager.GetComponentData<DoorInteractable>(target);
            if (door.IsTeleport == 0)
            {
                TryQueueInteractionAudio(target, InteractionAudioKind.Door, "door");
                Debug.Log("[VVardenfell][Streaming] non-teleport door activated; transition deferred for this slice.");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            bool goesToInterior = door.DestinationCellId.Length > 0;
            CellData destinationInterior = null;
            if (goesToInterior)
            {
                string destinationCellId = door.DestinationCellId.ToString();
                if (!WorldResources.InteriorCells.TryGetValue(destinationCellId, out destinationInterior) || destinationInterior == null)
                {
                    Debug.LogWarning($"[VVardenfell][Streaming] teleport destination interior '{destinationCellId}' was not preloaded; transition aborted.");
                    transition.TransitionInProgress = 0;
                    ClearFocus();
                    return;
                }
            }

            TryQueueInteractionAudio(target, InteractionAudioKind.Door, "door");

            Debug.Log(
                goesToInterior
                    ? $"[VVardenfell][Streaming] entering interior '{door.DestinationCellId}' via teleport door."
                    : $"[VVardenfell][Streaming] exiting active interior to exterior destination ({door.DestinationPosition.x:F2}, {door.DestinationPosition.y:F2}, {door.DestinationPosition.z:F2}).");

            var streamingEntity = _streamingQuery.GetSingletonEntity();
            var configRef = EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var available = EntityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);

            if (goesToInterior)
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                WorldSpawner.HideExteriorVisibility(World, ref loaded);
                WorldSpawner.SpawnInteriorCell(World, destinationInterior, InteriorWorldOffset, transitionEntity, ref logicalRefLookup);
                configRef.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = door.DestinationCellId;
                ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
                runtimeState.PendingPickedItemPrune = 1;
            }
            else
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                configRef.ExteriorStreamingPaused = false;
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
            }

            float3 destinationPosition = door.DestinationPosition + (goesToInterior ? InteriorWorldOffset : float3.zero);
            quaternion bodyYawRotation = ExtractYawRotation(door.DestinationRotation);
            MovePlayerToDestination(destinationPosition, bodyYawRotation);

            if (!goesToInterior)
            {
                float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                configRef.CameraCell = new int2(
                    (int)math.floor(destinationPosition.x / cellM),
                    (int)math.floor(destinationPosition.z / cellM));
                WorldSpawner.SyncExteriorVisibility(World, configRef, available, ref loaded);
            }

            EntityManager.SetComponentData(streamingEntity, configRef);
            EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            EntityManager.SetComponentData(streamingEntity, loaded);

            ClearFocus();
            transition.TransitionInProgress = 0;

            Debug.Log(
                goesToInterior
                    ? $"[VVardenfell][Streaming] interior transition complete: '{door.DestinationCellId}'."
                    : $"[VVardenfell][Streaming] exterior transition complete: camera cell=({configRef.CameraCell.x},{configRef.CameraCell.y}).");
        }

        void MovePlayerToDestination(float3 destinationPosition, quaternion bodyYawRotation)
        {
            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var character = EntityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(playerEntity);
            var state = EntityManager.GetComponentData<PlayerCharacterState>(playerEntity);
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);

            playerTransform.Position = destinationPosition;
            playerTransform.Rotation = bodyYawRotation;
            EntityManager.SetComponentData(playerEntity, playerTransform);
            EntityManager.SetComponentData(playerEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition, bodyYawRotation, new float3(playerTransform.Scale))
            });

            control.LookDeltaDegrees = float2.zero;
            control.MoveVectorWorld = float3.zero;
            control.InteractPressed = false;
            control.JumpThisFixedTick = false;
            EntityManager.SetComponentData(playerEntity, control);

            state.WorldVelocity = float3.zero;
            state.Grounded = false;
            state.WasGrounded = false;
            state.GroundedTime = 0f;
            state.AirborneTime = 0f;
            EntityManager.SetComponentData(playerEntity, state);

            Entity viewEntity = _viewQuery.GetSingletonEntity();
            var view = EntityManager.GetComponentData<PlayerViewComponent>(viewEntity);
            float eyeHeight = state.Crouched ? character.CrouchingEyeHeight : character.StandingEyeHeight;
            view.LocalPitchDegrees = 0f;
            view.LocalViewRotation = quaternion.identity;
            view.LocalEyeOffset = new float3(0f, eyeHeight, 0f);
            EntityManager.SetComponentData(viewEntity, view);
            EntityManager.SetComponentData(viewEntity, LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                quaternion.identity,
                1f));
            EntityManager.SetComponentData(viewEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition + math.rotate(bodyYawRotation, view.LocalEyeOffset), bodyYawRotation, new float3(1f))
            });
        }

        void DestroyInteriorEntities(Entity transitionEntity, ref LogicalRefLookup logicalRefLookup)
        {
            var spawnedBuffer = EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            if (spawnedBuffer.Length == 0)
                return;

            var entitiesToDestroy = new Entity[spawnedBuffer.Length];
            for (int i = 0; i < spawnedBuffer.Length; i++)
                entitiesToDestroy[i] = spawnedBuffer[i].Value;

            for (int i = 0; i < entitiesToDestroy.Length; i++)
            {
                if (EntityManager.Exists(entitiesToDestroy[i])
                    && EntityManager.HasComponent<LogicalRefTag>(entitiesToDestroy[i]))
                {
                    InteractionEntityDestroyUtility.DestroyLogicalRef(EntityManager, entitiesToDestroy[i], ref logicalRefLookup);
                    continue;
                }

                if (EntityManager.Exists(entitiesToDestroy[i]))
                    EntityManager.DestroyEntity(entitiesToDestroy[i]);
            }

            EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity).Clear();
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!EntityManager.HasComponent<AudioEmitterAuthoring>(target))
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no AudioEmitterAuthoring component; skipping interaction one-shot.");
                return;
            }

            var emitter = EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no primary interaction sound; skipping interaction one-shot.");
                return;
            }

            float3 position = ResolveAudioPosition(target);
            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            uint sequence = audioState.NextSequence + 1u;
            audioState.NextSequence = sequence;

            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = sequence,
                Sound = emitter.PrimarySound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)kind,
            });

            Debug.Log($"[VVardenfell][Audio] queued {label} interaction one-shot: seq={sequence}, placedRef=0x{placedRefId:X8}, pos=({position.x:F2}, {position.y:F2}, {position.z:F2}).");
        }

        float3 ResolveAudioPosition(Entity target)
        {
            if (EntityManager.HasComponent<LocalToWorld>(target))
                return EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (EntityManager.HasComponent<LocalTransform>(target))
                return EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }

        void WarnMissingInteractionSoundOnce(uint placedRefId, string label, string reason)
        {
            if (placedRefId == 0u || !_loggedMissingInteractionSounds.Add(placedRefId))
                return;

            Debug.Log($"[VVardenfell][Audio] {label} 0x{placedRefId:X8} {reason}");
        }

        static quaternion ExtractYawRotation(quaternion sourceRotation)
        {
            float3 forward = math.rotate(sourceRotation, new float3(0f, 0f, 1f));
            forward.y = 0f;
            if (math.lengthsq(forward) < 1e-5f)
                return quaternion.identity;
            forward = math.normalize(forward);
            return quaternion.LookRotationSafe(forward, math.up());
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    public partial class LooseItemPickupSystem : SystemBase
    {
        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<PickedItemRecord>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.LooseItem)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint targetPlacedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target)
                || !EntityManager.HasComponent<ItemPickupAuthoring>(target)
                || !EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] loose-item activation request resolved to a missing or non-item logical entity.");
                ClearFocus();
                return;
            }

            var itemAuthoring = EntityManager.GetComponentData<ItemPickupAuthoring>(target);
            var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
            if (HasPickedItem(pickedItems, targetPlacedRefId))
            {
                Debug.Log($"[VVardenfell][Interaction] ignored duplicate pickup for placedRef=0x{targetPlacedRefId:X8}.");
                ClearFocus();
                return;
            }

            string itemName = ResolveItemName(RuntimeContentDatabase.Active, itemAuthoring.Definition);

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            int stackCount = AddInventoryItem(inventory, itemAuthoring.Definition);
            pickedItems.Add(new PickedItemRecord
            {
                PlacedRefId = targetPlacedRefId,
                Definition = itemAuthoring.Definition,
            });

            TryQueueInteractionAudio(target, InteractionAudioKind.LooseItem, "item");

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            DestroyLogicalRef(target, ref logicalRefLookup);
            EntityManager.SetComponentData(lookupEntity, logicalRefLookup);

            ClearFocus();

            ref var activationResult = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            activationResult.Sequence = sequence;
            activationResult.Kind = (byte)InteractableKind.LooseItem;
            activationResult.Success = 1;
            activationResult.PendingNotification = 1;
            activationResult.NotificationText = ToFixedString($"Picked up {itemName}");

            Debug.Log($"[VVardenfell][Interaction] picked up '{itemName}' from placedRef=0x{targetPlacedRefId:X8}; stack={stackCount}.");
        }

        static bool HasPickedItem(DynamicBuffer<PickedItemRecord> pickedItems, uint placedRefId)
        {
            for (int i = 0; i < pickedItems.Length; i++)
            {
                if (pickedItems[i].PlacedRefId == placedRefId)
                    return true;
            }

            return false;
        }

        static int AddInventoryItem(DynamicBuffer<PlayerInventoryItem> inventory, ItemDefHandle definition)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Definition.Value != definition.Value)
                    continue;

                var entry = inventory[i];
                entry.Count += 1;
                inventory[i] = entry;
                return entry.Count;
            }

            inventory.Add(new PlayerInventoryItem
            {
                Definition = definition,
                Count = 1,
            });
            return 1;
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!EntityManager.HasComponent<AudioEmitterAuthoring>(target))
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no AudioEmitterAuthoring component; skipping interaction one-shot.");
                return;
            }

            var emitter = EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no primary interaction sound; skipping interaction one-shot.");
                return;
            }

            float3 position = ResolveAudioPosition(target);
            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            uint sequence = audioState.NextSequence + 1u;
            audioState.NextSequence = sequence;

            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = sequence,
                Sound = emitter.PrimarySound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)kind,
            });

            Debug.Log($"[VVardenfell][Audio] queued {label} interaction one-shot: seq={sequence}, placedRef=0x{placedRefId:X8}, pos=({position.x:F2}, {position.y:F2}, {position.z:F2}).");
        }

        float3 ResolveAudioPosition(Entity target)
        {
            if (EntityManager.HasComponent<LocalToWorld>(target))
                return EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (EntityManager.HasComponent<LocalTransform>(target))
                return EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }

        void WarnMissingInteractionSoundOnce(uint placedRefId, string label, string reason)
        {
            if (placedRefId == 0u || !_loggedMissingInteractionSounds.Add(placedRefId))
                return;

            Debug.Log($"[VVardenfell][Audio] {label} 0x{placedRefId:X8} {reason}");
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }

        void DestroyLogicalRef(Entity logicalEntity, ref LogicalRefLookup logicalRefLookup)
        {
            InteractionEntityDestroyUtility.DestroyLogicalRef(EntityManager, logicalEntity, ref logicalRefLookup);
        }

        static string ResolveItemName(RuntimeContentDatabase contentDb, ItemDefHandle definition)
        {
            if (contentDb == null || !definition.IsValid)
                return "item";

            ref readonly var item = ref contentDb.Get(definition);
            if (!string.IsNullOrWhiteSpace(item.Name))
                return item.Name;
            if (!string.IsNullOrWhiteSpace(item.Id))
                return item.Id;
            return "item";
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

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LooseItemPickupSystem))]
    public partial class DeferredInteractionSystem : SystemBase
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<InteractionActivationResult>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            var kind = (InteractableKind)request.Kind;
            if (kind != InteractableKind.Container && kind != InteractableKind.Activator)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target) || !InteractionTargetResolver.TryResolveSupportedKind(EntityManager, target, out InteractableKind resolvedKind) || resolvedKind != kind)
            {
                Debug.LogWarning($"[VVardenfell][Interaction] deferred {InteractionMetadataResolver.ResolveKindLabel(kind)} activation resolved to a missing or mismatched logical entity.");
                ClearFocus();
                return;
            }

            string displayName = InteractionMetadataResolver.ResolveDisplayName(RuntimeContentDatabase.Active, EntityManager, target, kind)
                ?? InteractionMetadataResolver.ResolveKindLabel(kind);
            string kindLabel = InteractionMetadataResolver.ResolveKindLabel(kind);
            Debug.Log($"[VVardenfell][Interaction] {kindLabel} activation deferred for this slice: '{displayName}' placedRef=0x{placedRefId:X8}.");

            ClearFocus();

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)kind;
            result.Success = 0;
            result.PendingNotification = 0;
            result.NotificationText = default;
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateAfter(typeof(LooseItemPickupSystem))]
    [UpdateAfter(typeof(DeferredInteractionSystem))]
    public partial class PickedItemRespawnPruneSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            if (runtimeState.PendingPickedItemPrune == 0)
                return;

            runtimeState.PendingPickedItemPrune = 0;

            var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
            if (pickedItems.Length == 0)
                return;

            using var pickedSet = new NativeParallelHashSet<uint>(pickedItems.Length, Allocator.Temp);
            for (int i = 0; i < pickedItems.Length; i++)
                pickedSet.Add(pickedItems[i].PlacedRefId);

            var entitiesToDestroy = new List<Entity>();
            foreach (var (placedRefId, entity) in SystemAPI
                         .Query<RefRO<PlacedRefIdentity>>()
                         .WithAll<LogicalRefTag, ItemPickupAuthoring, InteriorCellMember>()
                         .WithEntityAccess())
            {
                if (pickedSet.Contains(placedRefId.ValueRO.Value))
                    entitiesToDestroy.Add(entity);
            }

            if (entitiesToDestroy.Count == 0)
                return;

            CompleteDependency();

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            for (int i = 0; i < entitiesToDestroy.Count; i++)
                DestroyLogicalRef(entitiesToDestroy[i], ref logicalRefLookup);
            EntityManager.SetComponentData(lookupEntity, logicalRefLookup);

            Debug.Log($"[VVardenfell][Interaction] pruned {entitiesToDestroy.Count} previously picked loose items after interior spawn.");
        }

        void DestroyLogicalRef(Entity logicalEntity, ref LogicalRefLookup logicalRefLookup)
        {
            InteractionEntityDestroyUtility.DestroyLogicalRef(EntityManager, logicalEntity, ref logicalRefLookup);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PickedItemRespawnPruneSystem))]
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

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InteractionOverlayPresentationSystem : SystemBase
    {
        VVardenfell.Runtime.UI.InteractionOverlayView _view;

        protected override void OnCreate()
        {
            RequireForUpdate<InteractionPresentationState>();
        }

        protected override void OnDestroy()
        {
            if (_view != null)
                Object.Destroy(_view.gameObject);
            _view = null;
        }

        protected override void OnUpdate()
        {
            _view ??= VVardenfell.Runtime.UI.InteractionOverlayView.Create();

            var state = SystemAPI.GetSingleton<InteractionPresentationState>();
            _view.Sync(
                !BootstrapPresentationGate.BlocksGameplayInput,
                state.ShowCrosshair != 0,
                state.ShowFocus != 0 ? state.FocusText.ToString() : null,
                state.ShowNotification != 0 ? state.NotificationText.ToString() : null);
        }
    }
}

namespace VVardenfell.Runtime.UI
{
    using UnityEngine;

    public sealed class InteractionOverlayView : MonoBehaviour
    {
        string _focusText;
        string _notificationText;
        bool _visible;
        bool _showCrosshair;

        GUIStyle _crosshairStyle;
        GUIStyle _focusStyle;
        GUIStyle _notificationStyle;

        public static InteractionOverlayView Create()
        {
            var go = new GameObject("VVardenfell.InteractionOverlay");
            Object.DontDestroyOnLoad(go);
            return go.AddComponent<InteractionOverlayView>();
        }

        public void Sync(bool visible, bool showCrosshair, string focusText, string notificationText)
        {
            _visible = visible;
            _showCrosshair = showCrosshair;
            _focusText = focusText;
            _notificationText = notificationText;
        }

        void OnGUI()
        {
            if (!_visible)
                return;

            EnsureStyles();

            float width = Screen.width;
            float height = Screen.height;

            if (_showCrosshair)
            {
                var rect = new Rect(width * 0.5f - 16f, height * 0.5f - 22f, 32f, 44f);
                GUI.Label(rect, "+", _crosshairStyle);
            }

            if (!string.IsNullOrWhiteSpace(_focusText))
            {
                var rect = new Rect(width * 0.5f - 320f, height * 0.5f + 28f, 640f, 30f);
                GUI.Label(rect, _focusText, _focusStyle);
            }

            if (!string.IsNullOrWhiteSpace(_notificationText))
            {
                var rect = new Rect(width * 0.5f - 360f, height * 0.18f, 720f, 34f);
                GUI.Label(rect, _notificationText, _notificationStyle);
            }
        }

        void EnsureStyles()
        {
            if (_crosshairStyle != null)
                return;

            _crosshairStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
            };
            _crosshairStyle.normal.textColor = new Color(0.96f, 0.93f, 0.85f);

            _focusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
            _focusStyle.normal.textColor = new Color(0.94f, 0.88f, 0.76f);

            _notificationStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
            };
            _notificationStyle.normal.textColor = new Color(0.96f, 0.92f, 0.74f);
        }
    }
}
