using System;
using Unity.Entities;
using UnityEngine;

namespace VVardenfell.Runtime.Rendering
{
    [RequireComponent(typeof(Camera))]
    public class MainCameraAuthor : MonoBehaviour
    {
        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld
                ?? throw new InvalidOperationException("[VVardenfell] default ECS world is missing; main camera cannot be registered.");
            EntityManager entityManager = world.EntityManager;
            Camera camera = GetComponent<Camera>();
            using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<MainCameraSingleton>());
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
            entityManager.SetName(entity, "VVardenfell.MainCamera");
            entityManager.SetComponentData(entity, new MainCameraSingleton
            {
                Ref = camera,
            });
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
