using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Pathfinding;

namespace VVardenfell.Runtime.Components
{
    public static class ActorHitAftermathStateUtility
    {
        public static void MarkDead(ref ActorHitAftermathState state)
        {
            state.HitRecovery = 0;
            state.KnockedDown = 0;
            state.KnockedDownOneFrame = 0;
            state.KnockedDownOverOneFrame = 0;
            state.KnockedOut = 0;
            state.DeathAnimationFinished = 0;
            state.DeathAnimationGroup = default;
            state.Sequence = NextSequence(state.Sequence);
        }

        public static void MarkDead(EntityManager entityManager, Entity actor, ref ActorVitalSet vitals, ref ActorHitAftermathState state)
        {
            if (!entityManager.HasComponent<ActorDead>(actor))
                throw new InvalidOperationException("[VVardenfell][Aftermath] Actor has no ActorDead component.");

            vitals.CurrentHealth = 0f;
            if (!entityManager.IsComponentEnabled<ActorDead>(actor))
            {
                entityManager.SetComponentEnabled<ActorDead>(actor, true);
                SetAftermathAnimationActive(entityManager, actor, true);
                MarkDead(ref state);
                StopDeadActorActions(entityManager, actor);
            }
        }

        public static void Resurrect(ref ActorHitAftermathState state)
        {
            state.HitRecovery = 0;
            state.KnockedDown = 0;
            state.KnockedDownOneFrame = 0;
            state.KnockedDownOverOneFrame = 0;
            state.KnockedOut = 0;
            state.DeathAnimationFinished = 0;
            state.DeathAnimationGroup = default;
            state.AnimatedSequence = 0;
            state.Sequence = NextSequence(state.Sequence);
        }

        public static void Resurrect(EntityManager entityManager, Entity actor, ref ActorVitalSet vitals, ref ActorHitAftermathState state)
        {
            if (!entityManager.HasComponent<ActorDead>(actor))
                throw new InvalidOperationException("[VVardenfell][Aftermath] Actor has no ActorDead component.");

            if (vitals.CurrentHealth <= 0f)
                vitals.CurrentHealth = vitals.ModifiedHealthBase > 0f ? vitals.ModifiedHealthBase : 1f;
            if (entityManager.IsComponentEnabled<ActorDead>(actor))
                entityManager.SetComponentEnabled<ActorDead>(actor, false);
            Resurrect(ref state);
            SetAftermathAnimationActive(entityManager, actor, false);
        }

        public static void BumpSequence(ref ActorHitAftermathState state)
            => state.Sequence = NextSequence(state.Sequence);

        public static bool IsDead(EntityManager entityManager, Entity actor)
            => entityManager.HasComponent<ActorDead>(actor) && entityManager.IsComponentEnabled<ActorDead>(actor);

        public static bool IsAlive(EntityManager entityManager, Entity actor)
            => !IsDead(entityManager, actor);

        public static ActorHitAftermathState Require(EntityManager entityManager, Entity actor, string context)
        {
            if (!entityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"{context} actor has no ActorHitAftermathState.");
            return entityManager.GetComponentData<ActorHitAftermathState>(actor);
        }

        public static ActorHitAftermathState Require(EntityManager entityManager, Entity actor)
        {
            if (!entityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException("[VVardenfell][Aftermath] Actor has no ActorHitAftermathState.");
            return entityManager.GetComponentData<ActorHitAftermathState>(actor);
        }

        static uint NextSequence(uint sequence)
            => sequence == uint.MaxValue ? 1u : sequence + 1u;

        static void SetAftermathAnimationActive(EntityManager entityManager, Entity actor, bool active)
        {
            if (!entityManager.HasComponent<ActorHitAftermathAnimationActive>(actor))
                throw new InvalidOperationException("[VVardenfell][Aftermath] Actor has no ActorHitAftermathAnimationActive component.");
            entityManager.SetComponentEnabled<ActorHitAftermathAnimationActive>(actor, active);
        }

        static void StopDeadActorActions(EntityManager entityManager, Entity actor)
        {
            if (entityManager.HasComponent<MorrowindMovementInput>(actor))
                entityManager.SetComponentData(actor, default(MorrowindMovementInput));

            if (entityManager.HasComponent<MorrowindMovementState>(actor))
            {
                var movement = entityManager.GetComponentData<MorrowindMovementState>(actor);
                movement.Inertia = float3.zero;
                movement.LastVelocity = float3.zero;
                movement.LocalMove = float2.zero;
                movement.SpeedFactor = 0f;
                movement.JumpAccepted = false;
                movement.RunHeld = false;
                movement.SneakHeld = false;
                movement.IsStrafing = false;
                entityManager.SetComponentData(actor, movement);
            }

            if (entityManager.HasComponent<PathGridTraversalState>(actor))
                entityManager.SetComponentData(actor, default(PathGridTraversalState));
            if (entityManager.HasComponent<PathGridTraversalPendingRequest>(actor))
            {
                entityManager.SetComponentData(actor, default(PathGridTraversalPendingRequest));
                entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            }
            if (entityManager.HasComponent<PathGridTraversalAwaitingResult>(actor))
                entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            if (entityManager.HasBuffer<PathGridTraversalNode>(actor))
                entityManager.GetBuffer<PathGridTraversalNode>(actor).Clear();

            if (entityManager.HasComponent<ActorAiState>(actor))
            {
                var aiState = entityManager.GetComponentData<ActorAiState>(actor);
                aiState.CurrentNodeIndex = -1;
                aiState.GoalNodeIndex = -1;
                aiState.WaitUntilTime = 0f;
                aiState.FollowActive = 0;
                aiState.PendingIdleGroup = 0;
                aiState.ActiveIdleGroupHash = 0UL;
                aiState.Status = (byte)ActorAiPlannerStatus.Idle;
                entityManager.SetComponentData(actor, aiState);
            }

            if (entityManager.HasComponent<ActorMeleeCombatAiState>(actor))
                entityManager.SetComponentData(actor, default(ActorMeleeCombatAiState));

            if (entityManager.HasComponent<ActorWeaponAnimationState>(actor))
            {
                var weapon = entityManager.GetComponentData<ActorWeaponAnimationState>(actor);
                weapon.Drawn = 0;
                weapon.Phase = ActorWeaponAnimationPhase.Hidden;
                weapon.AttackType = 0;
                weapon.AttackStrength = 0f;
                weapon.AttackMinTime = 0f;
                weapon.AttackMaxTime = 0f;
                weapon.ReadyWeaponTogglePressed = 0;
                weapon.AttackHeld = 0;
                weapon.AttackPressed = 0;
                weapon.AttackReleased = 0;
                weapon.ReleaseQueued = 0;
                weapon.AiAttackTypeOverride = 0;
                weapon.AiAttackType = 0;
                weapon.MeleeHitPending = 0;
                weapon.MeleeHitAttackType = 0;
                weapon.MeleeHitAttackStrength = 0f;
                weapon.MeleeHitWeaponContent = default;
                weapon.MeleeSwingPending = 0;
                weapon.MeleeSwingAttackStrength = 0f;
                weapon.MeleeSwingWeaponContent = default;
                weapon.SpellCastPressed = 0;
                weapon.SpellCastRange = 0;
                weapon.SpellCastSourceKind = 0;
                weapon.SpellCastSpell = default;
                weapon.SpellCastEnchantment = default;
                weapon.SpellCastItemContent = default;
                weapon.SpellCastInventoryIndex = 0;
                weapon.SpellCastReleasePending = 0;
                weapon.SpellCastReleaseSourceKind = 0;
                weapon.SpellCastReleaseSpell = default;
                weapon.SpellCastReleaseEnchantment = default;
                weapon.SpellCastReleaseItemContent = default;
                weapon.SpellCastReleaseInventoryIndex = 0;
                entityManager.SetComponentData(actor, weapon);
            }

            if (entityManager.HasBuffer<ActorAnimationOverlayState>(actor))
                entityManager.GetBuffer<ActorAnimationOverlayState>(actor).Clear();
        }
    }
}
