using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bake;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Bootstrap
{
    static class DirectActorPreviewBootstrap
    {
        const string PreviewActorId = "netch_betty";
        const bool PreviewFirstPerson = false;
        const float PreviewGroundY = 0f;
        const float PreviewActorSeparation = 0.85f;
        const float PreviewActorBoundsPadding = 0.25f;
        const int MaxPreviewSkinInfluences = 8;

        static readonly ActorVisualPartReference[] PreviewBodyParts =
        {
            ActorVisualPartReference.Neck,
            ActorVisualPartReference.Cuirass,
            ActorVisualPartReference.Groin,
            ActorVisualPartReference.RightHand,
            ActorVisualPartReference.LeftHand,
            ActorVisualPartReference.RightWrist,
            ActorVisualPartReference.LeftWrist,
            ActorVisualPartReference.RightForearm,
            ActorVisualPartReference.LeftForearm,
            ActorVisualPartReference.RightUpperarm,
            ActorVisualPartReference.LeftUpperarm,
            ActorVisualPartReference.RightFoot,
            ActorVisualPartReference.LeftFoot,
            ActorVisualPartReference.RightAnkle,
            ActorVisualPartReference.LeftAnkle,
            ActorVisualPartReference.RightKnee,
            ActorVisualPartReference.LeftKnee,
            ActorVisualPartReference.RightLeg,
            ActorVisualPartReference.LeftLeg,
            ActorVisualPartReference.Tail,
        };

        static GameObject s_Root;
        static ActorProceduralRenderResources s_GpuPreviewResources;

        public static IEnumerator InstallIncremental(MorrowindConfig config, RuntimeLoadProgress progress, Material previewMaterial)
        {
            if (config == null)
                throw new InvalidOperationException("Morrowind config is unavailable.");
            if (previewMaterial == null)
                throw new InvalidOperationException("Actor Preview Material is not assigned on the BootstrapController.");

            Cleanup();

            progress.BeginStage("Direct Actor Preview", "Reading actor records", 6);
            var records = LoadPreviewRecords(config.InstallPath, PreviewActorId);
            progress.Report($"Loaded actor '{records.Actor.Id}'", 1, 6);
            yield return null;

            progress.Report("Opening model archive", 2, 6);
            using var assets = new PreviewAssetResolver(config.InstallPath, previewMaterial);
            yield return null;

            progress.Report("Checking baked actor cache", 3, 6);
            var cacheBake = EnsureActorAnimationCacheCurrent(config, progress);
            while (cacheBake.MoveNext())
                yield return cacheBake.Current;

            progress.Report("Loading baked actor cache", 4, 6);
            var cache = new CacheLoader();
            var cacheLoad = cache.LoadIncremental(progress);
            while (cacheLoad.MoveNext())
                yield return cacheLoad.Current;

            progress.Report("Building live preview", 5, 6);
            s_Root = BuildPreviewScene(records, assets, cache);
            yield return null;

            progress.CompleteStage("Preview scene ready");
            progress.Complete("Ready", $"Direct preview ready for '{records.Actor.Id}'");
        }

        static IEnumerator EnsureActorAnimationCacheCurrent(MorrowindConfig config, RuntimeLoadProgress progress)
        {
            if (ActorAnimationFile.IsCurrentVersion(CachePaths.ActorAnimations))
                yield break;

            Debug.Log("[VVardenfell][ActorPreview] actor animation cache is missing or stale; running cache bake before preview load.");
            var bakeProgress = new BakeProgress();
            IEnumerator bake = BakeCoordinator.Bake(config, bakeProgress);
            while (bake.MoveNext())
            {
                string label = !string.IsNullOrWhiteSpace(bakeProgress.Label)
                    ? $"{bakeProgress.Stage}: {bakeProgress.Label}"
                    : "Rebaking actor cache";
                progress.Report(label, 3, 6);
                yield return bake.Current;
            }

            if (!string.IsNullOrEmpty(bakeProgress.Error))
                throw new InvalidOperationException($"Actor preview cache bake failed: {bakeProgress.Error}");

            if (!ActorAnimationFile.IsCurrentVersion(CachePaths.ActorAnimations))
                throw new InvalidOperationException($"Actor preview cache bake completed, but '{CachePaths.ActorAnimations}' is still missing or version-mismatched.");
        }

        static void Cleanup()
        {
            if (s_Root != null)
                UnityEngine.Object.Destroy(s_Root);
            s_Root = null;

            if (WorldResources.ActorProceduralRenderer == s_GpuPreviewResources)
                WorldResources.ActorProceduralRenderer = null;
            s_GpuPreviewResources?.Dispose();
            s_GpuPreviewResources = null;
        }

        static GameObject BuildPreviewScene(PreviewRecords records, PreviewAssetResolver assets, CacheLoader cache)
        {
            if (records.Actor.Kind == ActorDefKind.Creature)
                return BuildCreaturePreviewScene(records, assets, cache);

            var root = new GameObject("VVardenfell.DirectActorPreview");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Transform actorRoot = new GameObject($"PreviewActor.{records.Actor.Id}").transform;
            actorRoot.SetParent(root.transform, false);
            actorRoot.localPosition = new Vector3(-PreviewActorSeparation, PreviewGroundY, 0f);

            Transform gpuActorRoot = new GameObject($"PreviewActorGpu.{records.Actor.Id}").transform;
            gpuActorRoot.SetParent(root.transform, false);
            gpuActorRoot.localPosition = new Vector3(0f, PreviewGroundY, 0f);

            Transform bakedActorRoot = new GameObject($"PreviewActorBakedGpu.{records.Actor.Id}").transform;
            bakedActorRoot.SetParent(root.transform, false);
            bakedActorRoot.localPosition = new Vector3(PreviewActorSeparation, PreviewGroundY, 0f);

            string skeletonPath = ResolveNpcSkeletonModel(PreviewFirstPerson, records.IsFemale, records.IsBeast);
            Transform skeletonRoot = new GameObject("Skeleton").transform;
            skeletonRoot.SetParent(actorRoot, false);
            skeletonRoot.localPosition = Vector3.zero;

            var skeletonSource = assets.LoadModelSource(skeletonPath);
            var skeletonNodes = InstantiateModelSource(
                skeletonSource,
                skeletonRoot,
                includeRenderLeaf: _ => false,
                resolveMaterial: _ => null,
                mergeByName: null,
                out _);
            ApplyNpcLeftBoneMirroring(skeletonNodes);

            int totalRenderers = 0;
            var gpuLeaves = new List<DirectActorGpuPreviewLeaf>();

            AddExplicitNpcPart(records, assets, actorRoot, skeletonNodes, records.Actor.HeadId, ActorVisualPartReference.Head, gpuLeaves, ref totalRenderers);
            AddExplicitNpcPart(records, assets, actorRoot, skeletonNodes, records.Actor.HairId, ActorVisualPartReference.Hair, gpuLeaves, ref totalRenderers);

            for (int i = 0; i < PreviewBodyParts.Length; i++)
            {
                var reference = PreviewBodyParts[i];
                if (reference == ActorVisualPartReference.Tail && !records.IsBeast)
                    continue;

                if (!TryResolveNpcRaceBodyPart(
                        records.BodyParts,
                        records.Actor.RaceId,
                        reference,
                        records.IsFemale,
                        PreviewFirstPerson,
                        out var bodyPart))
                {
                    continue;
                }

                AttachBodyPart(records, assets, actorRoot, skeletonNodes, bodyPart, reference, gpuLeaves, ref totalRenderers);
            }

            LayoutPreviewRootsFromRenderBounds(actorRoot, gpuActorRoot, bakedActorRoot);
            BuildProceduralGpuPreview(actorRoot, gpuActorRoot, bakedActorRoot, gpuLeaves, cache, records.Actor.Id);
            CreateGround(root.transform);
            var boundsTarget = CreateBoundsTarget(root.transform, actorRoot, gpuActorRoot, bakedActorRoot);
            CreateOrAttachCamera(root.transform, boundsTarget, skeletonNodes);
            return root;
        }

        static GameObject BuildCreaturePreviewScene(PreviewRecords records, PreviewAssetResolver assets, CacheLoader cache)
        {
            var root = new GameObject("VVardenfell.DirectCreaturePreview");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Transform actorRoot = new GameObject($"PreviewCreature.{records.Actor.Id}").transform;
            actorRoot.SetParent(root.transform, false);
            actorRoot.localPosition = new Vector3(-PreviewActorSeparation, PreviewGroundY, 0f);

            Transform gpuActorRoot = new GameObject($"PreviewCreatureGpu.{records.Actor.Id}").transform;
            gpuActorRoot.SetParent(root.transform, false);
            gpuActorRoot.localPosition = new Vector3(0f, PreviewGroundY, 0f);

            Transform bakedActorRoot = new GameObject($"PreviewCreatureBakedGpu.{records.Actor.Id}").transform;
            bakedActorRoot.SetParent(root.transform, false);
            bakedActorRoot.localPosition = new Vector3(PreviewActorSeparation, PreviewGroundY, 0f);

            string modelPath = ResolveCreaturePreviewModelPath(records.Actor, assets);
            var source = assets.LoadModelSource(modelPath);
            var gpuLeaves = new List<DirectActorGpuPreviewLeaf>();
            InstantiateModelSource(
                source,
                actorRoot,
                includeRenderLeaf: _ => true,
                resolveMaterial: _ => assets.GetMaterial(ActorVisualPartReference.Cuirass),
                mergeByName: null,
                out _,
                gpuLeaves,
                modelPath);

            LayoutPreviewRootsFromRenderBounds(actorRoot, gpuActorRoot, bakedActorRoot);
            BuildProceduralGpuPreview(actorRoot, gpuActorRoot, bakedActorRoot, gpuLeaves, cache, records.Actor.Id);
            CreateGround(root.transform);
            var boundsTarget = CreateBoundsTarget(root.transform, actorRoot, gpuActorRoot, bakedActorRoot);
            CreateOrAttachCamera(root.transform, boundsTarget, null);
            return root;
        }

        static void AddExplicitNpcPart(
            PreviewRecords records,
            PreviewAssetResolver assets,
            Transform actorRoot,
            Dictionary<string, Transform> skeletonNodes,
            string bodyPartId,
            ActorVisualPartReference reference,
            List<DirectActorGpuPreviewLeaf> gpuLeaves,
            ref int totalRenderers)
        {
            if (string.IsNullOrWhiteSpace(bodyPartId))
                return;

            if (!records.BodyPartsById.TryGetValue(bodyPartId, out var bodyPart))
                return;

            AttachBodyPart(records, assets, actorRoot, skeletonNodes, bodyPart, reference, gpuLeaves, ref totalRenderers);
        }

        static void AttachBodyPart(
            PreviewRecords records,
            PreviewAssetResolver assets,
            Transform actorRoot,
            Dictionary<string, Transform> skeletonNodes,
            ActorBodyPartDef bodyPart,
            ActorVisualPartReference reference,
            List<DirectActorGpuPreviewLeaf> gpuLeaves,
            ref int totalRenderers)
        {
            if (actorRoot == null)
                return;

            string normalizedModelPath = NormalizeModelPath(bodyPart.Model);
            var source = assets.LoadModelSource(normalizedModelPath);
            Transform attachmentParent = IsSkeletonModel(source)
                ? actorRoot
                : ResolveAttachmentParent(reference, actorRoot, skeletonNodes);
            if (attachmentParent == null)
                return;

            string[] meshFilters = BuildMeshFilters(reference);
            InstantiateModelSource(
                source,
                attachmentParent,
                includeRenderLeaf: node => ShouldIncludeNodeForPart(node, meshFilters),
                resolveMaterial: _ => assets.GetMaterial(reference),
                mergeByName: skeletonNodes,
                out int renderersCreated,
                gpuLeaves,
                normalizedModelPath);

            totalRenderers += renderersCreated;
        }

        static bool IsSkeletonModel(ModelPrefabSource source)
        {
            if (source?.Nodes == null)
                return false;

            for (int i = 0; i < source.Nodes.Length; i++)
            {
                string name = source.Nodes[i]?.Name;
                if (string.Equals(name, "Bip01", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static Transform ResolveAttachmentParent(
            ActorVisualPartReference reference,
            Transform actorRoot,
            Dictionary<string, Transform> skeletonNodes)
        {
            if (reference == ActorVisualPartReference.Hair)
            {
                if (TryGetSkeletonNode(skeletonNodes, "Hair", out var hair))
                    return hair;
                if (TryGetSkeletonNode(skeletonNodes, "Head", out var head))
                    return head;
                if (TryGetSkeletonNode(skeletonNodes, "Bip01 Head", out var bipHead))
                    return bipHead;
                return actorRoot;
            }

            string attachmentName = ResolveAttachmentNodeName(reference);
            if (TryGetSkeletonNode(skeletonNodes, attachmentName, out var attachment))
                return attachment;

            string fallbackBone = ResolveAttachBoneName(reference);
            if (TryGetSkeletonNode(skeletonNodes, fallbackBone, out var bone))
                return bone;

            return actorRoot;
        }

        static bool TryGetSkeletonNode(Dictionary<string, Transform> skeletonNodes, string nodeName, out Transform node)
        {
            node = null;
            return !string.IsNullOrWhiteSpace(nodeName)
                && skeletonNodes != null
                && skeletonNodes.TryGetValue(nodeName, out node)
                && node != null;
        }

        static Dictionary<string, Transform> InstantiateModelSource(
            ModelPrefabSource source,
            Transform parent,
            Func<ModelPrefabSourceNode, bool> includeRenderLeaf,
            Func<ModelPrefabSourceNode, Material> resolveMaterial,
            Dictionary<string, Transform> mergeByName,
            out int renderersCreated,
            List<DirectActorGpuPreviewLeaf> gpuLeaves = null,
            string sourceModelPath = null)
        {
            var result = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            renderersCreated = 0;
            if (source?.Nodes == null || source.Nodes.Length == 0)
                return result;

            var transforms = new Transform[source.Nodes.Length];
            for (int i = 0; i < source.Nodes.Length; i++)
            {
                var node = source.Nodes[i];
                if (node == null || node.Kind == ModelPrefabNodeKind.SyntheticRoot)
                    continue;

                Transform nodeParent = parent;
                if (node.ParentIndex >= 0 && node.ParentIndex < transforms.Length && transforms[node.ParentIndex] != null)
                    nodeParent = transforms[node.ParentIndex];

                Transform existing = null;
                bool merged = node.Kind != ModelPrefabNodeKind.RenderLeaf
                    && !string.IsNullOrWhiteSpace(node.Name)
                    && mergeByName != null
                    && mergeByName.TryGetValue(node.Name, out  existing);

                Transform nodeTransform;
                if (merged)
                {
                    nodeTransform = existing;
                }
                else
                {
                    var go = new GameObject(string.IsNullOrWhiteSpace(node.Name) ? $"Node{i}" : node.Name);
                    go.transform.SetParent(nodeParent, false);
                    ApplySourceLocal(go.transform, node.SourceLocalMatrix);
                    nodeTransform = go.transform;
                }

                transforms[i] = nodeTransform;
                if (!string.IsNullOrWhiteSpace(node.Name) && !result.ContainsKey(node.Name))
                    result.Add(node.Name, nodeTransform);

                if (node.Kind != ModelPrefabNodeKind.RenderLeaf
                    || node.RenderLeaf.Vertices == null
                    || node.RenderLeaf.Vertices.Length == 0
                    || node.RenderLeaf.Indices == null
                    || node.RenderLeaf.Indices.Length == 0
                    || includeRenderLeaf == null
                    || !includeRenderLeaf(node))
                {
                    continue;
                }

                var mesh = BuildUnityMesh(node.RenderLeaf, nodeTransform.gameObject.name);
                if (mesh == null)
                    continue;

                GameObject rendererObject = nodeTransform.gameObject;
                var material = resolveMaterial?.Invoke(node);
                if (material == null)
                    throw new InvalidOperationException($"Preview material was null for node '{rendererObject.name}'.");

                if (TryCreateSkinnedRenderer(node, rendererObject, mesh, material, mergeByName, result, out var skinBones))
                {
                    gpuLeaves?.Add(new DirectActorGpuPreviewLeaf(sourceModelPath, node, nodeTransform, skinBones));
                    renderersCreated++;
                    continue;
                }

                var meshFilter = rendererObject.GetComponent<MeshFilter>();
                if (meshFilter == null)
                    meshFilter = rendererObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;
                var meshRenderer = rendererObject.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                    meshRenderer = rendererObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                gpuLeaves?.Add(new DirectActorGpuPreviewLeaf(sourceModelPath, node, nodeTransform, null));
                renderersCreated++;
            }

            return result;
        }

        static bool TryCreateSkinnedRenderer(
            ModelPrefabSourceNode node,
            GameObject rendererObject,
            Mesh mesh,
            Material material,
            Dictionary<string, Transform> skeletonNodes,
            Dictionary<string, Transform> localNodes,
            out Transform[] bones)
        {
            bones = null;
            if (node?.SkinBoneNames == null
                || node.SkinBoneNames.Length == 0
                || node.SkinBoneWeights == null
                || node.SkinBoneWeights.Length != mesh.vertexCount
                || node.SkinBindPoses == null
                || node.SkinBindPoses.Length != node.SkinBoneNames.Length)
            {
                return false;
            }

            bones = new Transform[node.SkinBoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                string boneName = node.SkinBoneNames[i];
                if (!TryResolvePreviewNode(skeletonNodes, localNodes, boneName, out bones[i]))
                    return false;
            }

            mesh.boneWeights = node.SkinBoneWeights;
            mesh.bindposes = node.SkinBindPoses;

            var skinned = rendererObject.GetComponent<SkinnedMeshRenderer>();
            if (skinned == null)
                skinned = rendererObject.AddComponent<SkinnedMeshRenderer>();

            skinned.sharedMesh = mesh;
            skinned.sharedMaterial = material;
            skinned.bones = bones;
            skinned.rootBone = ResolveSkinRoot(node, skeletonNodes, localNodes, bones);
            skinned.localBounds = mesh.bounds;
            skinned.updateWhenOffscreen = true;
            return true;
        }

        static Transform ResolveSkinRoot(
            ModelPrefabSourceNode node,
            Dictionary<string, Transform> skeletonNodes,
            Dictionary<string, Transform> localNodes,
            Transform[] bones)
        {
            if (TryResolvePreviewNode(skeletonNodes, localNodes, node.SkinRootName, out var root))
                return root;
            if (TryResolvePreviewNode(skeletonNodes, localNodes, "Bip01", out root))
                return root;
            return bones != null && bones.Length > 0 ? bones[0] : null;
        }

        static bool TryResolvePreviewNode(
            Dictionary<string, Transform> skeletonNodes,
            Dictionary<string, Transform> localNodes,
            string nodeName,
            out Transform node)
        {
            node = null;
            return !string.IsNullOrWhiteSpace(nodeName)
                && ((skeletonNodes != null && skeletonNodes.TryGetValue(nodeName, out node) && node != null)
                    || (localNodes != null && localNodes.TryGetValue(nodeName, out node) && node != null));
        }

        static void BuildProceduralGpuPreview(
            Transform referenceActorRoot,
            Transform gpuActorRoot,
            Transform bakedActorRoot,
            List<DirectActorGpuPreviewLeaf> leaves,
            CacheLoader cache,
            string actorId)
        {
            if (referenceActorRoot == null || gpuActorRoot == null || leaves == null || leaves.Count == 0)
                return;

            leaves.Sort(static (a, b) =>
            {
                int ai = a.Node?.RenderLeaf.Indices?.Length ?? 0;
                int bi = b.Node?.RenderLeaf.Indices?.Length ?? 0;
                return ai.CompareTo(bi);
            });

            var vertices = new List<ActorProceduralVertexGpu>();
            var indices = new List<int>();
            var boneMatrices = new List<ActorProceduralMatrixGpu>();
            var draws = new List<ActorProceduralDrawGpu>();
            var batches = new List<ActorProceduralDrawBatch>();

            Matrix4x4 referenceWorldToLocal = referenceActorRoot.worldToLocalMatrix;
            Matrix4x4 gpuLocalToWorld = gpuActorRoot.localToWorldMatrix;

            for (int i = 0; i < leaves.Count; i++)
            {
                var leaf = leaves[i];
                var raw = leaf.Node.RenderLeaf;
                if (raw.Vertices == null || raw.Vertices.Length == 0 || raw.Indices == null || raw.Indices.Length == 0)
                    continue;

                bool skinned = leaf.Bones != null
                    && leaf.Bones.Length > 0
                    && leaf.Node.SkinBindPoses != null
                    && leaf.Node.SkinBindPoses.Length == leaf.Bones.Length
                    && leaf.Node.SkinBoneWeights != null
                    && leaf.Node.SkinBoneWeights.Length == raw.Vertices.Length;

                int firstVertex = vertices.Count;
                int firstIndex = indices.Count;
                int boneMatrixOffset = boneMatrices.Count;
                for (int v = 0; v < raw.Vertices.Length; v++)
                {
                    Vector3 normal = raw.HasNormals ? raw.Normals[v] : Vector3.up;
                    Vector2 uv = raw.HasUvs ? raw.Uvs[v] : Vector2.zero;
                    BoneWeight weight = skinned ? leaf.Node.SkinBoneWeights[v] : default;
                    vertices.Add(new ActorProceduralVertexGpu
                    {
                        Position = raw.Vertices[v],
                        Normal = normal,
                        Uv = uv,
                        BoneIndices0 = skinned
                            ? new int4(weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3)
                            : new int4(-1),
                        BoneIndices1 = new int4(-1),
                        Weights0 = skinned
                            ? new float4(weight.weight0, weight.weight1, weight.weight2, weight.weight3)
                            : float4.zero,
                        Weights1 = float4.zero,
                    });
                }

                for (int t = 0; t < raw.Indices.Length; t++)
                    indices.Add(raw.Indices[t]);

                Matrix4x4 drawLocalToWorld;
                if (skinned)
                {
                    drawLocalToWorld = gpuLocalToWorld;
                    for (int b = 0; b < leaf.Bones.Length; b++)
                    {
                        Matrix4x4 skinMatrix = referenceWorldToLocal * leaf.Bones[b].localToWorldMatrix * leaf.Node.SkinBindPoses[b];
                        boneMatrices.Add(ToProceduralGpuMatrix(skinMatrix));
                    }
                }
                else
                {
                    drawLocalToWorld = gpuLocalToWorld * referenceWorldToLocal * leaf.Transform.localToWorldMatrix;
                }

                int drawIndex = draws.Count;
                draws.Add(new ActorProceduralDrawGpu
                {
                    FirstIndex = firstIndex,
                    FirstVertex = firstVertex,
                    BoneMatrixOffset = boneMatrixOffset,
                    BoneMatrixSource = 0,
                    TextureSlice = 0,
                    LocalToWorld = ToProceduralGpuMatrix(drawLocalToWorld),
                });
                AppendManualPreviewBatch(batches, drawIndex, raw.Indices.Length);
            }

            AppendBakedCachePreviewActor(
                cache,
                actorId,
                referenceActorRoot,
                bakedActorRoot,
                leaves,
                vertices,
                indices,
                boneMatrices,
                draws,
                batches);

            if (draws.Count == 0)
                return;

            if (WorldResources.ActorProceduralRenderer != null && WorldResources.ActorProceduralRenderer != s_GpuPreviewResources)
                WorldResources.ActorProceduralRenderer.Dispose();
            s_GpuPreviewResources?.Dispose();
            s_GpuPreviewResources = new ActorProceduralRenderResources();
            s_GpuPreviewResources.LoadManualPreview(
                vertices.ToArray(),
                indices.ToArray(),
                boneMatrices.ToArray(),
                draws.ToArray(),
                batches.ToArray());
            WorldResources.ActorProceduralRenderer = s_GpuPreviewResources;
        }

        static void AppendBakedCachePreviewActor(
            CacheLoader cache,
            string actorId,
            Transform referenceActorRoot,
            Transform bakedActorRoot,
            List<DirectActorGpuPreviewLeaf> directLeaves,
            List<ActorProceduralVertexGpu> vertices,
            List<int> indices,
            List<ActorProceduralMatrixGpu> boneMatrices,
            List<ActorProceduralDrawGpu> draws,
            List<ActorProceduralDrawBatch> batches)
        {
            if (cache?.ActorAnimationCatalog == null || cache.ContentDatabase == null || bakedActorRoot == null)
            {
                Debug.LogWarning("[VVardenfell][ActorPreviewBakedGpu] skipped: cache, content database, or baked root is missing.");
                return;
            }

            if (!cache.ContentDatabase.TryGetActorHandle(actorId, out var actorHandle) || !actorHandle.IsValid)
            {
                Debug.LogWarning($"[VVardenfell][ActorPreviewBakedGpu] skipped: actor '{actorId}' was not found in the baked content database.");
                return;
            }

            ref readonly var actor = ref cache.ContentDatabase.Get(actorHandle);
            if (!cache.TryGetActorVisualRecipe(actor.ContentId, PreviewFirstPerson, out var recipe) || recipe == null)
            {
                Debug.LogWarning($"[VVardenfell][ActorPreviewBakedGpu] skipped: actor '{actorId}' contentId={actor.ContentId.Value} has no baked visual recipe for firstPerson={PreviewFirstPerson}.");
                return;
            }

            var catalog = cache.ActorAnimationCatalog;
            var rigFamilies = catalog.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            if ((uint)recipe.RigFamilyIndex >= (uint)rigFamilies.Length)
            {
                Debug.LogWarning($"[VVardenfell][ActorPreviewBakedGpu] skipped: recipe rigFamilyIndex={recipe.RigFamilyIndex} is outside rigFamilies={rigFamilies.Length}.");
                return;
            }

            int skeletonIndex = rigFamilies[recipe.RigFamilyIndex]?.SkeletonIndex ?? -1;
            var skeletons = catalog.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            if ((uint)skeletonIndex >= (uint)skeletons.Length)
            {
                Debug.LogWarning($"[VVardenfell][ActorPreviewBakedGpu] skipped: skeletonIndex={skeletonIndex} is outside skeletons={skeletons.Length}.");
                return;
            }

            Matrix4x4[] bindLocalToRoot = BuildBakedBindLocalToRoot(skeletons[skeletonIndex]?.Bones);
            var entries = catalog.ActorVisualRecipeEntries ?? Array.Empty<ActorVisualRecipeEntryDef>();
            int entryEnd = Math.Min(entries.Length, recipe.FirstEntryIndex + recipe.EntryCount);
            bool logNetchDiagnostics = string.Equals(actorId, "netch_betty", StringComparison.OrdinalIgnoreCase);

            for (int entryIndex = recipe.FirstEntryIndex; entryIndex >= 0 && entryIndex < entryEnd; entryIndex++)
            {
                AppendBakedSkinMesh(
                    catalog,
                    entries[entryIndex],
                    bakedActorRoot,
                    bindLocalToRoot,
                    logNetchDiagnostics ? referenceActorRoot : null,
                    logNetchDiagnostics ? directLeaves : null,
                    vertices,
                    indices,
                    boneMatrices,
                    draws,
                    batches);
            }
        }

        static void AppendBakedSkinMesh(
            ActorAnimationCatalogData catalog,
            ActorVisualRecipeEntryDef entry,
            Transform bakedActorRoot,
            Matrix4x4[] bindLocalToRoot,
            Transform referenceActorRoot,
            List<DirectActorGpuPreviewLeaf> directLeaves,
            List<ActorProceduralVertexGpu> vertices,
            List<int> indices,
            List<ActorProceduralMatrixGpu> boneMatrices,
            List<ActorProceduralDrawGpu> draws,
            List<ActorProceduralDrawBatch> batches)
        {
            var skinMeshes = catalog.SkinMeshes ?? Array.Empty<ActorSkinMeshDef>();
            if ((uint)entry.SkinMeshIndex >= (uint)skinMeshes.Length)
                return;

            var mesh = skinMeshes[entry.SkinMeshIndex];
            int vertexCount = mesh?.VertexPositions?.Length / 3 ?? 0;
            int indexCount = mesh?.Indices?.Length ?? 0;
            if (vertexCount <= 0 || indexCount <= 0)
                return;

            int firstVertex = vertices.Count;
            int firstIndex = indices.Count;
            int boneMatrixOffset = boneMatrices.Count;
            LogNetchBakedEntryDiagnostic(referenceActorRoot, directLeaves, entry, mesh, bindLocalToRoot);
            AppendBakedVertices(mesh, catalog.SkinWeights ?? Array.Empty<ActorSkinWeightDef>(), vertices);
            for (int i = 0; i < indexCount; i++)
                indices.Add(mesh.Indices[i]);

            if (mesh.IsRigid != 0)
            {
                int attachBone = entry.AttachBoneIndex;
                Matrix4x4 attach = (uint)attachBone < (uint)bindLocalToRoot.Length
                    ? bindLocalToRoot[attachBone]
                    : Matrix4x4.identity;
                Matrix4x4 mirror = entry.RigidMirrorX != 0
                    ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))
                    : Matrix4x4.identity;
                Matrix4x4 gts = SourceAffineToUnity(ReadPackedMatrix(mesh.GeometryToSkeletonMatrix, 0, Matrix4x4.identity));
                boneMatrices.Add(ToProceduralGpuMatrix(attach * mirror * gts));
            }
            else
            {
                Matrix4x4 geometryToSkeleton = SourceAffineToUnity(ReadPackedMatrix(mesh.GeometryToSkeletonMatrix, 0, Matrix4x4.identity));
                int boneCount = mesh.BoneIndices?.Length ?? 0;
                Matrix4x4[] bakedSkinMatrices = referenceActorRoot != null && directLeaves != null
                    ? new Matrix4x4[boneCount]
                    : null;
                for (int i = 0; i < boneCount; i++)
                {
                    int actorBoneIndex = mesh.BoneIndices[i];
                    Matrix4x4 pose = (uint)actorBoneIndex < (uint)bindLocalToRoot.Length
                        ? bindLocalToRoot[actorBoneIndex]
                        : Matrix4x4.identity;
                    Matrix4x4 bindPose = SourceAffineToUnity(ReadPackedMatrix(mesh.BindPoseMatrices, i * 16, Matrix4x4.identity));
                    Matrix4x4 skinMatrix = geometryToSkeleton * pose * bindPose;
                    if (bakedSkinMatrices != null)
                        bakedSkinMatrices[i] = skinMatrix;
                    boneMatrices.Add(ToProceduralGpuMatrix(skinMatrix));
                }

                if (boneCount <= 0)
                    boneMatrices.Add(ToProceduralGpuMatrix(Matrix4x4.identity));

                if (bakedSkinMatrices != null)
                    LogNetchDirectBakedSkinDiagnostic(referenceActorRoot, directLeaves, mesh, bakedSkinMatrices);
            }

            int drawIndex = draws.Count;
            draws.Add(new ActorProceduralDrawGpu
            {
                FirstIndex = firstIndex,
                FirstVertex = firstVertex,
                BoneMatrixOffset = boneMatrixOffset,
                BoneMatrixSource = 0,
                TextureSlice = 0,
                LocalToWorld = ToProceduralGpuMatrix(bakedActorRoot.localToWorldMatrix),
            });
            AppendManualPreviewBatch(batches, drawIndex, indexCount);
        }

        static void AppendBakedVertices(
            ActorSkinMeshDef mesh,
            ActorSkinWeightDef[] skinWeights,
            List<ActorProceduralVertexGpu> vertices)
        {
            int vertexCount = mesh.VertexPositions?.Length / 3 ?? 0;
            ComputePreviewInfluences(mesh, skinWeights, vertexCount, out var boneIndices, out var weights);

            for (int v = 0; v < vertexCount; v++)
            {
                vertices.Add(new ActorProceduralVertexGpu
                {
                    Position = ReadPackedVector3(mesh.VertexPositions, v * 3, Vector3.zero),
                    Normal = ReadPackedVector3(mesh.VertexNormals, v * 3, Vector3.up),
                    Uv = ReadPackedVector2(mesh.VertexUvs, v * 2),
                    BoneIndices0 = new int4(boneIndices[v, 0], boneIndices[v, 1], boneIndices[v, 2], boneIndices[v, 3]),
                    BoneIndices1 = new int4(boneIndices[v, 4], boneIndices[v, 5], boneIndices[v, 6], boneIndices[v, 7]),
                    Weights0 = new float4(weights[v, 0], weights[v, 1], weights[v, 2], weights[v, 3]),
                    Weights1 = new float4(weights[v, 4], weights[v, 5], weights[v, 6], weights[v, 7]),
                });
            }
        }

        static Matrix4x4[] BuildBakedBindLocalToRoot(ActorSkeletonBoneDef[] bones)
        {
            bones ??= Array.Empty<ActorSkeletonBoneDef>();
            var matrices = new Matrix4x4[bones.Length];
            var sourceMatrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                Matrix4x4 local = ReadPackedMatrix(bone.BindLocalMatrix, 0, BuildDecomposedBakedBindLocalMatrix(bone));
                Matrix4x4 root = ReadPackedMatrix(bone.BindLocalToRootMatrix, 0, default);
                Matrix4x4 sourceLocalToRoot = root != default
                    ? root
                    : bone.ParentIndex >= 0 && bone.ParentIndex < i
                        ? sourceMatrices[bone.ParentIndex] * local
                        : local;
                sourceMatrices[i] = sourceLocalToRoot;
                matrices[i] = SourceAffineToUnity(sourceLocalToRoot);
            }

            return matrices;
        }

        static Matrix4x4 BuildDecomposedBakedBindLocalMatrix(ActorSkeletonBoneDef bone)
        {
            Quaternion rotation = new(bone.RotX, bone.RotY, bone.RotZ, bone.RotW);
            float lengthSq = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            rotation = lengthSq > 0.000001f ? rotation.normalized : Quaternion.identity;
            float scale = bone.Scale <= 0f ? 1f : bone.Scale;
            return Matrix4x4.TRS(
                new Vector3(bone.PosX, bone.PosY, bone.PosZ),
                rotation,
                Vector3.one * scale);
        }

        static void ComputePreviewInfluences(
            ActorSkinMeshDef mesh,
            ActorSkinWeightDef[] skinWeights,
            int vertexCount,
            out int[,] boneIndices,
            out float[,] weights)
        {
            boneIndices = new int[vertexCount, MaxPreviewSkinInfluences];
            weights = new float[vertexCount, MaxPreviewSkinInfluences];
            for (int v = 0; v < vertexCount; v++)
                for (int i = 0; i < MaxPreviewSkinInfluences; i++)
                    boneIndices[v, i] = -1;

            int firstWeight = mesh.FirstWeightIndex;
            int weightCount = mesh.WeightCount;
            if (firstWeight >= 0 && weightCount > 0)
            {
                int end = Math.Min(skinWeights.Length, firstWeight + weightCount);
                for (int i = firstWeight; i < end; i++)
                {
                    var weight = skinWeights[i];
                    if (weight.VertexIndex >= vertexCount || weight.Weight <= 0f)
                        continue;

                    InsertPreviewInfluence(boneIndices, weights, weight.VertexIndex, weight.BoneIndex, weight.Weight);
                }
            }

            for (int v = 0; v < vertexCount; v++)
                NormalizePreviewInfluences(boneIndices, weights, v);
        }

        static void InsertPreviewInfluence(int[,] boneIndices, float[,] weights, int vertex, int boneIndex, float weight)
        {
            int replace = -1;
            float minWeight = weight;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
            {
                if (weights[vertex, i] <= 0f)
                {
                    replace = i;
                    break;
                }

                if (weights[vertex, i] < minWeight)
                {
                    minWeight = weights[vertex, i];
                    replace = i;
                }
            }

            if (replace < 0)
                return;

            boneIndices[vertex, replace] = boneIndex;
            weights[vertex, replace] = weight;
        }

        static void NormalizePreviewInfluences(int[,] boneIndices, float[,] weights, int vertex)
        {
            float sum = 0f;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
                sum += Mathf.Max(0f, weights[vertex, i]);

            if (sum <= 0.000001f)
            {
                boneIndices[vertex, 0] = 0;
                weights[vertex, 0] = 1f;
                return;
            }

            float inv = 1f / sum;
            for (int i = 0; i < MaxPreviewSkinInfluences; i++)
            {
                weights[vertex, i] = Mathf.Max(0f, weights[vertex, i]) * inv;
                if (weights[vertex, i] <= 0f)
                    boneIndices[vertex, i] = -1;
            }
        }

        static void LogNetchDirectBakedSkinDiagnostic(
            Transform referenceActorRoot,
            List<DirectActorGpuPreviewLeaf> directLeaves,
            ActorSkinMeshDef bakedMesh,
            Matrix4x4[] bakedSkinMatrices)
        {
            if (referenceActorRoot == null || directLeaves == null || bakedMesh == null || bakedMesh.IsRigid != 0)
                return;

            DirectActorGpuPreviewLeaf direct = FindDirectLeaf(directLeaves, bakedMesh, requireSkinned: true);

            int directBones = direct?.Bones?.Length ?? 0;
            int bakedBones = bakedSkinMatrices?.Length ?? 0;
            if (direct == null || directBones == 0 || bakedBones == 0)
            {
                Debug.LogWarning(
                    $"[VVardenfell][NetchSkinDiag] missing-direct model='{bakedMesh.ModelPath}' node='{bakedMesh.NodeName}' " +
                    $"bakedBones={bakedBones} directLeaves={directLeaves.Count}");
                return;
            }

            Matrix4x4 referenceWorldToLocal = referenceActorRoot.worldToLocalMatrix;
            float maxDelta = 0f;
            int maxSlot = -1;
            int slotCount = Mathf.Min(directBones, bakedBones);
            for (int i = 0; i < slotCount; i++)
            {
                Matrix4x4 directSkin = referenceWorldToLocal * direct.Bones[i].localToWorldMatrix * direct.Node.SkinBindPoses[i];
                float delta = MatrixDelta(directSkin, bakedSkinMatrices[i]);
                if (delta > maxDelta)
                {
                    maxDelta = delta;
                    maxSlot = i;
                }
            }

            if (maxDelta <= 0.0005f && directBones == bakedBones)
                return;

            string directBoneName = maxSlot >= 0 && direct.Node.SkinBoneNames != null && maxSlot < direct.Node.SkinBoneNames.Length
                ? direct.Node.SkinBoneNames[maxSlot]
                : string.Empty;
            string bakedBoneName = maxSlot >= 0 && bakedMesh.BoneNames != null && maxSlot < bakedMesh.BoneNames.Length
                ? bakedMesh.BoneNames[maxSlot]
                : string.Empty;
            int bakedTargetBone = maxSlot >= 0 && bakedMesh.BoneIndices != null && maxSlot < bakedMesh.BoneIndices.Length
                ? bakedMesh.BoneIndices[maxSlot]
                : -1;

            Debug.LogWarning(
                $"[VVardenfell][NetchSkinDiag] model='{bakedMesh.ModelPath}' node='{bakedMesh.NodeName}' " +
                $"directBones={directBones} bakedBones={bakedBones} maxSkinMatrixDelta={maxDelta:0.00000000} slot={maxSlot} " +
                $"directBone='{directBoneName}' bakedBone='{bakedBoneName}' bakedTargetBone={bakedTargetBone} " +
                $"vertices={bakedMesh.VertexPositions?.Length / 3 ?? 0} indices={bakedMesh.Indices?.Length ?? 0}");
        }

        static void LogNetchBakedEntryDiagnostic(
            Transform referenceActorRoot,
            List<DirectActorGpuPreviewLeaf> directLeaves,
            ActorVisualRecipeEntryDef entry,
            ActorSkinMeshDef bakedMesh,
            Matrix4x4[] bindLocalToRoot)
        {
            if (referenceActorRoot == null || directLeaves == null || bakedMesh == null)
                return;

            DirectActorGpuPreviewLeaf directAny = FindDirectLeaf(directLeaves, bakedMesh, requireSkinned: false);
            bool directSkinned = directAny?.Bones != null
                && directAny.Bones.Length > 0
                && directAny.Node?.SkinBindPoses != null
                && directAny.Node.SkinBindPoses.Length == directAny.Bones.Length;

            float rigidDelta = 0f;
            if (bakedMesh.IsRigid != 0 && directAny != null)
            {
                Matrix4x4 directLocal = referenceActorRoot.worldToLocalMatrix * directAny.Transform.localToWorldMatrix;
                Matrix4x4 attach = (uint)entry.AttachBoneIndex < (uint)(bindLocalToRoot?.Length ?? 0)
                    ? bindLocalToRoot[entry.AttachBoneIndex]
                    : Matrix4x4.identity;
                Matrix4x4 mirror = entry.RigidMirrorX != 0
                    ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))
                    : Matrix4x4.identity;
                Matrix4x4 gts = SourceAffineToUnity(ReadPackedMatrix(bakedMesh.GeometryToSkeletonMatrix, 0, Matrix4x4.identity));
                rigidDelta = MatrixDelta(directLocal, attach * mirror * gts);
            }

            Debug.LogWarning(
                $"[VVardenfell][NetchEntryDiag] model='{bakedMesh.ModelPath}' node='{bakedMesh.NodeName}' " +
                $"matchedDirect={(directAny != null ? 1 : 0)} directSkinned={(directSkinned ? 1 : 0)} rigid={bakedMesh.IsRigid} " +
                $"attachBone={entry.AttachBoneIndex} mirror={entry.RigidMirrorX} bones={bakedMesh.BoneIndices?.Length ?? 0} " +
                $"firstBone={FirstBoneIndex(bakedMesh)} rigidMatrixDelta={rigidDelta:0.00000000} " +
                $"verts={bakedMesh.VertexPositions?.Length / 3 ?? 0} indices={bakedMesh.Indices?.Length ?? 0} " +
                $"boundsCenter=({bakedMesh.BoundsCenterX:0.###},{bakedMesh.BoundsCenterY:0.###},{bakedMesh.BoundsCenterZ:0.###}) " +
                $"boundsExtents=({bakedMesh.BoundsExtentsX:0.###},{bakedMesh.BoundsExtentsY:0.###},{bakedMesh.BoundsExtentsZ:0.###})");
        }

        static DirectActorGpuPreviewLeaf FindDirectLeaf(
            List<DirectActorGpuPreviewLeaf> directLeaves,
            ActorSkinMeshDef bakedMesh,
            bool requireSkinned)
        {
            if (directLeaves == null || bakedMesh == null)
                return null;

            for (int i = 0; i < directLeaves.Count; i++)
            {
                var leaf = directLeaves[i];
                if (leaf?.Node == null)
                    continue;

                if (requireSkinned
                    && (leaf.Bones == null
                        || leaf.Bones.Length == 0
                        || leaf.Node.SkinBindPoses == null
                        || leaf.Node.SkinBindPoses.Length != leaf.Bones.Length))
                {
                    continue;
                }

                if (!string.Equals(NormalizeModelPath(leaf.ModelPath), NormalizeModelPath(bakedMesh.ModelPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(leaf.Node.Name, bakedMesh.NodeName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(CanonicalDiagnosticName(leaf.Node.Name), CanonicalDiagnosticName(bakedMesh.NodeName), StringComparison.OrdinalIgnoreCase))
                {
                    return leaf;
                }
            }

            return null;
        }

        static int FirstBoneIndex(ActorSkinMeshDef bakedMesh)
            => bakedMesh?.BoneIndices != null && bakedMesh.BoneIndices.Length > 0
                ? bakedMesh.BoneIndices[0]
                : -1;

        static string CanonicalDiagnosticName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim().ToLowerInvariant();
            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");
            return value;
        }

        static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0f;
            max = Mathf.Max(max, Mathf.Abs(a.m00 - b.m00));
            max = Mathf.Max(max, Mathf.Abs(a.m01 - b.m01));
            max = Mathf.Max(max, Mathf.Abs(a.m02 - b.m02));
            max = Mathf.Max(max, Mathf.Abs(a.m03 - b.m03));
            max = Mathf.Max(max, Mathf.Abs(a.m10 - b.m10));
            max = Mathf.Max(max, Mathf.Abs(a.m11 - b.m11));
            max = Mathf.Max(max, Mathf.Abs(a.m12 - b.m12));
            max = Mathf.Max(max, Mathf.Abs(a.m13 - b.m13));
            max = Mathf.Max(max, Mathf.Abs(a.m20 - b.m20));
            max = Mathf.Max(max, Mathf.Abs(a.m21 - b.m21));
            max = Mathf.Max(max, Mathf.Abs(a.m22 - b.m22));
            max = Mathf.Max(max, Mathf.Abs(a.m23 - b.m23));
            max = Mathf.Max(max, Mathf.Abs(a.m30 - b.m30));
            max = Mathf.Max(max, Mathf.Abs(a.m31 - b.m31));
            max = Mathf.Max(max, Mathf.Abs(a.m32 - b.m32));
            max = Mathf.Max(max, Mathf.Abs(a.m33 - b.m33));
            return max;
        }

        static Matrix4x4 ReadPackedMatrix(float[] values, int start, Matrix4x4 fallback)
        {
            if (values == null || start < 0 || start + 15 >= values.Length)
                return fallback;

            var matrix = Matrix4x4.identity;
            matrix.m00 = values[start + 0];
            matrix.m01 = values[start + 1];
            matrix.m02 = values[start + 2];
            matrix.m03 = values[start + 3];
            matrix.m10 = values[start + 4];
            matrix.m11 = values[start + 5];
            matrix.m12 = values[start + 6];
            matrix.m13 = values[start + 7];
            matrix.m20 = values[start + 8];
            matrix.m21 = values[start + 9];
            matrix.m22 = values[start + 10];
            matrix.m23 = values[start + 11];
            matrix.m30 = values[start + 12];
            matrix.m31 = values[start + 13];
            matrix.m32 = values[start + 14];
            matrix.m33 = values[start + 15];
            return matrix;
        }

        static Matrix4x4 SourceAffineToUnity(Matrix4x4 source)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = source.m00;
            matrix.m01 = source.m02;
            matrix.m02 = source.m01;
            matrix.m10 = source.m20;
            matrix.m11 = source.m22;
            matrix.m12 = source.m21;
            matrix.m20 = source.m10;
            matrix.m21 = source.m12;
            matrix.m22 = source.m11;
            Vector3 translation = SourceTranslationToUnity(new Vector3(source.m03, source.m13, source.m23));
            matrix.m03 = translation.x;
            matrix.m13 = translation.y;
            matrix.m23 = translation.z;
            return matrix;
        }

        static Vector3 SourceTranslationToUnity(Vector3 source)
            => new(source.x * WorldScale.MwUnitsToMeters, source.z * WorldScale.MwUnitsToMeters, source.y * WorldScale.MwUnitsToMeters);

        static Vector3 ReadPackedVector3(float[] values, int start, Vector3 fallback)
        {
            if (values == null || start < 0 || start + 2 >= values.Length)
                return fallback;
            return new Vector3(values[start], values[start + 1], values[start + 2]);
        }

        static Vector2 ReadPackedVector2(float[] values, int start)
        {
            if (values == null || start < 0 || start + 1 >= values.Length)
                return Vector2.zero;
            return new Vector2(values[start], values[start + 1]);
        }

        static void AppendManualPreviewBatch(List<ActorProceduralDrawBatch> batches, int drawIndex, int indexCount)
        {
            if (batches.Count > 0)
            {
                var last = batches[batches.Count - 1];
                if (last.IndexCount == indexCount && last.DrawBase + last.DrawCount == drawIndex)
                {
                    last.DrawCount++;
                    batches[batches.Count - 1] = last;
                    return;
                }
            }

            batches.Add(new ActorProceduralDrawBatch
            {
                BucketIndex = 0,
                MaterialIndex = 0,
                DrawBase = drawIndex,
                DrawCount = 1,
                IndexCount = indexCount,
            });
        }

        static ActorProceduralMatrixGpu ToProceduralGpuMatrix(Matrix4x4 matrix)
        {
            return new ActorProceduralMatrixGpu
            {
                Row0 = new float4(matrix.m00, matrix.m01, matrix.m02, matrix.m03),
                Row1 = new float4(matrix.m10, matrix.m11, matrix.m12, matrix.m13),
                Row2 = new float4(matrix.m20, matrix.m21, matrix.m22, matrix.m23),
            };
        }

        static void ApplyNpcLeftBoneMirroring(Dictionary<string, Transform> skeletonNodes)
        {
            if (skeletonNodes == null)
                return;

            ApplyMirrorScale(skeletonNodes, "Left Clavicle");
            ApplyMirrorScale(skeletonNodes, "Left Upper Arm");
            ApplyMirrorScale(skeletonNodes, "Left Forearm");
            ApplyMirrorScale(skeletonNodes, "Left Wrist");
            ApplyMirrorScale(skeletonNodes, "Left Upper Leg");
            ApplyMirrorScale(skeletonNodes, "Left Knee");
            ApplyMirrorScale(skeletonNodes, "Left Ankle");
            ApplyMirrorScale(skeletonNodes, "Left Foot");
        }

        static void ApplyMirrorScale(Dictionary<string, Transform> skeletonNodes, string boneName)
        {
            if (!skeletonNodes.TryGetValue(boneName, out var bone) || bone == null)
                return;

            Vector3 scale = bone.localScale;
            bone.localScale = new Vector3(-Mathf.Abs(scale.x), scale.y, scale.z);
        }

        static Mesh BuildUnityMesh(in NifMeshBuilder.RawBuiltMesh raw, string name)
        {
            if (raw.Vertices == null || raw.Indices == null || raw.Vertices.Length == 0 || raw.Indices.Length == 0)
                return null;

            var mesh = new Mesh { name = name };
            mesh.indexFormat = raw.VertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(raw.Vertices);
            if (raw.HasNormals)
                mesh.SetNormals(raw.Normals);
            if (raw.HasUvs)
                mesh.SetUVs(0, raw.Uvs);
            mesh.SetTriangles(raw.Indices, 0);
            if (!raw.HasNormals)
                mesh.RecalculateNormals();
            mesh.bounds = raw.LocalBounds;
            return mesh;
        }

        static void ApplySourceLocal(Transform target, Matrix4x4 sourceLocalMatrix)
        {
            DecomposeReferenceStyle(sourceLocalMatrix, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }

        static void DecomposeReferenceStyle(Matrix4x4 source, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = new Vector3(source.m03, source.m23, source.m13) * WorldScale.MwUnitsToMeters;

            float sx = new Vector3(source.m00, source.m10, source.m20).magnitude;
            float sy = new Vector3(source.m01, source.m11, source.m21).magnitude;
            float sz = new Vector3(source.m02, source.m12, source.m22).magnitude;
            float uniformScale = (sx + sy + sz) / 3f;
            if (uniformScale <= 0.000001f)
                uniformScale = 1f;
            scale = new Vector3(uniformScale, uniformScale, uniformScale);

            float invScale = 1f / uniformScale;
            float m00 = source.m00 * invScale;
            float m02 = source.m01 * invScale;
            float m01 = source.m02 * invScale;
            float m20 = source.m10 * invScale;
            float m22 = source.m11 * invScale;
            float m21 = source.m12 * invScale;
            float m10 = source.m20 * invScale;
            float m12 = source.m21 * invScale;
            float m11 = source.m22 * invScale;

            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = 0.5f * s,
                    x = (m21 - m12) * recip,
                    y = (m02 - m20) * recip,
                    z = (m10 - m01) * recip,
                };
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m21 - m12) * recip,
                    x = 0.5f * s,
                    y = (m01 + m10) * recip,
                    z = (m02 + m20) * recip,
                };
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m02 - m20) * recip,
                    x = (m01 + m10) * recip,
                    y = 0.5f * s,
                    z = (m12 + m21) * recip,
                };
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11);
                float recip = 0.5f / s;
                rotation = new Quaternion
                {
                    w = (m10 - m01) * recip,
                    x = (m02 + m20) * recip,
                    y = (m12 + m21) * recip,
                    z = 0.5f * s,
                };
            }

            float rotationLengthSq = rotation.x * rotation.x
                + rotation.y * rotation.y
                + rotation.z * rotation.z
                + rotation.w * rotation.w;
            if (rotationLengthSq > 0.000001f)
                rotation = rotation.normalized;
            else
                rotation = Quaternion.identity;
        }

        static void CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "PreviewGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, PreviewGroundY - 0.02f, 0f);
            ground.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
        }

        static void LayoutPreviewRootsFromRenderBounds(Transform actorRoot, Transform gpuActorRoot, Transform bakedActorRoot)
        {
            float spacing = PreviewActorSeparation;
            if (TryGetRendererBounds(actorRoot, out var bounds))
            {
                float width = Mathf.Max(bounds.size.x, 0.1f);
                float padding = Mathf.Max(PreviewActorBoundsPadding, width * 0.15f);
                spacing = Mathf.Max(PreviewActorSeparation, width + padding);
            }

            if (actorRoot != null)
                actorRoot.localPosition = new Vector3(-spacing, PreviewGroundY, 0f);
            if (gpuActorRoot != null)
                gpuActorRoot.localPosition = new Vector3(0f, PreviewGroundY, 0f);
            if (bakedActorRoot != null)
                bakedActorRoot.localPosition = new Vector3(spacing, PreviewGroundY, 0f);
        }

        static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        static PreviewBoundsTarget CreateBoundsTarget(Transform parent, Transform actorRoot, Transform gpuActorRoot, Transform bakedActorRoot)
        {
            if (!TryGetRendererBounds(actorRoot, out var combined))
            {
                var fallback = new GameObject("PreviewBoundsTarget").transform;
                fallback.SetParent(parent, false);
                fallback.localPosition = new Vector3(0f, 1.2f, 0f);
                return new PreviewBoundsTarget
                {
                    Target = fallback,
                    Center = fallback.position,
                    Size = Vector3.one,
                };
            }

            Bounds directBounds = combined;
            Vector3 actorPosition = actorRoot != null ? actorRoot.position : Vector3.zero;
            EncapsulateCopyBounds(ref combined, directBounds, actorPosition, gpuActorRoot);
            EncapsulateCopyBounds(ref combined, directBounds, actorPosition, bakedActorRoot);

            var target = new GameObject("PreviewBoundsTarget").transform;
            target.SetParent(parent, false);
            target.position = combined.center;
            return new PreviewBoundsTarget
            {
                Target = target,
                Center = combined.center,
                Size = combined.size,
            };
        }

        static void EncapsulateCopyBounds(ref Bounds combined, Bounds sourceBounds, Vector3 actorPosition, Transform copyRoot)
        {
            if (copyRoot == null)
                return;

            Bounds copyBounds = sourceBounds;
            copyBounds.center += copyRoot.position - actorPosition;
            combined.Encapsulate(copyBounds);
        }

        static void CreateOrAttachCamera(Transform parent, PreviewBoundsTarget boundsTarget, Dictionary<string, Transform> skeletonNodes)
        {
            Transform focus = boundsTarget.Target;
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Head", out focus);
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Neck", out focus);
            if (focus == null)
                skeletonNodes?.TryGetValue("Bip01 Spine2", out focus);
            if (focus == null)
                focus = parent;

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            GameObject go;
            if (camera != null)
            {
                go = camera.gameObject;
            }
            else
            {
                go = new GameObject("PreviewCamera");
                go.tag = "MainCamera";
                go.transform.SetParent(parent, false);
                camera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 4096f;
            camera.fieldOfView = 50f;

            var orbit = go.GetComponent<DirectActorPreviewOrbitCamera>();
            if (orbit == null)
                orbit = go.AddComponent<DirectActorPreviewOrbitCamera>();
            orbit.Initialize(focus, boundsTarget.Size);
        }

        struct PreviewBoundsTarget
        {
            public Transform Target;
            public Vector3 Center;
            public Vector3 Size;
        }

        sealed class DirectActorGpuPreviewLeaf
        {
            public readonly string ModelPath;
            public readonly ModelPrefabSourceNode Node;
            public readonly Transform Transform;
            public readonly Transform[] Bones;

            public DirectActorGpuPreviewLeaf(string modelPath, ModelPrefabSourceNode node, Transform transform, Transform[] bones)
            {
                ModelPath = modelPath ?? string.Empty;
                Node = node;
                Transform = transform;
                Bones = bones;
            }
        }

        static string ResolveAttachBoneName(ActorVisualPartReference reference)
        {
            return reference switch
            {
                ActorVisualPartReference.Head => "Bip01 Head",
                ActorVisualPartReference.Hair => "Bip01 Head",
                ActorVisualPartReference.Neck => "Bip01 Neck",
                ActorVisualPartReference.Cuirass => "Bip01 Spine1",
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => "Bip01 Pelvis",
                ActorVisualPartReference.RightHand => "Bip01 R Hand",
                ActorVisualPartReference.LeftHand => "Bip01 L Hand",
                ActorVisualPartReference.RightWrist or ActorVisualPartReference.RightForearm => "Bip01 R Forearm",
                ActorVisualPartReference.LeftWrist or ActorVisualPartReference.LeftForearm => "Bip01 L Forearm",
                ActorVisualPartReference.RightUpperarm or ActorVisualPartReference.RightPauldron => "Bip01 R UpperArm",
                ActorVisualPartReference.LeftUpperarm or ActorVisualPartReference.LeftPauldron => "Bip01 L UpperArm",
                ActorVisualPartReference.RightFoot => "Bip01 R Foot",
                ActorVisualPartReference.LeftFoot => "Bip01 L Foot",
                ActorVisualPartReference.RightAnkle or ActorVisualPartReference.RightKnee => "Bip01 R Calf",
                ActorVisualPartReference.LeftAnkle or ActorVisualPartReference.LeftKnee => "Bip01 L Calf",
                ActorVisualPartReference.RightLeg => "Bip01 R Thigh",
                ActorVisualPartReference.LeftLeg => "Bip01 L Thigh",
                ActorVisualPartReference.Tail => "Bip01 Tail",
                _ => string.Empty,
            };
        }

        static string ResolveAttachmentNodeName(ActorVisualPartReference reference)
        {
            return reference switch
            {
                ActorVisualPartReference.Head => "Head",
                ActorVisualPartReference.Hair => "Hair",
                ActorVisualPartReference.Neck => "Neck",
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => "Groin",
                ActorVisualPartReference.RightHand => "Right Hand",
                ActorVisualPartReference.LeftHand => "Left Hand",
                ActorVisualPartReference.RightWrist => "Right Wrist",
                ActorVisualPartReference.LeftWrist => "Left Wrist",
                ActorVisualPartReference.RightForearm => "Right Forearm",
                ActorVisualPartReference.LeftForearm => "Left Forearm",
                ActorVisualPartReference.RightUpperarm => "Right Upper Arm",
                ActorVisualPartReference.LeftUpperarm => "Left Upper Arm",
                ActorVisualPartReference.RightPauldron => "Right Clavicle",
                ActorVisualPartReference.LeftPauldron => "Left Clavicle",
                ActorVisualPartReference.RightFoot => "Right Foot",
                ActorVisualPartReference.LeftFoot => "Left Foot",
                ActorVisualPartReference.RightAnkle => "Right Ankle",
                ActorVisualPartReference.LeftAnkle => "Left Ankle",
                ActorVisualPartReference.RightKnee => "Right Knee",
                ActorVisualPartReference.LeftKnee => "Left Knee",
                ActorVisualPartReference.RightLeg => "Right Upper Leg",
                ActorVisualPartReference.LeftLeg => "Left Upper Leg",
                ActorVisualPartReference.Shield => "Shield Bone",
                ActorVisualPartReference.Weapon => "Weapon Bone",
                ActorVisualPartReference.Tail => "Tail",
                _ => string.Empty,
            };
        }

        static bool ShouldIncludeNodeForPart(ModelPrefabSourceNode node, string[] meshFilters)
        {
            if (node == null || node.Kind != ModelPrefabNodeKind.RenderLeaf)
                return false;
            if (meshFilters == null || meshFilters.Length == 0)
                return true;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (MatchesMeshFilter(node.Name, meshFilters[i]))
                    return true;
            }

            return false;
        }

        static bool MatchesMeshFilter(string nodeName, string meshFilter)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(meshFilter))
                return true;

            if (string.Equals(nodeName, meshFilter, StringComparison.OrdinalIgnoreCase))
                return true;

            if (nodeName.StartsWith("tri ", StringComparison.OrdinalIgnoreCase)
                && nodeName.Substring(4).StartsWith(meshFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedNode = NormalizePartName(nodeName);
            string normalizedFilter = NormalizePartName(meshFilter);
            return normalizedNode.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizePartName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().Replace('_', ' ').Replace('-', ' ');
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            return normalized.ToLowerInvariant();
        }

        static string[] BuildMeshFilters(ActorVisualPartReference reference)
        {
            string primary = reference switch
            {
                ActorVisualPartReference.Head => "head",
                ActorVisualPartReference.Hair => "hair",
                ActorVisualPartReference.Neck => "neck",
                ActorVisualPartReference.Cuirass => "chest",
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => "groin",
                ActorVisualPartReference.RightHand => "right hand",
                ActorVisualPartReference.LeftHand => "left hand",
                ActorVisualPartReference.RightWrist => "right wrist",
                ActorVisualPartReference.LeftWrist => "left wrist",
                ActorVisualPartReference.RightForearm => "right forearm",
                ActorVisualPartReference.LeftForearm => "left forearm",
                ActorVisualPartReference.RightUpperarm => "right upper arm",
                ActorVisualPartReference.LeftUpperarm => "left upper arm",
                ActorVisualPartReference.RightFoot => "right foot",
                ActorVisualPartReference.LeftFoot => "left foot",
                ActorVisualPartReference.RightAnkle => "right ankle",
                ActorVisualPartReference.LeftAnkle => "left ankle",
                ActorVisualPartReference.RightKnee => "right knee",
                ActorVisualPartReference.LeftKnee => "left knee",
                ActorVisualPartReference.RightLeg => "right upper leg",
                ActorVisualPartReference.LeftLeg => "left upper leg",
                ActorVisualPartReference.RightPauldron => "right clavicle",
                ActorVisualPartReference.LeftPauldron => "left clavicle",
                ActorVisualPartReference.Tail => "tail",
                _ => string.Empty,
            };

            string meshPart = GetMeshPart(reference) switch
            {
                ActorBodyPartMeshPart.Head => "head",
                ActorBodyPartMeshPart.Hair => "hair",
                ActorBodyPartMeshPart.Neck => "neck",
                ActorBodyPartMeshPart.Chest => "chest",
                ActorBodyPartMeshPart.Groin => "groin",
                ActorBodyPartMeshPart.Hand => "hand",
                ActorBodyPartMeshPart.Wrist => "wrist",
                ActorBodyPartMeshPart.Forearm => "forearm",
                ActorBodyPartMeshPart.Upperarm => "upper arm",
                ActorBodyPartMeshPart.Foot => "foot",
                ActorBodyPartMeshPart.Ankle => "ankle",
                ActorBodyPartMeshPart.Knee => "knee",
                ActorBodyPartMeshPart.Upperleg => "upper leg",
                ActorBodyPartMeshPart.Clavicle => "clavicle",
                ActorBodyPartMeshPart.Tail => "tail",
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(meshPart)
                || string.Equals(primary, meshPart, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { primary };
            }

            return new[] { primary, meshPart };
        }

        static string ResolveNpcSkeletonModel(bool firstPerson, bool female, bool beast)
        {
            return NormalizeModelPath(firstPerson
                ? beast ? "meshes\\base_animkna.1st.nif"
                    : female ? "meshes\\base_anim_female.1st.nif"
                    : "meshes\\xbase_anim.1st.nif"
                : beast ? "meshes\\base_animkna.nif"
                    : female ? "meshes\\base_anim_female.nif"
                    : "meshes\\base_anim.nif");
        }

        static string ResolveCreaturePreviewModelPath(ActorDef actor, PreviewAssetResolver assets)
        {
            string model = NormalizeModelPath(actor.Model);
            if (string.IsNullOrEmpty(model) || assets == null)
                return model;

            string corrected = BuildPrefixedCreatureModelPath(model);
            string correctedKf = BuildCompanionKfPath(corrected);
            return assets.HasAsset(correctedKf) && assets.HasAsset(corrected)
                ? corrected
                : model;
        }

        static string BuildPrefixedCreatureModelPath(string modelPath)
        {
            string normalized = NormalizeModelPath(modelPath);
            int slash = normalized.LastIndexOf('\\');
            return slash >= 0
                ? normalized.Substring(0, slash + 1) + "x" + normalized.Substring(slash + 1)
                : "x" + normalized;
        }

        static string BuildCompanionKfPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return string.Empty;

            int dot = modelPath.LastIndexOf('.');
            return dot < 0
                ? modelPath + ".kf"
                : modelPath.Substring(0, dot) + ".kf";
        }

        static string NormalizeModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\", StringComparison.Ordinal))
                trimmed = trimmed.Replace("\\\\", "\\", StringComparison.Ordinal);

            return trimmed.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : "meshes\\" + trimmed;
        }

        static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorVisualPartReference partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
        {
            result = default;
            bodyParts ??= Array.Empty<ActorBodyPartDef>();

            ActorBodyPartMeshPart meshPart = GetMeshPart(partReference);
            bool isFirstPersonArmPart = meshPart is ActorBodyPartMeshPart.Hand
                or ActorBodyPartMeshPart.Wrist
                or ActorBodyPartMeshPart.Forearm
                or ActorBodyPartMeshPart.Upperarm;
            int bestScore = int.MaxValue;
            for (int i = 0; i < bodyParts.Length; i++)
            {
                var part = bodyParts[i];
                if (part.Type != ActorBodyPartMeshType.Skin
                    || part.Vampire != 0
                    || part.NotPlayable != 0
                    || part.Part != meshPart
                    || !string.Equals(part.RaceId, raceId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool partFirstPerson = part.FirstPerson != 0;
                bool partFemale = part.Female != 0;
                int score = ResolveNpcRaceBodyPartScore(firstPerson, female, isFirstPersonArmPart, partFirstPerson, partFemale);
                if (score >= bestScore)
                    continue;

                result = part;
                bestScore = score;
                if (score == 0)
                    return true;
            }

            return bestScore < int.MaxValue;
        }

        static int ResolveNpcRaceBodyPartScore(
            bool firstPerson,
            bool female,
            bool isFirstPersonArmPart,
            bool partFirstPerson,
            bool partFemale)
        {
            if (partFirstPerson == firstPerson && partFemale == female)
                return 0;
            if (firstPerson && isFirstPersonArmPart && !partFirstPerson && partFemale == female)
                return 10;
            if (female && partFirstPerson == firstPerson && !partFemale)
                return 20;
            if (firstPerson && isFirstPersonArmPart && female && !partFirstPerson && !partFemale)
                return 30;
            return int.MaxValue;
        }

        static ActorBodyPartMeshPart GetMeshPart(ActorVisualPartReference reference)
        {
            return reference switch
            {
                ActorVisualPartReference.Head => ActorBodyPartMeshPart.Head,
                ActorVisualPartReference.Hair => ActorBodyPartMeshPart.Hair,
                ActorVisualPartReference.Neck => ActorBodyPartMeshPart.Neck,
                ActorVisualPartReference.Cuirass => ActorBodyPartMeshPart.Chest,
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => ActorBodyPartMeshPart.Groin,
                ActorVisualPartReference.RightHand or ActorVisualPartReference.LeftHand => ActorBodyPartMeshPart.Hand,
                ActorVisualPartReference.RightWrist or ActorVisualPartReference.LeftWrist => ActorBodyPartMeshPart.Wrist,
                ActorVisualPartReference.RightForearm or ActorVisualPartReference.LeftForearm => ActorBodyPartMeshPart.Forearm,
                ActorVisualPartReference.RightUpperarm or ActorVisualPartReference.LeftUpperarm => ActorBodyPartMeshPart.Upperarm,
                ActorVisualPartReference.RightFoot or ActorVisualPartReference.LeftFoot => ActorBodyPartMeshPart.Foot,
                ActorVisualPartReference.RightAnkle or ActorVisualPartReference.LeftAnkle => ActorBodyPartMeshPart.Ankle,
                ActorVisualPartReference.RightKnee or ActorVisualPartReference.LeftKnee => ActorBodyPartMeshPart.Knee,
                ActorVisualPartReference.RightLeg or ActorVisualPartReference.LeftLeg => ActorBodyPartMeshPart.Upperleg,
                ActorVisualPartReference.RightPauldron or ActorVisualPartReference.LeftPauldron => ActorBodyPartMeshPart.Clavicle,
                ActorVisualPartReference.Tail => ActorBodyPartMeshPart.Tail,
                _ => ActorBodyPartMeshPart.Head,
            };
        }

        static PreviewRecords LoadPreviewRecords(string installPath, string actorId)
        {
            var races = new Dictionary<string, RaceDef>(StringComparer.OrdinalIgnoreCase);
            var bodyPartsById = new Dictionary<string, ActorBodyPartDef>(StringComparer.OrdinalIgnoreCase);
            var actorsById = new Dictionary<string, ActorDef>(StringComparer.OrdinalIgnoreCase);

            string[] sources = InstalledContentSources.ResolveGameplayRecordSources(installPath);
            if (sources == null || sources.Length == 0)
                throw new InvalidOperationException("No gameplay record sources were found.");

            for (int i = 0; i < sources.Length; i++)
            {
                using var esm = new EsmReader(sources[i]);
                while (esm.ReadRecordHeader(out var record))
                {
                    switch (record.Tag)
                    {
                        case var tag when tag == EsmFourCC.Make('R', 'A', 'C', 'E'):
                            ParseRaceRecord(esm, races);
                            break;
                        case var tag when tag == EsmFourCC.Make('B', 'O', 'D', 'Y'):
                            ParseBodyPartRecord(esm, bodyPartsById);
                            break;
                        case var tag when tag == EsmFourCC.Make('N', 'P', 'C', '_'):
                            ParseNpcRecord(esm, actorsById);
                            break;
                        case var tag when tag == EsmFourCC.Make('C', 'R', 'E', 'A'):
                            ParseCreatureRecord(esm, actorsById);
                            break;
                        default:
                            esm.SkipRecord();
                            break;
                    }
                }
            }

            if (!actorsById.TryGetValue(actorId ?? string.Empty, out var actor))
            {
                foreach (var pair in actorsById)
                {
                    actor = pair.Value;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                throw new InvalidOperationException($"Actor '{actorId}' was not found in the installed content sources.");

            RaceDef race = default;
            if (actor.Kind == ActorDefKind.Npc && !races.TryGetValue(actor.RaceId ?? string.Empty, out race))
                throw new InvalidOperationException($"Race '{actor.RaceId}' for actor '{actor.Id}' was not found.");

            return new PreviewRecords
            {
                Actor = actor,
                Race = race,
                BodyParts = Values(bodyPartsById),
                BodyPartsById = bodyPartsById,
                IsFemale = (actor.Flags & 0x1u) != 0,
                IsBeast = (race.Flags & 0x02) != 0,
            };
        }

        static ActorBodyPartDef[] Values(Dictionary<string, ActorBodyPartDef> map)
        {
            var values = new ActorBodyPartDef[map.Count];
            int index = 0;
            foreach (var pair in map)
                values[index++] = pair.Value;
            return values;
        }

        static void ParseBodyPartRecord(EsmReader esm, Dictionary<string, ActorBodyPartDef> target)
        {
            var typed = new ActorBodyPartDef();
            bool hasBydt = false;
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        typed.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        typed.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        typed.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('B', 'Y', 'D', 'T'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                        {
                            typed.Part = (ActorBodyPartMeshPart)bytes[0];
                            typed.Vampire = bytes[1];
                            typed.Female = (byte)((bytes[2] & 0x01) != 0 ? 1 : 0);
                            typed.NotPlayable = (byte)((bytes[2] & 0x02) != 0 ? 1 : 0);
                            typed.Type = (ActorBodyPartMeshType)bytes[3];
                            hasBydt = true;
                        }
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(typed.Id))
                return;

            if (deleted)
            {
                target.Remove(typed.Id);
                return;
            }

            typed.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('B', 'O', 'D', 'Y'), typed.Id);
            typed.FirstPerson = (byte)(typed.Id.EndsWith("1st", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            if (hasBydt)
                target[typed.Id] = typed;
        }

        static void ParseRaceRecord(EsmReader esm, Dictionary<string, RaceDef> target)
        {
            var race = new RaceDef();
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        race.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        race.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('R', 'A', 'D', 'T'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 140)
                            race.Flags = ReadInt32(bytes, 136);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(race.Id))
                return;

            if (deleted)
            {
                target.Remove(race.Id);
                return;
            }

            race.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('R', 'A', 'C', 'E'), race.Id);
            target[race.Id] = race;
        }

        static void ParseNpcRecord(EsmReader esm, Dictionary<string, ActorDef> target)
        {
            var actor = new ActorDef
            {
                Kind = ActorDefKind.Npc,
                RecordTag = EsmFourCC.Make('N', 'P', 'C', '_'),
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        actor.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        actor.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        actor.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('R', 'N', 'A', 'M'):
                        actor.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('B', 'N', 'A', 'M'):
                        actor.HeadId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('K', 'N', 'A', 'M'):
                        actor.HairId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('F', 'L', 'A', 'G'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            actor.Flags = ReadUInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                return;

            if (deleted)
            {
                target.Remove(actor.Id);
                return;
            }

            actor.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('N', 'P', 'C', '_'), actor.Id);
            target[actor.Id] = actor;
        }

        static void ParseCreatureRecord(EsmReader esm, Dictionary<string, ActorDef> target)
        {
            var actor = new ActorDef
            {
                Kind = ActorDefKind.Creature,
                RecordTag = EsmFourCC.Make('C', 'R', 'E', 'A'),
                Scale = 1f,
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        actor.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        actor.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        actor.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('C', 'N', 'A', 'M'):
                        actor.OriginalId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.Make('F', 'L', 'A', 'G'):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            actor.Flags = ReadUInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
                return;

            if (deleted)
            {
                target.Remove(actor.Id);
                return;
            }

            actor.ContentId = ContentId.FromTagAndId(EsmFourCC.Make('C', 'R', 'E', 'A'), actor.Id);
            target[actor.Id] = actor;
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
            => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

        static short ReadInt16(byte[] bytes, int offset)
            => (short)ReadUInt16(bytes, offset);

        static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));

        static int ReadInt32(byte[] bytes, int offset)
            => unchecked((int)ReadUInt32(bytes, offset));

        sealed class PreviewAssetResolver : IDisposable
        {
            readonly string _dataFilesPath;
            readonly BsaArchive _bsa;
            readonly Dictionary<string, BsaEntry> _entries;
            readonly Dictionary<string, ModelPrefabSource> _modelCache = new(StringComparer.OrdinalIgnoreCase);
            readonly Material _previewMaterial;

            public PreviewAssetResolver(string installPath, Material previewMaterial)
            {
                _previewMaterial = previewMaterial != null
                    ? previewMaterial
                    : throw new InvalidOperationException("Actor Preview Material is required.");
                _dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
                string bsaPath = Path.Combine(_dataFilesPath, "Morrowind.bsa");
                if (!File.Exists(bsaPath))
                    throw new FileNotFoundException("Morrowind.bsa is required for direct actor preview.", bsaPath);

                _bsa = BsaArchive.Open(bsaPath);
                _entries = new Dictionary<string, BsaEntry>(_bsa.Entries.Length, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _bsa.Entries.Length; i++)
                    _entries[_bsa.Entries[i].Name] = _bsa.Entries[i];
            }

            public ModelPrefabSource LoadModelSource(string modelPath)
            {
                string normalized = NormalizeModelPath(modelPath);
                if (_modelCache.TryGetValue(normalized, out var cached))
                    return cached;

                byte[] bytes = ReadAssetBytes(normalized);
                var nif = NifFile.Parse(normalized, bytes);
                var source = NifModelPrefabBuilder.Build(nif);
                _modelCache[normalized] = source;
                return source;
            }

            public Material GetMaterial(ActorVisualPartReference reference)
            {
                return _previewMaterial;
            }

            public bool HasAsset(string modelPath)
            {
                string normalized = NormalizeModelPath(modelPath);
                return File.Exists(Path.Combine(_dataFilesPath, normalized))
                    || _entries.ContainsKey(normalized);
            }

            byte[] ReadAssetBytes(string normalizedPath)
            {
                string fullPath = Path.Combine(_dataFilesPath, normalizedPath);
                if (File.Exists(fullPath))
                    return File.ReadAllBytes(fullPath);
                if (_entries.TryGetValue(normalizedPath, out var entry))
                    return _bsa.Read(entry);

                throw new FileNotFoundException($"Preview asset '{normalizedPath}' was not found in Data Files or Morrowind.bsa.");
            }

            public void Dispose()
            {
                _modelCache.Clear();
                _bsa?.Dispose();
            }
        }

        struct PreviewRecords
        {
            public ActorDef Actor;
            public RaceDef Race;
            public ActorBodyPartDef[] BodyParts;
            public Dictionary<string, ActorBodyPartDef> BodyPartsById;
            public bool IsFemale;
            public bool IsBeast;
        }
    }

    sealed class DirectActorPreviewOrbitCamera : MonoBehaviour
    {
        Transform _target;
        float _yaw = 180f;
        float _pitch = 8f;
        float _distance = 2.1f;
        float _height = 0.05f;

        public void Initialize(Transform target, Vector3 targetSize)
        {
            _target = target;
            float radius = Mathf.Max(targetSize.x, Mathf.Max(targetSize.y, targetSize.z));
            _distance = Mathf.Clamp(radius * 1.5f, 1.2f, 64f);
            _height = Mathf.Clamp(targetSize.y * 0.15f, 0.05f, 6f);
            Snap();
        }

        void LateUpdate()
        {
            if (_target == null)
                return;

            var mouse = Mouse.current;
            if (mouse?.rightButton.isPressed ?? false)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * 0.12f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.08f, -35f, 65f);
            }

            float scroll = mouse?.scroll.ReadValue().y ?? 0f;
            if (Mathf.Abs(scroll) > 0.0001f)
                _distance = Mathf.Clamp(_distance - scroll * 0.0025f, 0.35f, 8f);

            Snap();
        }

        void Snap()
        {
            Vector3 focus = _target.position + Vector3.up * _height;
            Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = focus + orbit * new Vector3(0f, 0f, -_distance);
            transform.rotation = Quaternion.LookRotation((focus - transform.position).normalized, Vector3.up);
        }
    }
}
