using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Streaming
{
    internal static class LightPresentationOffsetUtility
    {
        public static bool TryResolveAttachLightOffset(
            ref RuntimeContentBlob content,
            LightDefHandle handle,
            out float3 localPosition)
        {
            localPosition = default;
            if (!handle.IsValid)
                return false;

            ref RuntimeLightDefBlob light = ref RuntimeContentBlobUtility.Get(ref content, handle);
            if (light.ModelPathHash == 0UL)
                return false;

            EntityQuery query = RuntimeModelPrefabBlobQueryCache.Get(World.DefaultGameObjectInjectionWorld.EntityManager);
            if (!query.TryGetSingleton(out RuntimeModelPrefabBlobReference modelReference)
                || !modelReference.Blob.IsCreated)
                return false;

            ref RuntimeModelPrefabBlob modelBlob = ref modelReference.Blob.Value;
            if (!RuntimeModelPrefabBlobUtility.TryGetIndexByContentModelPathHash(ref modelBlob, light.ModelPathHash, out int modelPrefabIndex))
                return false;

            ref RuntimeModelPrefabDefBlob model = ref RuntimeModelPrefabBlobUtility.RequireRecord(ref modelBlob, modelPrefabIndex);
            if (model.HasAttachLightOffset == 0)
                return false;
            localPosition = model.AttachLightOffset;
            return true;
        }

        static class RuntimeModelPrefabBlobQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                if (s_QueryCreated)
                    s_Query.Dispose();

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeModelPrefabBlobReference>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
