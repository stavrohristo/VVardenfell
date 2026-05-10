using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationBlobCatalogSystem))]
    public partial struct ActorPresentationSpawnSystem : ISystem
    {
        static readonly System.Collections.Generic.HashSet<int> s_RigidEquipmentPrefabBuildSet = new();
        static int s_RobeSkirtDiagnosticLogCount;
        static readonly float3 k_MinAnimatedRenderCullExtents = new(1.25f, 2.25f, 1.25f);
        static readonly float3 k_AnimatedRenderCullPadding = new(0.75f, 1.0f, 0.75f);

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            CacheLoader cache = WorldResources.Cache;
            var blobRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            ref var catalog = ref blobRef.Value;
            PrebuildRigidEquipmentPrefabs(ref systemState, ref contentBlob);
            var actorRenderResources = WorldResources.ActorEntitiesGraphicsRenderer ?? new ActorEntitiesGraphicsRenderResources();
            WorldResources.ActorEntitiesGraphicsRenderer = actorRenderResources;
            actorRenderResources.Ensure(systemState.EntityManager, ref catalog);
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

                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, source.ValueRO.Definition);
                bool isNpc = actor.Kind == ActorDefKind.Npc;
                bool firstPerson = source.ValueRO.FirstPerson != 0;
                bool hasRuntimeAppearance = systemState.EntityManager.HasComponent<ActorRuntimeAppearance>(entity);
                ActorRuntimeAppearance runtimeAppearance = hasRuntimeAppearance
                    ? systemState.EntityManager.GetComponentData<ActorRuntimeAppearance>(entity)
                    : default;
                bool runtimeFemale = hasRuntimeAppearance && runtimeAppearance.Male == 0;
                ulong runtimeRaceHash = hasRuntimeAppearance
                    ? RuntimeContentStableHash.HashId(runtimeAppearance.RaceId.ToString())
                    : actor.RaceIdHash;
                bool isBeast = isNpc && ActorEquipmentRuntimeUtility.IsBeastRace(ref contentBlob, runtimeRaceHash);
                ActorVisualRecipeDef recipe = null;
                bool hasRecipe = false;
                if (hasRuntimeAppearance)
                {
                    recipe = new ActorVisualRecipeDef
                    {
                        BodyVariant = runtimeFemale ? ActorVisualBodyVariant.Female : ActorVisualBodyVariant.Male,
                        RigFamilyIndex = ResolveRuntimeNpcRigFamilyIndex(ref catalog, firstPerson, runtimeFemale, isBeast),
                        FirstEntryIndex = -1,
                        EntryCount = 0,
                    };
                    hasRecipe = true;
                }
                else if (cache != null)
                {
                    hasRecipe = cache.TryGetActorVisualRecipe(actor.ContentId, firstPerson, out recipe);
                }
                ActorRigFamilyDef rigFamily = hasRecipe && (uint)recipe.RigFamilyIndex < (uint)(cache?.ActorAnimationCatalog?.RigFamilies?.Length ?? 0)
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
                ecb.AddComponent(entity, new ActorAnimationMotionState());
                if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(entity))
                {
                    ecb.AddComponent(entity, new ActorWeaponAnimationState
                    {
                        WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                        Phase = ActorWeaponAnimationPhase.Hidden,
                    });
                }
                if (!systemState.EntityManager.HasComponent<ActorRigidEquipmentRenderOwnerActorDirty>(entity))
                {
                    ecb.AddComponent<ActorRigidEquipmentRenderOwnerActorDirty>(entity);
                    ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerActorDirty>(entity, false);
                }
                ecb.AddBuffer<ActorGpuAnimationRequest>(entity);
                ecb.AddBuffer<ActorAnimationOverlayState>(entity);
                ecb.AddBuffer<ActorAnimationEvent>(entity);
                if (!systemState.EntityManager.HasComponent<ActorRenderVisible>(entity))
                {
                    ecb.AddComponent<ActorRenderVisible>(entity);
                    ecb.SetComponentEnabled<ActorRenderVisible>(entity, true);
                }
                if (!systemState.EntityManager.HasComponent<ActorShadowCasterVisible>(entity))
                {
                    ecb.AddComponent<ActorShadowCasterVisible>(entity);
                    ecb.SetComponentEnabled<ActorShadowCasterVisible>(entity, true);
                }
                var boneBuffer = ecb.AddBuffer<ActorBone>(entity);
                PopulateBoneBuffer(boneBuffer, ref catalog, skeletonIndex);
                var sampledPoseBuffer = ecb.AddBuffer<ActorSampledBonePose>(entity);
                PopulateSampledPoseBuffer(sampledPoseBuffer, boneCount);
                var skinMeshBuffer = ecb.AddBuffer<ActorSkinMesh>(entity);
                bool hasEquipment = systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(entity);
                DynamicBuffer<ActorEquipmentSlot> equipmentBuffer = hasEquipment
                    ? systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(entity)
                    : default;
                uint hiddenPartMask = systemState.EntityManager.HasComponent<ActorHiddenVisualPartMask>(entity)
                    ? systemState.EntityManager.GetComponentData<ActorHiddenVisualPartMask>(entity).Mask
                    : 0u;
                PopulateSkinMeshBuffer(
                    skinMeshBuffer,
                    ref catalog,
                    cache,
                    ref contentBlob,
                    ref actor,
                    isNpc,
                    isBeast,
                    firstPerson,
                    recipe,
                    hasRuntimeAppearance,
                    runtimeAppearance,
                    hiddenPartMask,
                    hasEquipment,
                    equipmentBuffer);
                ecb.AddComponent(entity, BuildHeadAnimationState(skinMeshBuffer, ref catalog, entity.Index));

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
                    systemState.EntityManager,
                    actorRenderResources,
                    entity,
                    skinMeshBuffer,
                    ref catalog,
                    deformedVertexOffset,
                    actorBounds);

                using var rigidEquipment = new NativeList<ActorRigidEquipment>(Allocator.Temp);
                PopulateRigidEquipment(ref systemState, 
                    ref ecb,
                    entity,
                    rigidEquipment,
                    ref catalog,
                    skeletonIndex,
                    ref contentBlob,
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
                ecb.AddComponent(entity, new ActorPresentationEquipmentSignature
                {
                    Value = BuildEquipmentSignature(hasEquipment, equipmentBuffer),
                });
                ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                    systemState.EntityManager,
                    ref ecb,
                    entity,
                    enabled: false);
                ecb.AddComponent(entity, actorBounds);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static int ResolveBoneCount(ref ActorAnimationCatalogBlob catalog, int skeletonIndex)
            => ActorAnimationCatalogRuntimeUtility.ResolveBoneCount(ref catalog, skeletonIndex);

        static int ResolveRuntimeNpcRigFamilyIndex(ref ActorAnimationCatalogBlob catalog, bool firstPerson, bool female, bool beast)
        {
            ActorRigFamilyKind kind = firstPerson
                ? beast
                    ? ActorRigFamilyKind.NpcBeastFirstPerson
                    : female ? ActorRigFamilyKind.NpcFemaleFirstPerson : ActorRigFamilyKind.NpcMaleFirstPerson
                : beast
                    ? ActorRigFamilyKind.NpcBeast
                    : female ? ActorRigFamilyKind.NpcFemale : ActorRigFamilyKind.NpcMale;
            for (int i = 0; i < catalog.RigFamilies.Length; i++)
            {
                if (catalog.RigFamilies[i].FamilyKind == kind)
                    return i;
            }

            throw new InvalidOperationException($"[VVardenfell][CharGen] Missing actor rig family '{kind}' for runtime player appearance.");
        }

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

        static ActorHeadAnimationState BuildHeadAnimationState(
            DynamicBuffer<ActorSkinMesh> skinMeshes,
            ref ActorAnimationCatalogBlob catalog,
            int seed)
        {
            for (int i = 0; i < skinMeshes.Length; i++)
            {
                int skinMeshIndex = skinMeshes[i].SkinMeshIndex;
                if ((uint)skinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                    continue;

                var skinMesh = catalog.SkinMeshes[skinMeshIndex];
                if (skinMesh.FirstHeadMorphTargetIndex < 0
                    || skinMesh.HeadMorphTargetCount <= 1
                    || skinMesh.TalkStop <= skinMesh.TalkStart
                    || skinMesh.BlinkStop < skinMesh.BlinkStart)
                {
                    continue;
                }

                uint randomState = (uint)math.max(1, seed + 1);
                return new ActorHeadAnimationState
                {
                    TalkStart = skinMesh.TalkStart,
                    TalkStop = skinMesh.TalkStop,
                    BlinkStart = skinMesh.BlinkStart,
                    BlinkStop = skinMesh.BlinkStop,
                    CurrentTime = skinMesh.BlinkStop,
                    BlinkTimer = ResolveInitialBlinkTimer(ref randomState),
                    RandomState = randomState,
                    HasHeadMorph = 1,
                };
            }

            return default;
        }

        static float ResolveInitialBlinkTimer(ref uint randomState)
            => -(2f + RollDice(ref randomState, 6u));

        static uint RollDice(ref uint state, uint max)
        {
            if (max == 0u)
                return 0u;
            state = state == 0u ? 1u : state;
            state = 1664525u * state + 1013904223u;
            return state % max;
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
            int previousBucketIndex = -1;
            int previousMaterialIndex = -1;
            int previousTextureSlice = -1;
            bool localPlayerVisual = entityManager.HasComponent<LocalPlayerVisual>(actorEntity);
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
                if (!entityManager.Exists(prototype))
                    throw new InvalidOperationException($"Actor skin mesh {skinMeshIndex} render prototype entity={prototype.Index}:{prototype.Version} no longer exists.");
                ActorRenderChildPrototypeComponents prototypeComponents = ActorRenderChildPrototypeComponents.Resolve(entityManager, prototype);

                if (info.BucketIndex != previousBucketIndex
                    || info.MaterialIndex != previousMaterialIndex
                    || info.TextureSlice != previousTextureSlice)
                {
                    previousBucketIndex = info.BucketIndex;
                    previousMaterialIndex = info.MaterialIndex;
                    previousTextureSlice = info.TextureSlice;
                }

                Entity child = ecb.Instantiate(prototype);
                int renderMeshIndex = skinMeshes[i].RigidMirrorX != 0 && info.MirroredMeshIndex >= 0
                    ? info.MirroredMeshIndex
                    : info.MeshIndex;
                byte visibilityMode = localPlayerVisual && IsFirstPersonCameraHiddenPart(skinMeshes[i].PartReference)
                    ? ActorRenderMeshVisibilityMode.FirstPersonCameraHidden
                    : ActorRenderMeshVisibilityMode.Normal;
                ConfigureActorRenderChild(
                    ref ecb,
                    actorEntity,
                    child,
                    skinMeshIndex,
                    renderMeshIndex,
                    info.MaterialIndex,
                    info.TextureSlice,
                    vertexCursor,
                    initialLocalToWorld,
                    prototypeComponents,
                    actorBounds,
                    visibilityMode,
                    ref linked,
                    createdLinkedBuffer);

                if (visibilityMode == ActorRenderMeshVisibilityMode.FirstPersonCameraHidden)
                {
                    if (!renderResources.TryGetShadowOnlyPrototype(info.BucketIndex, out Entity shadowOnlyPrototype))
                        throw new InvalidOperationException($"Actor skin mesh {skinMeshIndex} has no Entities Graphics shadow-only render prototype.");
                    if (!entityManager.Exists(shadowOnlyPrototype))
                        throw new InvalidOperationException($"Actor skin mesh {skinMeshIndex} shadow-only render prototype entity={shadowOnlyPrototype.Index}:{shadowOnlyPrototype.Version} no longer exists.");
                    ActorRenderChildPrototypeComponents shadowOnlyPrototypeComponents = ActorRenderChildPrototypeComponents.Resolve(entityManager, shadowOnlyPrototype);

                    Entity shadowOnlyChild = ecb.Instantiate(shadowOnlyPrototype);
                    ConfigureActorRenderChild(
                        ref ecb,
                        actorEntity,
                        shadowOnlyChild,
                        skinMeshIndex,
                        renderMeshIndex,
                        info.MaterialIndex,
                        info.TextureSlice,
                        vertexCursor,
                        initialLocalToWorld,
                        shadowOnlyPrototypeComponents,
                        actorBounds,
                        ActorRenderMeshVisibilityMode.FirstPersonShadowOnly,
                        ref linked,
                        createdLinkedBuffer);
                }

                vertexCursor += math.max(0, skinMesh.VertexCount);
            }
        }

        static void ConfigureActorRenderChild(
            ref EntityCommandBuffer ecb,
            Entity actorEntity,
            Entity child,
            int skinMeshIndex,
            int renderMeshIndex,
            int materialIndex,
            int textureSlice,
            int vertexCursor,
            LocalToWorld initialLocalToWorld,
            ActorRenderChildPrototypeComponents prototypeComponents,
            ActorLocalBounds actorBounds,
            byte visibilityMode,
            ref DynamicBuffer<LinkedEntityGroup> linked,
            bool createdLinkedBuffer)
        {
            if (prototypeComponents.HasLocalTransform)
                ecb.SetComponent(child, LocalTransform.Identity);
            else
                ecb.AddComponent(child, LocalTransform.Identity);
            if (prototypeComponents.HasLocalToWorld)
                ecb.SetComponent(child, initialLocalToWorld);
            else
                ecb.AddComponent(child, initialLocalToWorld);
            ecb.AddComponent(child, new Parent { Value = actorEntity });
            SetOrAdd(ref ecb, child, prototypeComponents.HasMaterialMeshInfo, MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, renderMeshIndex));
            SetOrAdd(ref ecb, child, prototypeComponents.HasTextureSlice, new TextureSlice { Value = textureSlice });
            SetOrAdd(ref ecb, child, prototypeComponents.HasActorDeformedMeshIndex, new ActorDeformedMeshIndex { Value = vertexCursor });
            SetOrAdd(ref ecb, child, prototypeComponents.HasRenderBounds, new RenderBounds
            {
                Value = new AABB
                {
                    Center = actorBounds.Center,
                    Extents = BuildAnimatedRenderCullExtents(actorBounds),
                },
            });
            SetOrAdd(ref ecb, child, prototypeComponents.HasActorRenderMeshInstance, new ActorRenderMeshInstance
            {
                Actor = actorEntity,
                SkinMeshIndex = skinMeshIndex,
                VisibilityMode = visibilityMode,
            });

            if (createdLinkedBuffer)
                linked.Add(new LinkedEntityGroup { Value = child });
            else
                ecb.AppendToBuffer(actorEntity, new LinkedEntityGroup { Value = child });
        }

        static void SetOrAdd<T>(
            ref EntityCommandBuffer ecb,
            Entity entity,
            bool hasComponent,
            T value)
            where T : unmanaged, IComponentData
        {
            if (hasComponent)
                ecb.SetComponent(entity, value);
            else
                ecb.AddComponent(entity, value);
        }

        struct ActorRenderChildPrototypeComponents
        {
            public bool HasLocalTransform;
            public bool HasLocalToWorld;
            public bool HasMaterialMeshInfo;
            public bool HasTextureSlice;
            public bool HasActorDeformedMeshIndex;
            public bool HasRenderBounds;
            public bool HasActorRenderMeshInstance;

            public static ActorRenderChildPrototypeComponents Resolve(EntityManager entityManager, Entity prototype)
            {
                return new ActorRenderChildPrototypeComponents
                {
                    HasLocalTransform = entityManager.HasComponent<LocalTransform>(prototype),
                    HasLocalToWorld = entityManager.HasComponent<LocalToWorld>(prototype),
                    HasMaterialMeshInfo = entityManager.HasComponent<MaterialMeshInfo>(prototype),
                    HasTextureSlice = entityManager.HasComponent<TextureSlice>(prototype),
                    HasActorDeformedMeshIndex = entityManager.HasComponent<ActorDeformedMeshIndex>(prototype),
                    HasRenderBounds = entityManager.HasComponent<RenderBounds>(prototype),
                    HasActorRenderMeshInstance = entityManager.HasComponent<ActorRenderMeshInstance>(prototype),
                };
            }
        }

        static float3 BuildAnimatedRenderCullExtents(ActorLocalBounds actorBounds)
            => math.max(actorBounds.Extents + k_AnimatedRenderCullPadding, k_MinAnimatedRenderCullExtents);

        static bool IsFirstPersonCameraHiddenPart(ActorVisualPartReference part)
            => part == ActorVisualPartReference.Head || part == ActorVisualPartReference.Hair;

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
            ref RuntimeContentBlob contentBlob,
            ref RuntimeActorDefBlob actor,
            bool isNpc,
            bool isBeast,
            bool firstPerson,
            ActorVisualRecipeDef recipe,
            bool hasRuntimeAppearance,
            ActorRuntimeAppearance runtimeAppearance,
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
                    ref contentBlob,
                    recipe,
                    ref actor,
                    isBeast,
                    firstPerson,
                    equipment,
                    hiddenPartMask,
                    ref coveredParts);
            }

            added += hasRuntimeAppearance
                ? AddRuntimeActorVisualRecipe(buffer, ref catalog, ref contentBlob, runtimeAppearance, firstPerson, recipe.RigFamilyIndex, coveredParts, hiddenPartMask)
                : AddBakedActorVisualRecipe(buffer, ref catalog, cache, recipe, coveredParts, hiddenPartMask);
            return added;
        }

        static int AddBakedEquipmentVisuals(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            ref RuntimeContentBlob contentBlob,
            ActorVisualRecipeDef actorRecipe,
            ref RuntimeActorDefBlob actor,
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
                if (!itemHandle.IsValid || (uint)itemHandle.Index >= (uint)contentBlob.Items.Length)
                    continue;

                ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref contentBlob, itemHandle);
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

                    if (AddBakedSkinEntry(buffer, ref catalog, entry.SkinMeshIndex, entry.AttachBoneIndex, entry.RigidMirrorX, part))
                        added++;
                }

                coveredParts |= visual.CoverageMask | visualEntryParts;
            }

            return added;
        }

        static int AddRuntimeActorVisualRecipe(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            ref RuntimeContentBlob contentBlob,
            in ActorRuntimeAppearance appearance,
            bool firstPerson,
            int rigFamilyIndex,
            uint coveredParts,
            uint hiddenPartMask)
        {
            int added = 0;
            uint usedParts = coveredParts;
            if (!firstPerson)
            {
                added += AddRuntimeExplicitBodyPart(
                    buffer,
                    ref catalog,
                    ref contentBlob,
                    appearance.HeadId,
                    ActorVisualPartReference.Head,
                    rigFamilyIndex,
                    hiddenPartMask,
                    ref usedParts,
                    acceptDeclaredMeshes: false);
                added += AddRuntimeExplicitBodyPart(
                    buffer,
                    ref catalog,
                    ref contentBlob,
                    appearance.HairId,
                    ActorVisualPartReference.Hair,
                    rigFamilyIndex,
                    hiddenPartMask,
                    ref usedParts,
                    acceptDeclaredMeshes: true);
            }

            bool female = appearance.Male == 0;
            for (int part = (int)ActorVisualPartReference.Neck; part < (int)ActorVisualPartReference.Count; part++)
            {
                var reference = (ActorVisualPartReference)part;
                if (!ActorVisualContentRules.IsBaseSkinPartReference(reference))
                    continue;
                if (firstPerson && !ActorVisualContentRules.IsFirstPersonPartReference(reference))
                    continue;
                if (reference == ActorVisualPartReference.Tail
                    && !ActorEquipmentRuntimeUtility.IsBeastRace(ref contentBlob, RuntimeContentStableHash.HashId(appearance.RaceId.ToString())))
                {
                    continue;
                }

                ref RuntimeActorBodyPartDefBlob bodyPart = ref ResolveRuntimeRaceBodyPart(ref contentBlob, appearance.RaceId, female, firstPerson, reference);
                added += AddRuntimeBodyPart(
                    buffer,
                    ref catalog,
                    ref bodyPart,
                    reference,
                    rigFamilyIndex,
                    hiddenPartMask,
                    ref usedParts,
                    acceptDeclaredMeshes: false);
            }

            return added;
        }

        static int AddRuntimeExplicitBodyPart(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            ref RuntimeContentBlob contentBlob,
            FixedString64Bytes bodyPartId,
            ActorVisualPartReference reference,
            int rigFamilyIndex,
            uint hiddenPartMask,
            ref uint usedParts,
            bool acceptDeclaredMeshes)
        {
            if (bodyPartId.IsEmpty)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Selected player appearance has no {reference} body part.");
            if (!RuntimeContentBlobUtility.TryGetActorBodyPartHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(bodyPartId.ToString()), out var handle)
                || !handle.IsValid
                || (uint)handle.Index >= (uint)contentBlob.ActorBodyParts.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing selected player body part '{bodyPartId}'.");
            }

            ref RuntimeActorBodyPartDefBlob bodyPart = ref contentBlob.ActorBodyParts[handle.Index];
            return AddRuntimeBodyPart(
                buffer,
                ref catalog,
                ref bodyPart,
                reference,
                rigFamilyIndex,
                hiddenPartMask,
                ref usedParts,
                acceptDeclaredMeshes);
        }

        static ref RuntimeActorBodyPartDefBlob ResolveRuntimeRaceBodyPart(
            ref RuntimeContentBlob contentBlob,
            FixedString64Bytes raceId,
            bool female,
            bool firstPerson,
            ActorVisualPartReference reference)
        {
            ActorBodyPartMeshPart meshPart = ActorVisualMappingPolicy.GetMeshPart(reference);
            int bestIndex = -1;
            int bestScore = int.MaxValue;
            for (int i = 0; i < contentBlob.ActorBodyParts.Length; i++)
            {
                ref RuntimeActorBodyPartDefBlob bodyPart = ref contentBlob.ActorBodyParts[i];
                if (bodyPart.Type != ActorBodyPartMeshType.Skin
                    || bodyPart.Vampire != 0
                    || bodyPart.NotPlayable != 0
                    || bodyPart.Part != meshPart
                    || !string.Equals(bodyPart.RaceId.ToString(), raceId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool partFirstPerson = bodyPart.FirstPerson != 0;
                bool partFemale = bodyPart.Female != 0;
                bool isFirstPersonArmPart = ActorVisualContentRules.IsFirstPersonMeshPart(meshPart);
                int score = ActorVisualContentRules.ResolveNpcRaceBodyPartScore(firstPerson, female, isFirstPersonArmPart, partFirstPerson, partFemale);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Race '{raceId}' is missing runtime body part '{reference}' for firstPerson={firstPerson}, female={female}.");
            return ref contentBlob.ActorBodyParts[bestIndex];
        }

        static int AddRuntimeBodyPart(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            ref RuntimeActorBodyPartDefBlob bodyPart,
            ActorVisualPartReference reference,
            int rigFamilyIndex,
            uint hiddenPartMask,
            ref uint usedParts,
            bool acceptDeclaredMeshes)
        {
            ActorBodyPartMeshPart expectedPart = ActorVisualMappingPolicy.GetMeshPart(reference);
            if (bodyPart.Type != ActorBodyPartMeshType.Skin || bodyPart.Part != expectedPart)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Body part '{bodyPart.Id}' is {bodyPart.Type}/{bodyPart.Part}, expected Skin/{expectedPart} for {reference}.");

            uint mask = ActorVisualContentRules.PartMask(reference);
            if ((hiddenPartMask & mask) != 0)
            {
                usedParts |= mask;
                return 0;
            }
            if ((usedParts & mask) != 0)
                return 0;

            int skinBindingIndex = RequireRuntimeSkinBinding(ref catalog, bodyPart.Model.ToString(), rigFamilyIndex, $"player body part '{bodyPart.Id}'");
            int added = AddRuntimeSkinBindingMeshes(buffer, ref catalog, skinBindingIndex, reference, acceptDeclaredMeshes, $"player body part '{bodyPart.Id}'");
            if (added == 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Body part '{bodyPart.Id}' produced no renderable skin meshes.");
            usedParts |= mask;
            return added;
        }

        static int RequireRuntimeSkinBinding(ref ActorAnimationCatalogBlob catalog, string modelPath, int rigFamilyIndex, string context)
        {
            string normalized = ActorVisualContentRules.NormalizeModelPath(modelPath, lowerInvariant: true);
            if (string.IsNullOrEmpty(normalized))
                throw new InvalidOperationException($"[VVardenfell][CharGen] {context} has no model path.");
            for (int i = 0; i < catalog.SkinBindings.Length; i++)
            {
                ref ActorSkinBindingBlob binding = ref catalog.SkinBindings[i];
                if (binding.RigFamilyIndex == rigFamilyIndex
                    && string.Equals(binding.SkinModelPath.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"[VVardenfell][CharGen] {context} requires missing skin binding '{normalized}' for rig family {rigFamilyIndex}.");
        }

        static int AddRuntimeSkinBindingMeshes(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            int skinBindingIndex,
            ActorVisualPartReference reference,
            bool acceptDeclaredMeshes,
            string context)
        {
            ref ActorSkinBindingBlob binding = ref catalog.SkinBindings[skinBindingIndex];
            int attachBoneIndex = ResolveRuntimePartAttachBoneIndex(ref catalog, binding.RigFamilyIndex, reference, context);
            byte rigidMirrorX = ResolveRuntimeRigidMirrorX(ref catalog, binding.RigFamilyIndex, attachBoneIndex);
            if (acceptDeclaredMeshes || !SkinBindingHasSkinnedRenderableMeshes(ref catalog, ref binding))
                return AddRuntimeDeclaredMeshes(buffer, ref catalog, ref binding, reference, attachBoneIndex, rigidMirrorX, context);

            string[] meshFilters = ActorVisualMappingPolicy.GetMeshFilters(reference);
            int added = 0;
            for (int filterIndex = 0; filterIndex < meshFilters.Length && added == 0; filterIndex++)
            {
                string meshFilter = meshFilters[filterIndex];
                int end = math.min(catalog.SkinMeshes.Length, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
                for (int i = binding.FirstSkinMeshIndex; i >= 0 && i < end; i++)
                {
                    ref ActorSkinMeshBlob mesh = ref catalog.SkinMeshes[i];
                    if (!IsRenderableSkinMesh(mesh) || mesh.IsRigid != 0 || !MatchesMeshFilter(ref catalog, mesh, meshFilter))
                        continue;
                    AddRuntimeSkinMesh(buffer, i, mesh, attachBoneIndex, rigidMirrorX, reference);
                    added++;
                }
            }

            if (added == 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] {context} produced no renderable meshes matching '{string.Join("' or '", meshFilters)}'.");
            return added;
        }

        static int AddRuntimeDeclaredMeshes(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            ref ActorSkinBindingBlob binding,
            ActorVisualPartReference reference,
            int attachBoneIndex,
            byte rigidMirrorX,
            string context)
        {
            int added = 0;
            int end = math.min(catalog.SkinMeshes.Length, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = binding.FirstSkinMeshIndex; i >= 0 && i < end; i++)
            {
                ref ActorSkinMeshBlob mesh = ref catalog.SkinMeshes[i];
                if (!IsRenderableSkinMesh(mesh))
                    continue;
                if (mesh.IsRigid != 0 && attachBoneIndex < 0)
                    throw new InvalidOperationException($"[VVardenfell][CharGen] {context} declared rigid {reference} mesh '{mesh.NodeName}' but rig has no attach bone.");
                AddRuntimeSkinMesh(buffer, i, mesh, attachBoneIndex, rigidMirrorX, reference);
                added++;
            }

            return added;
        }

        static void AddRuntimeSkinMesh(
            DynamicBuffer<ActorSkinMesh> buffer,
            int skinMeshIndex,
            in ActorSkinMeshBlob mesh,
            int attachBoneIndex,
            byte rigidMirrorX,
            ActorVisualPartReference reference)
        {
            buffer.Add(new ActorSkinMesh
            {
                SkinMeshIndex = skinMeshIndex,
                AttachBoneIndex = mesh.IsRigid != 0 ? attachBoneIndex : -1,
                PartReference = reference,
                RigidMirrorX = mesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
        }

        static bool SkinBindingHasSkinnedRenderableMeshes(ref ActorAnimationCatalogBlob catalog, ref ActorSkinBindingBlob binding)
        {
            int end = math.min(catalog.SkinMeshes.Length, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            for (int i = binding.FirstSkinMeshIndex; i >= 0 && i < end; i++)
            {
                ref ActorSkinMeshBlob mesh = ref catalog.SkinMeshes[i];
                if (IsRenderableSkinMesh(mesh) && mesh.IsRigid == 0)
                    return true;
            }

            return false;
        }

        static bool IsRenderableSkinMesh(in ActorSkinMeshBlob mesh)
            => mesh.MeshIndex >= 0 && mesh.VertexCount > 0 && mesh.IndexCount > 0;

        static bool MatchesMeshFilter(ref ActorAnimationCatalogBlob catalog, in ActorSkinMeshBlob mesh, string meshFilter)
        {
            if (MatchesMeshFilter(mesh.NodeName.ToString(), meshFilter))
                return true;

            int graphNodeIndex = mesh.SourceGraphNodeIndex;
            int guard = 0;
            while ((uint)graphNodeIndex < (uint)catalog.GraphNodes.Length && guard++ < catalog.GraphNodes.Length)
            {
                ref ActorModelGraphNodeBlob graphNode = ref catalog.GraphNodes[graphNodeIndex];
                if (MatchesMeshFilter(graphNode.Name.ToString(), meshFilter))
                    return true;
                graphNodeIndex = graphNode.ParentIndex;
            }

            return false;
        }

        static bool MatchesMeshFilter(string nodeName, string meshFilter)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(meshFilter))
                return true;
            if (nodeName.StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase))
                return true;
            return nodeName.StartsWith("tri ", StringComparison.OrdinalIgnoreCase)
                   && nodeName.Substring(4).StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase);
        }

        static int ResolveRuntimePartAttachBoneIndex(
            ref ActorAnimationCatalogBlob catalog,
            int rigFamilyIndex,
            ActorVisualPartReference reference,
            string context)
        {
            if ((uint)rigFamilyIndex >= (uint)catalog.RigFamilies.Length)
                throw new InvalidOperationException($"[VVardenfell][CharGen] {context} references invalid rig family {rigFamilyIndex}.");
            int skeletonIndex = catalog.RigFamilies[rigFamilyIndex].SkeletonIndex;
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                throw new InvalidOperationException($"[VVardenfell][CharGen] {context} references invalid skeleton {skeletonIndex}.");

            string openMwName = ActorVisualMappingPolicy.GetBoneName(reference);
            string[] aliases = ActorVisualMappingPolicy.GetBoneAliases(reference);
            int result = ResolveRuntimeBoneIndex(ref catalog, skeletonIndex, openMwName);
            if (result >= 0)
                return result;
            for (int i = 0; i < aliases.Length; i++)
            {
                result = ResolveRuntimeBoneIndex(ref catalog, skeletonIndex, aliases[i]);
                if (result >= 0)
                    return result;
            }

            return -1;
        }

        static int ResolveRuntimeBoneIndex(ref ActorAnimationCatalogBlob catalog, int skeletonIndex, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return -1;
            ref ActorSkeletonBlob skeleton = ref catalog.Skeletons[skeletonIndex];
            int end = math.min(catalog.Bones.Length, skeleton.FirstBoneIndex + skeleton.BoneCount);
            for (int i = skeleton.FirstBoneIndex; i >= 0 && i < end; i++)
            {
                if (string.Equals(catalog.Bones[i].Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return i - skeleton.FirstBoneIndex;
            }

            string canonical = ActorVisualMappingPolicy.CanonicalizeBoneName(name);
            if (string.IsNullOrEmpty(canonical))
                return -1;
            for (int i = skeleton.FirstBoneIndex; i >= 0 && i < end; i++)
            {
                if (string.Equals(ActorVisualMappingPolicy.CanonicalizeBoneName(catalog.Bones[i].Name.ToString()), canonical, StringComparison.Ordinal))
                    return i - skeleton.FirstBoneIndex;
            }

            return -1;
        }

        static byte ResolveRuntimeRigidMirrorX(ref ActorAnimationCatalogBlob catalog, int rigFamilyIndex, int attachBoneIndex)
        {
            if ((uint)rigFamilyIndex >= (uint)catalog.RigFamilies.Length || attachBoneIndex < 0)
                return 0;
            int skeletonIndex = catalog.RigFamilies[rigFamilyIndex].SkeletonIndex;
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                return 0;
            ref ActorSkeletonBlob skeleton = ref catalog.Skeletons[skeletonIndex];
            int boneIndex = skeleton.FirstBoneIndex + attachBoneIndex;
            if ((uint)boneIndex >= (uint)catalog.Bones.Length)
                return 0;
            return ActorVisualMappingPolicy.IsLeftSideBoneName(catalog.Bones[boneIndex].Name.ToString()) ? (byte)1 : (byte)0;
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

                if (AddBakedSkinEntry(buffer, ref catalog, entry.SkinMeshIndex, entry.AttachBoneIndex, entry.RigidMirrorX, entry.PartReference))
                    added++;
            }

            return added;
        }

        static bool AddBakedSkinEntry(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            int skinMeshIndex,
            int attachBoneIndex,
            byte rigidMirrorX,
            ActorVisualPartReference reference)
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
                PartReference = reference,
                RigidMirrorX = skinMesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
            });
            return true;
        }

        void PrebuildRigidEquipmentPrefabs(ref SystemState systemState, ref RuntimeContentBlob contentBlob)
        {
            s_RigidEquipmentPrefabBuildSet.Clear();
            if (WorldResources.Cache == null
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
                if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                    continue;

                var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(entity);
                for (int i = 0; i < equipment.Length; i++)
                {
                    var slot = equipment[i];
                    if (slot.Content.Kind != ContentReferenceKind.Item)
                        continue;

                    var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                    if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, itemHandle, out var itemEquipment))
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
            {
                if ((uint)modelPrefabIndex >= (uint)WorldResources.ModelPrefabs.Length
                    || WorldResources.ModelPrefabs[modelPrefabIndex] == Entity.Null)
                {
                    throw new System.InvalidOperationException(
                        $"[VVardenfell][SpawnPrefabs] rigid equipment model prefab {modelPrefabIndex} is not loaded; rebake required.");
                }
            }
        }

        void PopulateRigidEquipment(ref SystemState systemState, 
            ref EntityCommandBuffer ecb,
            Entity actorEntity,
            NativeList<ActorRigidEquipment> rigidEquipment,
            ref ActorAnimationCatalogBlob catalog,
            int skeletonIndex,
            ref RuntimeContentBlob contentBlob,
            bool hasEquipment,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (!hasEquipment || WorldResources.ModelPrefabs == null)
                return;

            LocalTransform initialTransform = systemState.EntityManager.HasComponent<LocalTransform>(actorEntity)
                ? systemState.EntityManager.GetComponentData<LocalTransform>(actorEntity)
                : LocalTransform.Identity;

            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, itemHandle, out var itemEquipment))
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
                if (prefab == Entity.Null || !systemState.EntityManager.Exists(prefab))
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
                ecb.AddComponent<ActorRigidEquipmentRenderOwnerDirty>(equipmentRoot);

                if (systemState.EntityManager.HasComponent<InteriorCellMember>(actorEntity))
                    ecb.AddComponent<InteriorCellMember>(equipmentRoot);
                if (systemState.EntityManager.HasComponent<CellLink>(actorEntity))
                    ecb.AddComponent(equipmentRoot, systemState.EntityManager.GetComponentData<CellLink>(actorEntity));
            }
        }

        static bool ShouldSpawnRigidEquipmentAtPresentation(in ItemEquipmentDef equipment)
        {
            return equipment.Kind == ItemEquipmentKind.Weapon;
        }

        static ulong BuildEquipmentSignature(bool hasEquipment, DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (!hasEquipment || !equipment.IsCreated)
                return 0ul;

            return ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment);
        }

    }
}
