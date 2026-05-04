using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Projectiles
{
    static class MorrowindProjectileModelUtility
    {
        public static float FixedPhysicalProjectileRadius => WorldScale.MwUnitsToMeters;

        public static float RequireModelCollisionRadius(CacheLoader cache, string modelPath)
        {
            if (cache?.ModelPrefabCatalog?.Records == null)
                throw new InvalidOperationException("[VVardenfell][Projectile] Model prefab catalog is not loaded.");

            string normalized = ActorVisualContentRules.NormalizeModelPath(modelPath);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("[VVardenfell][Projectile] Projectile model path is empty.");

            var records = cache.ModelPrefabCatalog.Records;
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record == null || !string.Equals(ActorVisualContentRules.NormalizeModelPath(record.ModelPath), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (record.CollisionIndex < 0
                    || WorldResources.ColliderBlobs == null
                    || (uint)record.CollisionIndex >= (uint)WorldResources.ColliderBlobs.Length
                    || !WorldResources.ColliderBlobs[record.CollisionIndex].IsCreated)
                {
                    throw new InvalidOperationException($"[VVardenfell][Projectile] Projectile model '{normalized}' has no baked collision bounds; rebake required.");
                }

                BlobAssetReference<Collider> collider = WorldResources.ColliderBlobs[record.CollisionIndex];
                var body = new RigidBody
                {
                    Collider = collider,
                    Entity = Unity.Entities.Entity.Null,
                    WorldFromBody = RigidTransform.identity,
                    Scale = 1f,
                };
                Aabb aabb = body.CalculateAabb();
                float radius = math.length((aabb.Max - aabb.Min) * 0.5f) * 0.5f;
                if (radius <= 0f || !math.isfinite(radius))
                    throw new InvalidOperationException($"[VVardenfell][Projectile] Projectile model '{normalized}' produced invalid collision radius {radius}.");
                return radius;
            }

            throw new InvalidOperationException($"[VVardenfell][Projectile] Projectile model '{normalized}' is missing from model prefab cache; rebake required.");
        }
    }
}
