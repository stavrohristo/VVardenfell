using System;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class MainLightAuthoring : MonoBehaviour
    {
        [SerializeField] Light _light;


        void Reset()
        {
            _light = GetComponent<Light>();
        }

        private void Start()
        {
            if (_light == null)
            {
                enabled = false;
                throw new Exception($"Main Light Authoring has no light");
            }

            if (TryPublish())
                Destroy(this);
        }

        private void Update()
        {
            if (TryPublish())
                Destroy(this);
        }

        private bool TryPublish()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;

            var entityManager = world.EntityManager;
            EntityQuery query = MainLightQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                var entity = entityManager.CreateSingleton(new MainLightSingleton
                {
                    Value = _light,
                });
                entityManager.SetName(entity, "VVardenfell.MainLight");
                return true;
            }

            entityManager.SetComponentData(query.GetSingletonEntity(), new MainLightSingleton
            {
                Value = _light,
            });
            return true;
        }

        static class MainLightQueryCache
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
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MainLightSingleton>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
