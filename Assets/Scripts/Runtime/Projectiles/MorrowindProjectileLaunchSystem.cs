using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Vfx;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Projectiles
{
    [UpdateInGroup(typeof(MorrowindProjectileSystemGroup), OrderFirst = true)]
    public partial class MorrowindProjectileLaunchSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindProjectileLaunchRequest>();
        }

        protected override void OnUpdate()
        {
            CacheLoader cache = WorldResources.Cache
                ?? throw new InvalidOperationException("[VVardenfell][Projectile] Runtime cache is not loaded.");

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, requestEntity) in
                     SystemAPI.Query<RefRO<MorrowindProjectileLaunchRequest>>()
                         .WithEntityAccess())
            {
                Launch(cache, request.ValueRO, ref ecb);
                ecb.DestroyEntity(requestEntity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void Launch(CacheLoader cache, in MorrowindProjectileLaunchRequest request, ref EntityCommandBuffer ecb)
        {
            RequireLaunchRequest(request);

            float3 direction = math.normalizesafe(request.Direction);
            if (math.lengthsq(direction) <= 0f)
                throw new InvalidOperationException("[VVardenfell][Projectile] Launch direction is zero.");
            if (request.Speed <= 0f || !math.isfinite(request.Speed))
                throw new InvalidOperationException($"[VVardenfell][Projectile] Launch speed {request.Speed} is invalid.");

            string modelPath = request.ModelPath.ToString();
            float radius = request.CollisionRadius;
            if (request.UseModelCollisionRadius != 0)
                radius = MorrowindProjectileModelUtility.RequireModelCollisionRadius(cache, modelPath);
            else if (radius <= 0f)
                radius = MorrowindProjectileModelUtility.FixedPhysicalProjectileRadius;

            if (radius <= 0f || !math.isfinite(radius))
                throw new InvalidOperationException($"[VVardenfell][Projectile] Launch radius {radius} is invalid.");

            BlobAssetReference<Collider> collider = SphereCollider.Create(
                new SphereGeometry
                {
                    Center = float3.zero,
                    Radius = radius,
                },
                Unity.Physics.CollisionFilter.Default);

            Entity projectile = ecb.CreateEntity();
            ecb.SetName(projectile, new FixedString64Bytes("VVardenfell.Projectile"));
            ecb.AddComponent(projectile, new LocalTransform
            {
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = 1f,
            });
            ecb.AddComponent(projectile, new MorrowindProjectile
            {
                Caster = request.Caster,
                Target = request.Target,
                SourceContent = request.SourceContent,
                SourceKind = request.SourceKind,
                SpellHandleValue = request.SpellHandleValue,
                EffectId = request.EffectId,
                Velocity = direction * request.Speed,
                AttackStrength = request.AttackStrength,
                Radius = radius,
                ModelPath = request.ModelPath,
                TextureOverridePath = request.TextureOverridePath,
                ExteriorCell = request.ExteriorCell,
                InteriorCellId = request.InteriorCellId,
                InteriorCellHash = request.InteriorCellHash,
                IsInterior = request.IsInterior,
                Scripted = request.Scripted,
                IgnoreReflect = request.IgnoreReflect,
                IgnoreSpellAbsorption = request.IgnoreSpellAbsorption,
            });
            RuntimeColliderPhysicsUtility.QueueAttachNewSource(
                EntityManager,
                ref ecb,
                projectile,
                collider,
                RuntimeColliderKind.Projectile,
                active: true,
                temporary: true);

            if (request.SpawnVisual != 0)
                SpawnProjectileVisual(projectile, request, ref ecb);
        }

        void SpawnProjectileVisual(Entity projectile, in MorrowindProjectileLaunchRequest request, ref EntityCommandBuffer ecb)
        {
            if (request.ModelPath.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][Projectile] Visual projectile launch has no model path.");

            Entity visual = ecb.CreateEntity();
            ecb.SetName(visual, new FixedString64Bytes("VVardenfell.ProjectileVfxSpawn"));
            ecb.AddComponent(visual, new MorrowindVfxSpawnRequest
            {
                ModelPath = request.ModelPath,
                TextureOverridePath = request.TextureOverridePath,
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = 1f,
                FollowEntity = projectile,
                Loop = 1,
                EffectId = 0u,
                ExteriorCell = request.ExteriorCell,
                InteriorCellId = request.InteriorCellId,
                InteriorCellHash = request.InteriorCellHash,
                IsInterior = request.IsInterior,
            });
        }

        void RequireLaunchRequest(in MorrowindProjectileLaunchRequest request)
        {
            if (request.Caster == Entity.Null || !EntityManager.Exists(request.Caster))
                throw new InvalidOperationException("[VVardenfell][Projectile] Launch caster entity is missing.");
            if (!EntityManager.HasComponent<LocalTransform>(request.Caster))
                throw new InvalidOperationException("[VVardenfell][Projectile] Launch caster has no LocalTransform.");
            if (request.SourceKind == MorrowindProjectileSourceKind.None)
                throw new InvalidOperationException("[VVardenfell][Projectile] Launch source kind is None.");
        }
    }
}
