using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(MorrowindAudioPresentationSystemGroup))]
    [UpdateBefore(typeof(ActorGpuAnimationComputeSystem))]
    public partial struct ActorHeadSayAnimationSystem : ISystem
    {
        EntityQuery _headQuery;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<ActorHeadAnimationState> _headHandle;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;
        ComponentLookup<LocalPlayerVisual> _localPlayerVisualLookup;

        public void OnCreate(ref SystemState systemState)
        {
            _headQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<ActorHeadAnimationState>(),
                ComponentType.ReadOnly<ActorPresentation>());
            _entityHandle = systemState.GetEntityTypeHandle();
            _headHandle = systemState.GetComponentTypeHandle<ActorHeadAnimationState>(isReadOnly: false);
            _placedRefLookup = systemState.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);
            _localPlayerVisualLookup = systemState.GetComponentLookup<LocalPlayerVisual>(isReadOnly: true);
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptActiveSay>();
            systemState.RequireForUpdate(_headQuery);
            systemState.RequireForUpdate<RuntimePresentationEnabled>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var activeSays = systemState.EntityManager.GetBuffer<MorrowindScriptActiveSay>(runtimeEntity, true);
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);

            _entityHandle.Update(ref systemState);
            _headHandle.Update(ref systemState);
            _placedRefLookup.Update(ref systemState);
            _localPlayerVisualLookup.Update(ref systemState);

            systemState.Dependency = new HeadSayAnimationJob
            {
                EntityHandle = _entityHandle,
                HeadHandle = _headHandle,
                PlacedRefLookup = _placedRefLookup,
                LocalPlayerVisualLookup = _localPlayerVisualLookup,
                ActiveSays = activeSays.AsNativeArray(),
                DeltaTime = deltaTime,
            }.ScheduleParallel(_headQuery, systemState.Dependency);
        }

        [BurstCompile]
        struct HeadSayAnimationJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityHandle;
            public ComponentTypeHandle<ActorHeadAnimationState> HeadHandle;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<LocalPlayerVisual> LocalPlayerVisualLookup;
            [ReadOnly] public NativeArray<MorrowindScriptActiveSay> ActiveSays;
            public float DeltaTime;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var heads = chunk.GetNativeArray(ref HeadHandle);
                var entities = chunk.GetNativeArray(EntityHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var head = heads[i];
                    if (head.HasHeadMorph == 0)
                        continue;

                    Entity entity = entities[i];
                    uint placedRefId = PlacedRefLookup.HasComponent(entity)
                        ? PlacedRefLookup[entity].Value
                        : 0u;
                    Entity playerSource = LocalPlayerVisualLookup.HasComponent(entity)
                        ? LocalPlayerVisualLookup[entity].Player
                        : Entity.Null;

                    if (TryFindSay(ActiveSays, entity, placedRefId, playerSource, out float loudness))
                    {
                        head.CurrentTime = math.lerp(head.TalkStart, head.TalkStop, math.saturate(loudness * 2f));
                        head.Saying = 1;
                        heads[i] = head;
                        continue;
                    }

                    head.Saying = 0;
                    AdvanceBlink(ref head, DeltaTime);
                    heads[i] = head;
                }
            }
        }

        static bool TryFindSay(
            NativeArray<MorrowindScriptActiveSay> activeSays,
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
