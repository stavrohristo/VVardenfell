using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Projectiles;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Vfx;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ScriptedCastApplySystem : SystemBase
    {
        const int MagicRangeTarget = 2;

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ScriptedCastRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;
            var requestCopy = new NativeArray<ScriptedCastRequest>(requests.Length, Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
                requestCopy[i] = requests[i];
            requests.Clear();

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][Magic] Cast requires active runtime content.");

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < requestCopy.Length; i++)
                ApplyRequest(contentDb, requestCopy[i], lookup, ref ecb);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            requestCopy.Dispose();
        }

        void ApplyRequest(RuntimeContentDatabase contentDb, in ScriptedCastRequest request, in LogicalRefLookup lookup, ref EntityCommandBuffer ecb)
        {
            if (!request.Spell.IsValid
                || contentDb.Data.Spells == null
                || request.Spell.Index < 0
                || request.Spell.Index >= contentDb.Data.Spells.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast references invalid spell handle {request.Spell.Value}.");
            }

            Entity caster = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.CasterEntity, request.CasterPlacedRefId, lookup);
            if (caster == Entity.Null || !EntityManager.Exists(caster))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster ref={request.CasterPlacedRefId} is not loaded.");

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast target ref={request.TargetPlacedRefId} is not loaded.");

            if (!MorrowindActorMagicUtility.CanRepresentScriptedCast(contentDb, request.Spell))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast spell handle {request.Spell.Value} contains effects the scripted cast pipeline cannot represent.");

            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast target ref={request.TargetPlacedRefId} has no active magic effect state.");
            if (!EntityManager.HasComponent<ActorActiveMagicEffectDirty>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast target ref={request.TargetPlacedRefId} has no ActorActiveMagicEffectDirty marker.");

            bool deferredToProjectile = EmitVisuals(contentDb, request.Spell, caster, target, ref ecb);
            if (deferredToProjectile)
                return;

            var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target);
            if (MorrowindActorMagicUtility.ApplyScriptedCast(contentDb, targetEffects, request.Spell))
                EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(target, true);
        }

        bool EmitVisuals(RuntimeContentDatabase contentDb, SpellDefHandle spellHandle, Entity caster, Entity target, ref EntityCommandBuffer ecb)
        {
            ref readonly var spell = ref contentDb.Get(spellHandle);
            if (spell.EffectCount <= 0)
                return false;
            if (!EntityManager.HasComponent<LocalTransform>(caster))
                throw new InvalidOperationException("[VVardenfell][Magic] Cast VFX caster has no LocalTransform.");
            if (!EntityManager.HasComponent<LogicalRefLocation>(caster))
                throw new InvalidOperationException("[VVardenfell][Magic] Cast VFX caster has no LogicalRefLocation.");
            if (!EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException("[VVardenfell][Magic] Cast VFX target has no LocalTransform.");
            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException("[VVardenfell][Magic] Cast VFX target has no LogicalRefLocation.");

            bool deferredToProjectile = false;
            for (int i = 0; i < spell.EffectCount; i++)
            {
                int instanceIndex = spell.EffectStartIndex + i;
                if ((uint)instanceIndex >= (uint)contentDb.Data.MagicEffectInstances.Length)
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' effect instance index {instanceIndex} is invalid.");

                var instance = contentDb.Data.MagicEffectInstances[instanceIndex];
                if (!contentDb.TryGetMagicEffectHandle(instance.EffectId, out var effectHandle) || !effectHandle.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' references missing magic effect {instance.EffectId}.");

                ref readonly var effect = ref contentDb.Get(effectHandle);
                EmitMagicVfxObject(contentDb, effect.CastingObjectId, caster, ref ecb);
                if (instance.Range == MagicRangeTarget && !string.IsNullOrWhiteSpace(effect.BoltObjectId))
                {
                    EmitMagicProjectile(contentDb, spellHandle, instance, effect, caster, target, ref ecb);
                    deferredToProjectile = true;
                    continue;
                }

                EmitMagicVfxObject(contentDb, effect.HitObjectId, target, ref ecb);
                EmitMagicVfxObject(contentDb, effect.AreaObjectId, target, ref ecb);
            }

            return deferredToProjectile;
        }

        void EmitMagicProjectile(
            RuntimeContentDatabase contentDb,
            SpellDefHandle spellHandle,
            in MagicEffectInstanceDef instance,
            in MagicEffectDef effect,
            Entity caster,
            Entity target,
            ref EntityCommandBuffer ecb)
        {
            string model = ResolveMagicVfxModel(contentDb, effect.BoltObjectId);
            float maxSpeed = contentDb.RequireGameSettingFloat("fTargetSpellMaxSpeed");
            float speed = maxSpeed * effect.Speed;
            if (speed <= 0f || !math.isfinite(speed))
                throw new InvalidOperationException($"[VVardenfell][Magic] Target projectile effect {effect.Index} produced invalid speed {speed}.");

            var casterTransform = EntityManager.GetComponentData<LocalTransform>(caster);
            var targetTransform = EntityManager.GetComponentData<LocalTransform>(target);
            float3 origin = ResolveProjectileOrigin(caster, casterTransform);
            float3 targetPoint = ResolveProjectileAimPoint(target, targetTransform);
            float3 direction = math.normalizesafe(targetPoint - origin);
            if (math.lengthsq(direction) <= 0f)
                throw new InvalidOperationException("[VVardenfell][Magic] Target projectile direction is zero.");

            var location = EntityManager.GetComponentData<LogicalRefLocation>(caster);
            Entity request = ecb.CreateEntity();
            ecb.SetName(request, new FixedString64Bytes("VVardenfell.MagicProjectileLaunch"));
            ecb.AddComponent(request, new MorrowindProjectileLaunchRequest
            {
                Caster = caster,
                Target = target,
                SourceContent = default,
                SourceKind = MorrowindProjectileSourceKind.Magic,
                SpellHandleValue = spellHandle.Value,
                EffectId = instance.EffectId,
                Position = origin,
                Rotation = quaternion.LookRotationSafe(direction, math.up()),
                Direction = direction,
                Speed = speed,
                AttackStrength = 1f,
                CollisionRadius = 0f,
                UseModelCollisionRadius = 1,
                ModelPath = model,
                TextureOverridePath = effect.ParticleTexture ?? string.Empty,
                SpawnVisual = 1,
                ExteriorCell = location.ExteriorCell,
                InteriorCellId = location.InteriorCellId,
                InteriorCellHash = location.InteriorCellHash,
                IsInterior = location.IsInterior,
            });
        }

        float3 ResolveProjectileOrigin(Entity caster, in LocalTransform transform)
        {
            if (!EntityManager.HasComponent<ActorLocalBounds>(caster))
                return transform.Position;

            var bounds = EntityManager.GetComponentData<ActorLocalBounds>(caster);
            return transform.Position + bounds.Center + new float3(0f, bounds.Extents.y, 0f);
        }

        float3 ResolveProjectileAimPoint(Entity target, in LocalTransform transform)
        {
            if (!EntityManager.HasComponent<ActorLocalBounds>(target))
                return transform.Position;

            var bounds = EntityManager.GetComponentData<ActorLocalBounds>(target);
            return transform.Position + bounds.Center;
        }

        void EmitMagicVfxObject(RuntimeContentDatabase contentDb, string objectId, Entity anchor, ref EntityCommandBuffer ecb)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            string model = ResolveMagicVfxModel(contentDb, objectId);
            var transform = EntityManager.GetComponentData<LocalTransform>(anchor);
            var location = EntityManager.GetComponentData<LogicalRefLocation>(anchor);
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new MorrowindVfxSpawnRequest
            {
                ModelPath = model,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = transform.Scale <= 0f ? 1f : transform.Scale,
                FollowEntity = anchor,
                Loop = 0,
                EffectId = 0u,
                ExteriorCell = location.ExteriorCell,
                InteriorCellId = location.InteriorCellId,
                InteriorCellHash = location.InteriorCellHash,
                IsInterior = location.IsInterior,
            });
        }

        static string ResolveMagicVfxModel(RuntimeContentDatabase contentDb, string objectId)
        {
            if (contentDb.TryGetActivatorHandle(objectId, out var activator) && activator.IsValid)
            {
                ref readonly var def = ref contentDb.Get(activator);
                return RequireModel(objectId, def.Model);
            }

            if (contentDb.TryGetStaticHandle(objectId, out var stat) && stat.IsValid)
            {
                ref readonly var def = ref contentDb.GetStatic(stat);
                return RequireModel(objectId, def.Model);
            }

            if (contentDb.TryGetLightHandle(objectId, out var light) && light.IsValid)
            {
                ref readonly var def = ref contentDb.Get(light);
                return RequireModel(objectId, def.Model);
            }

            if (contentDb.TryGetItemHandle(objectId, out var item) && item.IsValid)
            {
                ref readonly var def = ref contentDb.Get(item);
                return RequireModel(objectId, def.Model);
            }

            throw new InvalidOperationException($"[VVardenfell][Magic] VFX object '{objectId}' is missing from runtime content.");
        }

        static string RequireModel(string objectId, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException($"[VVardenfell][Magic] VFX object '{objectId}' has no model.");
            return model;
        }

    }
}
