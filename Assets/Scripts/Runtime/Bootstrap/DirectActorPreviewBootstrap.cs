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
    static partial class DirectActorPreviewBootstrap
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
            using var assets = new DirectActorPreviewAssetResolver(config.InstallPath, previewMaterial);
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


        static GameObject BuildPreviewScene(PreviewRecords records, DirectActorPreviewAssetResolver assets, CacheLoader cache)
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
            var racePartTable = ActorVisualContentRules.BuildNpcRaceBodyPartTable(
                records.BodyParts,
                records.Actor.RaceId,
                records.IsFemale,
                PreviewFirstPerson,
                werewolf: false);

            for (int i = 0; i < PreviewBodyParts.Length; i++)
            {
                var reference = PreviewBodyParts[i];
                if (reference == ActorVisualPartReference.Tail && !records.IsBeast)
                    continue;

                var bodyPart = racePartTable[(int)reference];
                if (string.IsNullOrWhiteSpace(bodyPart.Id))
                {
                    continue;
                }

                AttachBodyPart(records, assets, actorRoot, skeletonNodes, bodyPart, reference, gpuLeaves, ref totalRenderers);
            }

            AttachPreviewAnimation(cache, actorRoot, skeletonNodes, records.Actor.Id, PreviewFirstPerson);
            LayoutPreviewRootsFromRenderBounds(actorRoot, gpuActorRoot, bakedActorRoot);
            BuildProceduralGpuPreview(actorRoot, gpuActorRoot, bakedActorRoot, gpuLeaves, cache, records.Actor.Id);
            CreateGround(root.transform);
            var boundsTarget = CreateBoundsTarget(root.transform, actorRoot, gpuActorRoot, bakedActorRoot);
            CreateOrAttachCamera(root.transform, boundsTarget, skeletonNodes);
            return root;
        }


        static GameObject BuildCreaturePreviewScene(PreviewRecords records, DirectActorPreviewAssetResolver assets, CacheLoader cache)
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
            var skeletonNodes = InstantiateModelSource(
                source,
                actorRoot,
                includeRenderLeaf: _ => true,
                resolveMaterial: _ => assets.GetMaterial(ActorVisualPartReference.Cuirass),
                mergeByName: null,
                out _,
                gpuLeaves,
                modelPath);

            AttachPreviewAnimation(cache, actorRoot, skeletonNodes, records.Actor.Id, PreviewFirstPerson);
            LayoutPreviewRootsFromRenderBounds(actorRoot, gpuActorRoot, bakedActorRoot);
            BuildProceduralGpuPreview(actorRoot, gpuActorRoot, bakedActorRoot, gpuLeaves, cache, records.Actor.Id);
            CreateGround(root.transform);
            var boundsTarget = CreateBoundsTarget(root.transform, actorRoot, gpuActorRoot, bakedActorRoot);
            CreateOrAttachCamera(root.transform, boundsTarget, null);
            return root;
        }


        static void AddExplicitNpcPart(
            PreviewRecords records,
            DirectActorPreviewAssetResolver assets,
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
            DirectActorPreviewAssetResolver assets,
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
            bool filterSkinnedLeaves = SourceHasSkinnedRenderLeaves(source);
            InstantiateModelSource(
                source,
                attachmentParent,
                includeRenderLeaf: node => ShouldIncludeNodeForPart(source, node, meshFilters, filterSkinnedLeaves),
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
                if (TryGetSkeletonNodeByPolicy(skeletonNodes, ActorVisualPartReference.Hair, out var head))
                    return head;
                return actorRoot;
            }

            if (TryGetSkeletonNodeByPolicy(skeletonNodes, reference, out var attachment))
                return attachment;

            return actorRoot;
        }


        static bool TryGetSkeletonNodeByPolicy(
            Dictionary<string, Transform> skeletonNodes,
            ActorVisualPartReference reference,
            out Transform node)
        {
            node = null;
            string openMwName = ActorVisualMappingPolicy.GetBoneName(reference);
            if (TryGetSkeletonNode(skeletonNodes, openMwName, out node))
                return true;

            string[] aliases = ActorVisualMappingPolicy.GetBoneAliases(reference);
            for (int i = 0; i < aliases.Length; i++)
            {
                if (TryGetSkeletonNode(skeletonNodes, aliases[i], out node))
                    return true;
            }

            string canonical = ActorVisualMappingPolicy.CanonicalizeBoneName(openMwName);
            if (string.IsNullOrEmpty(canonical) || skeletonNodes == null)
                return false;

            foreach (var pair in skeletonNodes)
            {
                if (pair.Value != null
                    && string.Equals(
                        ActorVisualMappingPolicy.CanonicalizeBoneName(pair.Key),
                        canonical,
                        StringComparison.Ordinal))
                {
                    node = pair.Value;
                    return true;
                }
            }

            return false;
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
            var directSkinMatrices = new Dictionary<string, Matrix4x4[]>(StringComparer.OrdinalIgnoreCase);
            var directRigidMatrices = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);

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
                    var leafSkinMatrices = new Matrix4x4[leaf.Bones.Length];
                    for (int b = 0; b < leaf.Bones.Length; b++)
                    {
                        Matrix4x4 skinMatrix = referenceWorldToLocal * leaf.Bones[b].localToWorldMatrix * leaf.Node.SkinBindPoses[b];
                        leafSkinMatrices[b] = skinMatrix;
                        boneMatrices.Add(ToProceduralGpuMatrix(skinMatrix));
                    }

                    directSkinMatrices[BuildDirectPreviewLeafKey(leaf.ModelPath, leaf.Node)] = leafSkinMatrices;
                }
                else
                {
                    Matrix4x4 directLocalMatrix = referenceWorldToLocal * leaf.Transform.localToWorldMatrix;
                    directRigidMatrices[BuildDirectPreviewLeafKey(leaf.ModelPath, leaf.Node)] = directLocalMatrix;
                    drawLocalToWorld = gpuLocalToWorld * directLocalMatrix;
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
                bakedActorRoot,
                vertices,
                indices,
                boneMatrices,
                draws,
                batches,
                directSkinMatrices,
                directRigidMatrices);

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
            Transform bakedActorRoot,
            List<ActorProceduralVertexGpu> vertices,
            List<int> indices,
            List<ActorProceduralMatrixGpu> boneMatrices,
            List<ActorProceduralDrawGpu> draws,
            List<ActorProceduralDrawBatch> batches,
            Dictionary<string, Matrix4x4[]> directSkinMatrices,
            Dictionary<string, Matrix4x4> directRigidMatrices)
        {
            if (cache?.ActorAnimationCatalog == null || cache.ContentDatabase == null || bakedActorRoot == null)
                return;

            if (!cache.ContentDatabase.TryGetActorHandle(actorId, out var actorHandle) || !actorHandle.IsValid)
                return;

            ref readonly var actor = ref cache.ContentDatabase.Get(actorHandle);
            if (!cache.TryGetActorVisualRecipe(actor.ContentId, PreviewFirstPerson, out var recipe) || recipe == null)
                return;

            var catalog = cache.ActorAnimationCatalog;
            var rigFamilies = catalog.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            if ((uint)recipe.RigFamilyIndex >= (uint)rigFamilies.Length)
                return;

            int skeletonIndex = rigFamilies[recipe.RigFamilyIndex]?.SkeletonIndex ?? -1;
            var skeletons = catalog.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            if ((uint)skeletonIndex >= (uint)skeletons.Length)
                return;

            Matrix4x4[] bindLocalToRoot = BuildBakedBindLocalToRoot(skeletons[skeletonIndex]?.Bones);
            var entries = catalog.ActorVisualRecipeEntries ?? Array.Empty<ActorVisualRecipeEntryDef>();
            int entryEnd = Math.Min(entries.Length, recipe.FirstEntryIndex + recipe.EntryCount);

            for (int entryIndex = recipe.FirstEntryIndex; entryIndex >= 0 && entryIndex < entryEnd; entryIndex++)
            {
                AppendBakedSkinMesh(
                    catalog,
                    entries[entryIndex],
                    bakedActorRoot,
                    bindLocalToRoot,
                    vertices,
                    indices,
                    boneMatrices,
                    draws,
                    batches,
                    directSkinMatrices,
                    directRigidMatrices);
            }
        }


        static void AppendBakedSkinMesh(
            ActorAnimationCatalogData catalog,
            ActorVisualRecipeEntryDef entry,
            Transform bakedActorRoot,
            Matrix4x4[] bindLocalToRoot,
            List<ActorProceduralVertexGpu> vertices,
            List<int> indices,
            List<ActorProceduralMatrixGpu> boneMatrices,
            List<ActorProceduralDrawGpu> draws,
            List<ActorProceduralDrawBatch> batches,
            Dictionary<string, Matrix4x4[]> directSkinMatrices,
            Dictionary<string, Matrix4x4> directRigidMatrices)
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
                Matrix4x4 rigidMatrix = attach * mirror * gts;
                boneMatrices.Add(ToProceduralGpuMatrix(rigidMatrix));
                LogRigidRigAssemblyDiag(mesh, rigidMatrix, directRigidMatrices);
            }
            else
            {
                Matrix4x4 geometryToSkeleton = SourceAffineToUnity(ReadPackedMatrix(mesh.GeometryToSkeletonMatrix, 0, Matrix4x4.identity));
                int boneCount = mesh.BoneIndices?.Length ?? 0;
                Matrix4x4[] bakedSkinMatrices = ShouldLogRigAssemblyDiag(mesh) ? new Matrix4x4[boneCount] : null;
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

                LogRigAssemblyDiag(mesh, geometryToSkeleton, bakedSkinMatrices, directSkinMatrices);
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


        static string BuildDirectPreviewLeafKey(string modelPath, ModelPrefabSourceNode node)
        {
            string model = NormalizeModelPath(modelPath);
            return $"{model}|{node?.SourceRecordIndex ?? -1}|{node?.Name ?? string.Empty}";
        }


        static string BuildBakedPreviewLeafKey(ActorSkinMeshDef mesh)
        {
            string model = NormalizeModelPath(mesh?.ModelPath);
            return $"{model}|{mesh?.SourceRecordIndex ?? -1}|{mesh?.NodeName ?? string.Empty}";
        }


        static bool ShouldLogRigAssemblyDiag(ActorSkinMeshDef mesh)
        {
            string name = mesh?.NodeName ?? string.Empty;
            return name.Contains("chest", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("hand", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("tail", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("tentacle", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("append", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("netch", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("body", StringComparison.OrdinalIgnoreCase)
                   || (mesh?.RigAssemblyKind == ActorRigAssemblyKind.FullModel
                       && name.Contains("leg", StringComparison.OrdinalIgnoreCase));
        }


        static void LogRigAssemblyDiag(
            ActorSkinMeshDef mesh,
            Matrix4x4 geometryToSkeleton,
            Matrix4x4[] bakedSkinMatrices,
            Dictionary<string, Matrix4x4[]> directSkinMatrices)
        {
            if (!ShouldLogRigAssemblyDiag(mesh) || bakedSkinMatrices == null || directSkinMatrices == null)
                return;

            string key = BuildBakedPreviewLeafKey(mesh);
            if (!directSkinMatrices.TryGetValue(key, out var directMatrices) || directMatrices == null)
                return;

            int count = Math.Min(bakedSkinMatrices.Length, directMatrices.Length);
            float maxSkinDelta = 0f;
            for (int i = 0; i < count; i++)
                maxSkinDelta = Mathf.Max(maxSkinDelta, MaxMatrixDelta(bakedSkinMatrices[i], directMatrices[i]));

            float geometryDelta = MaxMatrixDelta(geometryToSkeleton, Matrix4x4.identity);
            if (maxSkinDelta <= 0.0001f && geometryDelta <= 0.0001f)
                return;

            Debug.LogWarning(
                $"[VVardenfell][ActorPreviewRigAssemblyDiag] model='{mesh.ModelPath}' node='{mesh.NodeName}' " +
                $"sourceRecord={mesh.SourceRecordIndex} kind={mesh.RigAssemblyKind} sourceGraph={mesh.SourceGraphNodeIndex} " +
                $"skinRootSourceRecord={mesh.SkinRootSourceRecordIndex} skinRootGraph={mesh.SkinRootGraphNodeIndex} copiedRootGraph={mesh.CopiedRigRootGraphNodeIndex} " +
                $"insertedParentGraph={mesh.InsertedParentGraphNodeIndex} cancelledGraph={mesh.CancelledTransformGraphNodeIndex} " +
                $"sourceSkeletonRootGraph={mesh.SourceSkeletonRootGraphNodeIndex} targetBone={mesh.TargetBoneIndex} " +
                $"bones baked={bakedSkinMatrices.Length} direct={directMatrices.Length} " +
                $"geometryToSkeletonDelta={geometryDelta:0.00000000} finalSkinDelta={maxSkinDelta:0.00000000} " +
                $"gts={FormatMatrixBrief(geometryToSkeleton)} baked0={FormatMatrixBrief(count > 0 ? bakedSkinMatrices[0] : Matrix4x4.identity)} " +
                $"direct0={FormatMatrixBrief(count > 0 ? directMatrices[0] : Matrix4x4.identity)}");
        }


        static void LogRigidRigAssemblyDiag(
            ActorSkinMeshDef mesh,
            Matrix4x4 bakedRigidMatrix,
            Dictionary<string, Matrix4x4> directRigidMatrices)
        {
            if (!ShouldLogRigAssemblyDiag(mesh) || directRigidMatrices == null)
                return;

            string key = BuildBakedPreviewLeafKey(mesh);
            if (!directRigidMatrices.TryGetValue(key, out var directRigidMatrix))
                return;

            float matrixDelta = MaxMatrixDelta(bakedRigidMatrix, directRigidMatrix);
            if (matrixDelta <= 0.0001f)
                return;

            Debug.LogWarning(
                $"[VVardenfell][ActorPreviewRigidAssemblyDiag] model='{mesh.ModelPath}' node='{mesh.NodeName}' " +
                $"sourceRecord={mesh.SourceRecordIndex} kind={mesh.RigAssemblyKind} sourceGraph={mesh.SourceGraphNodeIndex} " +
                $"targetBone={mesh.TargetBoneIndex} geometryToSkeletonSourceRecord={mesh.SourceRecordIndex} " +
                $"rigidMatrixDelta={matrixDelta:0.00000000}");
        }


        static float MaxMatrixDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0f;
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    max = Mathf.Max(max, Mathf.Abs(a[row, col] - b[row, col]));
            return max;
        }


        static string FormatMatrixBrief(Matrix4x4 m)
            => $"[{m.m00:0.###},{m.m01:0.###},{m.m02:0.###},{m.m03:0.###};" +
               $"{m.m10:0.###},{m.m11:0.###},{m.m12:0.###},{m.m13:0.###};" +
               $"{m.m20:0.###},{m.m21:0.###},{m.m22:0.###},{m.m23:0.###}]";


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
