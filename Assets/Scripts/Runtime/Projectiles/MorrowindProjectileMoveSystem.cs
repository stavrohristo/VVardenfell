using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Vfx;

namespace VVardenfell.Runtime.Projectiles
{
    [UpdateInGroup(typeof(MorrowindProjectileSystemGroup))]
    [UpdateAfter(typeof(MorrowindProjectileLaunchSystem))]
    public partial class MorrowindProjectileMoveSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindProjectile>();
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            foreach (var (projectile, transform, collider, entity) in
                     SystemAPI.Query<RefRW<MorrowindProjectile>, RefRW<LocalTransform>, RefRO<PhysicsCollider>>()
                         .WithEntityAccess())
            {
                MoveProjectile(dt, ref projectile.ValueRW, ref transform.ValueRW, collider.ValueRO, entity, fixedTick, ref ecb);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void MoveProjectile(
            float dt,
            ref MorrowindProjectile projectile,
            ref LocalTransform transform,
            in PhysicsCollider collider,
            Entity projectileEntity,
            uint fixedTick,
            ref EntityCommandBuffer ecb)
        {
            if (!collider.Value.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Projectile] Active projectile has no collider.");
            if (!math.all(math.isfinite(projectile.Velocity)))
                throw new InvalidOperationException("[VVardenfell][Projectile] Active projectile velocity is not finite.");

            if (projectile.PendingQuerySequence != 0u)
            {
                if (DeferredPhysicsQueryUtility.TryGetResultBySequence(
                        EntityManager,
                        DeferredPhysicsQueryKind.ProjectileSegment,
                        projectile.PendingQuerySequence,
                        DeferredPhysicsQueryUtility.DefaultMaxResultAgeTicks,
                        out var result))
                {
                    projectile.PendingQuerySequence = 0u;
                    projectile.PendingQueryFixedTick = 0u;
                    if (result.Status == DeferredPhysicsQueryStatus.Hit)
                    {
                        EmitHitEvent(projectile, projectileEntity, result, ref ecb);
                        EmitVisualRemove(projectileEntity, ref ecb);
                        ecb.DestroyEntity(projectileEntity);
                        return;
                    }
                }
                else if (fixedTick <= projectile.PendingQueryFixedTick + DeferredPhysicsQueryUtility.DefaultMaxResultAgeTicks)
                {
                    return;
                }
                else
                {
                    projectile.PendingQuerySequence = 0u;
                    projectile.PendingQueryFixedTick = 0u;
                }
            }

            float3 start = transform.Position;
            float3 end = start + projectile.Velocity * dt;
            if (math.distancesq(start, end) <= 1e-8f)
                return;

            projectile.PendingQuerySequence = DeferredPhysicsQueryUtility.EnqueueColliderCast(
                EntityManager,
                DeferredPhysicsQueryKind.ProjectileSegment,
                projectileEntity,
                projectile.Target,
                projectile.Caster,
                collider.Value,
                start,
                end,
                transform.Rotation);
            projectile.PendingQueryFixedTick = fixedTick;
            transform.Position = end;
        }

        void EmitHitEvent(in MorrowindProjectile projectile, Entity projectileEntity, in DeferredPhysicsQueryResult hit, ref EntityCommandBuffer ecb)
        {
            Entity evt = ecb.CreateEntity();
            ecb.SetName(evt, new FixedString64Bytes("VVardenfell.ProjectileHitEvent"));
            ecb.AddComponent(evt, new MorrowindProjectileHitEvent
            {
                Projectile = projectileEntity,
                Caster = projectile.Caster,
                Target = hit.HitEntity,
                SourceContent = projectile.SourceContent,
                SourceKind = projectile.SourceKind,
                HitKind = ResolveHitKind(hit.HitEntity),
                SpellHandleValue = projectile.SpellHandleValue,
                EffectId = projectile.EffectId,
                AttackStrength = projectile.AttackStrength,
                HitPosition = hit.Position,
                HitNormal = hit.Normal,
                ModelPrefabIndex = projectile.ModelPrefabIndex,
                ModelPathHash = projectile.ModelPathHash,
                TextureOverridePathHash = projectile.TextureOverridePathHash,
                Scripted = projectile.Scripted,
                IgnoreReflect = projectile.IgnoreReflect,
                IgnoreSpellAbsorption = projectile.IgnoreSpellAbsorption,
            });
        }

        static void EmitVisualRemove(Entity projectileEntity, ref EntityCommandBuffer ecb)
        {
            Entity request = ecb.CreateEntity();
            ecb.SetName(request, new FixedString64Bytes("VVardenfell.ProjectileVfxRemove"));
            ecb.AddComponent(request, new MorrowindVfxRemoveRequest
            {
                Owner = projectileEntity,
                EffectId = 0u,
            });
        }

        MorrowindProjectileHitKind ResolveHitKind(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return MorrowindProjectileHitKind.Geometry;
            if (EntityManager.HasComponent<MorrowindProjectile>(entity))
                return MorrowindProjectileHitKind.Projectile;
            if (EntityManager.HasComponent<ActorVitalSet>(entity))
                return MorrowindProjectileHitKind.Actor;
            return MorrowindProjectileHitKind.Object;
        }
    }
}
