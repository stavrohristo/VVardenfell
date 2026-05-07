using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(MorrowindAudioPresentationSystemGroup))]
    [UpdateBefore(typeof(ActorGpuAnimationComputeSystem))]
    public partial struct ActorHeadSayAnimationSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptActiveSay>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var activeSays = systemState.EntityManager.GetBuffer<MorrowindScriptActiveSay>(runtimeEntity, true);
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);

            foreach (var (head, entity) in
                     SystemAPI.Query<RefRW<ActorHeadAnimationState>>()
                         .WithAll<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (head.ValueRO.HasHeadMorph == 0)
                    continue;

                uint placedRefId = systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                    ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                    : 0u;
                Entity playerSource = systemState.EntityManager.HasComponent<LocalPlayerVisual>(entity)
                    ? systemState.EntityManager.GetComponentData<LocalPlayerVisual>(entity).Player
                    : Entity.Null;

                if (TryFindSay(activeSays, entity, placedRefId, playerSource, out float loudness))
                {
                    head.ValueRW.CurrentTime = math.lerp(head.ValueRO.TalkStart, head.ValueRO.TalkStop, math.saturate(loudness * 2f));
                    head.ValueRW.Saying = 1;
                    continue;
                }

                head.ValueRW.Saying = 0;
                AdvanceBlink(ref head.ValueRW, deltaTime);
            }
        }

        static bool TryFindSay(
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            Entity entity,
            uint placedRefId,
            Entity playerSource,
            out float loudness)
        {
            for (int i = 0; i < activeSays.Length; i++)
            {
                var active = activeSays[i];
                bool matches = placedRefId != 0u && active.SourcePlacedRefId != 0u
                    ? active.SourcePlacedRefId == placedRefId
                    : active.SourceEntity == entity
                      || (playerSource != Entity.Null && active.SourceEntity == playerSource);
                if (!matches)
                    continue;

                loudness = active.Loudness;
                return true;
            }

            loudness = 0f;
            return false;
        }

        static void AdvanceBlink(ref ActorHeadAnimationState head, float deltaTime)
        {
            head.BlinkTimer += deltaTime;
            float duration = math.max(0f, head.BlinkStop - head.BlinkStart);
            if (head.BlinkTimer >= 0f && head.BlinkTimer <= duration)
                head.CurrentTime = head.BlinkStart + head.BlinkTimer;
            else
                head.CurrentTime = head.BlinkStop;

            if (head.BlinkTimer > duration)
                head.BlinkTimer = ResolveBlinkTimer(ref head.RandomState);
        }

        static float ResolveBlinkTimer(ref uint randomState)
            => -(2f + RollDice(ref randomState, 6u));

        static uint RollDice(ref uint state, uint max)
        {
            if (max == 0u)
                return 0u;
            state = state == 0u ? 1u : state;
            state = 1664525u * state + 1013904223u;
            return state % max;
        }
    }
}
