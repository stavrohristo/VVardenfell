using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Runtime.Interactions;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public struct RuntimeActorColliderResource : IComponentData
    {
        public BlobAssetReference<Collider> ActorCapsuleCollider;
        public BlobAssetReference<Collider> ActorPickCapsuleCollider;

        public static RuntimeActorColliderResource Create()
        {
            const float Radius = 0.35f;
            const float Height = 1.8f;
            var geometry = new CapsuleGeometry
            {
                Vertex0 = new float3(0f, Radius, 0f),
                Vertex1 = new float3(0f, Height - Radius, 0f),
                Radius = Radius,
            };

            return new RuntimeActorColliderResource
            {
                ActorCapsuleCollider = CapsuleCollider.Create(geometry, InteractionCollisionLayers.PlayerBodyFilter),
                ActorPickCapsuleCollider = CapsuleCollider.Create(geometry, InteractionCollisionLayers.InteractionPickFilter),
            };
        }

        public void Dispose()
        {
            if (ActorCapsuleCollider.IsCreated)
                ActorCapsuleCollider.Dispose();
            if (ActorPickCapsuleCollider.IsCreated)
                ActorPickCapsuleCollider.Dispose();
            ActorCapsuleCollider = default;
            ActorPickCapsuleCollider = default;
        }

        public static RuntimeActorColliderResource Require(EntityManager entityManager)
        {
            var query = RuntimeActorColliderResourceQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][ActorCollider] RuntimeActorColliderResource singleton is missing.");

            var resource = entityManager.GetComponentData<RuntimeActorColliderResource>(query.GetSingletonEntity());
            if (!resource.ActorCapsuleCollider.IsCreated || !resource.ActorPickCapsuleCollider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ActorCollider] Runtime actor collider blobs are not loaded.");
            return resource;
        }

        static class RuntimeActorColliderResourceQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeActorColliderResource>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
