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
    public partial class MorrowindMagicProjectileHitSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindProjectileHitEvent>();
            RequireForUpdate<MorrowindScriptRuntimeState>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity);
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
                    TargetPlacedRefId = target == Entity.Null ? 0u : PlacedRefId(target),
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

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
