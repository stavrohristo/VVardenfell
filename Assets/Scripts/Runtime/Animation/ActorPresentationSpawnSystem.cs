using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Animation
{
    enum ActorPartReferenceType : byte
    {
        Head = 0,
        Hair = 1,
        Neck = 2,
        Cuirass = 3,
        Groin = 4,
        Skirt = 5,
        RightHand = 6,
        LeftHand = 7,
        RightWrist = 8,
        LeftWrist = 9,
        Shield = 10,
        RightForearm = 11,
        LeftForearm = 12,
        RightUpperarm = 13,
        LeftUpperarm = 14,
        RightFoot = 15,
        LeftFoot = 16,
        RightAnkle = 17,
        LeftAnkle = 18,
        RightKnee = 19,
        LeftKnee = 20,
        RightLeg = 21,
        LeftLeg = 22,
        RightPauldron = 23,
        LeftPauldron = 24,
        Weapon = 25,
        Tail = 26,
        Count = 27,
    }

    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationBlobCatalogSystem))]
    public partial class ActorPresentationSpawnSystem : SystemBase
    {
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
                bool firstPerson = false;
                int bindingIndex = -1;
                ActorAnimationModelBindingDef binding = null;
                bool hasBinding = cache != null
                    && cache.TryResolveActorAnimationBinding(
                        contentDb,
                        actor,
                        firstPerson,
                        out bindingIndex,
                        out binding);
                int boneCount = ResolveBoneCount(ref catalog, hasBinding ? binding.SkeletonIndex : -1);

                ecb.AddComponent(entity, new ActorPresentation
                {
                    Actor = source.ValueRO.Definition,
                    IsNpc = (byte)(isNpc ? 1 : 0),
                    IsCreature = (byte)(isNpc ? 0 : 1),
                    IsFemale = (byte)((actor.Flags & 0x1u) != 0 ? 1 : 0),
                    IsFirstPerson = (byte)(firstPerson ? 1 : 0),
                    ModelBindingIndex = hasBinding ? bindingIndex : -1,
                    SkeletonIndex = hasBinding ? binding.SkeletonIndex : -1,
                    FirstSkinMeshIndex = hasBinding ? binding.FirstSkinMeshIndex : -1,
                    SkinMeshCount = hasBinding ? binding.SkinMeshCount : 0,
                    FirstClipIndex = hasBinding ? binding.FirstClipIndex : -1,
                    ClipCount = hasBinding ? binding.ClipCount : 0,
                });
                ecb.AddComponent(entity, new ActorSkeleton
                {
                    SkeletonIndex = hasBinding ? binding.SkeletonIndex : -1,
                    BoneCount = boneCount,
                    AccumulationBoneIndex = ResolveAccumulationBoneIndex(ref catalog, hasBinding ? binding.SkeletonIndex : -1),
                    AccumulationSubtreeEndIndex = ResolveAccumulationSubtreeEndIndex(ref catalog, hasBinding ? binding.SkeletonIndex : -1),
                    FirstClipIndex = hasBinding ? binding.FirstClipIndex : -1,
                    ClipCount = hasBinding ? binding.ClipCount : 0,
                });
                ecb.AddComponent<CPUAnimation>(entity);
                ecb.AddComponent<GPUAnimation>(entity);
                ecb.SetComponentEnabled<GPUAnimation>(entity, false);
                ecb.AddComponent(entity, new ActorAnimationController
                {
                    RequestedGroup = new FixedString64Bytes("idle"),
                    Speed = 1f,
                    ActiveMask = ActorAnimationBlendMask.All,
                });
                ecb.AddComponent(entity, new ActorAnimationState());
                ecb.AddComponent(entity, new ActorRootMotion());
                ecb.AddComponent(entity, new ActorAnimationEventCursor());
                ecb.AddComponent(entity, new ActorProceduralRenderState());
                var boneBuffer = ecb.AddBuffer<ActorBone>(entity);
                PopulateBoneBuffer(boneBuffer, ref catalog, hasBinding ? binding.SkeletonIndex : -1);
                var sampledPoseBuffer = ecb.AddBuffer<ActorSampledBonePose>(entity);
                PopulateSampledPoseBuffer(sampledPoseBuffer, boneCount);
                var skinMeshBuffer = ecb.AddBuffer<ActorSkinMesh>(entity);
                PopulateSkinMeshBuffer(
                    skinMeshBuffer,
                    boneBuffer,
                    ref catalog,
                    cache,
                    contentDb,
                    actor,
                    isNpc,
                    firstPerson,
                        binding);

                var layerBuffer = ecb.AddBuffer<ActorAnimationLayer>(entity);
                if (hasBinding && binding.FirstClipIndex >= 0 && binding.ClipCount > 0)
                {
                    ulong clipHash = ResolveClipHash(ref catalog, binding.FirstClipIndex);
                    layerBuffer.Add(new ActorAnimationLayer
                    {
                        Group = new FixedString64Bytes("idle"),
                        ClipIndex = binding.FirstClipIndex,
                        ClipHash = clipHash,
                        Time = 0f,
                        Weight = 1f,
                        Priority = 0,
                        Mask = ActorAnimationBlendMask.All,
                    });
                }
                ecb.AddBuffer<ActorAnimationEvent>(entity);
                ecb.AddBuffer<ActorGpuAnimationRequest>(entity);
                ecb.AddBuffer<ActorProceduralDraw>(entity);
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

        static string ResolveBindingSkeletonPath(
            ref ActorAnimationCatalogBlob catalog,
            ActorAnimationModelBindingDef binding)
        {
            if (binding == null)
                return string.Empty;

            int skeletonIndex = binding.SkeletonIndex;
            if ((uint)skeletonIndex >= (uint)catalog.Skeletons.Length)
                throw new InvalidOperationException(
                    $"NPC actor binding '{binding.ModelPath}' has invalid skeleton index {skeletonIndex}.");

            string path = catalog.Skeletons[skeletonIndex].ModelPath.ToString();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    $"NPC actor binding '{binding.ModelPath}' resolved an empty skeleton path.");

            return path;
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
                var position = source.BindPosition;
                float4x4 localToParent = float4x4.TRS(position, rotation, new float3(scale));
                float4x4 localToRoot = source.ParentIndex >= 0 && source.ParentIndex < localIndex
                    ? math.mul(buffer[source.ParentIndex].LocalToRoot, localToParent)
                    : localToParent;
                buffer.Add(new ActorBone
                {
                    Name = source.Name,
                    ParentIndex = source.ParentIndex,
                    BindPosition = position,
                    BindRotation = rotation,
                    BindScale = scale,
                    LocalPosition = position,
                    LocalRotation = rotation,
                    LocalScale = scale,
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
            DynamicBuffer<ActorBone> bones,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            bool isNpc,
            bool firstPerson,
            ActorAnimationModelBindingDef binding)
        {
            if (isNpc)
            {
                int npcSkinCount = PopulateNpcBodySkinMeshBuffer(
                    buffer,
                    bones,
                    ref catalog,
                    cache,
                    contentDb,
                    actor,
                    firstPerson,
                    binding);
                if (npcSkinCount > 0)
                    return npcSkinCount;
            }

            return AddBindingSkinMeshes(buffer, ref catalog, binding);
        }

        static int PopulateNpcBodySkinMeshBuffer(
            DynamicBuffer<ActorSkinMesh> buffer,
            DynamicBuffer<ActorBone> bones,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            bool firstPerson,
            ActorAnimationModelBindingDef actorBinding)
        {
            if (cache == null || contentDb?.Data?.ActorBodyParts == null || actorBinding == null)
                return 0;

            string referenceSkeletonPath = ResolveBindingSkeletonPath(ref catalog, actorBinding);
            bool female = (actor.Flags & 0x1u) != 0;
            uint usedPartReferences = 0u;
            int added = 0;

            added += TryAddNpcExplicitBodyPart(
                buffer,
                bones,
                ref catalog,
                cache,
                contentDb,
                actor.HeadId,
                ActorBodyPartMeshPart.Head,
                female,
                firstPerson,
                referenceSkeletonPath,
                ref usedPartReferences);
            added += TryAddNpcExplicitBodyPart(
                buffer,
                bones,
                ref catalog,
                cache,
                contentDb,
                actor.HairId,
                ActorBodyPartMeshPart.Hair,
                female,
                firstPerson,
                referenceSkeletonPath,
                ref usedPartReferences);

            for (int partReference = (int)ActorPartReferenceType.Neck; partReference < (int)ActorPartReferenceType.Count; partReference++)
            {
                if (!IsBaseSkinPartReference((ActorPartReferenceType)partReference))
                    continue;

                if (!TryResolveNpcRaceBodyPart(
                        contentDb,
                        actor.RaceId,
                        (ActorPartReferenceType)partReference,
                        female,
                        firstPerson,
                        out var part))
                {
                    continue;
                }

                uint mask = 1u << partReference;
                if ((usedPartReferences & mask) != 0)
                    continue;
                usedPartReferences |= mask;

                if (!cache.TryGetActorAnimationBinding(part.Model, referenceSkeletonPath, out _, out var bodyBinding))
                    throw new InvalidOperationException(
                        $"NPC body skin binding missing for model '{part.Model}' with reference skeleton '{referenceSkeletonPath}'.");

                added += AddBindingSkinMeshes(
                    buffer,
                    ref catalog,
                    bodyBinding,
                    GetMeshFilter((ActorPartReferenceType)partReference),
                    ResolveAttachBoneIndex(bones, GetAttachBoneName((ActorPartReferenceType)partReference)),
                    IsLeftPartReference((ActorPartReferenceType)partReference) ? (byte)1 : (byte)0);
            }

            return added;
        }

        static bool IsBaseSkinPartReference(ActorPartReferenceType type)
        {
            return type is ActorPartReferenceType.Neck
                or ActorPartReferenceType.Cuirass
                or ActorPartReferenceType.Groin
                or ActorPartReferenceType.RightHand
                or ActorPartReferenceType.LeftHand
                or ActorPartReferenceType.RightWrist
                or ActorPartReferenceType.LeftWrist
                or ActorPartReferenceType.RightForearm
                or ActorPartReferenceType.LeftForearm
                or ActorPartReferenceType.RightUpperarm
                or ActorPartReferenceType.LeftUpperarm
                or ActorPartReferenceType.RightFoot
                or ActorPartReferenceType.LeftFoot
                or ActorPartReferenceType.RightAnkle
                or ActorPartReferenceType.LeftAnkle
                or ActorPartReferenceType.RightKnee
                or ActorPartReferenceType.LeftKnee
                or ActorPartReferenceType.RightLeg
                or ActorPartReferenceType.LeftLeg
                or ActorPartReferenceType.Tail;
        }

        static bool TryResolveNpcRaceBodyPart(
            RuntimeContentDatabase contentDb,
            string raceId,
            ActorPartReferenceType partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
        {
            result = default;
            if (contentDb?.Data?.ActorBodyParts == null)
                return false;

            ActorBodyPartMeshPart meshPart = GetMeshPart(partReference);
            bool isHand = meshPart == ActorBodyPartMeshPart.Hand
                          || meshPart == ActorBodyPartMeshPart.Wrist
                          || meshPart == ActorBodyPartMeshPart.Forearm
                          || meshPart == ActorBodyPartMeshPart.Upperarm;
            bool hasFallback = false;
            var bodyParts = contentDb.Data.ActorBodyParts;
            for (int i = 0; i < bodyParts.Length; i++)
            {
                var part = bodyParts[i];
                if (part.Type != ActorBodyPartMeshType.Skin
                    || part.Vampire != 0
                    || part.NotPlayable != 0
                    || part.Part != meshPart
                    || !string.Equals(part.RaceId, raceId ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool partFirstPerson = part.FirstPerson != 0;
                bool partFemale = part.Female != 0;
                if (firstPerson && isHand && !partFirstPerson)
                {
                    if (!hasFallback || partFemale == female || female)
                    {
                        result = part;
                        hasFallback = true;
                    }
                    continue;
                }

                if (partFirstPerson != firstPerson)
                    continue;

                if (female && !partFemale)
                {
                    if (!hasFallback)
                    {
                        result = part;
                        hasFallback = true;
                    }
                    continue;
                }

                if (female != partFemale)
                    continue;

                result = part;
                return true;
            }

            return hasFallback;
        }

        static int TryAddNpcExplicitBodyPart(
            DynamicBuffer<ActorSkinMesh> buffer,
            DynamicBuffer<ActorBone> bones,
            ref ActorAnimationCatalogBlob catalog,
            CacheLoader cache,
            RuntimeContentDatabase contentDb,
            string bodyPartId,
            ActorBodyPartMeshPart expectedPart,
            bool female,
            bool firstPerson,
            string referenceSkeletonPath,
            ref uint usedPartReferences)
        {
            if (string.IsNullOrWhiteSpace(bodyPartId)
                || cache == null
                || contentDb == null
                || !contentDb.TryGetActorBodyPartHandle(bodyPartId, out var handle))
            {
                return 0;
            }

            ref readonly var part = ref contentDb.GetActorBodyPart(handle);
            if (string.IsNullOrWhiteSpace(part.Model) || part.Type != ActorBodyPartMeshType.Skin)
                return 0;

            ActorPartReferenceType partReference = expectedPart == ActorBodyPartMeshPart.Hair
                ? ActorPartReferenceType.Hair
                : ActorPartReferenceType.Head;
            int partBit = (int)partReference;
            if ((uint)partBit < 32u)
            {
                uint mask = 1u << partBit;
                if ((usedPartReferences & mask) != 0)
                    return 0;
                usedPartReferences |= mask;
            }

            if (!cache.TryGetActorAnimationBinding(part.Model, referenceSkeletonPath, out _, out var binding))
                throw new InvalidOperationException(
                    $"NPC explicit body skin binding missing for part '{bodyPartId}' model '{part.Model}' with reference skeleton '{referenceSkeletonPath}'.");

            return AddBindingSkinMeshes(
                buffer,
                ref catalog,
                binding,
                GetMeshFilter(partReference),
                ResolveAttachBoneIndex(bones, GetAttachBoneName(partReference)),
                IsLeftPartReference(partReference) ? (byte)1 : (byte)0);
        }

        static int AddBindingSkinMeshes(
            DynamicBuffer<ActorSkinMesh> buffer,
            ref ActorAnimationCatalogBlob catalog,
            ActorAnimationModelBindingDef binding,
            FixedString64Bytes meshFilter = default,
            int attachBoneIndex = -1,
            byte rigidMirrorX = 0)
        {
            if (binding == null || binding.FirstSkinMeshIndex < 0 || binding.SkinMeshCount <= 0)
                return 0;

            int end = math.min(catalog.SkinMeshes.Length, binding.FirstSkinMeshIndex + binding.SkinMeshCount);
            int added = 0;
            for (int i = binding.FirstSkinMeshIndex; i < end; i++)
            {
                var skinMesh = catalog.SkinMeshes[i];
                if (skinMesh.VertexCount <= 0 || skinMesh.IndexCount <= 0)
                    continue;
                if (skinMesh.IsRigid == 0
                    && !meshFilter.IsEmpty
                    && !MatchesMeshFilter(skinMesh.NodeName, meshFilter))
                {
                    continue;
                }

                buffer.Add(new ActorSkinMesh
                {
                    SkinMeshIndex = i,
                    MeshIndex = skinMesh.MeshIndex,
                    MaterialIndex = skinMesh.MaterialIndex,
                    TextureIndex = skinMesh.TextureIndex,
                    FirstBoneIndex = 0,
                    BoneCount = skinMesh.SkinBoneCount,
                    AttachBoneIndex = skinMesh.IsRigid != 0 ? attachBoneIndex : -1,
                    RigidMirrorX = skinMesh.IsRigid != 0 ? rigidMirrorX : (byte)0,
                });
                added++;
            }

            return added;
        }

        static ActorBodyPartMeshPart GetMeshPart(ActorPartReferenceType type)
        {
            return type switch
            {
                ActorPartReferenceType.Head => ActorBodyPartMeshPart.Head,
                ActorPartReferenceType.Hair => ActorBodyPartMeshPart.Hair,
                ActorPartReferenceType.Neck => ActorBodyPartMeshPart.Neck,
                ActorPartReferenceType.Cuirass => ActorBodyPartMeshPart.Chest,
                ActorPartReferenceType.Groin => ActorBodyPartMeshPart.Groin,
                ActorPartReferenceType.RightHand or ActorPartReferenceType.LeftHand => ActorBodyPartMeshPart.Hand,
                ActorPartReferenceType.RightWrist or ActorPartReferenceType.LeftWrist => ActorBodyPartMeshPart.Wrist,
                ActorPartReferenceType.RightForearm or ActorPartReferenceType.LeftForearm => ActorBodyPartMeshPart.Forearm,
                ActorPartReferenceType.RightUpperarm or ActorPartReferenceType.LeftUpperarm => ActorBodyPartMeshPart.Upperarm,
                ActorPartReferenceType.RightFoot or ActorPartReferenceType.LeftFoot => ActorBodyPartMeshPart.Foot,
                ActorPartReferenceType.RightAnkle or ActorPartReferenceType.LeftAnkle => ActorBodyPartMeshPart.Ankle,
                ActorPartReferenceType.RightKnee or ActorPartReferenceType.LeftKnee => ActorBodyPartMeshPart.Knee,
                ActorPartReferenceType.RightLeg or ActorPartReferenceType.LeftLeg => ActorBodyPartMeshPart.Upperleg,
                ActorPartReferenceType.RightPauldron or ActorPartReferenceType.LeftPauldron => ActorBodyPartMeshPart.Clavicle,
                ActorPartReferenceType.Tail => ActorBodyPartMeshPart.Tail,
                _ => ActorBodyPartMeshPart.Chest,
            };
        }

        static FixedString64Bytes GetMeshFilter(ActorPartReferenceType type)
        {
            return type switch
            {
                ActorPartReferenceType.Head => new FixedString64Bytes("head"),
                ActorPartReferenceType.Hair => new FixedString64Bytes("hair"),
                ActorPartReferenceType.Neck => new FixedString64Bytes("neck"),
                ActorPartReferenceType.Cuirass => new FixedString64Bytes("chest"),
                ActorPartReferenceType.Groin or ActorPartReferenceType.Skirt => new FixedString64Bytes("groin"),
                ActorPartReferenceType.RightHand => new FixedString64Bytes("right hand"),
                ActorPartReferenceType.LeftHand => new FixedString64Bytes("left hand"),
                ActorPartReferenceType.RightWrist => new FixedString64Bytes("right wrist"),
                ActorPartReferenceType.LeftWrist => new FixedString64Bytes("left wrist"),
                ActorPartReferenceType.RightForearm => new FixedString64Bytes("right forearm"),
                ActorPartReferenceType.LeftForearm => new FixedString64Bytes("left forearm"),
                ActorPartReferenceType.RightUpperarm => new FixedString64Bytes("right upper arm"),
                ActorPartReferenceType.LeftUpperarm => new FixedString64Bytes("left upper arm"),
                ActorPartReferenceType.RightFoot => new FixedString64Bytes("right foot"),
                ActorPartReferenceType.LeftFoot => new FixedString64Bytes("left foot"),
                ActorPartReferenceType.RightAnkle => new FixedString64Bytes("right ankle"),
                ActorPartReferenceType.LeftAnkle => new FixedString64Bytes("left ankle"),
                ActorPartReferenceType.RightKnee => new FixedString64Bytes("right knee"),
                ActorPartReferenceType.LeftKnee => new FixedString64Bytes("left knee"),
                ActorPartReferenceType.RightLeg => new FixedString64Bytes("right upper leg"),
                ActorPartReferenceType.LeftLeg => new FixedString64Bytes("left upper leg"),
                ActorPartReferenceType.RightPauldron => new FixedString64Bytes("right clavicle"),
                ActorPartReferenceType.LeftPauldron => new FixedString64Bytes("left clavicle"),
                ActorPartReferenceType.Tail => new FixedString64Bytes("tail"),
                _ => default,
            };
        }

        static FixedString64Bytes GetAttachBoneName(ActorPartReferenceType type)
        {
            return type switch
            {
                ActorPartReferenceType.Hair => new FixedString64Bytes("head"),
                _ => GetMeshFilter(type),
            };
        }

        static bool IsLeftPartReference(ActorPartReferenceType type)
        {
            return type is ActorPartReferenceType.LeftHand
                or ActorPartReferenceType.LeftWrist
                or ActorPartReferenceType.LeftForearm
                or ActorPartReferenceType.LeftUpperarm
                or ActorPartReferenceType.LeftFoot
                or ActorPartReferenceType.LeftAnkle
                or ActorPartReferenceType.LeftKnee
                or ActorPartReferenceType.LeftLeg
                or ActorPartReferenceType.LeftPauldron;
        }

        static int ResolveAttachBoneIndex(DynamicBuffer<ActorBone> bones, FixedString64Bytes name)
        {
            if (name.IsEmpty)
                return -1;

            for (int i = 0; i < bones.Length; i++)
                if (bones[i].Name.Equals(name))
                    return i;
            return -1;
        }

        static bool MatchesMeshFilter(FixedString64Bytes nodeName, FixedString64Bytes meshFilter)
        {
            if (nodeName.IsEmpty || meshFilter.IsEmpty)
                return true;

            string node = nodeName.ToString();
            string filter = meshFilter.ToString();
            if (node == filter)
                return true;

            const string prefix = "tri ";
            return node.StartsWith(prefix, System.StringComparison.Ordinal)
                   && node.Substring(prefix.Length).StartsWith(filter, System.StringComparison.Ordinal);
        }
    }
}
