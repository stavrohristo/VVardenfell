using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Esm;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;
using Object = UnityEngine.Object;

namespace VVardenfell.Importer.Bake
{
    internal static partial class WorldBakeService
    {
        private static void WriteRuntimeCellSection(
            StagedCellData staged,
            BlobAssetReference<Collider> terrainCollider,
            BlobAssetReference<Collider> staticCollider,
            ModelPrefabCatalogData modelPrefabCatalog,
            TextureBakery textures,
            MaterialBakery materials,
            BlobAssetReference<RuntimeContentBlob> runtimeContentBlob,
            CollisionBakery collisions)
        {
            string path = BuildCellSectionPath(staged.WorkItem);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            uint flags = BuildCellSectionFlags(staged);
            using var sectionWorld = new World($"VV.CellSectionBake({staged.WorkItem.Key})");
            var em = sectionWorld.EntityManager;
            using var renderBake = new RendererBakeResources(MaxSectionRenderMeshIndex(staged, modelPrefabCatalog), materials.Count);
            Entity section = CreateSectionRoot(em, staged, flags);
            var logicalByPlacedRef = new Dictionary<uint, Entity>();

            if ((flags & CacheFormat.CellFlagHasTerrain) != 0)
                CreateTerrainEntity(em, section, staged, flags, terrainCollider, renderBake);
            if (staticCollider.IsCreated)
                CreateStaticColliderEntity(em, section, staged, staticCollider);

            CreatePlacedRefEntities(em, section, staged, modelPrefabCatalog, textures, renderBake, logicalByPlacedRef, runtimeContentBlob, collisions);
            CreateCombinedRenderEntities(em, section, staged, renderBake, logicalByPlacedRef, textures, materials);

            using (var writer = new MemoryBinaryWriter(em))
            {
                SerializeUtility.SerializeWorld(em, writer, out var referencedObjects);
                if (referencedObjects.Length != 0)
                    throw new InvalidDataException($"Authored cell section '{staged.WorkItem.Key}' serialized {referencedObjects.Length} Unity render object references; direct runtime render ID sections must serialize none.");
                var bytes = new byte[writer.Length];
                unsafe
                {
                    Marshal.Copy((IntPtr)writer.Data, bytes, 0, writer.Length);
                }
                RuntimeRenderObjectReferenceFile.WriteWrappedEntityWorld(path, bytes, referencedObjects, renderBake.ObjectReferences);
            }

            if (!TryValidateAuthoredRuntimeCellSection(em, section, staged, flags, out string error))
                throw new InvalidDataException($"Authored invalid DOTS cell section '{path}' for '{staged.WorkItem.Key}': {error}");
        }

        static Entity CreateSectionRoot(EntityManager em, StagedCellData staged, uint flags)
        {
            Entity entity = em.CreateEntity();
            var workItem = staged.WorkItem;
            em.AddComponentData(entity, new RuntimeCellSectionHeader
            {
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                Flags = flags,
                GridX = SectionGridX(staged),
                GridY = SectionGridY(staged),
                IsInterior = (byte)(workItem.IsInterior ? 1 : 0),
                CellId = new FixedString128Bytes(SectionCellId(staged)),
                InteriorCellHash = workItem.IsInterior ? RuntimeContentStableHash.HashInteriorCellId(SectionCellId(staged)) : 0UL,
                Environment = new CellEnvironmentDataBlob
                {
                    HasMood = staged.Environment.HasMood,
                    HasWater = staged.Environment.HasWater,
                    AmbientColorRgba = staged.Environment.AmbientColorRgba,
                    DirectionalColorRgba = staged.Environment.DirectionalColorRgba,
                    FogColorRgba = staged.Environment.FogColorRgba,
                    FogDensity = staged.Environment.FogDensity,
                    WaterHeight = staged.Environment.WaterHeight,
                    RegionId = new FixedString128Bytes(staged.Environment.RegionId ?? string.Empty),
                },
            });
            em.AddComponentData(entity, new RuntimeCellSectionResident
            {
                ExteriorCoord = new int2(SectionGridX(staged), SectionGridY(staged)),
                InteriorCellHash = workItem.IsInterior ? RuntimeContentStableHash.HashInteriorCellId(SectionCellId(staged)) : 0UL,
                IsInterior = (byte)(workItem.IsInterior ? 1 : 0),
            });
            em.SetComponentEnabled<RuntimeCellSectionResident>(entity, false);
            em.AddComponent<RuntimeCellSectionResourcesBound>(entity);
            em.SetComponentEnabled<RuntimeCellSectionResourcesBound>(entity, false);
            em.AddBuffer<RuntimeCellSectionRenderEntity>(entity);
            em.AddBuffer<RuntimeCellSectionTerrainEntity>(entity);
            em.AddBuffer<RuntimeCellSectionCombinedRenderEntity>(entity);
            em.AddBuffer<RuntimeCellSectionColliderEntity>(entity);
            em.AddBuffer<RuntimeCellSectionLogicalRefEntity>(entity);
            em.AddBuffer<RuntimeCellSectionExplicitRefEntry>(entity);
            em.AddBuffer<RuntimeCellSectionActorInitEntity>(entity);
            em.AddBuffer<RuntimeCellSectionTransformRootEntity>(entity);
            return entity;
        }

        static RuntimeCellSectionMember BuildMembership(Entity section, StagedCellData staged)
            => new()
            {
                Section = section,
                ExteriorCoord = new int2(SectionGridX(staged), SectionGridY(staged)),
                InteriorCellHash = staged.WorkItem.IsInterior ? RuntimeContentStableHash.HashInteriorCellId(SectionCellId(staged)) : 0UL,
                IsInterior = (byte)(staged.WorkItem.IsInterior ? 1 : 0),
            };

        static void AddCellMembership(EntityManager em, Entity entity, Entity section, StagedCellData staged)
        {
            em.AddComponentData(entity, BuildMembership(section, staged));
            if (staged.WorkItem.IsInterior)
            {
                em.AddComponent<InteriorCellMember>(entity);
            }
            else
            {
                em.AddComponentData(entity, new CellLink { Value = new int2(SectionGridX(staged), SectionGridY(staged)) });
            }
        }

        static void AddSectionRenderEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionRenderEntity>(section).Add(new RuntimeCellSectionRenderEntity { Value = entity });

        static void AddSectionTerrainEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionTerrainEntity>(section).Add(new RuntimeCellSectionTerrainEntity { Value = entity });

        static void AddSectionCombinedRenderEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionCombinedRenderEntity>(section).Add(new RuntimeCellSectionCombinedRenderEntity { Value = entity });

        static void AddSectionColliderEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionColliderEntity>(section).Add(new RuntimeCellSectionColliderEntity { Value = entity });

        static void AddSectionLogicalRefEntity(EntityManager em, Entity section, Entity entity, ContentReference contentReference, uint placedRefId)
        {
            em.GetBuffer<RuntimeCellSectionLogicalRefEntity>(section).Add(new RuntimeCellSectionLogicalRefEntity { Value = entity });
            if (placedRefId == 0u || !contentReference.IsValid)
                return;
            em.GetBuffer<RuntimeCellSectionExplicitRefEntry>(section).Add(new RuntimeCellSectionExplicitRefEntry
            {
                ContentKey = PackExplicitRefContentKey(contentReference),
                PlacedRefId = placedRefId,
                Entity = entity,
            });
        }

        static int PackExplicitRefContentKey(ContentReference content)
            => ((int)content.Kind << 24) | (content.HandleValue & 0x00FFFFFF);

        static void AddSectionActorInitEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionActorInitEntity>(section).Add(new RuntimeCellSectionActorInitEntity { Value = entity });

        static void AddSectionTransformRootEntity(EntityManager em, Entity section, Entity entity)
            => em.GetBuffer<RuntimeCellSectionTransformRootEntity>(section).Add(new RuntimeCellSectionTransformRootEntity { Value = entity });

        static void CreateTerrainEntity(EntityManager em, Entity section, StagedCellData staged, uint flags, BlobAssetReference<Collider> terrainCollider, RendererBakeResources renderBake)
        {
            Entity entity = em.CreateEntity();
            em.AddComponent<RuntimeCellSectionTerrainTag>(entity);
            AddCellMembership(em, entity, section, staged);
            AddSectionTerrainEntity(em, section, entity);
            AddSectionTransformRootEntity(em, section, entity);
            em.AddComponentData(entity, new CellCoord { Value = new int2(SectionGridX(staged), SectionGridY(staged)) });
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                new float3(SectionGridX(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters, 0f, SectionGridY(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters),
                quaternion.identity,
                1f));
            em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
            em.AddComponent<Unity.Transforms.Static>(entity);
            if (staged.TerrainMeshIndex < 0)
                throw new InvalidDataException($"{staged.WorkItem.Key} terrain has no baked render mesh index.");
            if (staged.TerrainSplatSlice < 0)
                throw new InvalidDataException($"{staged.WorkItem.Key} terrain has no baked splat slice.");
            GetTerrainLocalBounds(staged.Land, out float3 boundsCenter, out float3 boundsExtents);
            em.AddComponentData(entity, new RuntimeCellSectionTerrainRenderResource
            {
                MeshIndex = staged.TerrainMeshIndex,
                SplatSlice = staged.TerrainSplatSlice,
                BoundsCenter = boundsCenter,
                BoundsExtents = boundsExtents,
            });
            RenderMeshUtility.AddComponents(
                entity,
                em,
                renderBake.RenderDesc,
                renderBake.TerrainRenderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, staged.TerrainMeshIndex));
            em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
            em.SetComponentData(entity, new RenderBounds
            {
                Value = new AABB
                {
                    Center = boundsCenter,
                    Extents = boundsExtents,
                }
            });
            em.AddComponentData(entity, new TerrainSplatSlice { Value = staged.TerrainSplatSlice });
            StripBakeOnlyRenderMeshArray(em, entity);
            if (terrainCollider.IsCreated)
            {
                em.AddComponentData(entity, new RuntimeCellSectionTerrainCollider { Blob = terrainCollider });
                em.AddComponentData(entity, new RuntimeColliderSource
                {
                    Value = terrainCollider,
                    Kind = RuntimeColliderKind.TerrainCell,
                });
                AddSectionColliderEntity(em, section, entity);
            }

            AppendHeights(em.AddBuffer<RuntimeCellSectionTerrainHeight>(entity), staged.Land);
            if ((flags & CacheFormat.CellFlagHasNormals) != 0)
                AppendNormals(em.AddBuffer<RuntimeCellSectionTerrainNormal>(entity), staged.Land);
            if ((flags & CacheFormat.CellFlagHasVtex) != 0)
                AppendLayers(em.AddBuffer<RuntimeCellSectionTerrainLayer>(entity), staged.LayerGrid);
            if ((flags & CacheFormat.CellFlagHasWorldMap) != 0)
                AppendWorldMap(em.AddBuffer<RuntimeCellSectionWorldMapSample>(entity), staged.Land);
        }

        static void CreateStaticColliderEntity(EntityManager em, Entity section, StagedCellData staged, BlobAssetReference<Collider> staticCollider)
        {
            Entity entity = em.CreateEntity();
            em.AddComponent<RuntimeCellSectionStaticColliderTag>(entity);
            AddCellMembership(em, entity, section, staged);
            AddSectionColliderEntity(em, section, entity);
            AddSectionTransformRootEntity(em, section, entity);
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                staged.WorkItem.IsInterior
                    ? float3.zero
                    : new float3(SectionGridX(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters, 0f, SectionGridY(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters),
                quaternion.identity,
                1f));
            em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
            em.AddComponent<Unity.Transforms.Static>(entity);
            em.AddComponentData(entity, new RuntimeCellSectionStaticCollider { Blob = staticCollider });
            em.AddComponentData(entity, new RuntimeColliderSource
            {
                Value = staticCollider,
                Kind = RuntimeColliderKind.StaticCell,
            });
        }

        static void GetTerrainLocalBounds(LandRecord land, out float3 center, out float3 extents)
        {
            if (land?.Heights == null || land.Heights.Length != LandRecord.Size * LandRecord.Size)
                throw new InvalidDataException("terrain height count mismatch.");

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < land.Heights.Length; i++)
            {
                float y = land.Heights[i] * WorldScale.MwUnitsToMeters;
                minY = math.min(minY, y);
                maxY = math.max(maxY, y);
            }

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            center = new float3(cellMeters * 0.5f, (minY + maxY) * 0.5f, cellMeters * 0.5f);
            extents = new float3(cellMeters * 0.5f, math.max(0.01f, (maxY - minY) * 0.5f), cellMeters * 0.5f);
        }

        static void CreatePlacedRefEntities(
            EntityManager em,
            Entity section,
            StagedCellData staged,
            ModelPrefabCatalogData modelPrefabCatalog,
            TextureBakery textures,
            RendererBakeResources renderBake,
            Dictionary<uint, Entity> logicalByPlacedRef,
            BlobAssetReference<RuntimeContentBlob> runtimeContentBlob,
            CollisionBakery collisions)
        {
            if (!runtimeContentBlob.IsCreated)
                throw new InvalidDataException($"{staged.WorkItem.Key} cannot author logical refs without runtime content blob.");
            ref RuntimeContentBlob runtimeContent = ref runtimeContentBlob.Value;
            var refs = staged.BakedRefs ?? new List<CellBakery.BakedRef>();
            var records = modelPrefabCatalog?.Records ?? Array.Empty<ModelPrefabDef>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            try
            {
                for (int i = 0; i < refs.Count; i++)
                {
                    var entry = ToRefEntry(refs[i]);
                    if (!TryGetContentReference(entry, out ContentReference contentReference))
                        continue;

                    Entity logicalEntity = CreateLogicalRefEntity(em, section, staged, entry, contentReference, ref runtimeContent, ref ecb);
                    em.AddComponentData(logicalEntity, new RuntimeCellSectionRefOrder { Value = i });
                    if (entry.PlacedRefId != 0u)
                        logicalByPlacedRef[entry.PlacedRefId] = logicalEntity;

                    if ((RefSpawnMode)entry.SpawnModeRaw != RefSpawnMode.ModelPrefab || entry.ModelPrefabIndex < 0)
                        continue;
                    if ((uint)entry.ModelPrefabIndex >= (uint)records.Length)
                        throw new InvalidDataException($"{staged.WorkItem.Key} ref 0x{entry.PlacedRefId:X8} references model prefab {entry.ModelPrefabIndex}, outside catalog length {records.Length}.");

                    Entity renderRoot = CreatePlacedModelGraph(em, section, staged, records[entry.ModelPrefabIndex], entry, textures, renderBake, collisions);
                    if (renderRoot == Entity.Null)
                        continue;

                    AppendLogicalChild(em, logicalEntity, renderRoot);
                    AddLogicalParentToLinkedGraph(em, logicalEntity, renderRoot);
                }

                ecb.Playback(em);
            }
            finally
            {
                ecb.Dispose();
            }
        }

        static Entity CreateLogicalRefEntity(
            EntityManager em,
            Entity section,
            StagedCellData staged,
            RefEntry entry,
            ContentReference contentReference,
            ref RuntimeContentBlob runtimeContent,
            ref EntityCommandBuffer ecb)
        {
            Entity entity = em.CreateEntity();
            em.AddComponent<LogicalRefTag>(entity);
            em.AddComponentData(entity, new PlacedRefIdentity { Value = entry.PlacedRefId });
            em.AddComponentData(entity, new LogicalRefContent { Value = contentReference });
            em.AddComponentData(entity, new PlacedRefRuntimeState());
            em.AddComponentData(entity, new PlacedRefInitialTransform
            {
                Position = new float3(entry.PosX, entry.PosY, entry.PosZ),
                Rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                Scale = entry.Scale,
            });
            em.AddComponentData(entity, new LogicalRefLocation
            {
                ExteriorCell = new int2(SectionGridX(staged), SectionGridY(staged)),
                InteriorCellId = staged.WorkItem.IsInterior ? new FixedString128Bytes(SectionCellId(staged)) : default,
                InteriorCellHash = staged.WorkItem.IsInterior ? RuntimeContentStableHash.HashInteriorCellId(SectionCellId(staged)) : 0UL,
                IsInterior = (byte)(staged.WorkItem.IsInterior ? 1 : 0),
            });
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                new float3(entry.PosX, entry.PosY, entry.PosZ),
                new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                entry.Scale));
            em.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(
                    new float3(entry.PosX, entry.PosY, entry.PosZ),
                    new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                    new float3(entry.Scale)),
            });
            em.AddBuffer<LogicalRefChild>(entity);
            AddCellMembership(em, entity, section, staged);
            AddSectionLogicalRefEntity(em, section, entity, contentReference, entry.PlacedRefId);
            AddSectionTransformRootEntity(em, section, entity);
            AddPlacedMetadata(em, entity, staged, entry, ref runtimeContent);
            DoorInteractable door = default;
            bool attachDoor = false;
            if (contentReference.Kind == ContentReferenceKind.Door)
            {
                if (!em.HasComponent<RuntimeCellSectionDoorMetadata>(entity))
                    throw new InvalidDataException($"{staged.WorkItem.Key} door ref 0x{entry.PlacedRefId:X8} is missing door metadata.");
                var metadata = em.GetComponentData<RuntimeCellSectionDoorMetadata>(entity);
                door = new DoorInteractable
                {
                    IsTeleport = (byte)((metadata.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                    DestinationCellId = metadata.DestinationCellId,
                    DestinationCellHash = string.IsNullOrWhiteSpace(metadata.DestinationCellId.ToString()) ? 0UL : RuntimeContentStableHash.HashInteriorCellId(metadata.DestinationCellId.ToString()),
                    DestinationPosition = metadata.DestinationPosition,
                    DestinationRotation = metadata.DestinationRotation,
                };
                attachDoor = true;
            }

            if (!QueueStableLogicalRefAuthoring(ref ecb, entity, ref runtimeContent, contentReference, staged, entry, attachDoor, door))
                throw new InvalidDataException($"{staged.WorkItem.Key} ref 0x{entry.PlacedRefId:X8} content {contentReference.Kind}:{contentReference.HandleValue} could not be authored.");
            if (contentReference.Kind == ContentReferenceKind.Actor)
            {
                ecb.AddComponent<RuntimeCellSectionActorNeedsInitialization>(entity);
                AddSectionActorInitEntity(em, section, entity);
            }
            if (contentReference.Kind == ContentReferenceKind.Door)
                ecb.AddComponent<InteractionActivationProxyBuildPending>(entity);
            return entity;
        }

        static void AddPlacedMetadata(EntityManager em, Entity entity, StagedCellData staged, RefEntry entry, ref RuntimeContentBlob runtimeContent)
        {
            if (entry.DoorMetaIndex >= 0 && staged.DoorEntries != null && entry.DoorMetaIndex < staged.DoorEntries.Count)
            {
                var door = staged.DoorEntries[entry.DoorMetaIndex];
                if (door.PlacedRefId == entry.PlacedRefId)
                {
                    em.AddComponentData(entity, new RuntimeCellSectionDoorMetadata
                    {
                        Flags = door.Flags,
                        DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                        DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
                        DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                    });
                }
            }

            var locks = staged.LockStates;
            for (int i = 0; i < (locks?.Count ?? 0); i++)
            {
                var state = locks[i];
                if (state.PlacedRefId != entry.PlacedRefId)
                    continue;
                em.AddComponentData(entity, new PlacedRefLockState
                {
                    LockLevel = state.LockLevel,
                    Locked = state.Locked,
                    KeyId = new FixedString64Bytes(state.KeyId ?? string.Empty),
                    TrapId = new FixedString64Bytes(state.TrapId ?? string.Empty),
                });
                break;
            }

            var souls = staged.CapturedSouls;
            for (int i = 0; i < (souls?.Count ?? 0); i++)
            {
                var soul = souls[i];
                if (soul.PlacedRefId != entry.PlacedRefId)
                    continue;
                em.AddComponentData(entity, new PlacedRefCapturedSoul
                {
                    SoulId = new FixedString64Bytes(soul.SoulId ?? string.Empty),
                    SoulActorHandleValue = ResolveCapturedSoulActorHandle(staged, entry.PlacedRefId, soul.SoulId, ref runtimeContent),
                });
                break;
            }
        }

        static int ResolveCapturedSoulActorHandle(StagedCellData staged, uint placedRefId, string soulId, ref RuntimeContentBlob runtimeContent)
        {
            if (string.IsNullOrWhiteSpace(soulId))
                throw new InvalidDataException($"{staged.WorkItem.Key} captured soul ref 0x{placedRefId:X8} has no soul id.");

            ulong soulIdHash = RuntimeContentStableHash.HashId(soulId);
            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref runtimeContent, soulIdHash, out var actorHandle) || !actorHandle.IsValid)
                throw new InvalidDataException($"{staged.WorkItem.Key} captured soul '{soulId}' for ref 0x{placedRefId:X8} does not resolve to an actor.");
            return actorHandle.Value;
        }

        static bool QueueStableLogicalRefAuthoring(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ContentReference contentReference,
            StagedCellData staged,
            RefEntry entry,
            bool attachDoorInteractable,
            DoorInteractable doorInteractable)
        {
            if (!RuntimeContentBlobUtility.IsValid(ref content, contentReference))
                return false;

            switch (contentReference.Kind)
            {
                case ContentReferenceKind.Actor:
                {
                    var handle = new ActorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ActorSpawnSource { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildPassiveActorPresence(ref actor));
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, actor.ScriptIdHash);
                    ActorBaselineAuthoringUtility.QueueBaseline(
                        ref ecb,
                        logicalEntity,
                        ref content,
                        handle,
                        ref actor,
                        worldPosition: new float3(entry.PosX, entry.PosY, entry.PosZ),
                        isInterior: staged.WorkItem.IsInterior,
                        exteriorCell: new int2(SectionGridX(staged), SectionGridY(staged)),
                        interiorCellId: staged.WorkItem.IsInterior ? new FixedString128Bytes(SectionCellId(staged)) : default);
                    return true;
                }
                case ContentReferenceKind.Activator:
                {
                    var handle = new ActivatorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ActivatorAuthoring { Definition = handle });
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }
                case ContentReferenceKind.Door:
                {
                    var handle = new DoorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new DoorAuthoring { Definition = handle });
                    if (!attachDoorInteractable)
                        throw new InvalidDataException($"[VVardenfell][CellSectionBake] door handle {handle.Value} has no placed door metadata.");
                    if (doorInteractable.IsTeleport == 0)
                    {
                        ecb.AddComponent(logicalEntity, new DoorMotionState
                        {
                            RangeRadians = math.radians(90f),
                            SpeedRadiansPerSecond = math.radians(90f),
                            Axis = 2,
                        });
                        ecb.AddComponent<DoorActivated>(logicalEntity);
                        ecb.SetComponentEnabled<DoorActivated>(logicalEntity, false);
                    }
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    ecb.AddComponent(logicalEntity, doorInteractable);
                    return true;
                }
                case ContentReferenceKind.Container:
                {
                    var handle = new ContainerDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ContainerAuthoring { Definition = handle });
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }
                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ItemPickupAuthoring { Definition = handle });
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    if (def.RecordTag == MakeTag('B', 'O', 'O', 'K'))
                        ecb.AddComponent<BookTag>(logicalEntity);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }
                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeLightDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new LightSourceAuthoring { Definition = handle });
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    var flags = BuildLightInstanceFlags(def.Flags);
                    ecb.AddComponent(logicalEntity, flags);
                    ecb.AddComponent(logicalEntity, BuildLightInstanceState(ref def));
                    if (IsAnimatedLight(flags))
                        ecb.AddComponent<LightInstanceAnimated>(logicalEntity);
                    ecb.AddComponent(logicalEntity, new LightPresentationLink { Slot = -1 });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, 0UL);
                    return true;
                }
                case ContentReferenceKind.Static:
                {
                    var handle = new GenericRecordDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeGenericRecordDefBlob def = ref RuntimeContentBlobUtility.GetStatic(ref content, handle);
                    ecb.AddComponent(logicalEntity, new StaticRefAuthoring { Definition = handle });
                    TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    return true;
                }
                case ContentReferenceKind.LeveledItem:
                    ecb.AddComponent(logicalEntity, new LeveledItemAuthoring { Definition = new ItemLeveledListDefHandle { Value = contentReference.HandleValue } });
                    return true;
                case ContentReferenceKind.LeveledCreature:
                    ecb.AddComponent(logicalEntity, new LeveledCreatureAuthoring { Definition = new CreatureLeveledListDefHandle { Value = contentReference.HandleValue } });
                    return true;
                default:
                    return false;
            }
        }

        static PassiveActorPresence BuildPassiveActorPresence(ref RuntimeActorDefBlob actor)
        {
            bool canTalk = actor.Kind == ActorDefKind.Npc;
            return new PassiveActorPresence
            {
                Family = (byte)(actor.Kind == ActorDefKind.Npc ? PassiveActorFamily.Npc : PassiveActorFamily.Creature),
                CanTalk = (byte)(canTalk ? 1 : 0),
            };
        }

        static bool TryQueueObjectScriptByIdHash(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ulong scriptIdHash)
        {
            if (scriptIdHash == 0UL)
                return false;
            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref content, scriptIdHash, out var programHandle) || !programHandle.IsValid)
                return false;

            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            var status = (MorrowindScriptProgramStatus)program.Status;
            ecb.AddComponent(logicalEntity, new MorrowindScriptInstance
            {
                Program = programHandle,
                ProgramIndex = programHandle.Index,
                Status = status == MorrowindScriptProgramStatus.Compiled
                    ? (byte)MorrowindScriptInstanceStatus.Running
                    : (byte)MorrowindScriptInstanceStatus.Disabled,
                DisabledReason = status == MorrowindScriptProgramStatus.Compiled
                    ? default
                    : new FixedString128Bytes(program.DisabledReason.ToString()),
            });
            RuntimeContentBlobUtility.RequireRange(program.FirstLocalIndex, program.LocalCount, content.MorrowindScriptLocals.Length, "script local");
            if (program.LocalCount > 0)
            {
                var locals = ecb.AddBuffer<MorrowindScriptLocalValue>(logicalEntity);
                for (int i = 0; i < program.LocalCount; i++)
                    locals.Add(new MorrowindScriptLocalValue { ValueKind = content.MorrowindScriptLocals[program.FirstLocalIndex + i].ValueKind });
            }
            if (status == MorrowindScriptProgramStatus.Compiled)
                ecb.AddBuffer<MorrowindScriptStackValue>(logicalEntity);
            return true;
        }

        static void TryQueueAudioEmitterAuthoring(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ulong primarySoundIdHash,
            ulong secondarySoundIdHash)
        {
            TryGetSoundHandle(ref content, primarySoundIdHash, out SoundDefHandle primarySound);
            TryGetSoundHandle(ref content, secondarySoundIdHash, out SoundDefHandle secondarySound);
            if (!primarySound.IsValid && !secondarySound.IsValid)
                return;
            ecb.AddComponent(logicalEntity, new AudioEmitterAuthoring
            {
                PrimarySound = primarySound,
                SecondarySound = secondarySound,
            });
        }

        static bool TryGetSoundHandle(ref RuntimeContentBlob content, ulong soundIdHash, out SoundDefHandle handle)
        {
            handle = default;
            return soundIdHash != 0UL
                   && RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref content, soundIdHash, out handle)
                   && handle.IsValid;
        }

        static LightInstanceFlags BuildLightInstanceFlags(int flags)
        {
            return new LightInstanceFlags
            {
                Carry = (byte)((flags & 0x002) != 0 ? 1 : 0),
                Negative = (byte)((flags & 0x004) != 0 ? 1 : 0),
                Flicker = (byte)((flags & 0x008) != 0 ? 1 : 0),
                OffDefault = (byte)((flags & 0x020) != 0 ? 1 : 0),
                FlickerSlow = (byte)((flags & 0x040) != 0 ? 1 : 0),
                Pulse = (byte)((flags & 0x080) != 0 ? 1 : 0),
                PulseSlow = (byte)((flags & 0x100) != 0 ? 1 : 0),
            };
        }

        static bool IsAnimatedLight(in LightInstanceFlags flags)
            => flags.Flicker != 0 || flags.FlickerSlow != 0 || flags.Pulse != 0 || flags.PulseSlow != 0;

        static LightInstanceState BuildLightInstanceState(ref RuntimeLightDefBlob def)
        {
            float3 color = new float3(
                ((def.ColorRgba >> 0) & 0xFFu) / 255f,
                ((def.ColorRgba >> 8) & 0xFFu) / 255f,
                ((def.ColorRgba >> 16) & 0xFFu) / 255f);
            float rangeMeters = math.max(0.25f, def.Radius * WorldScale.MwUnitsToMeters);
            float intensity = math.max(0.25f, math.cmax(color));
            return new LightInstanceState
            {
                Enabled = (byte)((def.Flags & 0x020) == 0 ? 1 : 0),
                BaseColorRgb = color,
                BaseIntensity = intensity,
                BaseRange = rangeMeters,
                CurrentIntensity = intensity,
                CurrentRange = rangeMeters,
            };
        }

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

        static Entity CreatePlacedModelGraph(
            EntityManager em,
            Entity section,
            StagedCellData staged,
            ModelPrefabDef def,
            RefEntry entry,
            TextureBakery textures,
            RendererBakeResources renderBake,
            CollisionBakery collisions)
        {
            var nodes = def?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            if (nodes.Length == 0)
                return Entity.Null;

            var entities = new Entity[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i] ?? throw new InvalidDataException($"Model prefab {entry.ModelPrefabIndex} has null node {i}.");
                Entity entity = em.CreateEntity();
                entities[i] = entity;
                em.AddComponentData(entity, new RuntimeSpawnPrefabNode
                {
                    ModelPrefabIndex = entry.ModelPrefabIndex,
                    NodeIndex = i,
                    ParentIndex = node.ParentIndex,
                    Kind = (byte)node.Kind,
                });
                em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                    new float3(node.PosX, node.PosY, node.PosZ),
                    new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                    node.Scale));
                em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                AddStablePlacedModelNodeRuntimeMetadata(em, entity, def, node, entry.ModelPrefabIndex, i, root: Entity.Null);
                AddCellMembership(em, entity, section, staged);
            }

            int rootIndex = math.clamp(def.RootNodeIndex, 0, entities.Length - 1);
            Entity root = entities[rootIndex];
            AddSectionTransformRootEntity(em, section, root);
            em.SetComponentData(root, LocalTransform.FromPositionRotationScale(
                new float3(entry.PosX, entry.PosY, entry.PosZ),
                new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                entry.Scale));
            em.SetComponentData(root, new LocalToWorld
            {
                Value = float4x4.TRS(
                    new float3(entry.PosX, entry.PosY, entry.PosZ),
                    new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
                    new float3(entry.Scale)),
            });
            em.AddComponentData(root, new PlacedRefIdentity { Value = entry.PlacedRefId });
            em.AddComponentData(root, new RuntimeCellSectionRenderRoot
            {
                ModelPrefabIndex = entry.ModelPrefabIndex,
                CollisionIndex = entry.CollisionIndex,
            });
            if (entry.CollisionIndex >= 0)
            {
                ValidateGlobalColliderIndex(collisions, entry.CollisionIndex, $"{staged.WorkItem.Key} placed ref 0x{entry.PlacedRefId:X8}");
                em.AddComponentData(root, new RuntimeColliderSource
                {
                    Kind = RuntimeColliderKind.PlacedRef,
                });
                AddSectionColliderEntity(em, section, root);
            }

            for (int i = 0; i < entities.Length; i++)
                PatchObjectAnimationRoot(em, entities[i], root);

            var linked = em.AddBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < entities.Length; i++)
                linked.Add(new LinkedEntityGroup { Value = entities[i] });

            for (int i = 0; i < entities.Length; i++)
            {
                var node = nodes[i];
                Entity entity = entities[i];
                if (i != rootIndex && node.ParentIndex >= 0 && node.ParentIndex < entities.Length)
                    em.AddComponentData(entity, new Parent { Value = entities[node.ParentIndex] });

                if (node.Kind != ModelPrefabNodeKind.RenderLeaf || node.GlobalMeshIndex < 0)
                    continue;

                int textureSlice = ResolveTextureSlice(textures, node.TextureIndex);
                em.AddComponentData(entity, new RuntimeSpawnPrefabRenderResource
                {
                    ModelPrefabIndex = entry.ModelPrefabIndex,
                    NodeIndex = i,
                    MeshIndex = node.GlobalMeshIndex,
                    MaterialIndex = node.MaterialIndex,
                    TextureIndex = node.TextureIndex,
                    BoundsCenter = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                    BoundsExtents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                });
                em.AddComponentData(entity, new ModelPrefabRenderLeaf
                {
                    NodeIndex = i,
                    MeshIndex = node.GlobalMeshIndex,
                    MaterialIndex = node.MaterialIndex,
                    TextureIndex = node.TextureIndex,
                });
                RenderMeshUtility.AddComponents(
                    entity,
                    em,
                    renderBake.RenderDesc,
                    renderBake.RefRenderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(node.MaterialIndex, node.GlobalMeshIndex));
                em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
                em.SetComponentData(entity, new RenderBounds
                {
                    Value = new AABB
                    {
                        Center = new float3(node.BoundsCenterX, node.BoundsCenterY, node.BoundsCenterZ),
                        Extents = new float3(node.BoundsExtentsX, node.BoundsExtentsY, node.BoundsExtentsZ),
                    }
                });
                em.AddComponentData(entity, new TextureSlice { Value = textureSlice });
                AddSectionRenderEntity(em, section, entity);
                StripBakeOnlyRenderMeshArray(em, entity);
                if (node.PickColliderIndex >= 0)
                {
                    if (entity == root && entry.CollisionIndex >= 0)
                        throw new InvalidDataException($"Placed ref 0x{entry.PlacedRefId:X8} model prefab {entry.ModelPrefabIndex} node {i} cannot bake both root and pick collider sources on one entity.");
                    ValidateGlobalColliderIndex(collisions, node.PickColliderIndex, $"{staged.WorkItem.Key} placed ref 0x{entry.PlacedRefId:X8} pick node {i}");
                    em.AddComponentData(entity, new RuntimeSpawnPrefabPickCollider
                    {
                        ColliderIndex = node.PickColliderIndex,
                    });
                    em.AddComponentData(entity, new RuntimeColliderSource
                    {
                        Kind = RuntimeColliderKind.InteractionPick,
                    });
                    em.AddComponent<InteractionPickSurfaceTag>(entity);
                    AddSectionColliderEntity(em, section, entity);
                }
            }

            return root;
        }

        static void AddStablePlacedModelNodeRuntimeMetadata(
            EntityManager em,
            Entity entity,
            ModelPrefabDef def,
            ModelPrefabNodeDef node,
            int modelPrefabIndex,
            int nodeIndex,
            Entity root)
        {
            em.AddComponent<ModelPrefabNodeTag>(entity);

            if (def.ObjectAnimation?.IsEnabled == true)
            {
                em.AddComponentData(entity, new ObjectAnimationNode
                {
                    Root = root,
                    ModelPrefabIndex = modelPrefabIndex,
                    NodeIndex = nodeIndex,
                    ParentIndex = node.ParentIndex,
                    BindPosition = new float3(node.PosX, node.PosY, node.PosZ),
                    BindRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                    BindScale = node.Scale,
                });
            }

            if (node.Kind == ModelPrefabNodeKind.Billboard)
            {
                em.AddComponent<ModelBillboardTag>(entity);
                em.AddComponentData(entity, new ModelBillboardState
                {
                    BaseLocalRotation = new quaternion(node.RotX, node.RotY, node.RotZ, node.RotW),
                });
            }
        }

        static void PatchObjectAnimationRoot(EntityManager em, Entity entity, Entity root)
        {
            if (!em.HasComponent<ObjectAnimationNode>(entity))
                return;
            var node = em.GetComponentData<ObjectAnimationNode>(entity);
            node.Root = root;
            em.SetComponentData(entity, node);
        }

        static void ValidateGlobalColliderIndex(CollisionBakery collisions, int index, string context)
        {
            if (collisions == null || (uint)index >= (uint)collisions.Count)
                throw new InvalidDataException($"{context} references missing global collider index {index}.");
        }

        static void AddLogicalParentToLinkedGraph(EntityManager em, Entity logicalEntity, Entity renderRoot)
        {
            if (!em.HasBuffer<LinkedEntityGroup>(renderRoot))
                return;
            var linked = em.GetBuffer<LinkedEntityGroup>(renderRoot);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < linked.Length; i++)
                ecb.AddComponent(linked[i].Value, new LogicalRefParent { Value = logicalEntity });
            ecb.Playback(em);
        }

        static void AppendLogicalChild(EntityManager em, Entity logicalEntity, Entity renderRoot)
        {
            var children = em.GetBuffer<LogicalRefChild>(logicalEntity);
            if (em.HasBuffer<LinkedEntityGroup>(renderRoot))
            {
                var linked = em.GetBuffer<LinkedEntityGroup>(renderRoot);
                for (int i = 0; i < linked.Length; i++)
                    children.Add(new LogicalRefChild { Value = linked[i].Value });
            }
            else
            {
                children.Add(new LogicalRefChild { Value = renderRoot });
            }
        }

        static void CreateCombinedRenderEntities(
            EntityManager em,
            Entity section,
            StagedCellData staged,
            RendererBakeResources renderBake,
            Dictionary<uint, Entity> logicalByPlacedRef,
            TextureBakery textures,
            MaterialBakery materials)
        {
            var chunks = staged.CombinedRenderChunks ?? new List<CombinedCellRenderChunkDef>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var source = chunks[i] ?? throw new InvalidDataException($"Combined render chunk {i} is null.");
                if (source.GlobalMeshIndex < 0)
                    throw new InvalidDataException($"Combined render chunk {i} has no baked global mesh index.");
                Entity entity = em.CreateEntity();
                AddCellMembership(em, entity, section, staged);
                AddSectionCombinedRenderEntity(em, section, entity);
                AddSectionTransformRootEntity(em, section, entity);
                em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(
                    staged.WorkItem.IsInterior
                        ? float3.zero
                        : new float3(SectionGridX(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters, 0f, SectionGridY(staged) * LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters),
                    quaternion.identity,
                    1f));
                em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                em.AddComponentData(entity, new RuntimeCellSectionCombinedRenderResource
                {
                    MeshIndex = source.GlobalMeshIndex,
                    MaterialIndex = source.MaterialIndex,
                    TextureBucketKey = source.TextureBucketKey,
                    TileX = source.TileX,
                    TileY = source.TileY,
                    BoundsCenter = new float3(source.BoundsCenterX, source.BoundsCenterY, source.BoundsCenterZ),
                    BoundsExtents = new float3(source.BoundsExtentsX, source.BoundsExtentsY, source.BoundsExtentsZ),
                    VertexCount = source.VertexCount,
                    IndexCount = source.IndexCount,
                    MeshFlags = source.MeshFlags,
                });
                em.AddComponentData(entity, new CombinedCellRenderChunk
                {
                    Cell = new int2(SectionGridX(staged), SectionGridY(staged)),
                    TileX = source.TileX,
                    TileY = source.TileY,
                    MaterialIndex = source.MaterialIndex,
                    TextureBucketKey = source.TextureBucketKey,
                    Disabled = 0,
                });
                em.AddComponent<Unity.Transforms.Static>(entity);
                RenderMeshUtility.AddComponents(
                    entity,
                    em,
                    renderBake.RenderDesc,
                    renderBake.CombinedRenderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(source.MaterialIndex, source.GlobalMeshIndex));
                em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
                em.SetComponentData(entity, new RenderBounds
                {
                    Value = new AABB
                    {
                        Center = new float3(source.BoundsCenterX, source.BoundsCenterY, source.BoundsCenterZ),
                        Extents = new float3(source.BoundsExtentsX, source.BoundsExtentsY, source.BoundsExtentsZ),
                    }
                });
                StripBakeOnlyRenderMeshArray(em, entity);

                em.AddBuffer<CombinedCellRenderChunkMember>(entity);
                var members = source.Members ?? Array.Empty<CombinedCellRenderChunkMemberDef>();
                for (int m = 0; m < members.Length; m++)
                {
                    var member = members[m] ?? throw new InvalidDataException($"Combined render chunk {i} member {m} is null.");
                    BakeCombinedMemberLink(em, staged, entity, source, member, logicalByPlacedRef, textures, materials);
                }
            }
        }


        static void BakeCombinedMemberLink(
            EntityManager em,
            StagedCellData staged,
            Entity chunkEntity,
            CombinedCellRenderChunkDef source,
            CombinedCellRenderChunkMemberDef member,
            Dictionary<uint, Entity> logicalByPlacedRef,
            TextureBakery textures,
            MaterialBakery materials)
        {
            if (!logicalByPlacedRef.TryGetValue(member.PlacedRefId, out Entity logicalEntity)
                || logicalEntity == Entity.Null
                || !em.Exists(logicalEntity))
            {
                throw new InvalidDataException($"{staged.WorkItem.Key} combined static member 0x{member.PlacedRefId:X8} has no baked logical entity.");
            }

            Entity renderEntity = FindCombinedMemberRenderLeaf(
                em,
                staged,
                source,
                member,
                logicalEntity,
                textures,
                materials);
            if (renderEntity == Entity.Null)
                throw new InvalidDataException($"{staged.WorkItem.Key} combined static member 0x{member.PlacedRefId:X8} node {member.NodeIndex} has no matching render leaf.");

            if (!em.HasComponent<CombinedCellRenderSuppressed>(renderEntity))
                em.AddComponent<CombinedCellRenderSuppressed>(renderEntity);
            if (!em.HasComponent<MaterialMeshInfo>(renderEntity))
                throw new InvalidDataException($"{staged.WorkItem.Key} combined static member 0x{member.PlacedRefId:X8} node {member.NodeIndex} render leaf has no baked MaterialMeshInfo.");
            em.SetComponentEnabled<MaterialMeshInfo>(renderEntity, false);

            em.GetBuffer<CombinedCellRenderChunkMember>(chunkEntity).Add(new CombinedCellRenderChunkMember
            {
                RenderEntity = renderEntity,
                LogicalRefEntity = logicalEntity,
                PlacedRefId = member.PlacedRefId,
                NodeIndex = member.NodeIndex,
            });

            DynamicBuffer<CombinedCellRenderLink> links = em.HasBuffer<CombinedCellRenderLink>(logicalEntity)
                ? em.GetBuffer<CombinedCellRenderLink>(logicalEntity)
                : em.AddBuffer<CombinedCellRenderLink>(logicalEntity);
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].Chunk == chunkEntity)
                    return;
            }
            links.Add(new CombinedCellRenderLink { Chunk = chunkEntity });
        }


        static Entity FindCombinedMemberRenderLeaf(
            EntityManager em,
            StagedCellData staged,
            CombinedCellRenderChunkDef source,
            CombinedCellRenderChunkMemberDef member,
            Entity logicalEntity,
            TextureBakery textures,
            MaterialBakery materials)
        {
            if (!em.HasBuffer<LogicalRefChild>(logicalEntity))
                return Entity.Null;
            var childBuffer = em.GetBuffer<LogicalRefChild>(logicalEntity);
            var children = new Entity[childBuffer.Length];
            for (int i = 0; i < childBuffer.Length; i++)
                children[i] = childBuffer[i].Value;

            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i];
                if (child == Entity.Null || !em.Exists(child) || !em.HasComponent<ModelPrefabRenderLeaf>(child))
                    continue;
                var leaf = em.GetComponentData<ModelPrefabRenderLeaf>(child);
                if (leaf.NodeIndex != member.NodeIndex)
                    continue;
                int combinedMaterialIndex = GetCombinedCellRenderMaterialIndex(materials.GetFlags(leaf.MaterialIndex));
                if (combinedMaterialIndex != source.MaterialIndex)
                    throw new InvalidDataException($"{staged.WorkItem.Key} combined static member 0x{member.PlacedRefId:X8} node {member.NodeIndex} material variant mismatch.");
                int textureBucketKey = GetCombinedCellRenderTextureBucketKey(leaf.TextureIndex, textures);
                if (textureBucketKey != source.TextureBucketKey)
                    throw new InvalidDataException($"{staged.WorkItem.Key} combined static member 0x{member.PlacedRefId:X8} node {member.NodeIndex} texture bucket mismatch.");
                return child;
            }

            return Entity.Null;
        }

        static RefEntry ToRefEntry(CellBakery.BakedRef source)
            => new()
            {
                SpawnModeRaw = source.SpawnModeRaw,
                ModelPrefabIndex = source.ModelPrefabIndex,
                LocalMeshIndex = source.LocalMeshIndex,
                LocalMaterialIndex = source.LocalMaterialIndex,
                SliceIndex = source.SliceIndex,
                CollisionIndex = source.CollisionIndex,
                PlacedRefId = source.PlacedRefId,
                DoorMetaIndex = source.DoorMetaIndex,
                ContentHandleValue = source.ContentHandleValue,
                ContentKind = source.ContentKind,
                PosX = source.PositionUnity.x,
                PosY = source.PositionUnity.y,
                PosZ = source.PositionUnity.z,
                RotX = source.RotationUnity.x,
                RotY = source.RotationUnity.y,
                RotZ = source.RotationUnity.z,
                RotW = source.RotationUnity.w,
                Scale = source.Scale,
            };

        static bool TryGetContentReference(RefEntry entry, out ContentReference contentReference)
        {
            contentReference = new ContentReference
            {
                Kind = (ContentReferenceKind)entry.ContentKind,
                HandleValue = entry.ContentHandleValue,
            };
            return contentReference.IsValid;
        }

        static void AppendHeights(DynamicBuffer<RuntimeCellSectionTerrainHeight> buffer, LandRecord land)
        {
            if (land?.Heights == null || land.Heights.Length != LandRecord.Size * LandRecord.Size)
                throw new InvalidDataException("terrain height count mismatch.");
            buffer.ResizeUninitialized(land.Heights.Length);
            for (int i = 0; i < land.Heights.Length; i++)
                buffer[i] = new RuntimeCellSectionTerrainHeight { Value = land.Heights[i] * WorldScale.MwUnitsToMeters };
        }

        static void AppendNormals(DynamicBuffer<RuntimeCellSectionTerrainNormal> buffer, LandRecord land)
        {
            if (land?.Normals == null || land.Normals.Length != 3 * LandRecord.Size * LandRecord.Size)
                throw new InvalidDataException("terrain normal count mismatch.");
            buffer.ResizeUninitialized(land.Normals.Length);
            for (int i = 0; i < land.Normals.Length; i++)
                buffer[i] = new RuntimeCellSectionTerrainNormal { Value = land.Normals[i] };
        }

        static void AppendWorldMap(DynamicBuffer<RuntimeCellSectionWorldMapSample> buffer, LandRecord land)
        {
            if (land?.WorldMap == null || land.WorldMap.Length != 81)
                throw new InvalidDataException("world map sample count mismatch.");
            buffer.ResizeUninitialized(land.WorldMap.Length);
            for (int i = 0; i < land.WorldMap.Length; i++)
                buffer[i] = new RuntimeCellSectionWorldMapSample { Value = land.WorldMap[i] };
        }

        static void AppendLayers(DynamicBuffer<RuntimeCellSectionTerrainLayer> buffer, ushort[] layerGrid)
        {
            if (layerGrid == null || layerGrid.Length != LandRecord.NumTextures)
                throw new InvalidDataException("terrain layer grid count mismatch.");
            buffer.ResizeUninitialized(layerGrid.Length);
            for (int i = 0; i < layerGrid.Length; i++)
                buffer[i] = new RuntimeCellSectionTerrainLayer { Value = layerGrid[i] };
        }

        static int ResolveTextureSlice(TextureBakery textures, int textureIndex)
        {
            int bucketKey = textures.GetBucketKey(textureIndex);
            return textures.GetBucketSliceOrFallback(textureIndex, bucketKey);
        }

        static int MaxSectionRenderMeshIndex(StagedCellData staged, ModelPrefabCatalogData modelPrefabCatalog)
        {
            int max = math.max(0, staged.TerrainMeshIndex);
            var refs = staged.BakedRefs ?? new List<CellBakery.BakedRef>();
            var records = modelPrefabCatalog?.Records ?? Array.Empty<ModelPrefabDef>();
            for (int i = 0; i < refs.Count; i++)
            {
                int modelPrefabIndex = refs[i].ModelPrefabIndex;
                if ((uint)modelPrefabIndex >= (uint)records.Length)
                    continue;
                var nodes = records[modelPrefabIndex]?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
                for (int n = 0; n < nodes.Length; n++)
                {
                    var node = nodes[n];
                    if (node != null && node.Kind == ModelPrefabNodeKind.RenderLeaf)
                        max = math.max(max, node.GlobalMeshIndex);
                }
            }

            var chunks = staged.CombinedRenderChunks ?? new List<CombinedCellRenderChunkDef>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk != null)
                    max = math.max(max, chunk.GlobalMeshIndex);
            }

            return max;
        }

        static uint BuildCellSectionFlags(StagedCellData staged)
        {
            uint flags = 0;
            if (HasTerrain(staged))
                flags |= CacheFormat.CellFlagHasTerrain;
            if (HasTerrainNormals(staged))
                flags |= CacheFormat.CellFlagHasNormals;
            if (HasTerrainLayerGrid(staged))
                flags |= CacheFormat.CellFlagHasVtex;
            if (HasStaticCollision(staged))
                flags |= CacheFormat.CellFlagHasStaticCollision;
            if (staged.Environment.HasAnyData)
                flags |= CacheFormat.CellFlagHasEnvironment;
            if (HasWorldMap(staged))
                flags |= CacheFormat.CellFlagHasWorldMap;
            return flags;
        }

        static bool HasTerrain(StagedCellData staged)
            => staged.Land != null && staged.Land.HasHeights;

        static bool HasTerrainNormals(StagedCellData staged)
            => HasTerrain(staged) && staged.Land.Normals != null;

        static bool HasTerrainLayerGrid(StagedCellData staged)
            => HasTerrain(staged) && staged.LayerGrid != null && staged.LayerGrid.Length == LandRecord.NumTextures;

        static bool HasWorldMap(StagedCellData staged)
            => HasTerrain(staged) && staged.Land.WorldMap != null && staged.Land.WorldMap.Length == 81;

        static bool HasStaticCollision(StagedCellData staged)
            => !staged.StaticCollision.IsEmpty;

        static string SectionCellId(StagedCellData staged)
            => staged.WorkItem.Cell.Name ?? string.Empty;

        static int SectionGridX(StagedCellData staged)
            => staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridX;

        static int SectionGridY(StagedCellData staged)
            => staged.WorkItem.IsInterior ? 0 : staged.WorkItem.Cell.GridY;

        static void StripBakeOnlyRenderMeshArray(EntityManager em, Entity entity)
        {
            if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
                em.RemoveComponent(entity, ComponentType.ReadWrite<RenderMeshArray>());
        }

        struct RendererBakeResources : IDisposable
        {
            public readonly RenderMeshDescription RenderDesc;
            public readonly RenderMeshArray RefRenderMeshArray;
            public readonly RenderMeshArray CombinedRenderMeshArray;
            public readonly RenderMeshArray TerrainRenderMeshArray;
            public readonly Dictionary<Object, RuntimeRenderObjectReference> ObjectReferences;
            readonly Mesh _mesh;
            readonly Material[] _refMaterials;
            readonly Material[] _combinedMaterials;
            readonly Material _terrainMaterial;

            public RendererBakeResources(int maxMeshIndex, int refMaterialCount)
            {
                RenderDesc = new RenderMeshDescription(
                    shadowCastingMode: ShadowCastingMode.On,
                    receiveShadows: true,
                    staticShadowCaster: true);
                ObjectReferences = new Dictionary<Object, RuntimeRenderObjectReference>(
                    RuntimeRenderObjectReferenceFile.UnityObjectReferenceComparer.Instance);
                int refMaterialCountSafe = math.max(1, refMaterialCount);

                _mesh = new Mesh { name = "VV:BakePlaceholderMesh" };
                _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                _mesh.triangles = new[] { 0, 1, 2 };
                _mesh.bounds = new Bounds(Vector3.zero, Vector3.one);

                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? throw new InvalidDataException("[VVardenfell][RendererBake] URP/Lit shader is required for bake-only renderer authoring.");
                _refMaterials = CreatePlaceholderMaterials(shader, refMaterialCountSafe, "Ref");
                _combinedMaterials = CreatePlaceholderMaterials(shader, 2, "Combined");
                _terrainMaterial = new Material(shader)
                {
                    name = "VV:BakePlaceholderTerrainMaterial",
                    enableInstancing = true,
                };

                var meshes = new Mesh[math.max(1, maxMeshIndex + 1)];
                for (int i = 0; i < meshes.Length; i++)
                    meshes[i] = _mesh;
                RefRenderMeshArray = new RenderMeshArray(_refMaterials, meshes);
                CombinedRenderMeshArray = new RenderMeshArray(_combinedMaterials, meshes);
                TerrainRenderMeshArray = new RenderMeshArray(new[] { _terrainMaterial }, meshes);
            }

            public void Dispose()
            {
                DestroyBakeObject(_terrainMaterial);
                for (int i = 0; i < _combinedMaterials.Length; i++)
                    DestroyBakeObject(_combinedMaterials[i]);
                for (int i = 0; i < _refMaterials.Length; i++)
                    DestroyBakeObject(_refMaterials[i]);
                DestroyBakeObject(_mesh);
            }

        }

        static Material[] CreatePlaceholderMaterials(Shader shader, int count, string label)
        {
            var materials = new Material[count];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = new Material(shader)
                {
                    name = $"VV:BakePlaceholder{label}Material{i}",
                    enableInstancing = true,
                };
            }
            return materials;
        }

        static void DestroyBakeObject(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        static bool TryValidateAuthoredRuntimeCellSection(EntityManager em, Entity entity, StagedCellData staged, uint flags, out string error)
        {
            error = null;
            try
            {
                var header = em.GetComponentData<RuntimeCellSectionHeader>(entity);
                if (header.PipelineVersion != CacheFormat.WorldBakePipelineVersion)
                {
                    error = $"pipeline version {header.PipelineVersion} does not match {CacheFormat.WorldBakePipelineVersion}";
                    return false;
                }
                if (header.IsInterior != (staged.WorkItem.IsInterior ? (byte)1 : (byte)0)
                    || header.GridX != SectionGridX(staged)
                    || header.GridY != SectionGridY(staged)
                    || header.Flags != flags)
                {
                    error = "section identity mismatch";
                    return false;
                }

                if (!ValidateSectionRootBuffers(em, entity, out error))
                    return false;
                if (!ValidateExplicitRefEntries(em, entity, out error))
                    return false;
                if (!ValidatePrebakedRenderComponents(em, out error))
                    return false;
                if (!ValidatePrebakedColliderComponents(em, out error))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool ValidateSectionRootBuffers(EntityManager em, Entity sectionEntity, out string error)
        {
            error = null;
            if (!em.HasComponent<RuntimeCellSectionResident>(sectionEntity))
            {
                error = "section root missing resident state";
                return false;
            }
            if (!em.HasComponent<RuntimeCellSectionResourcesBound>(sectionEntity))
            {
                error = "section root missing resource binding state";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionRenderEntity>(sectionEntity))
            {
                error = "section root missing render entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionTerrainEntity>(sectionEntity))
            {
                error = "section root missing terrain entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionCombinedRenderEntity>(sectionEntity))
            {
                error = "section root missing combined render entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionColliderEntity>(sectionEntity))
            {
                error = "section root missing collider entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionLogicalRefEntity>(sectionEntity))
            {
                error = "section root missing logical ref entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionExplicitRefEntry>(sectionEntity))
            {
                error = "section root missing explicit-ref entry buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionActorInitEntity>(sectionEntity))
            {
                error = "section root missing actor init entity buffer";
                return false;
            }
            if (!em.HasBuffer<RuntimeCellSectionTransformRootEntity>(sectionEntity))
            {
                error = "section root missing transform root entity buffer";
                return false;
            }
            return true;
        }

        static bool ValidateExplicitRefEntries(EntityManager em, Entity sectionEntity, out string error)
        {
            error = null;
            var entries = em.GetBuffer<RuntimeCellSectionExplicitRefEntry>(sectionEntity);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.ContentKey == 0 || entry.PlacedRefId == 0u)
                {
                    error = $"explicit-ref entry {i} has invalid key/id";
                    return false;
                }
                if (entry.Entity == Entity.Null || !em.Exists(entry.Entity))
                {
                    error = $"explicit-ref entry {i} references missing entity";
                    return false;
                }
                if (!em.HasComponent<LogicalRefContent>(entry.Entity) || !em.HasComponent<PlacedRefIdentity>(entry.Entity))
                {
                    error = $"explicit-ref entry {i} references entity missing logical identity";
                    return false;
                }
                var content = em.GetComponentData<LogicalRefContent>(entry.Entity).Value;
                var identity = em.GetComponentData<PlacedRefIdentity>(entry.Entity).Value;
                if (!content.IsValid || PackExplicitRefContentKey(content) != entry.ContentKey || identity != entry.PlacedRefId)
                {
                    error = $"explicit-ref entry {i} is stale";
                    return false;
                }
            }
            return true;
        }

        static bool ValidatePrebakedRenderComponents(EntityManager em, out string error)
        {
            error = null;
            using var prefabQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnPrefabRenderResource>());
            using var prefabEntities = prefabQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabEntities.Length; i++)
            {
                Entity entity = prefabEntities[i];
                if (!ValidateCommonRenderComponents(em, entity, "placed render leaf", out error))
                    return false;
                var resource = em.GetComponentData<RuntimeSpawnPrefabRenderResource>(entity);
                if (resource.MeshIndex < 0 || resource.MaterialIndex < 0)
                {
                    error = "placed render leaf has invalid logical render resource";
                    return false;
                }
                if (!em.HasComponent<ModelPrefabRenderLeaf>(entity))
                {
                    error = "placed render leaf missing ModelPrefabRenderLeaf";
                    return false;
                }
                if (!em.HasComponent<TextureSlice>(entity))
                {
                    error = "placed render leaf missing TextureSlice";
                    return false;
                }
            }

            using var terrainQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionTerrainRenderResource>());
            using var terrainEntities = terrainQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < terrainEntities.Length; i++)
            {
                Entity entity = terrainEntities[i];
                if (!ValidateCommonRenderComponents(em, entity, "terrain render entity", out error))
                    return false;
                var resource = em.GetComponentData<RuntimeCellSectionTerrainRenderResource>(entity);
                if (resource.MeshIndex < 0 || resource.SplatSlice < 0)
                {
                    error = "terrain render entity has invalid logical render resource";
                    return false;
                }
                if (!em.HasComponent<TerrainSplatSlice>(entity))
                {
                    error = "terrain render entity missing TerrainSplatSlice";
                    return false;
                }
            }

            using var combinedQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionCombinedRenderResource>());
            using var combinedEntities = combinedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < combinedEntities.Length; i++)
            {
                Entity entity = combinedEntities[i];
                if (!ValidateCommonRenderComponents(em, entity, "combined render chunk", out error))
                    return false;
                var resource = em.GetComponentData<RuntimeCellSectionCombinedRenderResource>(entity);
                if (resource.MeshIndex < 0 || resource.MaterialIndex < 0 || resource.TextureBucketKey == 0)
                {
                    error = "combined render chunk has invalid logical render resource";
                    return false;
                }
                if (!em.HasBuffer<CombinedCellRenderChunkMember>(entity))
                {
                    error = "combined render chunk missing baked member buffer";
                    return false;
                }
                var members = em.GetBuffer<CombinedCellRenderChunkMember>(entity);
                if (members.Length == 0)
                {
                    error = "combined render chunk has no baked members";
                    return false;
                }
                for (int m = 0; m < members.Length; m++)
                {
                    var member = members[m];
                    if (member.LogicalRefEntity == Entity.Null || !em.Exists(member.LogicalRefEntity))
                    {
                        error = $"combined render member 0x{member.PlacedRefId:X8} missing logical entity";
                        return false;
                    }
                    if (member.RenderEntity == Entity.Null || !em.Exists(member.RenderEntity))
                    {
                        error = $"combined render member 0x{member.PlacedRefId:X8} missing render entity";
                        return false;
                    }
                    if (!em.HasComponent<CombinedCellRenderSuppressed>(member.RenderEntity))
                    {
                        error = $"combined render member 0x{member.PlacedRefId:X8} missing suppression tag";
                        return false;
                    }
                    if (!em.HasBuffer<CombinedCellRenderLink>(member.LogicalRefEntity))
                    {
                        error = $"combined render member 0x{member.PlacedRefId:X8} missing logical chunk link";
                        return false;
                    }
                }
            }

            return true;
        }

        static bool ValidatePrebakedColliderComponents(EntityManager em, out string error)
        {
            error = null;

            using (var terrainQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionTerrainCollider>()))
            {
                using var entities = terrainQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!em.HasComponent<RuntimeColliderSource>(entities[i]))
                    {
                        error = "terrain collider missing embedded RuntimeColliderSource";
                        return false;
                    }
                    var source = em.GetComponentData<RuntimeColliderSource>(entities[i]);
                    if (!source.Value.IsCreated || source.Kind != RuntimeColliderKind.TerrainCell)
                    {
                        error = "terrain collider has invalid embedded RuntimeColliderSource";
                        return false;
                    }
                }
            }

            using (var staticQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionStaticCollider>()))
            {
                using var entities = staticQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!em.HasComponent<RuntimeColliderSource>(entities[i]))
                    {
                        error = "static cell collider missing embedded RuntimeColliderSource";
                        return false;
                    }
                    var source = em.GetComponentData<RuntimeColliderSource>(entities[i]);
                    if (!source.Value.IsCreated || source.Kind != RuntimeColliderKind.StaticCell)
                    {
                        error = "static cell collider has invalid embedded RuntimeColliderSource";
                        return false;
                    }
                }
            }

            using (var rootQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeCellSectionRenderRoot>()))
            {
                using var entities = rootQuery.ToEntityArray(Allocator.Temp);
                using var roots = rootQuery.ToComponentDataArray<RuntimeCellSectionRenderRoot>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (roots[i].CollisionIndex >= 0)
                    {
                        if (!em.HasComponent<RuntimeColliderSource>(entities[i]))
                        {
                            error = "placed-ref root missing reusable RuntimeColliderSource placeholder";
                            return false;
                        }
                        var source = em.GetComponentData<RuntimeColliderSource>(entities[i]);
                        if (source.Value.IsCreated || source.Kind != RuntimeColliderKind.PlacedRef)
                        {
                            error = "placed-ref root has invalid reusable RuntimeColliderSource placeholder";
                            return false;
                        }
                    }
                }
            }

            using (var pickQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnPrefabPickCollider>()))
            {
                using var entities = pickQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!em.HasComponent<InteractionPickSurfaceTag>(entities[i]))
                    {
                        error = "pick collider missing InteractionPickSurfaceTag";
                        return false;
                    }
                    if (!em.HasComponent<RuntimeColliderSource>(entities[i]))
                    {
                        error = "pick collider missing reusable RuntimeColliderSource placeholder";
                        return false;
                    }
                    var source = em.GetComponentData<RuntimeColliderSource>(entities[i]);
                    if (source.Value.IsCreated || source.Kind != RuntimeColliderKind.InteractionPick)
                    {
                        error = "pick collider has invalid reusable RuntimeColliderSource placeholder";
                        return false;
                    }
                }
            }

            return true;
        }

        static bool ValidateCommonRenderComponents(EntityManager em, Entity entity, string label, out string error)
        {
            error = null;
            if (!em.HasComponent<MaterialMeshInfo>(entity))
            {
                error = $"{label} missing MaterialMeshInfo";
                return false;
            }
            if (!em.HasComponent<RenderBounds>(entity))
            {
                error = $"{label} missing RenderBounds";
                return false;
            }
            if (em.HasComponent(entity, ComponentType.ReadWrite<RenderMeshArray>()))
            {
                error = $"{label} serialized a RenderMeshArray shared component";
                return false;
            }

            var info = em.GetComponentData<MaterialMeshInfo>(entity);
            if (info.Mesh >= 0 || info.Material >= 0)
            {
                error = $"{label} has runtime MaterialMeshInfo ids";
                return false;
            }
            if (MaterialMeshInfo.StaticIndexToArrayIndex(info.Mesh) < 0
                || MaterialMeshInfo.StaticIndexToArrayIndex(info.Material) < 0)
            {
                error = $"{label} has invalid logical MaterialMeshInfo placeholders";
                return false;
            }
            return true;
        }

        static Object[] CreateValidationObjects(RuntimeRenderObjectReference[] references)
        {
            var objects = new Object[references?.Length ?? 0];
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidDataException("[VVardenfell][RendererBake] URP/Lit shader is required for validation.");
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

        static void DestroyValidationObjects(Object[] objects)
        {
            if (objects == null)
                return;
            for (int i = 0; i < objects.Length; i++)
                DestroyBakeObject(objects[i]);
        }
    }
}
