using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    [UpdateBefore(typeof(PathGridTraversalRequestSystem))]
    [BurstCompile]
    public partial struct ActorAiGreetingSystem : ISystem
    {
        const float InitialGreetingDelaySeconds = 0.5f;

        EntityQuery _playerQuery;
        MorrowindDialogueFilterUtility.QueryContext _dialogueFilterQueries;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ActorVitalSet>());

            _dialogueFilterQueries = new MorrowindDialogueFilterUtility.QueryContext
            {
                PlayerFaction = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>()),
                InteriorTransition = systemState.GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>()),
                StreamingConfig = systemState.GetEntityQuery(ComponentType.ReadOnly<StreamingConfig>()),
                ScriptGlobal = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindScriptGlobalValue>()),
                QuestJournal = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindQuestJournalIndex>()),
                PlayerIdentity = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorIdentitySet>()),
                PlayerCrime = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>()),
                Player = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>()),
                PlayerVitals = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorVitalSet>()),
                DialogueFactionReaction = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindDialogueState>(), ComponentType.ReadOnly<MorrowindFactionReactionOverride>()),
                PlayerEquipment = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorEquipmentSlot>()),
                PlayerAttribute = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorAttributeSet>()),
                PlayerSkill = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorSkillSet>()),
            };

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<ActorAiGreetingState>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorAiPassiveGreetingSayRequest>();
            systemState.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            systemState.RequireForUpdate<MorrowindPhysicsFrameState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
            systemState.RequireForUpdate<RuntimeHudPreferences>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            ref RuntimeWorldCellBlob worldCells = ref SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>().Blob.Value;

            int greetDistanceMultiplier = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref contentBlob, RuntimeContentKnownHashes.iGreetDistanceMultiplier);
            int greetDuration = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref contentBlob, RuntimeContentKnownHashes.iGreetDuration);
            float greetDistanceResetMw = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fGreetDistanceReset);

            bool hasHelloDialogue = TryResolveHelloVoiceDialogue(ref contentBlob, out int helloDialogueIndex);
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var sayRequests = systemState.EntityManager.GetBuffer<ActorAiPassiveGreetingSayRequest>(runtimeEntity);
            Entity player = _playerQuery.GetSingletonEntity();
            var playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(player);
            var playerVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(player);
            Entity deferredPhysicsQueueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            uint fixedTick = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick;
            bool showSubtitles = SystemAPI.GetSingleton<RuntimeHudPreferences>().ShowSubtitles != 0;
            if (playerVitals.CurrentHealth <= 0f)
            {
                ResetAllGreetingsAndRelease(ref systemState);
                return;
            }

            float elapsedSeconds = math.max(0f, SystemAPI.Time.DeltaTime);
            float resetDistance = math.max(0f, greetDistanceResetMw) * WorldScale.MwUnitsToMeters;
            float resetDistanceSq = resetDistance * resetDistance;
            bool queuedPhysicsRequest = false;

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
                if (vitals.ValueRO.CurrentHealth <= 0f || IsCombatBlocked(ref systemState, entity) || !CanPassiveGreetCurrentPackage(ref systemState, entity))
                {
                    if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                        ReleaseGreetingInterrupt(ref systemState, entity);
                    greeting = default;
                    continue;
                }

                float3 actorPosition = transformRef.ValueRO.Position;
                float3 playerPosition = playerTransform.Position;
                float distanceSq = math.lengthsq(playerPosition - actorPosition);
                if (greeting.Phase != (byte)ActorAiGreetingPhase.None && distanceSq >= resetDistanceSq)
                {
                    if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                        ReleaseGreetingInterrupt(ref systemState, entity);
                    greeting = default;
                    continue;
                }

                if (greeting.Phase == (byte)ActorAiGreetingPhase.InProgress)
                {
                    greeting.Timer += elapsedSeconds;
                    FacePlayer(ref transformRef.ValueRW, playerPosition);
                    InterruptMovementForGreeting(ref systemState, entity, forceIdle: true);
                    if (greeting.Timer >= math.max(0f, greetDuration))
                    {
                        greeting.Phase = (byte)ActorAiGreetingPhase.Done;
                        greeting.Timer = 0f;
                        ReleaseGreetingInterrupt(ref systemState, entity);
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

                if (!ActorAiLineOfSightUtility.TryGetLineOfSightOrRequest(
                        systemState.EntityManager,
                        deferredPhysicsQueueEntity,
                        fixedTick,
                        entity,
                        player,
                        EyePosition(playerPosition),
                        EyePosition(actorPosition),
                        out bool hasLineOfSight,
                        out bool queuedLineOfSightRequest,
                        markPending: false))
                {
                    queuedPhysicsRequest |= queuedLineOfSightRequest;
                    continue;
                }

                if (!hasLineOfSight)
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

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                        ref contentBlob,
                        ref worldCells,
                        systemState.EntityManager,
                        entity,
                        source.ValueRO.Definition,
                        helloDialogueIndex,
                        choice: 0,
                        ref _dialogueFilterQueries,
                        out int infoIndex,
                        out byte unsupportedFunction))
                {
                    if (unsupportedFunction != 0)
                        throw new InvalidOperationException("[VVardenfell][AI] Passive hello dialogue has an unsupported select function.");

                    greeting.Phase = (byte)ActorAiGreetingPhase.Done;
                    greeting.Timer = 0f;
                    continue;
                }

                ref var info = ref contentBlob.DialogueInfos[infoIndex];
                FixedString512Bytes soundFile = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.SoundFile);
                FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
                if (soundFile.Length > 0)
                {
                    sayRequests.Add(new ActorAiPassiveGreetingSayRequest
                    {
                        TargetEntity = entity,
                        TargetPlacedRefId = placedRef.ValueRO.Value,
                        VoicePath = soundFile,
                        Subtitle = response,
                    });
                }
                else if (showSubtitles
                         && response.Length > 0
                         && SystemAPI.TryGetSingletonRW<RuntimeSubtitleState>(out var subtitle))
                {
                    RuntimeShellStateUtility.ShowSubtitle(
                        ref subtitle.ValueRW,
                        response,
                        RuntimeShellStateUtility.EstimateSubtitleDurationSeconds(response));
                }

                greeting.Phase = (byte)ActorAiGreetingPhase.InProgress;
                greeting.Timer = 0f;
                FacePlayer(ref transformRef.ValueRW, playerPosition);
                InterruptMovementForGreeting(ref systemState, entity, forceIdle: true);
            }

            if (queuedPhysicsRequest && !systemState.EntityManager.IsComponentEnabled<DeferredPhysicsQueryPending>(deferredPhysicsQueueEntity))
                systemState.EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(deferredPhysicsQueueEntity, true);
        }

        void ResetAllGreetingsAndRelease(ref SystemState systemState)
        {
            foreach (var (greeting, entity) in SystemAPI.Query<RefRW<ActorAiGreetingState>>().WithEntityAccess())
            {
                if (greeting.ValueRO.Phase == (byte)ActorAiGreetingPhase.InProgress)
                    ReleaseGreetingInterrupt(ref systemState, entity);
                greeting.ValueRW = default;
            }
        }

        bool IsCombatBlocked(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorCombatTargetState>(actor))
                return false;

            return systemState.EntityManager.GetComponentData<ActorCombatTargetState>(actor).Active != 0;
        }

        bool CanPassiveGreetCurrentPackage(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorAiState>(actor) || !systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
                return true;

            var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
            var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return true;

            byte packageType = packages[aiState.CurrentPackageIndex].Type;
            return packageType == (byte)ActorAiRuntimePackageType.Wander
                   || packageType == (byte)ActorAiRuntimePackageType.Travel;
        }

        void InterruptMovementForGreeting(ref SystemState systemState, Entity actor, bool forceIdle)
        {
            if (systemState.EntityManager.HasComponent<ActorAiState>(actor) && systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
            {
                var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
                var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
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
                systemState.EntityManager.SetComponentData(actor, aiState);
            }

            if (systemState.EntityManager.HasComponent<PathGridTraversalState>(actor))
                systemState.EntityManager.SetComponentData(actor, new PathGridTraversalState());
            if (systemState.EntityManager.HasComponent<PathGridTraversalPendingRequest>(actor))
            {
                systemState.EntityManager.SetComponentData(actor, default(PathGridTraversalPendingRequest));
                systemState.EntityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            }
            if (systemState.EntityManager.HasComponent<PathGridTraversalAwaitingResult>(actor))
                systemState.EntityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            if (systemState.EntityManager.HasBuffer<PathGridTraversalNode>(actor))
                systemState.EntityManager.GetBuffer<PathGridTraversalNode>(actor).Clear();
        }

        void ReleaseGreetingInterrupt(ref SystemState systemState, Entity actor)
        {
            if (!systemState.EntityManager.HasComponent<ActorAiState>(actor))
                return;

            var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
            if (aiState.Status == (byte)ActorAiPlannerStatus.Waiting && float.IsPositiveInfinity(aiState.WaitUntilTime))
            {
                aiState.WaitUntilTime = 0f;
                aiState.Status = (byte)ActorAiPlannerStatus.Idle;
            }

            aiState.PendingIdleGroup = 0;
            systemState.EntityManager.SetComponentData(actor, aiState);
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

        static bool TryResolveHelloVoiceDialogue(ref RuntimeContentBlob contentBlob, out int dialogueIndex)
        {
            dialogueIndex = -1;
            if (!RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentKnownHashes.hello, out var handle)
                || !handle.IsValid)
            {
                return false;
            }

            ref var dialogue = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
            if (dialogue.Type != DialogueDefType.Voice)
                return false;

            dialogueIndex = handle.Index;
            return true;
        }
    }
}
