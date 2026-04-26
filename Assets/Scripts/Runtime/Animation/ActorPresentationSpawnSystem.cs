using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationBlobCatalogSystem))]
    public partial class ActorPresentationSpawnSystem : SystemBase
    {
        static readonly bool s_SpawnWeaponsDrawnOnPresentation = false;
        static readonly ItemEquipmentSlot[] s_NpcEquipmentSlotOrder =
        {
            ItemEquipmentSlot.Robe,
            ItemEquipmentSlot.Skirt,
            ItemEquipmentSlot.Helmet,
            ItemEquipmentSlot.Cuirass,
            ItemEquipmentSlot.Greaves,
            ItemEquipmentSlot.LeftPauldron,
            ItemEquipmentSlot.RightPauldron,
            ItemEquipmentSlot.Boots,
            ItemEquipmentSlot.Shoes,
            ItemEquipmentSlot.LeftHand,
            ItemEquipmentSlot.RightHand,
            ItemEquipmentSlot.Shirt,
            ItemEquipmentSlot.Pants,
            ItemEquipmentSlot.Shield,
        };
        static readonly System.Collections.Generic.HashSet<int> s_RigidEquipmentPrefabBuildSet = new();

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
                int firstClipIndex = rigFamily?.FirstClipIndex ?? -1;
                int clipCount = rigFamily?.ClipCount ?? 0;
                int boneCount = ResolveBoneCount(ref catalog, skeletonIndex);

                ecb.AddComponent(entity, new ActorPresentation
                {
                    Actor = source.ValueRO.Definition,
                    IsNpc = (byte)(isNpc ? 1 : 0),
                    IsCreature = (byte)(isNpc ? 0 : 1),
                    IsFemale = (byte)((actor.Flags & 0x1u) != 0 ? 1 : 0),
                    IsFirstPerson = (byte)(firstPerson ? 1 : 0),
                    RigFamilyIndex = hasRecipe ? recipe.RigFamilyIndex : -1,
                    SkeletonIndex = skeletonIndex,
                    FirstSkinMeshIndex = hasRecipe ? recipe.FirstEntryIndex : -1,
                    SkinMeshCount = hasRecipe ? recipe.EntryCount : 0,
                    FirstClipIndex = firstClipIndex,
                    ClipCount = clipCount,
                });
                ecb.AddComponent(entity, new ActorSkeleton
                {
                    SkeletonIndex = skeletonIndex,
                    BoneCount = boneCount,
                    AccumulationBoneIndex = ResolveAccumulationBoneIndex(ref catalog, skeletonIndex),
                    AccumulationSubtreeEndIndex = ResolveAccumulationSubtreeEndIndex(ref catalog, skeletonIndex),
                    FirstClipIndex = firstClipIndex,
                    ClipCount = clipCount,
                });
                ecb.AddComponent<CPUAnimation>(entity);
                ecb.AddComponent<GPUAnimation>(entity);
                ecb.AddComponent(entity, new ActorAnimationController
                {
                    RequestedGroup = new FixedString64Bytes("idle"),
                    Speed = 1f,
                    ActiveMask = ActorAnimationBlendMask.All,
                });
                ecb.AddComponent(entity, new ActorAnimationState());
                ecb.AddComponent(entity, new ActorRootMotion());
                ecb.AddComponent(entity, new ActorGpuAnimationState
                {
                    SkeletonIndex = skeletonIndex,
                });
                ecb.AddComponent<ActorAttachmentBoneAnimation>(entity);
                ecb.AddComponent(entity, new ActorAnimationEventCursor());
                if (!EntityManager.HasComponent<ActorRenderVisible>(entity))
                {
                    ecb.AddComponent<ActorRenderVisible>(entity);
                    ecb.SetComponentEnabled<ActorRenderVisible>(entity, true);
                }
                var boneBuffer = ecb.AddBuffer<ActorBone>(entity);
                PopulateBoneBuffer(boneBuffer, ref catalog, skeletonIndex);
                var sampledPoseBuffer = ecb.AddBuffer<ActorSampledBonePose>(entity);
                PopulateSampledPoseBuffer(sampledPoseBuffer, boneCount);
                var skinMeshBuffer = ecb.AddBuffer<ActorSkinMesh>(entity);
                var rigidEquipmentBuffer = ecb.AddBuffer<ActorRigidEquipment>(entity);
                bool hasEquipment = EntityManager.HasBuffer<ActorEquipmentSlot>(entity);
                DynamicBuffer<ActorEquipmentSlot> equipmentBuffer = hasEquipment
                    ? EntityManager.GetBuffer<ActorEquipmentSlot>(entity)
                    : default;
                PopulateSkinMeshBuffer(
                    skinMeshBuffer,
                    rigidEquipmentBuffer,
                    boneBuffer,
                    ref catalog,
                    cache,
                    contentDb,
                    actor,
                    isNpc,
                    firstPerson,
                    recipe,
                    hasEquipment,
                    equipmentBuffer);
                PopulateRigidEquipment(
                    ref ecb,
                    entity,
                    rigidEquipmentBuffer,
                    boneBuffer,
                    contentDb,
                    hasEquipment,
                    equipmentBuffer);
                var attachmentBoneBuffer = ecb.AddBuffer<ActorAttachmentBone>(entity);
                PopulateAttachmentBoneBuffer(attachmentBoneBuffer, bones: boneBuffer, rigidEquipment: rigidEquipmentBuffer);
                ecb.AddComponent(entity, BuildLocalBounds(skinMeshBuffer, ref catalog));
                ecb.SetComponentEnabled<GPUAnimation>(entity, false);
                ecb.SetComponentEnabled<CPUAnimation>(entity, false);
                ecb.SetComponentEnabled<ActorAttachmentBoneAnimation>(entity, false);

                var layerBuffer = ecb.AddBuffer<ActorAnimationLayer>(entity);
                if (hasRecipe && firstClipIndex >= 0 && clipCount > 0)
                {
                    ulong clipHash = ResolveClipHash(ref catalog, firstClipIndex);
                    layerBuffer.Add(new ActorAnimationLayer
                    {
                        Group = new FixedString64Bytes("idle"),
                        ClipIndex = firstClipIndex,
                        ClipHash = clipHash,
                        Time = 0f,
                        Weight = 1f,
                        Priority = 0,
                        Mask = ActorAnimationBlendMask.All,
                    });
                }
                ecb.AddBuffer<ActorAnimationEvent>(entity);
                ecb.AddBuffer<ActorGpuAnimationRequest>(entity);
                ecb.AddComponent<ActorAnimationPoseDirty>(entity);
                ecb.SetComponentEnabled<ActorAnimationPoseDirty>(entity, boneCount > 0);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
        {
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                return 0;
            return catalog.Skeletons[skeletonIndex].BoneCount;
        }

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

        static void PopulateAttachmentBoneBuffer(
            DynamicBuffer<ActorAttachmentBone> attachmentBones,
            DynamicBuffer<ActorBone> bones,
            DynamicBuffer<ActorRigidEquipment> rigidEquipment)
        {
            if (bones.Length == 0 || rigidEquipment.Length == 0)
                return;

            var included = new bool[bones.Length];
            for (int i = 0; i < rigidEquipment.Length; i++)
            {
                int boneIndex = rigidEquipment[i].AttachBoneIndex;
                while ((uint)boneIndex < (uint)bones.Length && !included[boneIndex])
                {
                    included[boneIndex] = true;
                    boneIndex = bones[boneIndex].ParentIndex;
                }
            }

            for (int boneIndex = 0; boneIndex < included.Length; boneIndex++)
            {
                if (!included[boneIndex])
                    continue;

                attachmentBones.Add(new ActorAttachmentBone
                {
                    BoneIndex = boneIndex,
                });
            }
        }

        static int ResolveAccumulationSubtreeEndIndex(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
        {
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                return -1;
            return catalog.Skeletons[skeletonIndex].AccumulationSubtreeEndIndex;
        }

        static ulong ResolveClipHash(ref ActorAnimationCatalogBlob catalog, int clipIndex)
        {
            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return 0UL;
            return catalog.Clips[clipIndex].AnimationHash;
        }

        static void PopulateBoneBuffer(DynamicBuffer<ActorBone> buffer, ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
        {
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                return;

            var skeleton = catalog.Skeletons[skeletonIndex];
            int end = skeleton.FirstBoneIndex + skeleton.BoneCount;
            for (int sourceIndex = skeleton.FirstBoneIndex; sourceIndex < end; sourceIndex++)
            {
                var source = catalog.Bones[sourceIndex];
                int localIndex = sourceIndex - skeleton.FirstBoneIndex;
                var rotation = source.BindRotation;
                if (math.lengthsq(rotation.value) <= 0f)
                    rotation = quaternion.identity;

                float scale = source.BindScale <= 0f ? 1f : source.BindScale;
                var position = ActorAnimationSpaceConversion.SourceTranslationToUnity(source.BindPosition);
                rotation = ActorAnimationSpaceConversion.SourceQuaternionToUnity(rotation);
                float4x4 localToParent = ActorAnimationSpaceConversion.SourceAffineToUnity(source.BindLocalMatrix);
                float4x4 localToRoot = ActorAnimationSpaceConversion.SourceAffineToUnity(source.BindLocalToRootMatrix);
                buffer.Add(new ActorBone
                {
                    Name = source.Name,
                    ParentIndex = source.ParentIndex,
                    BindPosition = position,
                    BindRotation = rotation,
                    BindScale = scale,
                    BindLocalMatrix = localToParent,
                    BindLocalToRootMatrix = localToRoot,
                    LocalPosition = position,
                    LocalRotation = rotation,
                    LocalScale = scale,
                    LocalPoseAnimated = 0,
                    LocalToRoot = localToRoot,
                    SkinMatrix = localToRoot,
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
            DynamicBuffer<ActorRigidEquipment> rigidEquipment,
            DynamicBuffer<ActorBone> bones,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            bool isNpc,
            bool firstPerson,
            ActorVisualRecipeDef recipe,
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
                    firstPerson,
                    equipment,
                    ref coveredParts);
            }

            added += AddBakedActorVisualRecipe(buffer, ref catalog, cache, recipe, coveredParts);
            return added;
        }

        static int AddBakedEquipmentVisuals(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            ActorVisualRecipeDef actorRecipe,
            bool firstPerson,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ref uint coveredParts)
        {
            int added = 0;
            for (int slotIndex = 0; slotIndex < s_NpcEquipmentSlotOrder.Length; slotIndex++)
            {
                if (!TryGetEquipmentInSlot(equipment, s_NpcEquipmentSlotOrder[slotIndex], out var slot))
                    continue;
                if (slot.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!itemHandle.IsValid || (uint)itemHandle.Index >= (uint)(contentDb?.Data?.Items?.Length ?? 0))
                    continue;

                ref readonly var item = ref contentDb.Get(itemHandle);
                if (!cache.TryGetEquipmentVisual(
                        item.ContentId,
                        actorRecipe.RigFamilyIndex,
                        firstPerson,
                        actorRecipe.BodyVariant,
                        out var visual)
                    || visual == null
                    || visual.IsValid == 0)
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
                    uint mask = PartMask(part);
                    if ((coveredParts & mask) != 0)
                        continue;

                    visualEntryParts |= mask;
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
            uint coveredParts)
        {
            int added = 0;
            int entryEnd = math.min(
                cache.ActorAnimationCatalog.ActorVisualRecipeEntries?.Length ?? 0,
                recipe.FirstEntryIndex + recipe.EntryCount);
            for (int entryIndex = recipe.FirstEntryIndex; entryIndex >= 0 && entryIndex < entryEnd; entryIndex++)
            {
                var entry = cache.ActorAnimationCatalog.ActorVisualRecipeEntries[entryIndex];
                uint mask = PartMask(entry.PartReference);
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
                MeshIndex = skinMesh.MeshIndex,
                MaterialIndex = skinMesh.MaterialIndex,
                TextureIndex = skinMesh.TextureIndex,
                FirstBoneIndex = 0,
                BoneCount = skinMesh.SkinBoneCount,
                AttachBoneIndex = skinMesh.IsRigid != 0 ? attachBoneIndex : -1,
                RigidMirrorX = skinMesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
            return true;
        }

        static uint PartMask(ActorVisualPartReference reference)
        {
            int bit = (int)reference;
            return (uint)bit < 32u ? 1u << bit : 0u;
        }

        static bool TryGetEquipmentInSlot(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ItemEquipmentSlot target,
            out ActorEquipmentSlot result)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Slot == target)
                {
                    result = slot;
                    return true;
                }
            }

            result = default;
            return false;
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
            DynamicBuffer<ActorRigidEquipment> rigidEquipment,
            DynamicBuffer<ActorBone> bones,
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

                int attachBoneIndex = ResolveRigidEquipmentAttachBone(bones, itemEquipment);
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
            // Vanilla keeps equipped weapons sheathed/invisible until the actor is in a drawn weapon state.
            // Keep the data equipped now; a later combat/draw-state pass can spawn or enable this visual.
            return equipment.Kind == ItemEquipmentKind.Weapon && s_SpawnWeaponsDrawnOnPresentation;
        }

        static int ResolveRigidEquipmentAttachBone(DynamicBuffer<ActorBone> bones, in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon)
            {
                if (equipment.Type == 9)
                {
                    int leftWeaponBone = ResolveAttachBoneIndex(bones, new FixedString64Bytes("weapon bone left"));
                    if (leftWeaponBone >= 0)
                        return leftWeaponBone;
                }

                int weaponBone = ResolveAttachBoneIndex(bones, new FixedString64Bytes("weapon bone"));
                return weaponBone >= 0 ? weaponBone : ResolveAttachBoneIndex(bones, new FixedString64Bytes("bip01 r hand"));
            }

            int shieldBone = ResolveAttachBoneIndex(bones, new FixedString64Bytes("shield bone"));
            return shieldBone >= 0 ? shieldBone : ResolveAttachBoneIndex(bones, new FixedString64Bytes("bip01 l forearm"));
        }

        static int ResolveAttachBoneIndex(DynamicBuffer<ActorBone> bones, FixedString64Bytes name)
        {
            if (name.IsEmpty)
                return -1;

            for (int i = 0; i < bones.Length; i++)
                if (FixedStringEqualsIgnoreCase(bones[i].Name, name))
                    return i;
            return -1;
        }

        static bool FixedStringEqualsIgnoreCase(FixedString64Bytes a, FixedString64Bytes b)
        {
            if (a.Length != b.Length)
                return false;

            return string.Equals(a.ToString(), b.ToString(), System.StringComparison.OrdinalIgnoreCase);
        }

    }
}
