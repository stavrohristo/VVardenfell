using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Material = UnityEngine.Material;

namespace VVardenfell.Runtime.Streaming
{
    internal static class WorldModelPrefabUtility
    {
        public static void BuildRuntimeSpawnPrefabLookups(CacheLoader cache)
        {
            var modelDefs = cache?.ModelPrefabCatalog?.Records ?? System.Array.Empty<ModelPrefabDef>();
            var modelLookup = BuildModelDescriptorLookup(modelDefs);

            var contentBlob = cache?.ContentBlob ?? default;
            if (!contentBlob.IsCreated)
            {
                WorldResources.SpawnableCreaturePrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableItemPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                WorldResources.SpawnableLightPrefabs = System.Array.Empty<WorldResources.RuntimeSpawnPrefabDescriptor>();
                return;
            }

            ref RuntimeContentBlob content = ref contentBlob.Value;
            var creatures = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Actors.Length];
            for (int i = 0; i < creatures.Length; i++)
            {
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, ActorDefHandle.FromIndex(i));
                if (actor.Kind != ActorDefKind.Creature)
                    continue;

                creatures[i] = ResolveModelDescriptor(modelLookup, actor.Model.ToString());
            }

            var items = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref content, ItemDefHandle.FromIndex(i));
                items[i] = ResolveModelDescriptor(modelLookup, item.Model.ToString());
            }

            var lights = new WorldResources.RuntimeSpawnPrefabDescriptor[content.Lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                ref RuntimeLightDefBlob light = ref RuntimeContentBlobUtility.Get(ref content, LightDefHandle.FromIndex(i));
                lights[i] = ResolveModelDescriptor(modelLookup, light.Model.ToString());
            }

            WorldResources.SpawnableCreaturePrefabs = creatures;
            WorldResources.SpawnableItemPrefabs = items;
            WorldResources.SpawnableLightPrefabs = lights;
        }

        internal static Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> BuildModelDescriptorLookup(ModelPrefabDef[] modelDefs)
        {
            var lookup = new Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor>(
                modelDefs?.Length ?? 0,
                System.StringComparer.OrdinalIgnoreCase);
            if (modelDefs == null)
                return lookup;

            for (int i = 0; i < modelDefs.Length; i++)
            {
                var def = modelDefs[i];
                if (def == null || string.IsNullOrWhiteSpace(def.ModelPath))
                    continue;

                lookup[NormalizeContentModelPath(def.ModelPath)] = new WorldResources.RuntimeSpawnPrefabDescriptor
                {
                    ModelPrefabIndex = i,
                    CollisionIndex = def.CollisionIndex,
                    Supported = 1,
                };
            }

            return lookup;
        }

        internal static bool TryResolveModelDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath,
            out WorldResources.RuntimeSpawnPrefabDescriptor descriptor)
        {
            descriptor = ResolveModelDescriptor(modelLookup, modelPath);
            return descriptor.IsSupported;
        }

        static WorldResources.RuntimeSpawnPrefabDescriptor ResolveModelDescriptor(
            Dictionary<string, WorldResources.RuntimeSpawnPrefabDescriptor> modelLookup,
            string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return default;

            string normalizedPath = NormalizeContentModelPath(modelPath);
            return modelLookup.TryGetValue(normalizedPath, out var descriptor) ? descriptor : default;
        }

        internal static string NormalizeContentModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\"))
                trimmed = trimmed.Replace("\\\\", "\\");
            if (trimmed.StartsWith("meshes\\", System.StringComparison.OrdinalIgnoreCase))
                return trimmed;

            return $"meshes\\{trimmed}";
        }
    }
}
