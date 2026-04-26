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
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MainLightSingleton>());
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
    }
}
