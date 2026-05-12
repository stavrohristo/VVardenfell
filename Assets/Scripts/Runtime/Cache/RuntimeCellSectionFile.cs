using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Streaming;
using EntitiesBinaryReader = Unity.Entities.Serialization.BinaryReader;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.Cache
{
    public readonly struct RuntimeCellSectionLoadResult
    {
        public readonly Entity SectionEntity;
        public readonly RuntimeCellSectionHeader Header;

        public RuntimeCellSectionLoadResult(Entity sectionEntity, RuntimeCellSectionHeader header)
        {
            SectionEntity = sectionEntity;
            Header = header;
        }
    }

    public static class RuntimeCellSectionFile
    {
        public static RuntimeCellSectionLoadResult LoadIntoWorld(EntityManager target, string path, bool isInterior, string cellId = null, RuntimeMaterializationResources resources = null)
        {
            if (TryFindResident(target, isInterior, path, cellId, out var existing))
                return existing;

            if (resources == null)
                resources = RuntimeMaterializationResources.Require(target);

            RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorldHeader(path, out var renderReferences, out long payloadOffset, out int payloadLength);
            ValidateEmptyRenderObjectTable(renderReferences, path);
            var unityObjects = resources.ResolveRenderObjectReferences(renderReferences, $"Runtime cell section '{path}'");
            using var reader = new RuntimeCellSectionPayloadBinaryReader(path, payloadOffset, payloadLength);
            using (var sectionWorld = DeserializeToTempWorld(path, reader, unityObjects))
            {
                ValidateSingleSection(sectionWorld.EntityManager, path, isInterior, cellId);
                target.MoveEntitiesFrom(sectionWorld.EntityManager);
            }

            if (!TryFindUnclaimed(target, isInterior, path, cellId, out var loaded))
                throw new InvalidDataException($"Runtime cell section '{path}' was deserialized but no matching unclaimed section entity was found.");

            if (!target.HasComponent<RuntimeCellSectionResident>(loaded.SectionEntity))
                throw new InvalidDataException($"Runtime cell section '{path}' is missing resident state component; rebake required.");
            target.SetComponentData(loaded.SectionEntity, new RuntimeCellSectionResident
            {
                ExteriorCoord = new int2(loaded.Header.GridX, loaded.Header.GridY),
                InteriorCellHash = loaded.Header.InteriorCellHash,
                IsInterior = loaded.Header.IsInterior,
            });
            target.SetComponentEnabled<RuntimeCellSectionResident>(loaded.SectionEntity, true);
            return loaded;
        }

        public static void ValidateFile(string path, bool isInterior, string cellId = null)
        {
            RuntimeRenderObjectReferenceFile.ReadWrappedEntityWorldHeader(path, out var renderReferences, out long payloadOffset, out int payloadLength);
            ValidateEmptyRenderObjectTable(renderReferences, path);
            var placeholders = CreateValidationObjects(renderReferences);
            try
            {
                using var reader = new RuntimeCellSectionPayloadBinaryReader(path, payloadOffset, payloadLength);
                using var sectionWorld = DeserializeToTempWorld(path, reader, placeholders);
                var entity = ValidateSingleSection(sectionWorld.EntityManager, path, isInterior, cellId);
                ValidateSectionPayload(sectionWorld.EntityManager, entity, path);
            }
            finally
            {
                DestroyValidationObjects(placeholders);
            }
        }

        static World DeserializeToTempWorld(string path, EntitiesBinaryReader reader, object[] unityObjects)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Runtime cell section path is empty.");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Runtime cell section '{path}' does not exist.", path);

            var world = new World($"VV.CellSectionLoad({Path.GetFileName(path)})");
            try
            {
                var tx = world.EntityManager.BeginExclusiveEntityTransaction();
                SerializeUtility.DeserializeWorld(tx, reader, unityObjects);
                world.EntityManager.EndExclusiveEntityTransaction();

                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        static Object[] CreateValidationObjects(RuntimeRenderObjectReference[] references)
        {
            var objects = new Object[references?.Length ?? 0];
            if (objects.Length == 0)
                return objects;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidDataException("URP/Lit shader is required to validate runtime cell section render references.");
            for (int i = 0; i < objects.Length; i++)
            {
                var reference = references[i];
                switch (reference.Kind)
                {
                    case RuntimeRenderObjectReferenceKind.Mesh:
                        var mesh = new Mesh { name = $"VV:ValidateMesh[{reference.Index}]" };
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
                            name = $"VV:ValidateMaterial[{reference.Kind}:{reference.Index}]",
                            enableInstancing = true,
                        };
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported render object reference kind {(int)reference.Kind}.");
                }
            }
            return objects;
        }

        static void ValidateEmptyRenderObjectTable(RuntimeRenderObjectReference[] references, string path)
        {
            if (references == null)
                throw new InvalidDataException($"Runtime cell section '{path}' has no render object reference table.");
            if (references.Length != 0)
                throw new InvalidDataException($"Runtime cell section '{path}' contains {references.Length} serialized Unity render object references; rebake required for direct runtime render IDs.");
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

        static Entity ValidateSingleSection(EntityManager em, string path, bool isInterior, string cellId)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidDataException($"Runtime cell section '{path}' must contain exactly one cell header entity.");

            Entity entity = query.GetSingletonEntity();
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            ValidateHeader(header, path, isInterior, cellId);
            return entity;
        }

        static void ValidateHeader(RuntimeCellSectionHeader header, string path, bool isInterior, string cellId)
        {
            if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                throw new InvalidDataException($"Runtime cell section '{path}' pipeline {header.PipelineVersion} does not match runtime pipeline {CacheFormat.WorldBakePipelineVersion}; rebake required.");

            bool headerInterior = header.IsInterior != 0;
            if (headerInterior != isInterior)
                throw new InvalidDataException($"Runtime cell section '{path}' interior flag mismatch.");

            if (isInterior)
            {
                string expected = cellId ?? string.Empty;
                string actual = header.CellId.ToString();
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Runtime cell section '{path}' interior id mismatch: found '{actual}', expected '{expected}'.");
            }
        }

        static void ValidateSectionPayload(EntityManager em, Entity entity, string path)
        {
            var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
            RequireComponent<RuntimeCellSectionResident>(em, entity, path, "section resident state");
            RequireComponent<RuntimeCellSectionResourcesBound>(em, entity, path, "section resource binding state");
            RequireBufferPresent<RuntimeCellSectionRenderEntity>(em, entity, path, "section render entity buffer");
            RequireBufferPresent<RuntimeCellSectionTerrainEntity>(em, entity, path, "section terrain entity buffer");
            RequireBufferPresent<RuntimeCellSectionCombinedRenderEntity>(em, entity, path, "section combined render entity buffer");
            RequireBufferPresent<RuntimeCellSectionColliderEntity>(em, entity, path, "section collider entity buffer");
            RequireBufferPresent<RuntimeCellSectionLogicalRefEntity>(em, entity, path, "section logical ref entity buffer");
            RequireBufferPresent<RuntimeCellSectionExplicitRefEntry>(em, entity, path, "section explicit-ref entry buffer");
            RequireBufferPresent<RuntimeCellSectionActorInitEntity>(em, entity, path, "section actor init entity buffer");
            RequireBufferPresent<RuntimeCellSectionTransformRootEntity>(em, entity, path, "section transform root entity buffer");
            uint flags = header.Flags;
            if ((flags & CacheFormat.CellFlagHasTerrain) != 0)
            {
                Entity terrain = RequireSingleEntity<RuntimeCellSectionTerrainTag>(em, path, "terrain entity");
                var renderResource = RequireComponent<RuntimeCellSectionTerrainRenderResource>(em, terrain, path, "terrain render resource");
                if (renderResource.MeshIndex < 0 || renderResource.SplatSlice < 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' terrain has invalid render resource indices ({renderResource.MeshIndex}, {renderResource.SplatSlice}); rebake required.");
                RequireColliderSource(em, terrain, RuntimeColliderKind.TerrainCell, path, "terrain collider source");
                ValidatePrebakedRenderEntity(em, terrain, path, "terrain entity");
                RequireComponent<TerrainSplatSlice>(em, terrain, path, "terrain splat slice");
                ValidateMaterialMeshInfo(em, terrain, path, "terrain entity");
                RequireBufferLength<RuntimeCellSectionTerrainHeight>(em, terrain, 65 * 65, path, "terrain heights");
                if ((flags & CacheFormat.CellFlagHasNormals) != 0)
                    RequireBufferLength<RuntimeCellSectionTerrainNormal>(em, terrain, 3 * 65 * 65, path, "terrain normals");
                if ((flags & CacheFormat.CellFlagHasVtex) != 0)
                    RequireBufferLength<RuntimeCellSectionTerrainLayer>(em, terrain, 16 * 16, path, "terrain layer grid");
                if ((flags & CacheFormat.CellFlagHasWorldMap) != 0)
                    RequireBufferLength<RuntimeCellSectionWorldMapSample>(em, terrain, 81, path, "world map");
            }

            if ((flags & CacheFormat.CellFlagHasStaticCollision) != 0)
            {
                Entity staticCollider = RequireSingleEntity<RuntimeCellSectionStaticColliderTag>(em, path, "static collider entity");
                RequireComponent<RuntimeCellSectionStaticCollider>(em, staticCollider, path, "static collider blob");
                RequireColliderSource(em, staticCollider, RuntimeColliderKind.StaticCell, path, "static collider source");
            }

            using var logicalQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LogicalRefContent>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var logicalEntities = logicalQuery.ToEntityArray(Allocator.Temp);
            using var identities = logicalQuery.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            var seen = new NativeHashSet<uint>(logicalEntities.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < logicalEntities.Length; i++)
                {
                    uint placedRefId = identities[i].Value;
                    if (placedRefId == 0u)
                        throw new InvalidDataException($"Runtime cell section '{path}' has logical ref with zero placed ref id.");
                    if (!seen.Add(placedRefId))
                        throw new InvalidDataException($"Runtime cell section '{path}' has duplicate logical ref 0x{placedRefId:X8}.");
                }
            }
            finally
            {
                seen.Dispose();
            }

            ValidateCombinedChunks(em, entity, path);
            ValidatePlacedRenderLeaves(em, path);
            ValidatePlacedRenderRoots(em, path);
            ValidatePickColliders(em, path);
            ValidateExplicitRefEntries(em, entity, path);
            ValidateLogicalRefAuthoring(em, path);
        }

        static void ValidateLogicalRefAuthoring(EntityManager em, string path)
        {
            using var logicalQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<LogicalRefContent>());
            using var entities = logicalQuery.ToEntityArray(Allocator.Temp);
            using var contents = logicalQuery.ToComponentDataArray<LogicalRefContent>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                switch (contents[i].Value.Kind)
                {
                    case ContentReferenceKind.Actor:
                        RequireComponent<ActorSpawnSource>(em, entity, path, "actor spawn source");
                        RequireComponent<PassiveActorPresence>(em, entity, path, "passive actor presence");
                        RequireActorBaseline(em, entity, path);
                        RequireComponent<RuntimeCellSectionActorNeedsInitialization>(em, entity, path, "actor initialization marker");
                        break;
                    case ContentReferenceKind.Activator:
                        RequireComponent<ActivatorAuthoring>(em, entity, path, "activator authoring");
                        break;
                    case ContentReferenceKind.Door:
                        RequireComponent<DoorAuthoring>(em, entity, path, "door authoring");
                        RequireComponent<DoorInteractable>(em, entity, path, "door interactable");
                        RequireComponent<InteractionActivationProxyBuildPending>(em, entity, path, "door activation proxy marker");
                        break;
                    case ContentReferenceKind.Container:
                        RequireComponent<ContainerAuthoring>(em, entity, path, "container authoring");
                        break;
                    case ContentReferenceKind.Item:
                        RequireComponent<ItemPickupAuthoring>(em, entity, path, "item pickup authoring");
                        break;
                    case ContentReferenceKind.Light:
                        RequireComponent<LightSourceAuthoring>(em, entity, path, "light source authoring");
                        break;
                    case ContentReferenceKind.Static:
                        RequireComponent<StaticRefAuthoring>(em, entity, path, "static ref authoring");
                        break;
                    case ContentReferenceKind.LeveledItem:
                        RequireComponent<LeveledItemAuthoring>(em, entity, path, "leveled item authoring");
                        break;
                    case ContentReferenceKind.LeveledCreature:
                        RequireComponent<LeveledCreatureAuthoring>(em, entity, path, "leveled creature authoring");
                        break;
                }
            }
        }

        static void RequireActorBaseline(EntityManager em, Entity entity, string path)
        {
            RequireComponent<ActorAttributeSet>(em, entity, path, "actor attribute set");
            RequireComponent<ActorAttributeBaseSet>(em, entity, path, "actor attribute base set");
            RequireComponent<ActorAttributeDamageSet>(em, entity, path, "actor attribute damage set");
            RequireComponent<ActorAttributeModifierSet>(em, entity, path, "actor attribute modifier set");
            RequireComponent<ActorSkillSet>(em, entity, path, "actor skill set");
            RequireComponent<ActorSkillBaseSet>(em, entity, path, "actor skill base set");
            RequireComponent<ActorSkillDamageSet>(em, entity, path, "actor skill damage set");
            RequireComponent<ActorSkillModifierSet>(em, entity, path, "actor skill modifier set");
            RequireComponent<ActorVitalSet>(em, entity, path, "actor vital set");
            RequireComponent<ActorVitalBaseSet>(em, entity, path, "actor vital base set");
            RequireComponent<ActorVitalModifierSet>(em, entity, path, "actor vital modifier set");
            RequireComponent<ActorEffectStatModifiers>(em, entity, path, "actor effect stat modifiers");
            RequireComponent<ActorDispositionState>(em, entity, path, "actor disposition state");
            RequireComponent<ActorAiSettingsState>(em, entity, path, "actor AI settings state");
            RequireComponent<ActorScriptEventState>(em, entity, path, "actor script event state");
            RequireComponent<ActorHitAftermathState>(em, entity, path, "actor hit aftermath state");
            RequireComponent<ActorHitAftermathAnimationActive>(em, entity, path, "actor hit aftermath animation marker");
            RequireComponent<ActorDead>(em, entity, path, "actor dead marker");
            RequireComponent<ActorActiveCombatTarget>(em, entity, path, "actor active combat target");
            RequireComponent<ActorCrimeState>(em, entity, path, "actor crime state");
            RequireComponent<ActorFriendlyHitState>(em, entity, path, "actor friendly hit state");
            RequireComponent<ActorBlockState>(em, entity, path, "actor block state");
            RequireComponent<ActorMeleeCombatAiState>(em, entity, path, "actor melee combat AI state");
            RequireComponent<ActorCombatMovementState>(em, entity, path, "actor combat movement state");
            RequireComponent<ActorAiGreetingState>(em, entity, path, "actor AI greeting state");
            RequireComponent<ActorDerivedMovementStats>(em, entity, path, "actor derived movement stats");
            RequireComponent<ActorMagicCastState>(em, entity, path, "actor magic cast state");
            RequireComponent<ActorActiveMagicEffectDirty>(em, entity, path, "actor active magic effect dirty marker");
            RequireComponent<ActorActiveMagicEffectTicking>(em, entity, path, "actor active magic effect ticking marker");
            RequireBufferPresent<ActorCombatTarget>(em, entity, path, "actor combat target buffer");
            RequireBufferPresent<ActorKnownSpell>(em, entity, path, "actor known spell buffer");
            RequireBufferPresent<ActorActiveMagicEffect>(em, entity, path, "actor active magic effect buffer");
            RequireBufferPresent<ActorActiveSpell>(em, entity, path, "actor active spell buffer");
            RequireBufferPresent<ActorUsedPower>(em, entity, path, "actor used power buffer");
            if (em.HasComponent<ActorAiState>(entity))
            {
                RequireComponent<ActorAiNavigationAnchor>(em, entity, path, "actor AI navigation anchor");
                RequireComponent<ActorAiNavigationAnchorDirty>(em, entity, path, "actor AI navigation anchor dirty marker");
                RequireBufferPresent<ActorAiPackageRuntime>(em, entity, path, "actor AI package buffer");
                RequireComponent<MorrowindMovementInput>(em, entity, path, "actor movement input");
                RequireComponent<MorrowindMovementState>(em, entity, path, "actor movement state");
                RequireComponent<MorrowindMovementSpeed>(em, entity, path, "actor movement speed");
                RequireComponent<PathGridTraversalState>(em, entity, path, "actor pathgrid traversal state");
                RequireComponent<PathGridTraversalPendingRequest>(em, entity, path, "actor pathgrid traversal pending request");
                RequireComponent<PathGridTraversalAwaitingResult>(em, entity, path, "actor pathgrid traversal awaiting result");
                RequireBufferPresent<PathGridTraversalNode>(em, entity, path, "actor pathgrid traversal node buffer");
            }
        }

        static void ValidatePlacedRenderLeaves(EntityManager em, string path)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnPrefabRenderResource>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var resources = query.ToComponentDataArray<RuntimeSpawnPrefabRenderResource>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var resource = resources[i];
                if (resource.MeshIndex < 0 || resource.MaterialIndex < 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' placed render leaf has invalid logical render resource; rebake required.");
                ValidatePrebakedRenderEntity(em, entity, path, "placed render leaf");
                RequireComponent<ModelPrefabRenderLeaf>(em, entity, path, "model prefab render leaf");
                RequireComponent<TextureSlice>(em, entity, path, "texture slice");
                ValidateMaterialMeshInfo(em, entity, path, "placed render leaf");
            }
        }

        static void ValidateCombinedChunks(EntityManager em, Entity entity, string path)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionCombinedRenderResource>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity chunkEntity = entities[i];
                var resource = em.GetComponentData<RuntimeCellSectionCombinedRenderResource>(chunkEntity);
                if (resource.MeshIndex < 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' combined chunk has no baked global mesh index; rebake required.");
                if (resource.MaterialIndex < 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' combined chunk has invalid material variant; rebake required.");
                if (resource.TextureBucketKey == 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' combined chunk has no texture bucket key; rebake required.");
                ValidatePrebakedRenderEntity(em, chunkEntity, path, "combined chunk");
                ValidateMaterialMeshInfo(em, chunkEntity, path, "combined chunk");
                if (!em.HasBuffer<CombinedCellRenderChunkMember>(chunkEntity))
                    throw new InvalidDataException($"Runtime cell section '{path}' combined chunk is missing baked member buffer; rebake required.");
                var members = em.GetBuffer<CombinedCellRenderChunkMember>(chunkEntity);
                if (members.Length == 0)
                    throw new InvalidDataException($"Runtime cell section '{path}' combined chunk has no baked members; rebake required.");
                for (int m = 0; m < members.Length; m++)
                {
                    var member = members[m];
                    if (member.LogicalRefEntity == Entity.Null || !em.Exists(member.LogicalRefEntity))
                        throw new InvalidDataException($"Runtime cell section '{path}' combined chunk member 0x{member.PlacedRefId:X8} has no logical entity; rebake required.");
                    if (member.RenderEntity == Entity.Null || !em.Exists(member.RenderEntity))
                        throw new InvalidDataException($"Runtime cell section '{path}' combined chunk member 0x{member.PlacedRefId:X8} has no render entity; rebake required.");
                    RequireComponent<CombinedCellRenderSuppressed>(em, member.RenderEntity, path, "combined render suppression");
                    RequireComponent<ModelPrefabRenderLeaf>(em, member.RenderEntity, path, "combined render leaf");
                    if (!em.HasBuffer<CombinedCellRenderLink>(member.LogicalRefEntity))
                        throw new InvalidDataException($"Runtime cell section '{path}' combined member 0x{member.PlacedRefId:X8} logical ref has no chunk link; rebake required.");
                }
            }
        }

        static void ValidatePlacedRenderRoots(EntityManager em, string path)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionRenderRoot>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var roots = query.ToComponentDataArray<RuntimeCellSectionRenderRoot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (roots[i].CollisionIndex >= 0)
                {
                    var source = RequireComponent<RuntimeColliderSource>(em, entities[i], path, "placed-ref reusable collider source");
                    if (source.Value.IsCreated || source.Kind != RuntimeColliderKind.PlacedRef)
                        throw new InvalidDataException($"Runtime cell section '{path}' placed-ref root has invalid reusable RuntimeColliderSource placeholder; rebake required.");
                }
            }
        }

        static void ValidatePickColliders(EntityManager em, string path)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnPrefabPickCollider>(),
                ComponentType.ReadOnly<RuntimeCellSectionMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var source = RequireComponent<RuntimeColliderSource>(em, entities[i], path, "pick reusable collider source");
                if (source.Value.IsCreated || source.Kind != RuntimeColliderKind.InteractionPick)
                    throw new InvalidDataException($"Runtime cell section '{path}' pick collider has invalid reusable RuntimeColliderSource placeholder; rebake required.");
                RequireComponent<InteractionPickSurfaceTag>(em, entities[i], path, "interaction pick surface tag");
            }
        }

        static void ValidatePrebakedRenderEntity(EntityManager em, Entity entity, string path, string label)
        {
            RequireComponent<MaterialMeshInfo>(em, entity, path, $"{label} MaterialMeshInfo");
            RequireComponent<RenderBounds>(em, entity, path, $"{label} RenderBounds");
            if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                throw new InvalidDataException($"Runtime cell section '{path}' {label} serialized a RenderMeshArray shared component; rebake required.");
        }

        static void ValidateMaterialMeshInfo(EntityManager em, Entity entity, string path, string label)
        {
            var info = em.GetComponentData<MaterialMeshInfo>(entity);
            if (info.Mesh >= 0 || info.Material >= 0)
                throw new InvalidDataException($"Runtime cell section '{path}' {label} has runtime MaterialMeshInfo ids; rebake required.");
            int meshIndex = MaterialMeshInfo.StaticIndexToArrayIndex(info.Mesh);
            int materialIndex = MaterialMeshInfo.StaticIndexToArrayIndex(info.Material);
            if (meshIndex < 0 || materialIndex < 0)
                throw new InvalidDataException($"Runtime cell section '{path}' {label} has invalid logical MaterialMeshInfo placeholders; rebake required.");
        }

        static void ValidateExplicitRefEntries(EntityManager em, Entity sectionEntity, string path)
        {
            var entries = em.GetBuffer<RuntimeCellSectionExplicitRefEntry>(sectionEntity);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.ContentKey == 0 || entry.PlacedRefId == 0u)
                    throw new InvalidDataException($"Runtime cell section '{path}' explicit-ref entry {i} has invalid key/id.");
                if (entry.Entity == Entity.Null || !em.Exists(entry.Entity))
                    throw new InvalidDataException($"Runtime cell section '{path}' explicit-ref entry {i} references a missing entity.");

                var content = RequireComponent<LogicalRefContent>(em, entry.Entity, path, "explicit-ref logical content").Value;
                var identity = RequireComponent<PlacedRefIdentity>(em, entry.Entity, path, "explicit-ref placed identity");
                if (!content.IsValid
                    || ActiveExplicitRefLookupUtility.Pack(content) != entry.ContentKey
                    || identity.Value != entry.PlacedRefId)
                {
                    throw new InvalidDataException($"Runtime cell section '{path}' explicit-ref entry {i} is stale; rebake required.");
                }
            }
        }

        static Entity RequireSingleEntity<T>(EntityManager em, string path, string label)
            where T : unmanaged, IComponentData
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            int count = query.CalculateEntityCount();
            if (count != 1)
                throw new InvalidDataException($"Runtime cell section '{path}' must contain exactly one {label}; found {count}.");
            return query.GetSingletonEntity();
        }

        static void RequireBufferPresent<T>(EntityManager em, Entity entity, string path, string label)
            where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(entity))
                throw new InvalidDataException($"Runtime cell section '{path}' is missing {label}.");
        }

        static T RequireComponent<T>(EntityManager em, Entity entity, string path, string label)
            where T : unmanaged, IComponentData
        {
            if (!em.HasComponent<T>(entity))
                throw new InvalidDataException($"Runtime cell section '{path}' is missing {label}.");
            if (TypeManager.IsZeroSized(TypeManager.GetTypeIndex<T>()))
                return default;
            return em.GetComponentData<T>(entity);
        }

        static void RequireColliderSource(EntityManager em, Entity entity, RuntimeColliderKind expectedKind, string path, string label)
        {
            var source = RequireComponent<RuntimeColliderSource>(em, entity, path, label);
            if (!source.Value.IsCreated)
                throw new InvalidDataException($"Runtime cell section '{path}' {label} has no collider blob; rebake required.");
            if (source.Kind != expectedKind)
                throw new InvalidDataException($"Runtime cell section '{path}' {label} has kind {source.Kind}, expected {expectedKind}; rebake required.");
        }

        static void RequireBufferLength<T>(EntityManager em, Entity entity, int expected, string path, string label)
            where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(entity))
                throw new InvalidDataException($"Runtime cell section '{path}' is missing {label}.");
            int length = em.GetBuffer<T>(entity).Length;
            if (length != expected)
                throw new InvalidDataException($"Runtime cell section '{path}' has {length} {label}; expected {expected}.");
        }

        static bool TryFindResident(EntityManager em, bool isInterior, string path, string cellId, out RuntimeCellSectionLoadResult result)
            => TryFind(em, isInterior, path, cellId, requireResident: true, out result);

        static bool TryFindUnclaimed(EntityManager em, bool isInterior, string path, string cellId, out RuntimeCellSectionLoadResult result)
            => TryFind(em, isInterior, path, cellId, requireResident: false, out result);

        static bool TryFind(EntityManager em, bool isInterior, string path, string cellId, bool requireResident, out RuntimeCellSectionLoadResult result)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<RuntimeCellSectionHeader>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                bool hasResident = em.HasComponent<RuntimeCellSectionResident>(entity)
                    && em.IsComponentEnabled<RuntimeCellSectionResident>(entity);
                if (hasResident != requireResident)
                    continue;

                var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
                if (!Matches(header, isInterior, cellId, path))
                    continue;

                ValidateHeader(header, path, isInterior, cellId);
                result = new RuntimeCellSectionLoadResult(entity, header);
                return true;
            }

            result = default;
            return false;
        }

        static bool Matches(RuntimeCellSectionHeader header, bool isInterior, string cellId, string path)
        {
            if ((header.IsInterior != 0) != isInterior)
                return false;
            if (!isInterior)
            {
                if (!TryParseExteriorCoord(path, out int2 coord))
                    return true;
                return header.GridX == coord.x && header.GridY == coord.y;
            }

            return string.Equals(header.CellId.ToString(), cellId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryParseExteriorCoord(string path, out int2 coord)
        {
            coord = default;
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            int split = name.IndexOf('_');
            if (split <= 0 || split >= name.Length - 1)
                return false;
            if (!int.TryParse(name.Substring(0, split), out int x) || !int.TryParse(name.Substring(split + 1), out int y))
                return false;
            coord = new int2(x, y);
            return true;
        }

        static void ValidateRange(int start, int count, int total, string path, string label)
        {
            if (start < 0 || count < 0 || start > total || count > total - start)
                throw new InvalidDataException($"Runtime cell section '{path}' has invalid {label} range {start}+{count}/{total}.");
        }
    }
}
