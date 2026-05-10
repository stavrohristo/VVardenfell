using System;
using Unity.Entities;
using UnityEngine;

namespace VVardenfell.Runtime.Rendering
{
    [RequireComponent(typeof(Camera))]
    public class MainCameraAuthor : MonoBehaviour
    {
        static World s_QueryWorld;
        static EntityQuery s_Query;
        static bool s_QueryCreated;

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld
                ?? throw new InvalidOperationException("[VVardenfell] default ECS world is missing; main camera cannot be registered.");
            EntityManager entityManager = world.EntityManager;
            Camera camera = GetComponent<Camera>();
            EntityQuery query = GetQuery(world);
            if (!query.IsEmptyIgnoreFilter)
            {
                var existing = query.GetSingleton<MainCameraSingleton>();
                Camera existingCamera = existing.Camera;
                if (existingCamera != null && existingCamera != camera)
                    throw new InvalidOperationException("[VVardenfell] multiple main camera authoring components attempted to register MainCameraSingleton.");

                entityManager.SetComponentData(query.GetSingletonEntity(), new MainCameraSingleton
                {
                    Ref = camera,
                });
                return;
            }

            Entity entity = entityManager.CreateEntity(typeof(MainCameraSingleton));
            entityManager.SetComponentData(entity, new MainCameraSingleton
            {
                Ref = camera,
            });
        }

        static EntityQuery GetQuery(World world)
        {
            if (s_QueryCreated && s_QueryWorld == world)
                return s_Query;

            s_QueryWorld = world;
            s_Query = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<MainCameraSingleton>());
            s_QueryCreated = true;
            return s_Query;
        }
    }

    public struct MainCameraSingleton : IComponentData
    {
        public UnityObjectRef<Camera> Ref;
        
        public Camera Camera => Ref.Value;

        public Camera GetRequiredCamera()
        {
            Camera camera = Ref.Value;
            if (camera == null)
                throw new InvalidOperationException("[VVardenfell] MainCameraSingleton exists but its Camera reference is null.");

            return camera;
        }
    }
}
