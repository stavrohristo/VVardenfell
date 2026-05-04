using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    [UpdateBefore(typeof(PathGridTraversalRequestSystem))]
    public partial class ActorAiGreetingSystem : SystemBase
    {
        const string HelloDialogueId = "hello";
        const float InitialGreetingDelaySeconds = 0.5f;

        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ActorVitalSet>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<ActorAiGreetingState>();
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorAiPassiveGreetingSayRequest>();
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            int greetDistanceMultiplier = contentDb.RequireGameSettingInt("iGreetDistanceMultiplier");
            int greetDuration = contentDb.RequireGameSettingInt("iGreetDuration");
            float greetDistanceResetMw = contentDb.RequireGameSettingFloat("fGreetDistanceReset");

            bool hasHelloDialogue = TryResolveHelloVoiceDialogue(contentDb, out int helloDialogueIndex);
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var sayRequests = EntityManager.GetBuffer<ActorAiPassiveGreetingSayRequest>(runtimeEntity);
            Entity player = _playerQuery.GetSingletonEntity();
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(player);
            var playerVitals = EntityManager.GetComponentData<ActorVitalSet>(player);
            if (playerVitals.CurrentHealth <= 0f)
            {
                ResetAllGreetingsAndRelease();
                return;
            }

            float elapsedSeconds = math.max(0f, SystemAPI.Time.DeltaTime);
            float resetDistance = math.max(0f, greetDistanceResetMw) * WorldScale.MwUnitsToMeters;
            float resetDistanceSq = resetDistance * resetDistance;

            foreach (var (greetingRef, aiSettings, source, placedRef, transformRef, vitals, entity) in SystemAPI
                         .Query<
                             RefRW<ActorAiGreetingState>,
                             RefRO<ActorAiSettingsState>,
                             RefRO<ActorSpawnSource>,
                             RefRO<PlacedRefIdentity>,
                             RefRW<LocalTransform>,
                             RefRO<ActorVitalSet>>()
                         .WithNone<PlayerTag>()
                         .WithEntityAccess())
            {
                ref var greeting = ref greetingRef.ValueRW;
                if (vitals.ValueRO.CurrentHealth <= 0f || IsCombatBlocked(entity) || !CanPassiveGreetCurrentPackage(entity))
                {
                    if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                        ReleaseGreetingInterrupt(entity);
                    greeting = default;
                    continue;
                }

                float3 actorPosition = transformRef.ValueRO.Position;
                float3 playerPosition = playerTransform.Position;
                float distanceSq = math.lengthsq(playerPosition - actorPosition);
                if (greeting.Phase != (byte)ActorAiGreetingPhase.None && distanceSq >= resetDistanceSq)
                {
                    if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                        ReleaseGreetingInterrupt(entity);
                    greeting = default;
                    continue;
                }

                if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                {
                    greeting.Timer += elapsedSeconds;
                    FacePlayer(ref transformRef.ValueRW, playerPosition);
                    InterruptMovementForGreeting(entity, forceIdle: true);
                    if (greeting.Timer >= math.max(0f, greetDuration))
                    {
                        greeting.Phase = (byte)ActorAiGreetingPhase.Done;
                        greeting.Timer = 0f;
                        ReleaseGreetingInterrupt(entity);
                    }

                    continue;
                }

                if (greeting.Phase == (byte)ActorAiGreetingPhase.Done)
                    continue;

                float helloDistance = math.max(0f, aiSettings.ValueRO.Hello) * math.max(0f, greetDistanceMultiplier) * WorldScale.MwUnitsToMeters;
                if (helloDistance <= 0f || distanceSq > helloDistance * helloDistance)
                {
                    greeting.Timer = 0f;
                    continue;
                }

                if (!ActorAiLineOfSightUtility.HasLineOfSightOrRequest(
                        EntityManager,
                        entity,
                        player,
                        EyePosition(playerPosition),
                        EyePosition(actorPosition)))
                {
                    greeting.Timer = 0f;
                    continue;
                }

                greeting.Timer += elapsedSeconds;
                if (greeting.Timer < InitialGreetingDelaySeconds)
                    continue;

                if (!hasHelloDialogue)
                {
                    greeting.Phase = (byte)ActorAiGreetingPhase.Done;
                    greeting.Timer = 0f;
                    continue;
                }

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        contentDb,
                        EntityManager,
                        entity,
                        source.ValueRO.Definition,
                        helloDialogueIndex,
                        choice: 0,
                        out int infoIndex,
                        out string unsupportedReason))
                {
                    if (!string.IsNullOrWhiteSpace(unsupportedReason))
                        throw new InvalidOperationException($"[VVardenfell][AI] Passive hello dialogue for actor ref={placedRef.ValueRO.Value} is unsupported: {unsupportedReason}");

                    greeting.Phase = (byte)ActorAiGreetingPhase.Done;
                    greeting.Timer = 0f;
                    continue;
                }

                ref readonly var info = ref contentDb.Data.DialogueInfos[infoIndex];
                if (!string.IsNullOrWhiteSpace(info.SoundFile))
                {
                    sayRequests.Add(new ActorAiPassiveGreetingSayRequest
                    {
                        TargetEntity = entity,
                        TargetPlacedRefId = placedRef.ValueRO.Value,
                        VoicePath = RuntimeFixedStringUtility.ToFixed512OrDefaultWhiteSpace(info.SoundFile),
                        Subtitle = RuntimeFixedStringUtility.ToFixed512OrDefaultWhiteSpace(info.Response),
                    });
                }
                else if (HudUserPreferences.ShowSubtitles
                         && !string.IsNullOrWhiteSpace(info.Response)
                         && SystemAPI.TryGetSingletonRW<RuntimeSubtitleState>(out var subtitle))
                {
                    var subtitleText = RuntimeFixedStringUtility.ToFixed512OrDefaultWhiteSpace(info.Response);
                    RuntimeShellStateUtility.ShowSubtitle(
                        ref subtitle.ValueRW,
                        subtitleText,
                        RuntimeShellStateUtility.EstimateSubtitleDurationSeconds(subtitleText));
                }

                greeting.Phase = (byte)ActorAiGreetingPhase.InProgress;
                greeting.Timer = 0f;
                FacePlayer(ref transformRef.ValueRW, playerPosition);
                InterruptMovementForGreeting(entity, forceIdle: true);
            }
        }

        void ResetAllGreetingsAndRelease()
        {
            foreach (var (greeting, entity) in SystemAPI.Query<RefRW<ActorAiGreetingState>>().WithEntityAccess())
            {
                if (greeting.ValueRO.Phase == (byte)ActorAiGreetingPhase.InProgress)
                    ReleaseGreetingInterrupt(entity);
                greeting.ValueRW = default;
            }
        }

        bool IsCombatBlocked(Entity actor)
        {
            if (!EntityManager.HasComponent<ActorCombatTargetState>(actor))
                return false;

            return EntityManager.GetComponentData<ActorCombatTargetState>(actor).Active != 0;
        }

        bool CanPassiveGreetCurrentPackage(Entity actor)
        {
            if (!EntityManager.HasComponent<ActorAiState>(actor) || !EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
                return true;

            var aiState = EntityManager.GetComponentData<ActorAiState>(actor);
            var packages = EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return true;

            byte packageType = packages[aiState.CurrentPackageIndex].Type;
            return packageType == (byte)ActorAiRuntimePackageType.Wander
                   || packageType == (byte)ActorAiRuntimePackageType.Travel;
        }

        void InterruptMovementForGreeting(Entity actor, bool forceIdle)
        {
            if (EntityManager.HasComponent<ActorAiState>(actor) && EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
            {
                var packages = EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
                var aiState = EntityManager.GetComponentData<ActorAiState>(actor);
                if ((uint)aiState.CurrentPackageIndex < (uint)packages.Length)
                {
                    byte packageType = packages[aiState.CurrentPackageIndex].Type;
                    if (packageType != (byte)ActorAiRuntimePackageType.Wander
                        && packageType != (byte)ActorAiRuntimePackageType.Travel)
                    {
                        return;
                    }
                }

                if (forceIdle && aiState.ActiveIdleGroupHash == 0UL)
                    aiState.PendingIdleGroup = 2;
                aiState.WaitUntilTime = float.PositiveInfinity;
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
                EntityManager.SetComponentData(actor, aiState);
            }

            if (EntityManager.HasComponent<PathGridTraversalState>(actor))
                EntityManager.SetComponentData(actor, new PathGridTraversalState());
            if (EntityManager.HasComponent<PathGridTraversalPendingRequest>(actor))
            {
                EntityManager.SetComponentData(actor, default(PathGridTraversalPendingRequest));
                EntityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            }
            if (EntityManager.HasComponent<PathGridTraversalAwaitingResult>(actor))
                EntityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            if (EntityManager.HasBuffer<PathGridTraversalNode>(actor))
                EntityManager.GetBuffer<PathGridTraversalNode>(actor).Clear();
        }

        void ReleaseGreetingInterrupt(Entity actor)
        {
            if (!EntityManager.HasComponent<ActorAiState>(actor))
                return;

            var aiState = EntityManager.GetComponentData<ActorAiState>(actor);
            if (aiState.Status == (byte)ActorAiPlannerStatus.Waiting && float.IsPositiveInfinity(aiState.WaitUntilTime))
            {
                aiState.WaitUntilTime = 0f;
                aiState.Status = (byte)ActorAiPlannerStatus.Idle;
            }

            aiState.PendingIdleGroup = 0;
            EntityManager.SetComponentData(actor, aiState);
        }

        static void FacePlayer(ref LocalTransform actorTransform, float3 playerPosition)
        {
            float3 delta = playerPosition - actorTransform.Position;
            delta.y = 0f;
            if (math.lengthsq(delta) <= 0.000001f)
                return;

            actorTransform.Rotation = quaternion.LookRotationSafe(math.normalizesafe(delta), math.up());
        }

        static float3 EyePosition(float3 position)
            => position + new float3(0f, 1.5f, 0f);

        static bool TryResolveHelloVoiceDialogue(RuntimeContentDatabase contentDb, out int dialogueIndex)
        {
            dialogueIndex = -1;
            if (contentDb == null
                || !contentDb.TryGetDialogueHandle(HelloDialogueId, out var handle)
                || !handle.IsValid)
            {
                return false;
            }

            ref readonly var dialogue = ref contentDb.Get(handle);
            if (dialogue.Type != DialogueDefType.Voice)
                return false;

            dialogueIndex = handle.Index;
            return true;
        }
    }
}
