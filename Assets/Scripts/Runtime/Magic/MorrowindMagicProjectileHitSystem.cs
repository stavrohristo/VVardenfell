using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Projectiles;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindProjectileSystemGroup))]
    [UpdateAfter(typeof(MorrowindProjectileMoveSystem))]
    public partial struct MorrowindMagicProjectileHitSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindProjectileHitEvent>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (hit, entity) in SystemAPI.Query<RefRO<MorrowindProjectileHitEvent>>().WithEntityAccess())
            {
                var value = hit.ValueRO;
                if (value.SourceKind != MorrowindProjectileSourceKind.Magic)
                    continue;
                if (value.SpellHandleValue <= 0)
                    throw new System.InvalidOperationException("[VVardenfell][Magic] Magic projectile hit has no spell handle.");

                Entity target = value.HitKind == MorrowindProjectileHitKind.Actor ? value.Target : Entity.Null;
                requests.Add(new ActorSpellCastRequest
                {
                    CasterEntity = value.Caster,
                    TargetEntity = target,
                    TargetPlacedRefId = target == Entity.Null ? 0u : PlacedRefId(ref systemState, target),
                    Spell = new SpellDefHandle { Value = value.SpellHandleValue },
                    Scripted = value.Scripted,
                    AlwaysSucceed = 1,
                    IgnoreReflect = value.IgnoreReflect,
                    IgnoreSpellAbsorption = value.IgnoreSpellAbsorption,
                    ProjectileImpact = 1,
                    HasHitPosition = 1,
                    HitPosition = value.HitPosition,
                });

                ecb.DestroyEntity(entity);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
