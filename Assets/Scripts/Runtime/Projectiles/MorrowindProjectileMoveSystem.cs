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
    public partial struct MorrowindProjectileMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindProjectile>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            foreach (var (projectile, transform, collider, entity) in
                     SystemAPI.Query<RefRW<MorrowindProjectile>, RefRW<LocalTransform>, RefRO<PhysicsCollider>>()
                         .WithEntityAccess())
            {
                MoveProjectile(ref systemState, dt, ref projectile.ValueRW, ref transform.ValueRW, collider.ValueRO, entity, deferredPhysicsQueueEntity, fixedTick, ref ecb);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        void MoveProjectile(ref SystemState systemState, 
            float dt,
            ref MorrowindProjectile projectile,
            ref LocalTransform transform,
            in PhysicsCollider collider,
            Entity projectileEntity,
            Entity deferredPhysicsQueueEntity,
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
                        systemState.EntityManager,
                        deferredPhysicsQueueEntity,
                        fixedTick,
                        DeferredPhysicsQueryKind.ProjectileSegment,
                        projectile.PendingQuerySequence,
                        DeferredPhysicsQueryUtility.DefaultMaxResultAgeTicks,
                        out var result))
                {
                    projectile.PendingQuerySequence = 0u;
                    projectile.PendingQueryFixedTick = 0u;
                    if (result.Status == DeferredPhysicsQueryStatus.Hit)
                    {
                        EmitHitEvent(ref systemState, projectile, projectileEntity, result, ref ecb);
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
                systemState.EntityManager,
                deferredPhysicsQueueEntity,
                fixedTick,
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

        void EmitHitEvent(ref SystemState systemState, in MorrowindProjectile projectile, Entity projectileEntity, in DeferredPhysicsQueryResult hit, ref EntityCommandBuffer ecb)
        {
            Entity evt = ecb.CreateEntity();
            ecb.AddComponent(evt, new MorrowindProjectileHitEvent
            {
                Projectile = projectileEntity,
                Caster = projectile.Caster,
                Target = hit.HitEntity,
                SourceContent = projectile.SourceContent,
                SourceKind = projectile.SourceKind,
                HitKind = ResolveHitKind(ref systemState, hit.HitEntity),
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
            ecb.AddComponent(request, new MorrowindVfxRemoveRequest
            {
                Owner = projectileEntity,
                EffectId = 0u,
            });
        }

        MorrowindProjectileHitKind ResolveHitKind(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                return MorrowindProjectileHitKind.Geometry;
            if (systemState.EntityManager.HasComponent<MorrowindProjectile>(entity))
                return MorrowindProjectileHitKind.Projectile;
            if (systemState.EntityManager.HasComponent<ActorVitalSet>(entity))
                return MorrowindProjectileHitKind.Actor;
            return MorrowindProjectileHitKind.Object;
        }
    }
}
