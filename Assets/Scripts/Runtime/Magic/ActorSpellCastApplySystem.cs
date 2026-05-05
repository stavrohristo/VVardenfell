using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Projectiles;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Vfx;
using VVardenfell.Runtime.WorldRefs;
using Random = Unity.Mathematics.Random;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ActorSpellCastApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var scriptedRequests = EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            var castRequests = EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity);
            if (scriptedRequests.Length == 0 && castRequests.Length == 0)
                return;

            var scriptedCopy = new NativeArray<ScriptedCastRequest>(scriptedRequests.Length, Allocator.Temp);
            for (int i = 0; i < scriptedRequests.Length; i++)
                scriptedCopy[i] = scriptedRequests[i];
            scriptedRequests.Clear();

            var castCopy = new NativeArray<ActorSpellCastRequest>(castRequests.Length, Allocator.Temp);
            for (int i = 0; i < castRequests.Length; i++)
                castCopy[i] = castRequests[i];
            castRequests.Clear();

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var contentDb = RuntimeContentDatabase.Active ?? throw new InvalidOperationException("[VVardenfell][Magic] Cast requires active runtime content.");
            var magicRef = SystemAPI.GetSingletonRW<MorrowindMagicRuntimeState>();
            var random = new Random(magicRef.ValueRO.RandomState == 0u ? 0xA5C38F2Du : magicRef.ValueRO.RandomState);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < scriptedCopy.Length; i++)
            {
                var request = scriptedCopy[i];
                ApplyRequest(contentDb, lookup, ref random, ref magicRef.ValueRW, new ActorSpellCastRequest
                {
                    CasterEntity = request.CasterEntity,
                    CasterPlacedRefId = request.CasterPlacedRefId,
                    TargetEntity = request.TargetEntity,
                    TargetPlacedRefId = request.TargetPlacedRefId,
                    Spell = request.Spell,
                    Scripted = 1,
                    AlwaysSucceed = 1,
                    IgnoreReflect = 1,
                    IgnoreSpellAbsorption = 1,
                }, ref ecb);
            }

            for (int i = 0; i < castCopy.Length; i++)
                ApplyRequest(contentDb, lookup, ref random, ref magicRef.ValueRW, castCopy[i], ref ecb);

            magicRef.ValueRW.RandomState = random.state == 0u ? 0xA5C38F2Du : random.state;
            ecb.Playback(EntityManager);
            ecb.Dispose();
            scriptedCopy.Dispose();
            castCopy.Dispose();
        }

        void ApplyRequest(
            RuntimeContentDatabase contentDb,
            in LogicalRefLookup lookup,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState,
            in ActorSpellCastRequest request,
            ref EntityCommandBuffer ecb)
        {
            RequireValidSpell(contentDb, request.Spell);
            ref readonly var spell = ref contentDb.Get(request.Spell);
            Entity caster = ResolveActor(request.CasterEntity, request.CasterPlacedRefId, lookup, "caster");
            Entity target = ResolveOptionalTarget(request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (request.ProjectileImpact != 0)
            {
                InflictRangeEffects(contentDb, request, caster, target, MorrowindMagicRange.Target, request.HasHitPosition != 0 ? (float3?)request.HitPosition : null, ref random, ref magicState);
                return;
            }

            if (request.Scripted == 0 && !ValidateAndSpendNormalCast(contentDb, caster, request.Spell, spell, ref random))
                return;

            if (!MorrowindActorMagicUtility.TryGetSpellEffects(contentDb, spell, out var effects))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast spell '{spell.Id}' has no effects.");

            bool hasTargetProjectile = false;
            for (int i = 0; i < effects.Length; i++)
            {
                var instance = effects[i];
                ref readonly var effect = ref RequireMagicEffect(contentDb, instance.EffectId, spell.Id);
                if (!MorrowindMagicEffectApplicationUtility.IsSupported(instance.EffectId))
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' contains unsupported effect {instance.EffectId}.");

                EmitMagicVfxObject(contentDb, effect.CastingObjectId, caster, ref ecb);

                if (instance.Range == MorrowindMagicRange.Target)
                {
                    if (!hasTargetProjectile)
                    {
                        EmitMagicProjectile(contentDb, request.Spell, instance, effect, caster, target, request.Scripted, request.IgnoreReflect, request.IgnoreSpellAbsorption, ref ecb);
                        hasTargetProjectile = true;
                    }
                    continue;
                }

                Entity anchor = instance.Range == MorrowindMagicRange.Self ? caster : target;
                if (anchor == Entity.Null || !EntityManager.Exists(anchor))
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' touch effect has no resolved target.");

                EmitMagicVfxObject(contentDb, effect.HitObjectId, anchor, ref ecb);
                InflictRangeEffects(contentDb, request, caster, anchor, instance.Range, null, ref random, ref magicState);
            }
        }

        bool ValidateAndSpendNormalCast(
            RuntimeContentDatabase contentDb,
            Entity caster,
            SpellDefHandle spellHandle,
            in SpellDef spell,
            ref Random random)
        {
            if (!EntityManager.HasBuffer<ActorKnownSpell>(caster))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} has no known spell buffer.");
            if (!MorrowindActorMagicUtility.HasKnownSpell(EntityManager.GetBuffer<ActorKnownSpell>(caster, true), spellHandle))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} does not know spell '{spell.Id}'.");
            if (!EntityManager.HasComponent<ActorAttributeSet>(caster)
                || !EntityManager.HasComponent<ActorSkillSet>(caster)
                || !EntityManager.HasComponent<ActorVitalSet>(caster)
                || !EntityManager.HasComponent<ActorDerivedMovementStats>(caster)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(caster)
                || !EntityManager.HasBuffer<ActorUsedPower>(caster))
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} is missing actor magic composition.");
            }

            var attributes = EntityManager.GetComponentData<ActorAttributeSet>(caster);
            var skills = EntityManager.GetComponentData<ActorSkillSet>(caster);
            var derived = EntityManager.GetComponentData<ActorDerivedMovementStats>(caster);
            var casterEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(caster);
            var usedPowers = EntityManager.GetBuffer<ActorUsedPower>(caster);
            var vitals = EntityManager.GetComponentData<ActorVitalSet>(caster);
            int spellCost = MorrowindSpellCostUtility.CalculateSpellCost(contentDb, spell);

            if (MorrowindSpellCostUtility.CalculateSuccessChance(contentDb, spell, attributes, skills, vitals, derived, casterEffects, checkMagicka: true, out _) <= 0f)
            {
                if (spellCost > 0 && vitals.CurrentMagicka < spellCost)
                    QueuePlayerMessageIfPlayer(caster, contentDb.RequireGameSettingString("sMagicInsufficientSP"));
                else
                    QueuePlayerMessageIfPlayer(caster, contentDb.RequireGameSettingString("sMagicSkillFail"));
                return false;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
            {
                float totalHours = ResolveTotalGameHours();
                for (int i = 0; i < usedPowers.Length; i++)
                {
                    if (usedPowers[i].Spell.Value == spellHandle.Value && totalHours - usedPowers[i].LastUsedTotalGameHours < 24f)
                    {
                        QueuePlayerMessageIfPlayer(caster, contentDb.RequireGameSettingString("sPowerAlreadyUsed"));
                        return false;
                    }
                }
            }

            if (spellCost > 0)
            {
                if (vitals.CurrentMagicka < spellCost)
                {
                    QueuePlayerMessageIfPlayer(caster, contentDb.RequireGameSettingString("sMagicInsufficientSP"));
                    return false;
                }

                vitals.CurrentMagicka -= spellCost;
                float fatigueLoss = spellCost * (contentDb.RequireGameSettingFloat("fFatigueSpellBase") + derived.NormalizedEncumbrance * contentDb.RequireGameSettingFloat("fFatigueSpellMult"));
                vitals.CurrentFatigue -= fatigueLoss;
                EntityManager.SetComponentData(caster, vitals);
            }

            float chance = MorrowindSpellCostUtility.CalculateSuccessChance(contentDb, spell, attributes, skills, vitals, derived, casterEffects, checkMagicka: false, out _);
            if ((spell.Flags & MorrowindSpellCostUtility.SpellFlagAlways) == 0 && spell.SpellType == MorrowindSpellCostUtility.SpellTypeSpell && random.NextInt(0, 100) >= chance)
            {
                QueuePlayerMessageIfPlayer(caster, contentDb.RequireGameSettingString("sMagicSkillFail"));
                return false;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
                MarkPowerUsed(usedPowers, spellHandle, ResolveTotalGameHours());

            return true;
        }

        public void InflictRangeEffects(
            RuntimeContentDatabase contentDb,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            int range,
            float3? areaOrigin,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState)
        {
            ref readonly var spell = ref contentDb.Get(request.Spell);
            if (!MorrowindActorMagicUtility.TryGetSpellEffects(contentDb, spell, out var effects))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' has no effects.");

            for (int i = 0; i < effects.Length; i++)
            {
                var instance = effects[i];
                if (instance.Range != range)
                    continue;

                if (instance.Area > 0)
                {
                    float3 origin = areaOrigin ?? RequirePosition(target, $"[VVardenfell][Magic] Area spell '{spell.Id}' target");
                    ApplyAreaEffect(contentDb, request, caster, target, instance, i, origin, ref random, ref magicState);
                }
                else
                {
                    if (target == Entity.Null)
                        continue;
                    ApplyEffectToTarget(contentDb, request, caster, target, instance, i, ref random, ref magicState);
                }
            }
        }

        void ApplyAreaEffect(
            RuntimeContentDatabase contentDb,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity primaryTarget,
            in MagicEffectInstanceDef instance,
            int effectIndex,
            float3 origin,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState)
        {
            float radius = instance.Area * WorldScale.MwUnitsToMeters;
            foreach (var (_, transform, entity) in SystemAPI.Query<DynamicBuffer<ActorActiveMagicEffect>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (entity == caster)
                    continue;
                if (math.distancesq(transform.ValueRO.Position, origin) <= radius * radius)
                    ApplyEffectToTarget(contentDb, request, caster, entity, instance, effectIndex, ref random, ref magicState);
            }
        }

        void ApplyEffectToTarget(
            RuntimeContentDatabase contentDb,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            in MagicEffectInstanceDef instance,
            int effectIndex,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState)
        {
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Magic] Spell effect target is not loaded.");
            if (!EntityManager.HasBuffer<ActorActiveSpell>(target)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(target)
                || !EntityManager.HasComponent<ActorActiveMagicEffectDirty>(target))
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell effect target entity={target.Index}:{target.Version} has no active magic composition.");
            }

            ref readonly var spell = ref contentDb.Get(request.Spell);
            ref readonly var effectDef = ref RequireMagicEffect(contentDb, instance.EffectId, spell.Id);
            if (!MorrowindMagicEffectApplicationUtility.IsSupported(instance.EffectId))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spell.Id}' contains unsupported effect {instance.EffectId}.");

            Entity finalTarget = target;
            byte ignoreReflect = request.IgnoreReflect;
            byte ignoreAbsorb = request.IgnoreSpellAbsorption;
            float magnitude = ResolveMagnitude(instance, effectDef, ref random);
            if ((effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagHarmful) != 0 && target != caster)
            {
                if (!TryResolveProtection(contentDb, request, caster, target, instance.EffectId, effectDef, ref magnitude, ref finalTarget, ref ignoreReflect, ref ignoreAbsorb, ref random))
                    return;
            }

            var activeSpells = EntityManager.GetBuffer<ActorActiveSpell>(finalTarget);
            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(finalTarget);
            int activeSpellId = magicState.NextActiveSpellId <= 0 ? 1 : magicState.NextActiveSpellId++;
            activeSpells.Add(new ActorActiveSpell
            {
                ActiveSpellId = activeSpellId,
                CasterEntity = caster,
                CasterPlacedRefId = PlacedRefId(caster),
                Spell = request.Spell,
                SourceKind = request.Scripted != 0 ? ActorActiveSpellSourceKind.ScriptedSpell : ActorActiveSpellSourceKind.Spell,
                Flags = ActorActiveSpellFlags.Temporary
                        | (request.Scripted != 0 ? ActorActiveSpellFlags.Scripted : ActorActiveSpellFlags.None)
                        | (ignoreReflect != 0 ? ActorActiveSpellFlags.IgnoreReflect : ActorActiveSpellFlags.None)
                        | (ignoreAbsorb != 0 ? ActorActiveSpellFlags.IgnoreSpellAbsorption : ActorActiveSpellFlags.None),
                SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(spell.Id),
            });

            float duration = ResolveDuration(instance, effectDef);
            activeEffects.Add(new ActorActiveMagicEffect
            {
                ActiveSpellId = activeSpellId,
                EffectId = instance.EffectId,
                EffectIndex = (short)effectIndex,
                Skill = instance.Skill,
                Attribute = instance.Attribute,
                Magnitude = magnitude,
                MagnitudeMin = instance.MagnitudeMin,
                MagnitudeMax = instance.MagnitudeMax,
                DurationSeconds = duration,
                TimeLeftSeconds = duration,
                IgnoreReflect = ignoreReflect,
                IgnoreSpellAbsorption = ignoreAbsorb,
                SourceKind = request.Scripted != 0 ? ActorActiveMagicEffectSourceKind.ScriptedSpell : ActorActiveMagicEffectSourceKind.TimedSpell,
                SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(spell.Id),
            });
            EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(finalTarget, true);
        }

        bool TryResolveProtection(
            RuntimeContentDatabase contentDb,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            short effectId,
            in MagicEffectDef effectDef,
            ref float magnitude,
            ref Entity finalTarget,
            ref byte ignoreReflect,
            ref byte ignoreAbsorb,
            ref Random random)
        {
            var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            if (ignoreAbsorb == 0)
            {
                float absorption = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(targetEffects, MorrowindMagicEffectIds.SpellAbsorption);
                if (absorption > 0f && random.NextInt(0, 100) < absorption)
                {
                    RestoreAbsorbedMagicka(contentDb, request.Spell, target);
                    return false;
                }
            }

            if (ignoreReflect == 0 && (effectDef.Flags & 0x10000) == 0)
            {
                float reflect = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(targetEffects, MorrowindMagicEffectIds.Reflect);
                if (reflect > 0f && random.NextInt(0, 100) < reflect)
                {
                    finalTarget = caster;
                    ignoreReflect = 1;
                    ignoreAbsorb = 1;
                }
            }

            var effects = EntityManager.GetBuffer<ActorActiveMagicEffect>(finalTarget, true);
            float resist = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(effects, MorrowindMagicEffectApplicationUtility.ResolveResistanceEffect(effectId));
            float weakness = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(effects, MorrowindMagicEffectApplicationUtility.ResolveWeaknessEffect(effectId));
            magnitude *= 1f - (0.01f * (resist - weakness));
            return magnitude > 0f;
        }

        void RestoreAbsorbedMagicka(RuntimeContentDatabase contentDb, SpellDefHandle spellHandle, Entity target)
        {
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell absorption target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            ref readonly var spell = ref contentDb.Get(spellHandle);
            vitals.CurrentMagicka += MorrowindSpellCostUtility.CalculateSpellCost(contentDb, spell);
            EntityManager.SetComponentData(target, vitals);
        }

        void EmitMagicProjectile(
            RuntimeContentDatabase contentDb,
            SpellDefHandle spellHandle,
            in MagicEffectInstanceDef instance,
            in MagicEffectDef effect,
            Entity caster,
            Entity target,
            byte scripted,
            byte ignoreReflect,
            byte ignoreSpellAbsorption,
            ref EntityCommandBuffer ecb)
        {
            string model = ResolveMagicVfxModel(contentDb, effect.BoltObjectId);
            float speed = contentDb.RequireGameSettingFloat("fTargetSpellMaxSpeed") * effect.Speed;
            if (speed <= 0f || !math.isfinite(speed))
                throw new InvalidOperationException($"[VVardenfell][Magic] Target projectile effect {effect.Index} produced invalid speed {speed}.");

            var casterTransform = EntityManager.GetComponentData<LocalTransform>(caster);
            float3 origin = ResolveProjectileOrigin(caster, casterTransform);
            float3 targetPoint = target != Entity.Null && EntityManager.Exists(target)
                ? ResolveProjectileAimPoint(target, EntityManager.GetComponentData<LocalTransform>(target))
                : origin + math.mul(casterTransform.Rotation, new float3(0f, 0f, 1f)) * 128f;
            float3 direction = math.normalizesafe(targetPoint - origin);
            if (math.lengthsq(direction) <= 0f)
                throw new InvalidOperationException("[VVardenfell][Magic] Target projectile direction is zero.");

            var location = EntityManager.GetComponentData<LogicalRefLocation>(caster);
            Entity launchEntity = ecb.CreateEntity();
            ecb.SetName(launchEntity, new FixedString64Bytes("VVardenfell.MagicProjectileLaunch"));
            ecb.AddComponent(launchEntity, new MorrowindProjectileLaunchRequest
            {
                Caster = caster,
                Target = target,
                SourceKind = MorrowindProjectileSourceKind.Magic,
                SpellHandleValue = spellHandle.Value,
                EffectId = instance.EffectId,
                Position = origin,
                Rotation = quaternion.LookRotationSafe(direction, math.up()),
                Direction = direction,
                Speed = speed,
                UseModelCollisionRadius = 1,
                ModelPath = model,
                TextureOverridePath = effect.ParticleTexture ?? string.Empty,
                SpawnVisual = 1,
                Scripted = scripted,
                IgnoreReflect = ignoreReflect,
                IgnoreSpellAbsorption = ignoreSpellAbsorption,
                ExteriorCell = location.ExteriorCell,
                InteriorCellId = location.InteriorCellId,
                InteriorCellHash = location.InteriorCellHash,
                IsInterior = location.IsInterior,
            });
        }

        void EmitMagicVfxObject(RuntimeContentDatabase contentDb, string objectId, Entity anchor, ref EntityCommandBuffer ecb)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return;
            if (!EntityManager.HasComponent<LocalTransform>(anchor) || !EntityManager.HasComponent<LogicalRefLocation>(anchor))
                throw new InvalidOperationException($"[VVardenfell][Magic] VFX anchor entity={anchor.Index}:{anchor.Version} lacks transform/location.");

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
                ExteriorCell = location.ExteriorCell,
                InteriorCellId = location.InteriorCellId,
                InteriorCellHash = location.InteriorCellHash,
                IsInterior = location.IsInterior,
            });
        }

        static float ResolveDuration(in MagicEffectInstanceDef instance, in MagicEffectDef effectDef)
            => (effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoDuration) != 0 ? 1f : math.max(1f, instance.Duration);

        static float ResolveMagnitude(in MagicEffectInstanceDef instance, in MagicEffectDef effectDef, ref Random random)
        {
            if ((effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoMagnitude) != 0)
                return 1f;
            int min = math.min(instance.MagnitudeMin, instance.MagnitudeMax);
            int max = math.max(instance.MagnitudeMin, instance.MagnitudeMax);
            return max <= min ? min : random.NextInt(min, max + 1);
        }

        float ResolveTotalGameHours()
            => SystemAPI.TryGetSingleton<MorrowindTimeState>(out var time)
                ? (time.DaysPassed * 24f) + time.GameHour
                : throw new InvalidOperationException("[VVardenfell][Magic] Power cooldown requires MorrowindTimeState.");

        static void MarkPowerUsed(DynamicBuffer<ActorUsedPower> usedPowers, SpellDefHandle spell, float totalHours)
        {
            for (int i = 0; i < usedPowers.Length; i++)
            {
                if (usedPowers[i].Spell.Value == spell.Value)
                {
                    usedPowers[i] = new ActorUsedPower { Spell = spell, LastUsedTotalGameHours = totalHours };
                    return;
                }
            }

            usedPowers.Add(new ActorUsedPower { Spell = spell, LastUsedTotalGameHours = totalHours });
        }

        void QueuePlayerMessageIfPlayer(Entity actor, string message)
        {
            if (!EntityManager.HasComponent<PlayerTag>(actor))
                return;
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity).Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(message),
            });
        }

        Entity ResolveActor(Entity entity, uint placedRefId, in LogicalRefLookup lookup, string role)
        {
            Entity resolved = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, entity, placedRefId, lookup);
            if (resolved == Entity.Null || !EntityManager.Exists(resolved))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast {role} ref={placedRefId} is not loaded.");
            return resolved;
        }

        Entity ResolveOptionalTarget(Entity entity, uint placedRefId, in LogicalRefLookup lookup)
        {
            if (entity == Entity.Null && placedRefId == 0u)
                return Entity.Null;
            return ResolveActor(entity, placedRefId, lookup, "target");
        }

        static void RequireValidSpell(RuntimeContentDatabase contentDb, SpellDefHandle spell)
        {
            if (!spell.IsValid || contentDb.Data.Spells == null || spell.Index < 0 || spell.Index >= contentDb.Data.Spells.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast references invalid spell handle {spell.Value}.");
        }

        static ref readonly MagicEffectDef RequireMagicEffect(RuntimeContentDatabase contentDb, short effectId, string spellId)
        {
            if (!contentDb.TryGetMagicEffectHandle(effectId, out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell '{spellId}' references missing magic effect {effectId}.");
            return ref contentDb.Get(handle);
        }

        float3 RequirePosition(Entity entity, string context)
        {
            if (!EntityManager.HasComponent<LocalTransform>(entity))
                throw new InvalidOperationException($"{context} has no LocalTransform.");
            return EntityManager.GetComponentData<LocalTransform>(entity).Position;
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

        static string ResolveMagicVfxModel(RuntimeContentDatabase contentDb, string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                throw new InvalidOperationException("[VVardenfell][Magic] Magic projectile effect has no VFX object.");
            if (contentDb.TryGetActivatorHandle(objectId, out var activator) && activator.IsValid)
                return RequireModel(objectId, contentDb.Get(activator).Model);
            if (contentDb.TryGetStaticHandle(objectId, out var stat) && stat.IsValid)
                return RequireModel(objectId, contentDb.GetStatic(stat).Model);
            if (contentDb.TryGetLightHandle(objectId, out var light) && light.IsValid)
                return RequireModel(objectId, contentDb.Get(light).Model);
            if (contentDb.TryGetItemHandle(objectId, out var item) && item.IsValid)
                return RequireModel(objectId, contentDb.Get(item).Model);
            throw new InvalidOperationException($"[VVardenfell][Magic] VFX object '{objectId}' is missing from runtime content.");
        }

        static string RequireModel(string objectId, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException($"[VVardenfell][Magic] VFX object '{objectId}' has no model.");
            return model;
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
