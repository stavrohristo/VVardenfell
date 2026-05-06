using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorWeaponAnimationSystem))]
    public partial struct ActorMeleeCombatAnimationSyncSystem : ISystem
    {
        static readonly short ParalyzeEffectId = RequireEffectId("sEffectParalyze");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorCombatTargetState>();
            systemState.RequireForUpdate<ActorMeleeCombatAiState>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Actor melee combat animation sync requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var (combat, ai, transform, actorBounds, movementState, weaponState, entity) in
                     SystemAPI.Query<
                             RefRO<ActorCombatTargetState>,
                             RefRW<ActorMeleeCombatAiState>,
                             RefRW<LocalTransform>,
                             RefRO<ActorLocalBounds>,
                             RefRO<MorrowindMovementState>,
                             RefRW<ActorWeaponAnimationState>>()
                         .WithNone<PlayerTag, LocalPlayerVisual>()
                         .WithEntityAccess())
            {
                ref var aiState = ref ai.ValueRW;
                ref var weapon = ref weaponState.ValueRW;
                ResolveEquippedMeleeWeapon(ref systemState, ref content, entity, ref weapon);

                if (combat.ValueRO.Active == 0)
                {
                    StopActorCombatAnimation(ref aiState, ref weapon);
                    continue;
                }

                Entity target = combat.ValueRO.TargetEntity;
                RequireActiveCombatComposition(ref systemState, entity, target);
                if (IsActorDisabledOrDead(ref systemState, entity) || IsActorDisabledOrDead(ref systemState, target) || IsActorUnableToAttack(ref systemState, entity))
                {
                    ClearAttackControls(ref aiState, ref weapon);
                    continue;
                }

                if (!ActorWeaponAnimationUtility.IsSupportedMelee(weapon.WeaponType))
                {
                    ClearAttackControls(ref aiState, ref weapon);
                    continue;
                }

                if (weapon.Drawn == 0 && !IsAttacking(weapon.Phase))
                {
                    weapon.ReadyWeaponTogglePressed = 1;
                    continue;
                }

                if (aiState.CooldownSeconds > 0f)
                    aiState.CooldownSeconds = math.max(0f, aiState.CooldownSeconds - deltaTime);

                UpdateAttackRelease(ref aiState, ref weapon);

                if (aiState.AttackInProgress != 0 || aiState.CooldownSeconds > 0f)
                    continue;
                if (weapon.Drawn == 0 || weapon.Phase != ActorWeaponAnimationPhase.Equipped)
                    continue;
                if (!CanStartMeleeAttack(ref systemState, ref content, deferredPhysicsQueueEntity, fixedTick, entity, actorBounds.ValueRO, transform.ValueRO, target))
                    continue;

                FaceTarget(ref systemState, ref transform.ValueRW, target);
                aiState.DesiredAttackStrength = random.NextFloat();
                aiState.DesiredAttackType = ChooseAttackType(ref content, weapon.WeaponContent, ref random);
                aiState.AttackInProgress = 1;
                aiState.CooldownSeconds = ResolveAttackCooldown(ref systemState, ref content, entity, ref random);
                weapon.AiAttackTypeOverride = 1;
                weapon.AiAttackType = aiState.DesiredAttackType;
                weapon.AttackPressed = 1;
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        void ResolveEquippedMeleeWeapon(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor, ref ActorWeaponAnimationState state)
        {
            state.WeaponType = ActorWeaponAnimationUtility.NoWeaponType;
            state.WeaponContent = default;

            if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(actor))
            {
                if (IsCreature(ref systemState, ref content, actor))
                    return;

                throw new InvalidOperationException($"[VVardenfell][ActorMelee] NPC ref={PlacedRefId(ref systemState, actor)} has no ActorEquipmentSlot buffer.");
            }

            var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(actor, true);
            state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref content, equipment, out var weaponContent);
            state.WeaponContent = weaponContent;
        }

        void StopActorCombatAnimation(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon)
        {
            ClearAttackControls(ref aiState, ref weapon);
            aiState.CooldownSeconds = 0f;
            if (weapon.Drawn != 0 && !IsAttacking(weapon.Phase))
                weapon.ReadyWeaponTogglePressed = 1;
        }

        static void ClearAttackControls(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon)
        {
            aiState.AttackInProgress = 0;
            aiState.DesiredAttackStrength = 0f;
            weapon.AttackPressed = 0;
            weapon.AttackReleased = 0;
            weapon.AiAttackTypeOverride = 0;
        }

        static void UpdateAttackRelease(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon)
        {
            if (aiState.AttackInProgress == 0)
                return;

            if (!IsAttacking(weapon.Phase))
            {
                aiState.AttackInProgress = 0;
                weapon.AiAttackTypeOverride = 0;
                return;
            }

            if (weapon.Phase == ActorWeaponAnimationPhase.AttackWindUp
                && weapon.AttackStrength >= math.saturate(aiState.DesiredAttackStrength))
            {
                weapon.AttackReleased = 1;
            }
        }

        static bool IsAttacking(ActorWeaponAnimationPhase phase)
            => phase == ActorWeaponAnimationPhase.AttackWindUp
               || phase == ActorWeaponAnimationPhase.AttackRelease
               || phase == ActorWeaponAnimationPhase.AttackFollow;

        void RequireActiveCombatComposition(ref SystemState systemState, Entity actor, Entity target)
        {
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has an invalid active combat target.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has no ActorHitAftermathState.");
            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has no ActorActiveMagicEffect buffer.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorHitAftermathState.");
        }

        bool IsActorUnableToAttack(ref SystemState systemState, Entity actor)
        {
            var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(actor);
            if (aftermath.KnockedDown != 0 || aftermath.KnockedOut != 0 || aftermath.HitRecovery != 0)
                return true;

            var effects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(actor, true);
            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, ParalyzeEffectId) > 0f;
        }

        bool IsActorDisabledOrDead(ref SystemState systemState, Entity actor)
        {
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled != 0)
            {
                return true;
            }

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(actor);
            var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(actor);
            if (aftermath.Dead != 0 && vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} is marked dead but still has positive health.");
            return vitals.CurrentHealth <= 0f || aftermath.Dead != 0;
        }

        void FaceTarget(ref SystemState systemState, ref LocalTransform actorTransform, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no LocalTransform.");

            float3 delta = systemState.EntityManager.GetComponentData<LocalTransform>(target).Position - actorTransform.Position;
            delta.y = 0f;
            if (math.lengthsq(delta) > 0.000001f)
                actorTransform.Rotation = quaternion.LookRotationSafe(math.normalize(delta), math.up());
        }

        bool CanStartMeleeAttack(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            Entity actor,
            in ActorLocalBounds actorBounds,
            in LocalTransform actorTransform,
            Entity target)
        {
            if (!TryResolveTargetBounds(ref systemState, target, out float3 targetBase, out float targetRadius, out float targetHeight, out float3 targetCenter))
                return false;

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(actor).WeaponContent,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            float reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
            float actorRadius = math.max(actorBounds.Extents.x, actorBounds.Extents.z) * math.max(0.01f, actorTransform.Scale);
            float distanceToBounds = math.max(0f, math.distance(ToHorizontal(actorTransform.Position), ToHorizontal(targetBase)) - actorRadius - targetRadius);
            if (distanceToBounds > reach)
                return false;

            if (!PassesCombatCone(ref content, actorTransform, targetBase, targetHeight))
                return false;

            float3 source = math.transform(
                float4x4.TRS(actorTransform.Position, actorTransform.Rotation, new float3(actorTransform.Scale)),
                actorBounds.Center);
            return ActorAiLineOfSightUtility.HasLineOfSightOrRequest(
                systemState.EntityManager,
                deferredPhysicsQueueEntity,
                fixedTick,
                actor,
                target,
                source,
                targetCenter);
        }

        bool TryResolveTargetBounds(ref SystemState systemState, 
            Entity target,
            out float3 targetBase,
            out float targetRadius,
            out float targetHeight,
            out float3 targetCenter)
        {
            targetBase = default;
            targetRadius = 0f;
            targetHeight = 0f;
            targetCenter = default;
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no LocalTransform.");

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            targetBase = transform.Position;
            if (systemState.EntityManager.HasComponent<ActorLocalBounds>(target))
            {
                var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(target);
                float scale = math.max(0.01f, transform.Scale);
                targetRadius = math.max(bounds.Extents.x, bounds.Extents.z) * scale;
                targetHeight = math.max(0.01f, bounds.Extents.y * 2f * scale);
                targetCenter = math.transform(float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)), bounds.Center);
                return true;
            }

            if (systemState.EntityManager.HasComponent<PlayerCharacterComponent>(target))
            {
                var player = systemState.EntityManager.GetComponentData<PlayerCharacterComponent>(target);
                targetRadius = math.max(0.01f, player.Radius);
                targetHeight = math.max(0.01f, player.StandingHeight);
                targetCenter = targetBase + new float3(0f, targetHeight * 0.5f, 0f);
                return true;
            }

            throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
        }

        static bool PassesCombatCone(ref RuntimeContentBlob content, in LocalTransform actorTransform, float3 targetBase, float targetHeight)
        {
            float combatAngleXY = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleXY) / 90f;
            float combatAngleZ = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleZ) / 90f;
            float3 forward = math.normalizesafe(math.rotate(actorTransform.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            float3 forwardXY = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
            float3 toTargetXY = math.normalizesafe(ToHorizontal(targetBase - actorTransform.Position), float3.zero);
            if (math.lengthsq(toTargetXY) <= 0f)
                return false;
            if (math.dot(toTargetXY, forwardXY) <= 0f)
                return false;
            if (math.abs(toTargetXY.x * forwardXY.z - toTargetXY.z * forwardXY.x) > combatAngleXY)
                return false;

            float actorVerticalAngle = forward.y;
            float3 toFeet = math.normalizesafe(targetBase - actorTransform.Position, float3.zero);
            float3 toHead = math.normalizesafe(targetBase + new float3(0f, targetHeight, 0f) - actorTransform.Position, float3.zero);
            return actorVerticalAngle - toHead.y <= combatAngleZ && actorVerticalAngle - toFeet.y >= -combatAngleZ;
        }

        ActorWeaponAttackType ChooseAttackType(ref RuntimeContentBlob content, in ContentReference weaponContent, ref Unity.Mathematics.Random random)
        {
            if (!weaponContent.IsValid)
                return ChooseRandomAttackType(ref random);

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                weaponContent,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            if (!hasWeapon)
                return ChooseRandomAttackType(ref random);

            int slash = (weapon.SlashMin + weapon.SlashMax) / 2;
            int chop = (weapon.ChopMin + weapon.ChopMax) / 2;
            int thrust = (weapon.ThrustMin + weapon.ThrustMax) / 2;
            int total = slash + chop + thrust;
            if (total <= 0)
                return ChooseRandomAttackType(ref random);

            float roll = random.NextFloat() * total;
            if (roll <= slash)
                return ActorWeaponAttackType.Slash;
            if (roll <= slash + thrust)
                return ActorWeaponAttackType.Thrust;
            return ActorWeaponAttackType.Chop;
        }

        static ActorWeaponAttackType ChooseRandomAttackType(ref Unity.Mathematics.Random random)
        {
            float roll = random.NextFloat();
            if (roll >= 2f / 3f)
                return ActorWeaponAttackType.Thrust;
            if (roll >= 1f / 3f)
                return ActorWeaponAttackType.Slash;
            return ActorWeaponAttackType.Chop;
        }

        float ResolveAttackCooldown(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor, ref Unity.Mathematics.Random random)
        {
            ulong gmstHash = IsCreature(ref systemState, ref content, actor)
                ? RuntimeContentKnownHashes.fCombatDelayCreature
                : RuntimeContentKnownHashes.fCombatDelayNPC;
            float baseDelay = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, gmstHash);
            return math.min(baseDelay + 0.01f * random.NextInt(100), baseDelay + 0.9f);
        }

        bool IsCreature(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has no ActorSpawnSource.");
            var source = systemState.EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Actor ref={PlacedRefId(ref systemState, actor)} has invalid ActorSpawnSource.");

            ref RuntimeActorDefBlob actorDef = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            return actorDef.Kind == ActorDefKind.Creature;
        }

        static float3 ToHorizontal(float3 value)
            => new(value.x, 0f, value.z);

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => entity != Entity.Null
               && systemState.EntityManager.Exists(entity)
               && systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}

