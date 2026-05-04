using System;
using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public static class ActorHitAftermathStateUtility
    {
        public static void MarkDead(ref ActorHitAftermathState state)
        {
            if (state.Dead != 0)
                return;

            state.Dead = 1;
            state.HitRecovery = 0;
            state.KnockedDownOneFrame = 0;
            state.KnockedDownOverOneFrame = 0;
            state.DeathAnimationFinished = 0;
            state.DeathAnimationGroup = default;
            state.Sequence = NextSequence(state.Sequence);
        }

        public static void Resurrect(ref ActorHitAftermathState state)
        {
            state.HitRecovery = 0;
            state.KnockedDown = 0;
            state.KnockedDownOneFrame = 0;
            state.KnockedDownOverOneFrame = 0;
            state.KnockedOut = 0;
            state.Dead = 0;
            state.DeathAnimationFinished = 0;
            state.DeathAnimationGroup = default;
            state.AnimatedSequence = 0;
            state.Sequence = NextSequence(state.Sequence);
        }

        public static void BumpSequence(ref ActorHitAftermathState state)
            => state.Sequence = NextSequence(state.Sequence);

        public static ActorHitAftermathState Require(EntityManager entityManager, Entity actor, string context)
        {
            if (!entityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"{context} actor has no ActorHitAftermathState.");
            return entityManager.GetComponentData<ActorHitAftermathState>(actor);
        }

        static uint NextSequence(uint sequence)
            => sequence == uint.MaxValue ? 1u : sequence + 1u;
    }
}
