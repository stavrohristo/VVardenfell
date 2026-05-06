using System;
using Unity.Entities;
using UnityEngine;

namespace VVardenfell.Runtime.Rendering
{
    public static class MainCameraUtility
    {
        static World s_QueryWorld;
        static EntityQuery s_Query;
        static bool s_QueryCreated;

        public static Camera GetRequiredCamera()
        {
            World world = World.DefaultGameObjectInjectionWorld
                ?? throw new InvalidOperationException("[VVardenfell] default ECS world is missing; main camera cannot be resolved.");
            EntityQuery query = GetQuery(world);
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell] MainCameraSingleton is missing; ensure MainCameraAuthor registered the scene camera.");

            return query.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
        }

        static EntityQuery GetQuery(World world)
        {
            if (s_QueryCreated && s_QueryWorld == world)
                return s_Query;

            s_QueryWorld = world;
            s_Query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MainCameraSingleton>());
            s_QueryCreated = true;
            return s_Query;
        }
    }
}
