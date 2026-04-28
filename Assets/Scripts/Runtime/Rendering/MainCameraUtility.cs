using System;
using Unity.Entities;
using UnityEngine;

namespace VVardenfell.Runtime.Rendering
{
    public static class MainCameraUtility
    {
        public static Camera GetRequiredCamera()
        {
            World world = World.DefaultGameObjectInjectionWorld
                ?? throw new InvalidOperationException("[VVardenfell] default ECS world is missing; main camera cannot be resolved.");
            EntityManager entityManager = world.EntityManager;
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MainCameraSingleton>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell] MainCameraSingleton is missing; ensure MainCameraAuthor registered the scene camera.");

            return query.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
        }
    }
}
