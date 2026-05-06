using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
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
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct ActorSpellCastApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeModelPrefabBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var scriptedRequests = systemState.EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            var castRequests = systemState.EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity);
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
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Cast requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            var modelBlobReference = SystemAPI.GetSingleton<RuntimeModelPrefabBlobReference>();
            if (!modelBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ModelPrefabBlob] Cast requires runtime model prefab blob.");
            ref RuntimeModelPrefabBlob modelPrefabs = ref modelBlobReference.Blob.Value;
            var magicRef = SystemAPI.GetSingletonRW<MorrowindMagicRuntimeState>();
            var random = new Random(magicRef.ValueRO.RandomState == 0u ? 0xA5C38F2Du : magicRef.ValueRO.RandomState);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < scriptedCopy.Length; i++)
            {
                var request = scriptedCopy[i];
                ApplyRequest(ref systemState, ref content, ref modelPrefabs, lookup, ref random, ref magicRef.ValueRW, new ActorSpellCastRequest
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
                ApplyRequest(ref systemState, ref content, ref modelPrefabs, lookup, ref random, ref magicRef.ValueRW, castCopy[i], ref ecb);

            magicRef.ValueRW.RandomState = random.state == 0u ? 0xA5C38F2Du : random.state;
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            scriptedCopy.Dispose();
            castCopy.Dispose();
        }

        void ApplyRequest(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref RuntimeModelPrefabBlob modelPrefabs,
            in LogicalRefLookup lookup,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState,
            in ActorSpellCastRequest request,
            ref EntityCommandBuffer ecb)
        {
            RequireValidSpell(ref content, request.Spell);
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, request.Spell);
            Entity caster = ResolveActor(ref systemState, request.CasterEntity, request.CasterPlacedRefId, lookup, "caster");
            Entity target = ResolveOptionalTarget(ref systemState, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (request.ProjectileImpact != 0)
            {
                InflictRangeEffects(ref systemState, ref content, request, caster, target, MorrowindMagicRange.Target, request.HasHitPosition != 0 ? (float3?)request.HitPosition : null, ref random, ref magicState);
                return;
            }

            if (request.Scripted == 0 && !ValidateAndSpendNormalCast(ref systemState, ref content, caster, request.Spell, ref spell, ref random))
                return;

            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);

            bool hasTargetProjectile = false;
            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref RequireMagicEffect(ref content, instance.EffectId, spell.ContentId.Value);
                if (!MorrowindMagicEffectApplicationUtility.IsSupported(instance.EffectId))
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spell.ContentId.Value:X16} contains unsupported effect {instance.EffectId}.");

                EmitMagicVfxObject(ref systemState, ref content, ref modelPrefabs, effect.CastingObjectIdHash, caster, ref ecb);

                if (instance.Range == MorrowindMagicRange.Target)
                {
                    if (!hasTargetProjectile)
                    {
                        EmitMagicProjectile(ref systemState, ref content, ref modelPrefabs, request.Spell, instance, ref effect, caster, target, request.Scripted, request.IgnoreReflect, request.IgnoreSpellAbsorption, ref ecb);
                        hasTargetProjectile = true;
                    }
                    continue;
                }

                Entity anchor = instance.Range == MorrowindMagicRange.Self ? caster : target;
                if (anchor == Entity.Null || !systemState.EntityManager.Exists(anchor))
                    throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spell.ContentId.Value:X16} touch effect has no resolved target.");

                EmitMagicVfxObject(ref systemState, ref content, ref modelPrefabs, effect.HitObjectIdHash, anchor, ref ecb);
                InflictRangeEffects(ref systemState, ref content, request, caster, anchor, instance.Range, null, ref random, ref magicState);
            }
        }

        bool ValidateAndSpendNormalCast(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity caster,
            SpellDefHandle spellHandle,
            ref RuntimeSpellDefBlob spell,
            ref Random random)
        {
            if (!systemState.EntityManager.HasBuffer<ActorKnownSpell>(caster))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} has no known spell buffer.");
            if (!MorrowindActorMagicUtility.HasKnownSpell(systemState.EntityManager.GetBuffer<ActorKnownSpell>(caster, true), spellHandle))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} does not know spell contentId=0x{spell.ContentId.Value:X16}.");
            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(caster)
                || !systemState.EntityManager.HasComponent<ActorSkillSet>(caster)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(caster)
                || !systemState.EntityManager.HasComponent<ActorDerivedMovementStats>(caster)
                || !systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(caster)
                || !systemState.EntityManager.HasBuffer<ActorUsedPower>(caster))
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster entity={caster.Index}:{caster.Version} is missing actor magic composition.");
            }

            var attributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(caster);
            var skills = systemState.EntityManager.GetComponentData<ActorSkillSet>(caster);
            var derived = systemState.EntityManager.GetComponentData<ActorDerivedMovementStats>(caster);
            var casterEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(caster);
            var usedPowers = systemState.EntityManager.GetBuffer<ActorUsedPower>(caster);
            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(caster);
            int spellCost = MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell);

            if (MorrowindSpellCostUtility.CalculateSuccessChance(ref content, ref spell, attributes, skills, vitals, derived, casterEffects, checkMagicka: true, out _) <= 0f)
            {
                if (spellCost > 0 && vitals.CurrentMagicka < spellCost)
                    QueuePlayerMessageIfPlayer(ref systemState, caster, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicInsufficientSP));
                else
                    QueuePlayerMessageIfPlayer(ref systemState, caster, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicSkillFail));
                return false;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
            {
                float totalHours = ResolveTotalGameHours(ref systemState);
                for (int i = 0; i < usedPowers.Length; i++)
                {
                    if (usedPowers[i].Spell.Value == spellHandle.Value && totalHours - usedPowers[i].LastUsedTotalGameHours < 24f)
                    {
                        QueuePlayerMessageIfPlayer(ref systemState, caster, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sPowerAlreadyUsed));
                        return false;
                    }
                }
            }

            if (spellCost > 0)
            {
                if (vitals.CurrentMagicka < spellCost)
                {
                    QueuePlayerMessageIfPlayer(ref systemState, caster, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicInsufficientSP));
                    return false;
                }

                vitals.CurrentMagicka -= spellCost;
                float fatigueLoss = spellCost * (RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueSpellBase) + derived.NormalizedEncumbrance * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueSpellMult));
                vitals.CurrentFatigue -= fatigueLoss;
                systemState.EntityManager.SetComponentData(caster, vitals);
            }

            float chance = MorrowindSpellCostUtility.CalculateSuccessChance(ref content, ref spell, attributes, skills, vitals, derived, casterEffects, checkMagicka: false, out _);
            if ((spell.Flags & MorrowindSpellCostUtility.SpellFlagAlways) == 0 && spell.SpellType == MorrowindSpellCostUtility.SpellTypeSpell && random.NextInt(0, 100) >= chance)
            {
                QueuePlayerMessageIfPlayer(ref systemState, caster, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicSkillFail));
                return false;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
                MarkPowerUsed(usedPowers, spellHandle, ResolveTotalGameHours(ref systemState));

            return true;
        }

        public void InflictRangeEffects(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            int range,
            float3? areaOrigin,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState)
        {
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, request.Spell);
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);

            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                if (instance.Range != range)
                    continue;

                if (instance.Area > 0)
                {
                    float3 origin = areaOrigin ?? RequirePosition(ref systemState, target, $"[VVardenfell][Magic] Area spell contentId=0x{spell.ContentId.Value:X16} target");
                    ApplyAreaEffect(ref systemState, ref content, request, caster, target, instance, i, origin, ref random, ref magicState);
                }
                else
                {
                    if (target == Entity.Null)
                        continue;
                    ApplyEffectToTarget(ref systemState, ref content, request, caster, target, instance, i, ref random, ref magicState);
                }
            }
        }

        void ApplyAreaEffect(ref SystemState systemState, 
            ref RuntimeContentBlob content,
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
                    ApplyEffectToTarget(ref systemState, ref content, request, caster, entity, instance, effectIndex, ref random, ref magicState);
            }
        }

        void ApplyEffectToTarget(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            in MagicEffectInstanceDef instance,
            int effectIndex,
            ref Random random,
            ref MorrowindMagicRuntimeState magicState)
        {
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Magic] Spell effect target is not loaded.");
            if (!systemState.EntityManager.HasBuffer<ActorActiveSpell>(target)
                || !systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(target)
                || !systemState.EntityManager.HasComponent<ActorActiveMagicEffectDirty>(target))
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell effect target entity={target.Index}:{target.Version} has no active magic composition.");
            }

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, request.Spell);
            FixedString64Bytes sourceName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Name);
            FixedString64Bytes sourceId = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Id);
            if (sourceName.IsEmpty)
                sourceName = sourceId;
            ref RuntimeMagicEffectDefBlob effectDef = ref RequireMagicEffect(ref content, instance.EffectId, spell.ContentId.Value);
            if (!MorrowindMagicEffectApplicationUtility.IsSupported(instance.EffectId))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spell.ContentId.Value:X16} contains unsupported effect {instance.EffectId}.");

            Entity finalTarget = target;
            byte ignoreReflect = request.IgnoreReflect;
            byte ignoreAbsorb = request.IgnoreSpellAbsorption;
            float magnitude = ResolveMagnitude(instance, ref effectDef, ref random);
            if ((effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagHarmful) != 0 && target != caster)
            {
                if (!TryResolveProtection(ref systemState, ref content, request, caster, target, instance.EffectId, ref effectDef, ref magnitude, ref finalTarget, ref ignoreReflect, ref ignoreAbsorb, ref random))
                    return;
            }

            var activeSpells = systemState.EntityManager.GetBuffer<ActorActiveSpell>(finalTarget);
            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(finalTarget);
            int activeSpellId = magicState.NextActiveSpellId <= 0 ? 1 : magicState.NextActiveSpellId++;
            activeSpells.Add(new ActorActiveSpell
            {
                ActiveSpellId = activeSpellId,
                CasterEntity = caster,
                CasterPlacedRefId = PlacedRefId(ref systemState, caster),
                Spell = request.Spell,
                SourceKind = request.Scripted != 0 ? ActorActiveSpellSourceKind.ScriptedSpell : ActorActiveSpellSourceKind.Spell,
                Flags = ActorActiveSpellFlags.Temporary
                        | (request.Scripted != 0 ? ActorActiveSpellFlags.Scripted : ActorActiveSpellFlags.None)
                        | (ignoreReflect != 0 ? ActorActiveSpellFlags.IgnoreReflect : ActorActiveSpellFlags.None)
                        | (ignoreAbsorb != 0 ? ActorActiveSpellFlags.IgnoreSpellAbsorption : ActorActiveSpellFlags.None),
                SourceName = sourceName,
                SourceId = sourceId,
                SourceIdHash = spell.IdHash,
            });

            float duration = ResolveDuration(instance, ref effectDef);
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
                SourceName = sourceName,
                SourceId = sourceId,
                SourceIdHash = spell.IdHash,
            });
            systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(finalTarget, true);
        }

        bool TryResolveProtection(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            in ActorSpellCastRequest request,
            Entity caster,
            Entity target,
            short effectId,
            ref RuntimeMagicEffectDefBlob effectDef,
            ref float magnitude,
            ref Entity finalTarget,
            ref byte ignoreReflect,
            ref byte ignoreAbsorb,
            ref Random random)
        {
            var targetEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            if (ignoreAbsorb == 0)
            {
                float absorption = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(targetEffects, MorrowindMagicEffectIds.SpellAbsorption);
                if (absorption > 0f && random.NextInt(0, 100) < absorption)
                {
                    RestoreAbsorbedMagicka(ref systemState, ref content, request.Spell, target);
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

            var effects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(finalTarget, true);
            float resist = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(effects, MorrowindMagicEffectApplicationUtility.ResolveResistanceEffect(effectId));
            float weakness = MorrowindMagicEffectApplicationUtility.SumEffectMagnitude(effects, MorrowindMagicEffectApplicationUtility.ResolveWeaknessEffect(effectId));
            magnitude *= 1f - (0.01f * (resist - weakness));
            return magnitude > 0f;
        }

        void RestoreAbsorbedMagicka(ref SystemState systemState, ref RuntimeContentBlob content, SpellDefHandle spellHandle, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell absorption target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            vitals.CurrentMagicka += MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell);
            systemState.EntityManager.SetComponentData(target, vitals);
        }

        void EmitMagicProjectile(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref RuntimeModelPrefabBlob modelPrefabs,
            SpellDefHandle spellHandle,
            in MagicEffectInstanceDef instance,
            ref RuntimeMagicEffectDefBlob effect,
            Entity caster,
            Entity target,
            byte scripted,
            byte ignoreReflect,
            byte ignoreSpellAbsorption,
            ref EntityCommandBuffer ecb)
        {
            RuntimeMagicVfxModelRef model = ResolveMagicVfxModel(ref content, ref modelPrefabs, effect.BoltObjectIdHash, "projectile bolt");
            float speed = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fTargetSpellMaxSpeed) * effect.Speed;
            if (speed <= 0f || !math.isfinite(speed))
                throw new InvalidOperationException($"[VVardenfell][Magic] Target projectile effect {effect.Index} produced invalid speed {speed}.");

            var casterTransform = systemState.EntityManager.GetComponentData<LocalTransform>(caster);
            float3 origin = ResolveProjectileOrigin(ref systemState, caster, casterTransform);
            float3 targetPoint = target != Entity.Null && systemState.EntityManager.Exists(target)
                ? ResolveProjectileAimPoint(ref systemState, target, systemState.EntityManager.GetComponentData<LocalTransform>(target))
                : origin + math.mul(casterTransform.Rotation, new float3(0f, 0f, 1f)) * 128f;
            float3 direction = math.normalizesafe(targetPoint - origin);
            if (math.lengthsq(direction) <= 0f)
                throw new InvalidOperationException("[VVardenfell][Magic] Target projectile direction is zero.");

            var location = systemState.EntityManager.GetComponentData<LogicalRefLocation>(caster);
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
                CollisionRadius = RuntimeModelPrefabBlobUtility.RequireCollisionRadius(ref modelPrefabs, model.ModelPrefabIndex, "magic projectile"),
                ModelPrefabIndex = model.ModelPrefabIndex,
                ModelPathHash = model.ModelPathHash,
                TextureOverridePathHash = effect.ParticleTexturePathHash,
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

        void EmitMagicVfxObject(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref RuntimeModelPrefabBlob modelPrefabs,
            ulong objectIdHash,
            Entity anchor,
            ref EntityCommandBuffer ecb)
        {
            if (objectIdHash == 0UL)
                return;
            if (!systemState.EntityManager.HasComponent<LocalTransform>(anchor) || !systemState.EntityManager.HasComponent<LogicalRefLocation>(anchor))
                throw new InvalidOperationException($"[VVardenfell][Magic] VFX anchor entity={anchor.Index}:{anchor.Version} lacks transform/location.");

            RuntimeMagicVfxModelRef model = ResolveMagicVfxModel(ref content, ref modelPrefabs, objectIdHash, "anchored VFX");
            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(anchor);
            var location = systemState.EntityManager.GetComponentData<LogicalRefLocation>(anchor);
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new MorrowindVfxSpawnRequest
            {
                ModelPrefabIndex = model.ModelPrefabIndex,
                ModelPathHash = model.ModelPathHash,
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

        static float ResolveDuration(in MagicEffectInstanceDef instance, ref RuntimeMagicEffectDefBlob effectDef)
            => (effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoDuration) != 0 ? 1f : math.max(1f, instance.Duration);

        static float ResolveMagnitude(in MagicEffectInstanceDef instance, ref RuntimeMagicEffectDefBlob effectDef, ref Random random)
        {
            if ((effectDef.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoMagnitude) != 0)
                return 1f;
            int min = math.min(instance.MagnitudeMin, instance.MagnitudeMax);
            int max = math.max(instance.MagnitudeMin, instance.MagnitudeMax);
            return max <= min ? min : random.NextInt(min, max + 1);
        }

        float ResolveTotalGameHours(ref SystemState systemState)
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

        void QueuePlayerMessageIfPlayer(ref SystemState systemState, Entity actor, string message)
        {
            if (!systemState.EntityManager.HasComponent<PlayerTag>(actor))
                return;
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity).Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(message),
            });
        }

        Entity ResolveActor(ref SystemState systemState, Entity entity, uint placedRefId, in LogicalRefLookup lookup, string role)
        {
            Entity resolved = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, entity, placedRefId, lookup);
            if (resolved == Entity.Null || !systemState.EntityManager.Exists(resolved))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast {role} ref={placedRefId} is not loaded.");
            return resolved;
        }

        Entity ResolveOptionalTarget(ref SystemState systemState, Entity entity, uint placedRefId, in LogicalRefLookup lookup)
        {
            if (entity == Entity.Null && placedRefId == 0u)
                return Entity.Null;
            return ResolveActor(ref systemState, entity, placedRefId, lookup, "target");
        }

        static void RequireValidSpell(ref RuntimeContentBlob content, SpellDefHandle spell)
        {
            if (!spell.IsValid || spell.Index < 0 || spell.Index >= content.Spells.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast references invalid spell handle {spell.Value}.");
        }

        static ref RuntimeMagicEffectDefBlob RequireMagicEffect(ref RuntimeContentBlob content, short effectId, ulong spellContentId)
        {
            if (!RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref content, effectId, out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Magic] Spell contentId=0x{spellContentId:X16} references missing magic effect {effectId}.");
            return ref RuntimeContentBlobUtility.Get(ref content, handle);
        }

        float3 RequirePosition(ref SystemState systemState, Entity entity, string context)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(entity))
                throw new InvalidOperationException($"{context} has no LocalTransform.");
            return systemState.EntityManager.GetComponentData<LocalTransform>(entity).Position;
        }

        float3 ResolveProjectileOrigin(ref SystemState systemState, Entity caster, in LocalTransform transform)
        {
            if (!systemState.EntityManager.HasComponent<ActorLocalBounds>(caster))
                return transform.Position;
            var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(caster);
            return transform.Position + bounds.Center + new float3(0f, bounds.Extents.y, 0f);
        }

        float3 ResolveProjectileAimPoint(ref SystemState systemState, Entity target, in LocalTransform transform)
        {
            if (!systemState.EntityManager.HasComponent<ActorLocalBounds>(target))
                return transform.Position;
            var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(target);
            return transform.Position + bounds.Center;
        }

        static RuntimeMagicVfxModelRef ResolveMagicVfxModel(
            ref RuntimeContentBlob content,
            ref RuntimeModelPrefabBlob modelPrefabs,
            ulong objectIdHash,
            string context)
        {
            if (objectIdHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][Magic] Magic {context} effect has no VFX object hash.");
            if (RuntimeContentBlobUtility.TryGetActivatorHandleByIdHash(ref content, objectIdHash, out var activator) && activator.IsValid)
                return RequireModel(ref modelPrefabs, RuntimeContentBlobUtility.Get(ref content, activator).ModelPathHash, objectIdHash, context);
            if (RuntimeContentBlobUtility.TryGetStaticHandleByIdHash(ref content, objectIdHash, out var stat) && stat.IsValid)
                return RequireModel(ref modelPrefabs, RuntimeContentBlobUtility.GetStatic(ref content, stat).ModelPathHash, objectIdHash, context);
            if (RuntimeContentBlobUtility.TryGetLightHandleByIdHash(ref content, objectIdHash, out var light) && light.IsValid)
                return RequireModel(ref modelPrefabs, RuntimeContentBlobUtility.Get(ref content, light).ModelPathHash, objectIdHash, context);
            if (RuntimeContentBlobUtility.TryGetItemHandleByIdHash(ref content, objectIdHash, out var item) && item.IsValid)
                return RequireModel(ref modelPrefabs, RuntimeContentBlobUtility.Get(ref content, item).ModelPathHash, objectIdHash, context);
            throw new InvalidOperationException($"[VVardenfell][Magic] VFX object hash 0x{objectIdHash:X16} is missing from runtime content.");
        }

        static RuntimeMagicVfxModelRef RequireModel(
            ref RuntimeModelPrefabBlob modelPrefabs,
            ulong contentModelPathHash,
            ulong objectIdHash,
            string context)
        {
            if (contentModelPathHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][Magic] VFX object hash 0x{objectIdHash:X16} for {context} has no model path hash.");
            int modelPrefabIndex = RuntimeModelPrefabBlobUtility.RequireIndexByContentModelPathHash(ref modelPrefabs, contentModelPathHash, $"magic {context}");
            ref RuntimeModelPrefabDefBlob record = ref RuntimeModelPrefabBlobUtility.RequireRecord(ref modelPrefabs, modelPrefabIndex);
            return new RuntimeMagicVfxModelRef
            {
                ModelPrefabIndex = modelPrefabIndex,
                ModelPathHash = record.ModelPathHash,
            };
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        struct RuntimeMagicVfxModelRef
        {
            public int ModelPrefabIndex;
            public ulong ModelPathHash;
        }
    }
}


