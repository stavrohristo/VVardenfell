using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
        const int RaceFlagBeast = 0x02;

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
        static int s_RobeSkirtDiagnosticLogCount;

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
                bool isBeast = isNpc && IsBeastRace(contentDb, actor.RaceId);
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
                    hasEquipment,
                    equipmentBuffer);

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
                ecb.AddComponent(entity, BuildLocalBounds(skinMeshBuffer, ref catalog));
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
            => ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex);

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
            in ActorDef actor,
            bool isBeast,
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
                    uint mask = PartMask(part);
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

        static bool IsBeastRace(RuntimeContentDatabase contentDb, string raceId)
        {
            if (contentDb == null || string.IsNullOrWhiteSpace(raceId))
                return false;

            if (!contentDb.TryGetRaceHandle(raceId, out var raceHandle))
                return false;

            ref readonly var race = ref contentDb.GetRace(raceHandle);
            return (race.Flags & RaceFlagBeast) != 0;
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

                int attachBoneIndex = ResolveRigidEquipmentAttachBone(ref catalog, skeletonIndex, itemEquipment);
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

        static int ResolveRigidEquipmentAttachBone(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon)
            {
                if (equipment.Type == 9)
                {
                    int leftWeaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("weapon bone left"));
                    if (leftWeaponBone >= 0)
                        return leftWeaponBone;
                }

                int weaponBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("weapon bone"));
                return weaponBone >= 0
                    ? weaponBone
                    : ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("bip01 r hand"));
            }

            int shieldBone = ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("shield bone"));
            return shieldBone >= 0
                ? shieldBone
                : ResolveAttachBoneIndex(ref catalog, skeletonIndex, new FixedString64Bytes("bip01 l forearm"));
        }

        static int ResolveAttachBoneIndex(
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            FixedString64Bytes name)
        {
            if (name.IsEmpty)
                return -1;

            var skeleton = new ActorSkeleton
            {
                SkeletonIndex = skeletonIndex,
                BoneCount = ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex),
            };
            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                var boneName = ActorAnimationCatalogRuntimeUtility.ResolveBoneName(ref catalog, skeleton, i);
                if (FixedStringEqualsIgnoreCase(boneName, name))
                    return i;
            }

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
