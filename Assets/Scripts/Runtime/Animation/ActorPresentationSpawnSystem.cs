using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationBlobCatalogSystem))]
    public partial class ActorPresentationSpawnSystem : SystemBase
    {
        static readonly System.Collections.Generic.HashSet<int> s_RigidEquipmentPrefabBuildSet = new();
        static int s_RobeSkirtDiagnosticLogCount;
        static readonly float3 k_MinAnimatedRenderCullExtents = new(1.25f, 2.25f, 1.25f);
        static readonly float3 k_AnimatedRenderCullPadding = new(0.75f, 1.0f, 0.75f);

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;
            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                return;

            CacheLoader cache = WorldResources.Cache;
            var blobRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!blobRef.IsCreated)
                return;

            ref var catalog = ref blobRef.Value;
            PrebuildRigidEquipmentPrefabs(contentDb);
            var actorRenderResources = WorldResources.ActorEntitiesGraphicsRenderer ?? new ActorEntitiesGraphicsRenderResources();
            WorldResources.ActorEntitiesGraphicsRenderer = actorRenderResources;
            actorRenderResources.Ensure(EntityManager, ref catalog);
            var gpuAnimationResources = WorldResources.ActorGpuAnimation ?? new ActorGpuAnimationResources();
            WorldResources.ActorGpuAnimation = gpuAnimationResources;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (source, entity) in
                     SystemAPI.Query<RefRO<ActorSpawnSource>>()
                         .WithNone<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (!source.ValueRO.Definition.IsValid)
                    continue;

                ref readonly ActorDef actor = ref contentDb.Get(source.ValueRO.Definition);
                bool isNpc = actor.Kind == ActorDefKind.Npc;
                bool firstPerson = source.ValueRO.FirstPerson != 0;
                ActorVisualRecipeDef recipe = null;
                bool hasRecipe = cache != null
                                 && cache.TryGetActorVisualRecipe(actor.ContentId, firstPerson, out recipe);
                ActorRigFamilyDef rigFamily = hasRecipe && (uint)recipe.RigFamilyIndex < (uint)(cache.ActorAnimationCatalog?.RigFamilies?.Length ?? 0)
                    ? cache.ActorAnimationCatalog.RigFamilies[recipe.RigFamilyIndex]
                    : null;
                int skeletonIndex = rigFamily?.SkeletonIndex ?? -1;
                int boneCount = ResolveBoneCount(ref catalog, skeletonIndex);

                ecb.AddComponent(entity, new ActorPresentation
                {
                    RigFamilyIndex = hasRecipe ? recipe.RigFamilyIndex : -1,
                });
                ecb.AddComponent(entity, new ActorSkeleton
                {
                    SkeletonIndex = skeletonIndex,
                    BoneCount = boneCount,
                    AccumulationBoneIndex = ResolveAccumulationBoneIndex(ref catalog, skeletonIndex),
                });
                ecb.AddComponent(entity, new ActorAnimationState
                {
                    Playback = new ActorAnimationPlaybackState
                    {
                        ClipIndex = -1,
                        Speed = 1f,
                        LoopCount = uint.MaxValue,
                    },
                });
                ecb.AddComponent(entity, new ActorJumpAnimationState());
                ecb.AddBuffer<ActorGpuAnimationRequest>(entity);
                ecb.AddBuffer<ActorAnimationOverlayState>(entity);
                if (!EntityManager.HasComponent<ActorRenderVisible>(entity))
                {
                    ecb.AddComponent<ActorRenderVisible>(entity);
                    ecb.SetComponentEnabled<ActorRenderVisible>(entity, true);
                }
                if (!EntityManager.HasComponent<ActorShadowCasterVisible>(entity))
                {
                    ecb.AddComponent<ActorShadowCasterVisible>(entity);
                    ecb.SetComponentEnabled<ActorShadowCasterVisible>(entity, true);
                }
                var boneBuffer = ecb.AddBuffer<ActorBone>(entity);
                PopulateBoneBuffer(boneBuffer, ref catalog, skeletonIndex);
                var sampledPoseBuffer = ecb.AddBuffer<ActorSampledBonePose>(entity);
                PopulateSampledPoseBuffer(sampledPoseBuffer, boneCount);
                var skinMeshBuffer = ecb.AddBuffer<ActorSkinMesh>(entity);
                bool hasEquipment = EntityManager.HasBuffer<ActorEquipmentSlot>(entity);
                DynamicBuffer<ActorEquipmentSlot> equipmentBuffer = hasEquipment
                    ? EntityManager.GetBuffer<ActorEquipmentSlot>(entity)
                    : default;
                bool isBeast = isNpc && ActorEquipmentRuntimeUtility.IsBeastRace(contentDb, actor.RaceId);
                uint hiddenPartMask = EntityManager.HasComponent<ActorHiddenVisualPartMask>(entity)
                    ? EntityManager.GetComponentData<ActorHiddenVisualPartMask>(entity).Mask
                    : 0u;
                PopulateSkinMeshBuffer(
                    skinMeshBuffer,
                    ref catalog,
                    cache,
                    contentDb,
                    actor,
                    isNpc,
                    isBeast,
                    firstPerson,
                    recipe,
                    hiddenPartMask,
                    hasEquipment,
                    equipmentBuffer);

                int boneMatrixCount = CountOutputBoneMatrices(skinMeshBuffer, ref catalog);
                int deformedVertexCount = CountOutputVertices(skinMeshBuffer, ref catalog);
                gpuAnimationResources.AllocateActorRanges(
                    boneMatrixCount,
                    deformedVertexCount,
                    out int boneMatrixOffset,
                    out int deformedVertexOffset);
                ecb.AddComponent(entity, new ActorGpuAnimationState
                {
                    SkeletonIndex = skeletonIndex,
                    BoneMatrixOffset = boneMatrixOffset,
                    BoneMatrixCount = boneMatrixCount,
                    DeformedVertexOffset = deformedVertexOffset,
                    DeformedVertexCount = deformedVertexCount,
                });
                ActorLocalBounds actorBounds = BuildLocalBounds(skinMeshBuffer, ref catalog);
                QueueActorRenderChildren(
                    ref ecb,
                    EntityManager,
                    actorRenderResources,
                    entity,
                    skinMeshBuffer,
                    ref catalog,
                    deformedVertexOffset,
                    actorBounds);

                using var rigidEquipment = new NativeList<ActorRigidEquipment>(Allocator.Temp);
                PopulateRigidEquipment(
                    ref ecb,
                    entity,
                    rigidEquipment,
                    ref catalog,
                    skeletonIndex,
                    contentDb,
                    hasEquipment,
                    equipmentBuffer);
                if (rigidEquipment.Length > 0)
                {
                    var rigidEquipmentBuffer = ecb.AddBuffer<ActorRigidEquipment>(entity);
                    for (int i = 0; i < rigidEquipment.Length; i++)
                        rigidEquipmentBuffer.Add(rigidEquipment[i]);

                    using var attachmentBones = new NativeList<ActorAttachmentBone>(Allocator.Temp);
                    PopulateAttachmentBones(
                        attachmentBones,
                        ref catalog,
                        skeletonIndex,
                        rigidEquipment);
                    if (attachmentBones.Length > 0)
                    {
                        var attachmentBoneBuffer = ecb.AddBuffer<ActorAttachmentBone>(entity);
                        for (int i = 0; i < attachmentBones.Length; i++)
                            attachmentBoneBuffer.Add(attachmentBones[i]);
                    }
                }
                ecb.AddComponent(entity, actorBounds);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
            => ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex);

        static int CountOutputBoneMatrices(DynamicBuffer<ActorSkinMesh> skinMeshes, ref ActorAnimationCatalogBlob catalog)
        {
            int count = 0;
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                count += math.max(1, catalog.SkinMeshes[skinMeshIndex].SkinBoneCount);
            }

            return count;
        }

        static int CountOutputVertices(DynamicBuffer<ActorSkinMesh> skinMeshes, ref ActorAnimationCatalogBlob catalog)
        {
            int count = 0;
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                count += math.max(0, catalog.SkinMeshes[skinMeshIndex].VertexCount);
            }

            return count;
        }

        static void QueueActorRenderChildren(
            ref EntityCommandBuffer ecb,
            EntityManager entityManager,
            ActorEntitiesGraphicsRenderResources renderResources,
            Entity actorEntity,
            DynamicBuffer<ActorSkinMesh> skinMeshes,
            ref ActorAnimationCatalogBlob catalog,
            int deformedVertexOffset,
            ActorLocalBounds actorBounds)
        {
            DynamicBuffer<LinkedEntityGroup> linked = default;
            bool createdLinkedBuffer = false;
            if (!entityManager.HasBuffer<LinkedEntityGroup>(actorEntity))
            {
                linked = ecb.AddBuffer<LinkedEntityGroup>(actorEntity);
                linked.Add(new LinkedEntityGroup { Value = actorEntity });
                createdLinkedBuffer = true;
            }

            int vertexCursor = deformedVertexOffset;
            LocalToWorld initialLocalToWorld = entityManager.HasComponent<LocalToWorld>(actorEntity)
                ? entityManager.GetComponentData<LocalToWorld>(actorEntity)
                : new LocalToWorld { Value = float4x4.identity };
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                var skinMesh = catalog.SkinMeshes[skinMeshIndex];
                if (!renderResources.TryGetSkinMeshInfo(skinMeshIndex, out var info)
                    || !renderResources.TryGetPrototype(info.BucketIndex, out Entity prototype))
                {
                    throw new InvalidOperationException($"Actor skin mesh {skinMeshIndex} has no Entities Graphics render prototype.");
                }

                Entity child = ecb.Instantiate(prototype);
                ecb.SetName(child, $"VVardenfell.ActorRenderMesh[{skinMeshIndex}]");
                ecb.SetComponent(child, LocalTransform.Identity);
                ecb.SetComponent(child, initialLocalToWorld);
                ecb.AddComponent(child, new Parent { Value = actorEntity });
                ecb.SetComponent(child, MaterialMeshInfo.FromRenderMeshArrayIndices(info.MaterialIndex, info.MeshIndex));
                ecb.SetComponent(child, new TextureSlice { Value = info.TextureSlice });
                ecb.SetComponent(child, new ActorDeformedMeshIndex { Value = vertexCursor });
                ecb.SetComponent(child, new RenderBounds
                {
                    Value = new AABB
                    {
                        Center = actorBounds.Center,
                        Extents = BuildAnimatedRenderCullExtents(actorBounds),
                    },
                });
                ecb.SetComponent(child, new ActorRenderMeshInstance
                {
                    Actor = actorEntity,
                    SkinMeshIndex = skinMeshIndex,
                });

                if (createdLinkedBuffer)
                    linked.Add(new LinkedEntityGroup { Value = child });
                else
                    ecb.AppendToBuffer(actorEntity, new LinkedEntityGroup { Value = child });

                vertexCursor += math.max(0, skinMesh.VertexCount);
            }
        }

        static float3 BuildAnimatedRenderCullExtents(ActorLocalBounds actorBounds)
            => math.max(actorBounds.Extents + k_AnimatedRenderCullPadding, k_MinAnimatedRenderCullExtents);

        static int ResolveAccumulationBoneIndex(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
        {
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                return -1;
            return catalog.Skeletons[skeletonIndex].AccumulationBoneIndex;
        }

        static ActorLocalBounds BuildLocalBounds(DynamicBuffer<ActorSkinMesh> skinMeshes, ref ActorAnimationCatalogBlob catalog)
        {
            float3 min = default;
            float3 max = default;
            bool hasBounds = false;

            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                var skinMesh = catalog.SkinMeshes[skinMeshIndex];
                float3 meshExtents = math.max(skinMesh.BoundsExtents, new float3(0.05f));
                float3 meshMin = skinMesh.BoundsCenter - meshExtents;
                float3 meshMax = skinMesh.BoundsCenter + meshExtents;

                if (!hasBounds)
                {
                    min = meshMin;
                    max = meshMax;
                    hasBounds = true;
                    continue;
                }

                min = math.min(min, meshMin);
                max = math.max(max, meshMax);
            }

            if (!hasBounds)
            {
                return new ActorLocalBounds
                {
                    Center = float3.zero,
                    Extents = new float3(0.5f),
                };
            }

            return new ActorLocalBounds
            {
                Center = (min + max) * 0.5f,
                Extents = math.max((max - min) * 0.5f, new float3(0.05f)),
            };
        }

        static void PopulateAttachmentBones(
            NativeList<ActorAttachmentBone> attachmentBones,
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            NativeList<ActorRigidEquipment> rigidEquipment)
        {
            int boneCount = ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex);
            if (boneCount == 0 || rigidEquipment.Length == 0)
                return;

            var included = new NativeArray<byte>(boneCount, Allocator.Temp);
            try
            {
                var skeleton = new ActorSkeleton { SkeletonIndex = skeletonIndex, BoneCount = boneCount };
                for (int i = 0; i < rigidEquipment.Length; i++)
                {
                    int boneIndex = rigidEquipment[i].AttachBoneIndex;
                    while ((uint)boneIndex < (uint)boneCount && included[boneIndex] == 0)
                    {
                        included[boneIndex] = 1;
                        boneIndex = ActorAnimationCatalogRuntimeUtility.ResolveParentIndex(ref catalog, skeleton, boneIndex);
                    }
                }

                for (int boneIndex = 0; boneIndex < boneCount; boneIndex++)
                {
                    if (included[boneIndex] == 0)
                        continue;

                    attachmentBones.Add(new ActorAttachmentBone
                    {
                        BoneIndex = boneIndex,
                    });
                }
            }
            finally
            {
                if (included.IsCreated)
                    included.Dispose();
            }
        }

        static void PopulateBoneBuffer(DynamicBuffer<ActorBone> buffer, ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
        {
            if (!ActorAnimationCatalogRuntimeUtility.TryGetSkeletonBlob(ref catalog, skeletonIndex, out var skeleton))
                return;

            int end = skeleton.FirstBoneIndex + skeleton.BoneCount;
            for (int sourceIndex = skeleton.FirstBoneIndex; sourceIndex < end; sourceIndex++)
            {
                var source = catalog.Bones[sourceIndex];
                buffer.Add(new ActorBone
                {
                    LocalPosition = ActorAnimationCatalogRuntimeUtility.RuntimeBindPosition(source),
                    LocalRotation = ActorAnimationCatalogRuntimeUtility.RuntimeBindRotation(source),
                    LocalScale = ActorAnimationCatalogRuntimeUtility.RuntimeBindScale(source),
                    LocalPoseAnimated = 0,
                    LocalToRoot = ActorAnimationCatalogRuntimeUtility.RuntimeBindLocalToRootMatrix(source),
                });
            }
        }

        static void PopulateSampledPoseBuffer(DynamicBuffer<ActorSampledBonePose> buffer, int boneCount)
        {
            for (int i = 0; i < boneCount; i++)
            {
                buffer.Add(new ActorSampledBonePose
                {
                    Rotation = quaternion.identity,
                    Scale = 1f,
                });
            }
        }

        static int PopulateSkinMeshBuffer(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            bool isNpc,
            bool isBeast,
            bool firstPerson,
            ActorVisualRecipeDef recipe,
            uint hiddenPartMask,
            bool hasEquipment,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (cache == null || recipe == null)
                return 0;

            uint coveredParts = 0u;
            int added = 0;
            if (isNpc && hasEquipment)
            {
                added += AddBakedEquipmentVisuals(
                    buffer,
                    ref catalog,
                    cache,
                    contentDb,
                    recipe,
                    actor,
                    isBeast,
                    firstPerson,
                    equipment,
                    hiddenPartMask,
                    ref coveredParts);
            }

            added += AddBakedActorVisualRecipe(buffer, ref catalog, cache, recipe, coveredParts, hiddenPartMask);
            return added;
        }

        static int AddBakedEquipmentVisuals(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            ActorVisualRecipeDef actorRecipe,
            in ActorDef actor,
            bool isBeast,
            bool firstPerson,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            uint hiddenPartMask,
            ref uint coveredParts)
        {
            int added = 0;
            ReadOnlySpan<ItemEquipmentSlot> slotOrder = ActorEquipmentRuntimeUtility.NpcEquipmentVisualSlotOrder;
            for (int slotIndex = 0; slotIndex < slotOrder.Length; slotIndex++)
            {
                if (!ActorEquipmentRuntimeUtility.TryGetEquipmentInSlot(equipment, slotOrder[slotIndex], out var slot))
                    continue;
                if (slot.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!itemHandle.IsValid || (uint)itemHandle.Index >= (uint)(contentDb?.Data?.Items?.Length ?? 0))
                    continue;

                ref readonly var item = ref contentDb.Get(itemHandle);
                bool hasVisual = cache.TryGetEquipmentVisual(
                        item.ContentId,
                        actorRecipe.RigFamilyIndex,
                        firstPerson,
                        actorRecipe.BodyVariant,
                        out var visual);

                if (!hasVisual || visual == null || visual.IsValid == 0)
                {
                    continue;
                }

                uint visualEntryParts = 0u;
                int entryEnd = math.min(
                    cache.ActorAnimationCatalog.EquipmentVisualEntries?.Length ?? 0,
                    visual.FirstEntryIndex + visual.EntryCount);
                for (int entryIndex = visual.FirstEntryIndex; entryIndex >= 0 && entryIndex < entryEnd; entryIndex++)
                {
                    var entry = cache.ActorAnimationCatalog.EquipmentVisualEntries[entryIndex];
                    var part = (ActorVisualPartReference)(byte)entry.PartReference;
                    uint mask = ActorVisualContentRules.PartMask(part);
                    if ((hiddenPartMask & mask) != 0)
                    {
                        coveredParts |= mask;
                        continue;
                    }
                    if ((coveredParts & mask) != 0)
                        continue;

                    visualEntryParts |= mask;
                    if (entry.SkinMeshIndex < 0)
                        continue;

                    if (AddBakedSkinEntry(buffer, ref catalog, entry.SkinMeshIndex, entry.AttachBoneIndex, entry.RigidMirrorX))
                        added++;
                }

                coveredParts |= visual.CoverageMask | visualEntryParts;
            }

            return added;
        }

        static int AddBakedActorVisualRecipe(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            ActorVisualRecipeDef recipe,
            uint coveredParts,
            uint hiddenPartMask)
        {
            int added = 0;
            int entryEnd = math.min(
                cache.ActorAnimationCatalog.ActorVisualRecipeEntries?.Length ?? 0,
                recipe.FirstEntryIndex + recipe.EntryCount);
            for (int entryIndex = recipe.FirstEntryIndex; entryIndex >= 0 && entryIndex < entryEnd; entryIndex++)
            {
                var entry = cache.ActorAnimationCatalog.ActorVisualRecipeEntries[entryIndex];
                uint mask = ActorVisualContentRules.PartMask(entry.PartReference);
                if ((hiddenPartMask & mask) != 0)
                    continue;
                if ((coveredParts & mask) != 0)
                    continue;

                if (AddBakedSkinEntry(buffer, ref catalog, entry.SkinMeshIndex, entry.AttachBoneIndex, entry.RigidMirrorX))
                    added++;
            }

            return added;
        }

        static bool AddBakedSkinEntry(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            int skinMeshIndex,
            int attachBoneIndex,
            byte rigidMirrorX)
        {
            if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                return false;

            var skinMesh = catalog.SkinMeshes[skinMeshIndex];
            if (skinMesh.VertexCount <= 0 || skinMesh.IndexCount <= 0)
                return false;

            buffer.Add(new ActorSkinMesh
            {
                SkinMeshIndex = skinMeshIndex,
                AttachBoneIndex = skinMesh.IsRigid != 0 ? attachBoneIndex : -1,
                RigidMirrorX = skinMesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
            return true;
        }

        void PrebuildRigidEquipmentPrefabs(RuntimeContentDatabase contentDb)
        {
            s_RigidEquipmentPrefabBuildSet.Clear();
            if (contentDb == null
                || WorldResources.Cache == null
                || WorldResources.SpawnableItemPrefabs == null
                || WorldResources.ModelPrefabs == null)
            {
                return;
            }

            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<ActorSpawnSource>>()
                         .WithNone<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (!EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                    continue;

                var equipment = EntityManager.GetBuffer<ActorEquipmentSlot>(entity);
                for (int i = 0; i < equipment.Length; i++)
                {
                    var slot = equipment[i];
                    if (slot.Content.Kind != ContentReferenceKind.Item)
                        continue;

                    var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                    if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                        continue;

                    if (itemEquipment.Kind != ItemEquipmentKind.Weapon)
                        continue;
                    if (!ShouldSpawnRigidEquipmentAtPresentation(itemEquipment))
                        continue;

                    if ((uint)itemHandle.Index >= (uint)WorldResources.SpawnableItemPrefabs.Length)
                        continue;

                    var descriptor = WorldResources.SpawnableItemPrefabs[itemHandle.Index];
                    if (descriptor.IsSupported)
                        s_RigidEquipmentPrefabBuildSet.Add(descriptor.ModelPrefabIndex);
                }
            }

            foreach (int modelPrefabIndex in s_RigidEquipmentPrefabBuildSet)
                WorldBootstrap.EnsureModelPrefabBuilt(EntityManager, WorldResources.Cache, modelPrefabIndex);
        }

        void PopulateRigidEquipment(
            ref EntityCommandBuffer ecb,
            Entity actorEntity,
            NativeList<ActorRigidEquipment> rigidEquipment,
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            RuntimeContentDatabase contentDb,
            bool hasEquipment,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (!hasEquipment || contentDb == null || WorldResources.ModelPrefabs == null)
                return;

            LocalTransform initialTransform = EntityManager.HasComponent<LocalTransform>(actorEntity)
                ? EntityManager.GetComponentData<LocalTransform>(actorEntity)
                : LocalTransform.Identity;

            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;

                if (itemEquipment.Kind != ItemEquipmentKind.Weapon)
                    continue;
                if (!ShouldSpawnRigidEquipmentAtPresentation(itemEquipment))
                    continue;

                if ((uint)itemHandle.Index >= (uint)(WorldResources.SpawnableItemPrefabs?.Length ?? 0))
                    continue;

                var descriptor = WorldResources.SpawnableItemPrefabs[itemHandle.Index];
                if (!descriptor.IsSupported)
                    continue;

                Entity prefab = WorldResources.ModelPrefabs[descriptor.ModelPrefabIndex];
                if (prefab == Entity.Null || !EntityManager.Exists(prefab))
                    continue;

                int attachBoneIndex = ActorPresentationEquipmentUtility.ResolveRigidEquipmentAttachBone(
                    ref catalog,
                    skeletonIndex,
                    itemEquipment);
                if (attachBoneIndex < 0)
                    continue;

                rigidEquipment.Add(new ActorRigidEquipment
                {
                    Slot = itemEquipment.Slot,
                    Content = slot.Content,
                    ModelPrefabIndex = descriptor.ModelPrefabIndex,
                    AttachBoneIndex = attachBoneIndex,
                });

                Entity equipmentRoot = ecb.Instantiate(prefab);
                ecb.SetComponent(equipmentRoot, initialTransform);
                ecb.AddComponent(equipmentRoot, new ActorRigidEquipmentAttachment
                {
                    Actor = actorEntity,
                    Content = slot.Content,
                    Slot = itemEquipment.Slot,
                    BoneIndex = attachBoneIndex,
                    LocalPosition = float3.zero,
                    LocalRotation = quaternion.identity,
                    LocalScale = 1f,
                });

                if (EntityManager.HasComponent<InteriorCellMember>(actorEntity))
                    ecb.AddComponent<InteriorCellMember>(equipmentRoot);
                if (EntityManager.HasComponent<CellLink>(actorEntity))
                    ecb.AddComponent(equipmentRoot, EntityManager.GetComponentData<CellLink>(actorEntity));
            }
        }

        static bool ShouldSpawnRigidEquipmentAtPresentation(in ItemEquipmentDef equipment)
        {
            return equipment.Kind == ItemEquipmentKind.Weapon;
        }

    }
}
