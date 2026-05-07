using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [BurstCompile]
    public unsafe partial struct MorrowindScriptInterpreterSystem : ISystem
    {
        EntityQuery _scriptQuery;
        EntityQuery _globalScriptQuery;
        EntityQuery _playerInventoryQuery;
        BufferLookup<MorrowindScriptGlobalValue> _globalsLookup;
        BufferLookup<MorrowindActorDeathCount> _deathCountsLookup;
        BufferLookup<MorrowindQuestJournalIndex> _questJournalLookup;
        BufferLookup<ActorInventoryItem> _actorInventoryLookup;
        BufferLookup<ContainerSessionItem> _containerSessionItemLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefStateLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefIdentityLookup;
        ComponentLookup<LocalTransform> _transformLookup;
        ComponentLookup<PlacedRefInitialTransform> _initialTransformLookup;
        ComponentLookup<ActorVitalSet> _actorVitalLookup;
        ComponentLookup<ActorHitAftermathState> _actorHitAftermathLookup;
        ComponentLookup<MorrowindActorDeathCounted> _actorDeathCountedLookup;
        ComponentLookup<MorrowindActorOnDeathConsumed> _actorOnDeathConsumedLookup;
        byte _hasLastCellContext;
        byte _lastInteriorActive;
        int2 _lastExteriorCell;
        ulong _lastInteriorCellHash;

        static readonly ProfilerMarker k_RequirementScan = new("VV.MWScript.Interpreter.RequirementScan");
        static readonly ProfilerMarker k_SnapshotPrep = new("VV.MWScript.Interpreter.SnapshotPrep");
        static readonly ProfilerMarker k_RuntimeSnapshotPrep = new("VV.MWScript.Interpreter.SnapshotPrep.Runtime");
        static readonly ProfilerMarker k_PlayerSnapshotPrep = new("VV.MWScript.Interpreter.SnapshotPrep.Player");
        static readonly ProfilerMarker k_ActorSnapshotPrep = new("VV.MWScript.Interpreter.SnapshotPrep.Actor");
        static readonly ProfilerMarker k_LineOfSightSnapshotPrep = new("VV.MWScript.Interpreter.SnapshotPrep.LineOfSight");
        static readonly ProfilerMarker k_ScheduleJobs = new("VV.MWScript.Interpreter.ScheduleJobs");
        static readonly ProfilerMarker k_CompleteJobs = new("VV.MWScript.Interpreter.CompleteJobs");
        static readonly ProfilerMarker k_PlaybackCommands = new("VV.MWScript.Interpreter.PlaybackCommands");
        static readonly ProfilerMarker k_DisposeSnapshots = new("VV.MWScript.Interpreter.DisposeSnapshots");
        static readonly ProfilerMarker k_ExternalActorLocalsSnapshot = new("VV.MWScript.ActorSnapshot.ExternalActorLocals");
        static readonly ProfilerMarker k_ActorAiStatusSnapshot = new("VV.MWScript.ActorSnapshot.AiStatus");
        static readonly ProfilerMarker k_ActorCombatTargetSnapshot = new("VV.MWScript.ActorSnapshot.CombatTarget");
        static readonly ProfilerMarker k_LockStateSnapshot = new("VV.MWScript.ActorSnapshot.LockState");
        static readonly ProfilerMarker k_ActorEventSnapshot = new("VV.MWScript.ActorSnapshot.Event");
        static readonly ProfilerMarker k_ActorVitalSnapshot = new("VV.MWScript.ActorSnapshot.Vital");
        static readonly ProfilerMarker k_ActorAttributeSnapshot = new("VV.MWScript.ActorSnapshot.Attribute");
        static readonly ProfilerMarker k_ActorActiveEffectSnapshot = new("VV.MWScript.ActorSnapshot.ActiveEffect");
        static readonly ProfilerMarker k_ActorDiseaseSnapshot = new("VV.MWScript.ActorSnapshot.Disease");
        static readonly ProfilerMarker k_ActorIdentitySnapshot = new("VV.MWScript.ActorSnapshot.Identity");
        static readonly ProfilerMarker k_ActorAiSettingSnapshot = new("VV.MWScript.ActorSnapshot.AiSetting");
        static readonly ProfilerMarker k_ActorKnownSpellSnapshot = new("VV.MWScript.ActorSnapshot.KnownSpell");
        static readonly ProfilerMarker k_ActorDispositionSnapshot = new("VV.MWScript.ActorSnapshot.Disposition");

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MusicState>();
            state.RequireForUpdate<MorrowindWeatherState>();
            _scriptQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<PlacedRefRuntimeState>(),
                ComponentType.ReadOnly<LogicalRefLocation>(),
                ComponentType.ReadOnly<LocalTransform>());
            _globalScriptQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindGlobalScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>());
            _playerInventoryQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());
            _globalsLookup = state.GetBufferLookup<MorrowindScriptGlobalValue>(false);
            _deathCountsLookup = state.GetBufferLookup<MorrowindActorDeathCount>(false);
            _questJournalLookup = state.GetBufferLookup<MorrowindQuestJournalIndex>(false);
            _actorInventoryLookup = state.GetBufferLookup<ActorInventoryItem>(true);
            _containerSessionItemLookup = state.GetBufferLookup<ContainerSessionItem>(true);
            _placedRefStateLookup = state.GetComponentLookup<PlacedRefRuntimeState>(true);
            _placedRefIdentityLookup = state.GetComponentLookup<PlacedRefIdentity>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _initialTransformLookup = state.GetComponentLookup<PlacedRefInitialTransform>(true);
            _actorVitalLookup = state.GetComponentLookup<ActorVitalSet>(true);
            _actorHitAftermathLookup = state.GetComponentLookup<ActorHitAftermathState>(true);
            _actorDeathCountedLookup = state.GetComponentLookup<MorrowindActorDeathCounted>(true);
            _actorOnDeathConsumedLookup = state.GetComponentLookup<MorrowindActorOnDeathConsumed>(true);
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<MorrowindScriptRuntimeCatalog>();
            state.RequireForUpdate<MorrowindScriptInterpreterScratch>();
            state.RequireForUpdate<InteractionRuntimeState>();
            state.RequireForUpdate<ScriptActivationEvent>();
            state.RequireForUpdate<ScriptDefaultActivationRequest>();
            state.RequireForUpdate<MorrowindScriptRefStateRequest>();
            state.RequireForUpdate<PlacedRefLockRequest>();
            state.RequireForUpdate<MorrowindScriptTransformRequest>();
            state.RequireForUpdate<MorrowindScriptActorVitalRequest>();
            state.RequireForUpdate<MorrowindScriptAnimationGroupRequest>();
            state.RequireForUpdate<MorrowindScriptInventoryMutationRequest>();
            state.RequireForUpdate<ActorInventoryDropRequest>();
            state.RequireForUpdate<ActorSpellMutationRequest>();
            state.RequireForUpdate<ScriptedCastRequest>();
            state.RequireForUpdate<ActorForceGreetingRequest>();
            state.RequireForUpdate<PlayerReputationMutationRequest>();
            state.RequireForUpdate<ActorAttributeMutationRequest>();
            state.RequireForUpdate<PlayerSkillMutationRequest>();
            state.RequireForUpdate<PlayerFactionMutationRequest>();
            state.RequireForUpdate<ActorFactionRankMutationRequest>();
            state.RequireForUpdate<MorrowindScriptSayRequest>();
            state.RequireForUpdate<MorrowindQuestJournalRequest>();
            state.RequireForUpdate<MorrowindDialogueRequest>();
            state.RequireForUpdate<ShellMessageBoxRequest>();
            state.RequireForUpdate<GlobalMapRevealRequest>();
            state.RequireForUpdate<MorrowindScriptShellRequest>();
            state.RequireForUpdate<MorrowindScriptMovementFlagRequest>();
            state.RequireForUpdate<MorrowindScriptPlaceAtRequest>();
            state.RequireForUpdate<MorrowindScriptOnDeathConsumeRequest>();
            state.RequireForUpdate<MorrowindScriptActorEventConsumeRequest>();
            state.RequireForUpdate<MorrowindRegionWeatherOverrideRequest>();
            state.RequireForUpdate<InteractionActivationRequest>();
            state.RequireForUpdate<LoadedCellsMap>();
            state.RequireForUpdate<LogicalRefLookup>();
            state.RequireForUpdate<PlacedRefRuntimeStateLookup>();
            state.RequireForUpdate<ActiveExplicitRefLookup>();
            state.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            state.RequireForUpdate<MorrowindPhysicsFrameState>();
            state.RequireForUpdate<RuntimeContentBlobReference>();
            state.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnDestroy(ref SystemState state)
        {
            foreach (var catalogRef in SystemAPI.Query<RefRW<MorrowindScriptRuntimeCatalog>>())
                catalogRef.ValueRW.Dispose();
            foreach (var scratchRef in SystemAPI.Query<RefRW<MorrowindScriptInterpreterScratch>>())
                scratchRef.ValueRW.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalog = SystemAPI.GetSingleton<MorrowindScriptRuntimeCatalog>();
            bool objectScriptsEmpty = _scriptQuery.IsEmptyIgnoreFilter;
            bool globalScriptsEmpty = _globalScriptQuery.IsEmptyIgnoreFilter;
            if (!catalog.IsCreated || (objectScriptsEmpty && globalScriptsEmpty))
                return;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] script interpreter requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            if (SystemAPI.TryGetSingleton<RuntimeShellState>(out var hardShell)
                && IsHardMenuPause(hardShell))
            {
                return;
            }

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            Entity interactionRuntimeEntity = SystemAPI.GetSingletonEntity<InteractionRuntimeState>();
            ref var runtimeState = ref SystemAPI.GetSingletonRW<MorrowindScriptRuntimeState>().ValueRW;
            uint sequenceBase = runtimeState.NextAudioRequestSequence;
            int scriptCount = _scriptQuery.CalculateEntityCount() + _globalScriptQuery.CalculateEntityCount();
            runtimeState.NextAudioRequestSequence += (uint)math.max(1, scriptCount + 1);
            _globalsLookup.Update(ref state);
            _deathCountsLookup.Update(ref state);
            _questJournalLookup.Update(ref state);
            _actorInventoryLookup.Update(ref state);
            _containerSessionItemLookup.Update(ref state);
            _placedRefStateLookup.Update(ref state);
            _placedRefIdentityLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _initialTransformLookup.Update(ref state);
            _actorVitalLookup.Update(ref state);
            _actorHitAftermathLookup.Update(ref state);
            _actorDeathCountedLookup.Update(ref state);
            _actorOnDeathConsumedLookup.Update(ref state);
            var activeSources = state.EntityManager.GetBuffer<MorrowindScriptActiveSource>(runtimeEntity);
            activeSources.Clear();
            var refStateRequests = state.EntityManager.GetBuffer<MorrowindScriptRefStateRequest>(runtimeEntity);
            refStateRequests.Clear();
            var lockRequests = state.EntityManager.GetBuffer<PlacedRefLockRequest>(runtimeEntity);
            lockRequests.Clear();
            var transformRequests = state.EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            transformRequests.Clear();
            var questJournalRequests = state.EntityManager.GetBuffer<MorrowindQuestJournalRequest>(runtimeEntity);
            questJournalRequests.Clear();
            var dialogueRequests = state.EntityManager.GetBuffer<MorrowindDialogueRequest>(runtimeEntity);
            dialogueRequests.Clear();
            var inventoryMutationRequests = state.EntityManager.GetBuffer<MorrowindScriptInventoryMutationRequest>(runtimeEntity);
            inventoryMutationRequests.Clear();
            var inventoryDropRequests = state.EntityManager.GetBuffer<ActorInventoryDropRequest>(runtimeEntity);
            inventoryDropRequests.Clear();
            var actorVitalRequests = state.EntityManager.GetBuffer<MorrowindScriptActorVitalRequest>(runtimeEntity);
            actorVitalRequests.Clear();
            var animationGroupRequests = state.EntityManager.GetBuffer<MorrowindScriptAnimationGroupRequest>(runtimeEntity);
            animationGroupRequests.Clear();
            var sayRequests = state.EntityManager.GetBuffer<MorrowindScriptSayRequest>(runtimeEntity);
            sayRequests.Clear();
            var shellRequests = state.EntityManager.GetBuffer<MorrowindScriptShellRequest>(runtimeEntity);
            shellRequests.Clear();
            var movementFlagRequests = state.EntityManager.GetBuffer<MorrowindScriptMovementFlagRequest>(runtimeEntity);
            movementFlagRequests.Clear();
            var placeAtRequests = state.EntityManager.GetBuffer<MorrowindScriptPlaceAtRequest>(runtimeEntity);
            placeAtRequests.Clear();
            var onDeathConsumeRequests = state.EntityManager.GetBuffer<MorrowindScriptOnDeathConsumeRequest>(runtimeEntity);
            onDeathConsumeRequests.Clear();
            var actorEventConsumeRequests = state.EntityManager.GetBuffer<MorrowindScriptActorEventConsumeRequest>(runtimeEntity);
            actorEventConsumeRequests.Clear();

            ref var scratch = ref SystemAPI.GetSingletonRW<MorrowindScriptInterpreterScratch>().ValueRW;
            scratch.ActiveSources.Clear();
            if (scratch.ActiveSources.Capacity < scriptCount)
                scratch.ActiveSources.Capacity = scriptCount;
            NativeArray<MorrowindScriptRunningProgramSnapshot> runningPrograms;
            MorrowindScriptRequirementMask activeRequirements;
            using (k_RequirementScan.Auto())
            {
                runningPrograms = CopyRunningProgramSnapshots(ref state, catalog, ref scratch.RunningPrograms, out activeRequirements);
            }

            NativeArray<ulong> playingSoundKeys;
            k_SnapshotPrep.Begin();
            k_RuntimeSnapshotPrep.Begin();
            if (HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayingSounds))
            {
                var playingBuffer = state.EntityManager.GetBuffer<MorrowindScriptPlayingSound>(runtimeEntity);
                playingSoundKeys = new NativeArray<ulong>(playingBuffer.Length, Allocator.TempJob);
                for (int i = 0; i < playingBuffer.Length; i++)
                    playingSoundKeys[i] = playingBuffer[i].LoopKey;
            }
            else
            {
                playingSoundKeys = CreateEmptyTempJobArray<ulong>();
            }

            NativeArray<MorrowindScriptActiveSaySnapshot> activeSays;
            if (HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActiveSays))
            {
                var activeSayBuffer = state.EntityManager.GetBuffer<MorrowindScriptActiveSay>(runtimeEntity);
                activeSays = new NativeArray<MorrowindScriptActiveSaySnapshot>(activeSayBuffer.Length, Allocator.TempJob);
                for (int i = 0; i < activeSayBuffer.Length; i++)
                {
                    activeSays[i] = new MorrowindScriptActiveSaySnapshot
                    {
                        SourceEntity = activeSayBuffer[i].SourceEntity,
                        SourcePlacedRefId = activeSayBuffer[i].SourcePlacedRefId,
                        Loudness = activeSayBuffer[i].Loudness,
                    };
                }
            }
            else
            {
                activeSays = CreateEmptyTempJobArray<MorrowindScriptActiveSaySnapshot>();
            }

            NativeArray<ScriptActivationEvent> activationEvents;
            if (HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActivationEvents))
            {
                var activationBuffer = state.EntityManager.GetBuffer<ScriptActivationEvent>(interactionRuntimeEntity);
                activationEvents = new NativeArray<ScriptActivationEvent>(activationBuffer.Length, Allocator.TempJob);
                for (int i = 0; i < activationBuffer.Length; i++)
                    activationEvents[i] = activationBuffer[i];
            }
            else
            {
                activationEvents = CreateEmptyTempJobArray<ScriptActivationEvent>();
            }
            k_RuntimeSnapshotPrep.End();

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            var logicalRefs = SystemAPI.GetSingleton<LogicalRefLookup>();
            var placedRefRuntimeStates = SystemAPI.GetSingleton<PlacedRefRuntimeStateLookup>();
            var activeExplicitRefs = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0;
            FixedString128Bytes playerCellName = default;
            byte hasPlayerCellName = 0;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
                playerCellName = interiorTransition.ActiveInteriorCellId;
                hasPlayerCellName = playerCellName.IsEmpty ? (byte)0 : (byte)1;
            }

            byte hasPlayerPosition = 0;
            float3 playerPosition = default;
            Entity playerEntity = Entity.Null;
            uint playerStandingOnPlacedRefId = 0u;
            if (SystemAPI.TryGetSingletonEntity<PlayerTag>(out playerEntity)
                && SystemAPI.HasComponent<LocalTransform>(playerEntity))
            {
                playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
                hasPlayerPosition = 1;
                if (SystemAPI.HasComponent<MorrowindMovementState>(playerEntity))
                {
                    var movementState = SystemAPI.GetComponent<MorrowindMovementState>(playerEntity);
                    playerStandingOnPlacedRefId = ResolveStandingOnPlacedRefId(state.EntityManager, movementState.StandingOn);
                }
            }

            k_PlayerSnapshotPrep.Begin();
            var playerInventoryItems = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayerInventory)
                ? CopyPlayerInventoryItems(ref state)
                : CreateEmptyTempJobArray<PlayerInventoryItem>();
            var actorKnownSpells = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayerKnownSpells)
                ? CopyEntityBuffer<ActorKnownSpell>(state.EntityManager, playerEntity)
                : CreateEmptyTempJobArray<ActorKnownSpell>();
            var playerFactions = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayerFactions)
                ? CopyEntityBuffer<PlayerFactionMembership>(state.EntityManager, playerEntity)
                : CreateEmptyTempJobArray<PlayerFactionMembership>();
            byte hasPlayerSkills = 0;
            ActorSkillSet playerSkills = default;
            if (HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayerSkills)
                && playerEntity != Entity.Null
                && state.EntityManager.HasComponent<ActorSkillSet>(playerEntity))
            {
                playerSkills = state.EntityManager.GetComponentData<ActorSkillSet>(playerEntity);
                hasPlayerSkills = 1;
            }

            var playerCrime = PlayerCrimeState.Default;
            if (HasRequirement(activeRequirements, MorrowindScriptRequirementMask.PlayerCrime)
                && playerEntity != Entity.Null
                && state.EntityManager.HasComponent<PlayerCrimeState>(playerEntity))
                playerCrime = state.EntityManager.GetComponentData<PlayerCrimeState>(playerEntity);
            k_PlayerSnapshotPrep.End();

            k_ActorSnapshotPrep.Begin();
            var externalActorLocals = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ExternalActorLocals)
                ? CopyExternalActorLocals(ref state, ref scratch.ExternalActorLocals)
                : CreateEmptyTempJobArray<MorrowindScriptExternalActorLocalSnapshot>();
            var actorAiStatuses = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorAiStatuses)
                ? CopyActorAiStatusSnapshots(ref state, ref scratch.ActorAiStatuses)
                : CreateEmptyTempJobArray<MorrowindScriptActorAiStatusSnapshot>();
            var actorCombatTargets = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorCombatTargets)
                ? CopyActorCombatTargetSnapshots(ref state, state.EntityManager, ref scratch.ActorCombatTargets)
                : CreateEmptyTempJobArray<MorrowindScriptActorCombatTargetSnapshot>();
            var lockStates = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.LockStates)
                ? CopyLockStateSnapshots(ref state, ref scratch.LockStates)
                : CreateEmptyTempJobArray<MorrowindScriptLockStateSnapshot>();
            var actorEvents = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorEvents)
                ? CopyActorEventSnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorEvents)
                : CreateEmptyTempJobArray<MorrowindScriptActorEventSnapshot>();
            var actorVitals = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorVitals)
                ? CopyActorVitalSnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorVitals)
                : CreateEmptyTempJobArray<MorrowindScriptActorVitalSnapshot>();
            var actorAttributes = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorAttributes)
                ? CopyActorAttributeSnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorAttributes)
                : CreateEmptyTempJobArray<MorrowindScriptActorAttributeSnapshot>();
            var actorActiveEffects = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorActiveEffects)
                ? CopyActorActiveEffectSnapshots(ref state, ref contentBlob, state.EntityManager, playerEntity, ref scratch.ActorActiveEffects)
                : CreateEmptyTempJobArray<MorrowindScriptActorActiveEffectSnapshot>();
            var actorDiseases = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorDiseases)
                ? CopyActorDiseaseSnapshots(ref state, ref contentBlob, state.EntityManager, playerEntity, ref scratch.ActorDiseases)
                : CreateEmptyTempJobArray<MorrowindScriptActorDiseaseSnapshot>();
            var actorIdentities = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorIdentities)
                ? CopyActorIdentitySnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorIdentities)
                : CreateEmptyTempJobArray<MorrowindScriptActorIdentitySnapshot>();
            var actorAiSettings = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorAiSettings)
                ? CopyActorAiSettingSnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorAiSettings)
                : CreateEmptyTempJobArray<MorrowindScriptActorAiSettingSnapshot>();
            var actorKnownSpellSnapshots = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorKnownSpellSnapshots)
                ? CopyActorKnownSpellSnapshots(ref state, state.EntityManager, ref scratch.ActorKnownSpells)
                : CreateEmptyTempJobArray<MorrowindScriptActorKnownSpellSnapshot>();
            var actorDispositions = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorDispositions)
                ? CopyActorDispositionSnapshots(ref state, state.EntityManager, playerEntity, ref scratch.ActorDispositions)
                : CreateEmptyTempJobArray<MorrowindScriptActorDispositionSnapshot>();
            var actorLineOfSight = HasRequirement(activeRequirements, MorrowindScriptRequirementMask.ActorLineOfSight)
                ? CopyActorLineOfSightSnapshots(
                    ref state,
                    state.EntityManager,
                    SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>(),
                    SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().FixedTick,
                    catalog,
                    playerEntity,
                    logicalRefs,
                    activeExplicitRefs,
                    loadedCells.Active,
                    activeInteriorCellHash,
                    interiorActive,
                    loadedCells.Active.IsCreated ? (byte)1 : (byte)0,
                    ref scratch.ActorLineOfSight,
                    ref scratch.ActorLineOfSightPairs,
                    ref scratch.PendingLineOfSightScripts)
                : CreateEmptyTempJobArray<MorrowindScriptActorLineOfSightSnapshot>();
            k_ActorSnapshotPrep.End();
            k_SnapshotPrep.End();

            byte hasMenuMode = 0;
            byte menuMode = 0;
            byte hasModalButtonPressed = 0;
            int modalButtonPressed = -1;
            byte hasPlayerSleeping = 0;
            byte playerSleeping = 0;
            byte hasCurrentWeather = 0;
            int currentWeather = 0;
            Entity weatherRuntimeEntity = Entity.Null;
            Entity musicRuntimeEntity = Entity.Null;
            if (SystemAPI.TryGetSingleton<RuntimeShellState>(out var shell))
            {
                hasMenuMode = 1;
                hasPlayerSleeping = 1;
                hasModalButtonPressed = shell.ModalButtonPressedValid;
                modalButtonPressed = shell.ModalButtonPressedValid != 0 ? shell.ModalButtonPressed : -1;
                playerSleeping = shell.PlayerSleeping != 0 ? (byte)1 : (byte)0;
                menuMode = shell.InventoryOpen != 0
                    || shell.ContainerOpen != 0
                    || shell.PauseMenuOpen != 0
                    || shell.ModalOpen != 0
                    || shell.SaveLoadBrowserOpen != 0
                    || shell.OptionsOpen != 0
                    || shell.JournalOpen != 0
                    || shell.DialogueOpen != 0
                        ? (byte)1
                        : (byte)0;
            }
            if (SystemAPI.TryGetSingleton<MorrowindWeatherState>(out var weather))
            {
                hasCurrentWeather = 1;
                currentWeather = weather.CurrentWeather;
                weatherRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindWeatherState>();
            }
            if (SystemAPI.HasSingleton<MusicState>())
                musicRuntimeEntity = SystemAPI.GetSingletonEntity<MusicState>();
            Entity containerSessionEntity = SystemAPI.TryGetSingletonEntity<ContainerSessionItem>(out var resolvedContainerSessionEntity)
                ? resolvedContainerSessionEntity
                : Entity.Null;

            byte hasCellChanged = 0;
            byte cellChanged = 0;
            int2 currentExteriorCell = default;
            if (interiorActive != 0)
            {
                hasCellChanged = 1;
                cellChanged = _hasLastCellContext == 0
                    || _lastInteriorActive == 0
                    || _lastInteriorCellHash != activeInteriorCellHash
                        ? (byte)1
                        : (byte)0;
            }
            else if (hasPlayerPosition != 0)
            {
                currentExteriorCell = WorldBootstrap.WorldPositionToCell(playerPosition);
                hasPlayerCellName = TryResolveExteriorCellName(ref contentBlob, ref worldCells, currentExteriorCell, out playerCellName) ? (byte)1 : (byte)0;
                hasCellChanged = 1;
                cellChanged = _hasLastCellContext == 0
                    || _lastInteriorActive != 0
                    || math.any(currentExteriorCell != _lastExteriorCell)
                        ? (byte)1
                        : (byte)0;
            }

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var scriptRandomState = new NativeReference<uint>(runtimeState.RandomState == 0u ? 0x6E624EB7u : runtimeState.RandomState, Allocator.TempJob);
            var modalButtonPressedRead = new NativeReference<byte>(0, Allocator.TempJob);
            var common = new MorrowindScriptInterpretCommon
            {
                Programs = catalog.Programs,
                ProgramIds = catalog.ProgramIds,
                Instructions = catalog.Instructions,
                Messages = catalog.Messages,
                OpcodeHandlers = catalog.OpcodeHandlers,
                PreparedRequirements = (ulong)activeRequirements,
                Globals = _globalsLookup,
                DeathCounts = _deathCountsLookup,
                QuestJournal = _questJournalLookup,
                RefDisabledStates = placedRefRuntimeStates.DisabledByPlacedRef,
                LogicalRefs = logicalRefs.Map,
                ActiveExplicitRefs = activeExplicitRefs.ByContentKey,
                AllExplicitRefs = activeExplicitRefs.AllByContentKey,
                CurrentTransforms = _transformLookup,
                InitialTransformLookup = _initialTransformLookup,
                ActorInventories = _actorInventoryLookup,
                ContainerSessionItems = _containerSessionItemLookup,
                ActorVitalsLookup = _actorVitalLookup,
                ActorHitAftermathStates = _actorHitAftermathLookup,
                ActorDeathCountedStates = _actorDeathCountedLookup,
                ActorOnDeathConsumedStates = _actorOnDeathConsumedLookup,
                RuntimeEntity = runtimeEntity,
                ActiveSources = scratch.ActiveSources.AsParallelWriter(),
                RefStateRuntimeEntity = runtimeEntity,
                TransformRuntimeEntity = runtimeEntity,
                AiRuntimeEntity = runtimeEntity,
                Ecb = ecb.AsParallelWriter(),
                AudioSequenceBase = sequenceBase,
                PlayerPosition = playerPosition,
                PlayerEntity = playerEntity,
                PlayerStandingOnPlacedRefId = playerStandingOnPlacedRefId,
                PlayerInventoryItems = playerInventoryItems,
                ActorKnownSpells = actorKnownSpells,
                PlayerFactions = playerFactions,
                PlayerSkills = playerSkills,
                PlayerCrime = playerCrime,
                PlayerCrimeLevel = playerCrime.Bounty,
                ContainerSessionEntity = containerSessionEntity,
                ExternalActorLocals = externalActorLocals,
                ActorAiStatuses = actorAiStatuses,
                ActorCombatTargets = actorCombatTargets,
                LockStates = lockStates,
                ActorEvents = actorEvents,
                ActorVitals = actorVitals,
                ActorAttributes = actorAttributes,
                ActorActiveEffects = actorActiveEffects,
                ActorDiseases = actorDiseases,
                ActorIdentities = actorIdentities,
                ActorAiSettings = actorAiSettings,
                ActorDispositions = actorDispositions,
                ActorLineOfSight = actorLineOfSight,
                PendingLineOfSightScripts = scratch.PendingLineOfSightScripts,
                ActorKnownSpellSnapshots = actorKnownSpellSnapshots,
                RunningPrograms = runningPrograms,
                ActiveSays = activeSays,
                RandomState = (uint*)scriptRandomState.GetUnsafePtr(),
                HasPlayerPosition = hasPlayerPosition,
                HasPlayerSkills = hasPlayerSkills,
                PlayingScriptSoundKeys = playingSoundKeys,
                HasCellChanged = hasCellChanged,
                CellChanged = cellChanged,
                HasMenuMode = hasMenuMode,
                MenuMode = menuMode,
                HasModalButtonPressed = hasModalButtonPressed,
                ModalButtonPressed = modalButtonPressed,
                ModalButtonPressedRead = (byte*)modalButtonPressedRead.GetUnsafePtr(),
                HasPlayerSleeping = hasPlayerSleeping,
                PlayerSleeping = playerSleeping,
                HasPlayerCellName = hasPlayerCellName,
                PlayerCellName = playerCellName,
                HasCurrentWeather = hasCurrentWeather,
                CurrentWeather = currentWeather,
                SecondsPassed = math.max(0f, SystemAPI.Time.DeltaTime),
                InteractionRuntimeEntity = interactionRuntimeEntity,
                ShellRuntimeEntity = runtimeEntity,
                MovementRuntimeEntity = runtimeEntity,
                PlaceAtRuntimeEntity = runtimeEntity,
                AnimationRuntimeEntity = runtimeEntity,
                StartScriptRuntimeEntity = runtimeEntity,
                ActorEventRuntimeEntity = runtimeEntity,
                InventoryDropRuntimeEntity = runtimeEntity,
                WeatherRuntimeEntity = weatherRuntimeEntity,
                MusicRuntimeEntity = musicRuntimeEntity,
                ActivationEvents = activationEvents,
            };

            k_ScheduleJobs.Begin();
            if (!objectScriptsEmpty)
            {
                var job = new InterpretObjectScriptsJob
                {
                    Common = common,
                    ActiveExteriorCells = loadedCells.Active,
                    ActiveInteriorCellHash = activeInteriorCellHash,
                    InteriorActive = interiorActive,
                    HasActiveExteriorCells = loadedCells.Active.IsCreated ? (byte)1 : (byte)0,
                };
                state.Dependency = job.Schedule(_scriptQuery, state.Dependency);
            }

            if (!globalScriptsEmpty)
            {
                var globalJob = new InterpretGlobalScriptsJob
                {
                    Common = common,
                    TargetRuntimeStates = _placedRefStateLookup,
                    TargetIdentities = _placedRefIdentityLookup,
                    TargetTransforms = _transformLookup,
                };
                state.Dependency = globalJob.Schedule(_globalScriptQuery, state.Dependency);
            }
            k_ScheduleJobs.End();

            using (k_CompleteJobs.Auto())
            {
                state.Dependency.Complete();
            }

            using (k_PlaybackCommands.Auto())
            {
                activeSources = state.EntityManager.GetBuffer<MorrowindScriptActiveSource>(runtimeEntity);
                activeSources.ResizeUninitialized(scratch.ActiveSources.Length);
                for (int i = 0; i < scratch.ActiveSources.Length; i++)
                    activeSources[i] = scratch.ActiveSources[i];

                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
            runtimeState.RandomState = scriptRandomState.Value == 0u ? 0x6E624EB7u : scriptRandomState.Value;
            if (modalButtonPressedRead.Value != 0 && SystemAPI.TryGetSingletonRW<RuntimeShellState>(out var shellRef))
            {
                shellRef.ValueRW.ModalButtonPressedValid = 0;
                shellRef.ValueRW.ModalButtonPressed = -1;
            }

            k_DisposeSnapshots.Begin();
            scriptRandomState.Dispose();
            modalButtonPressedRead.Dispose();
            if (playingSoundKeys.IsCreated)
                playingSoundKeys.Dispose();
            if (activeSays.IsCreated)
                activeSays.Dispose();
            if (activationEvents.IsCreated)
                activationEvents.Dispose();
            if (playerInventoryItems.IsCreated)
                playerInventoryItems.Dispose();
            if (actorKnownSpells.IsCreated)
                actorKnownSpells.Dispose();
            if (playerFactions.IsCreated)
                playerFactions.Dispose();
            if (externalActorLocals.IsCreated)
                externalActorLocals.Dispose();
            if (actorAiStatuses.IsCreated)
                actorAiStatuses.Dispose();
            if (actorCombatTargets.IsCreated)
                actorCombatTargets.Dispose();
            if (lockStates.IsCreated)
                lockStates.Dispose();
            if (actorEvents.IsCreated)
                actorEvents.Dispose();
            if (actorVitals.IsCreated)
                actorVitals.Dispose();
            if (actorAttributes.IsCreated)
                actorAttributes.Dispose();
            if (actorActiveEffects.IsCreated)
                actorActiveEffects.Dispose();
            if (actorDiseases.IsCreated)
                actorDiseases.Dispose();
            if (actorIdentities.IsCreated)
                actorIdentities.Dispose();
            if (actorAiSettings.IsCreated)
                actorAiSettings.Dispose();
            if (actorDispositions.IsCreated)
                actorDispositions.Dispose();
            if (actorLineOfSight.IsCreated)
                actorLineOfSight.Dispose();
            if (actorKnownSpellSnapshots.IsCreated)
                actorKnownSpellSnapshots.Dispose();
            if (runningPrograms.IsCreated)
                runningPrograms.Dispose();
            k_DisposeSnapshots.End();

            if (hasCellChanged != 0)
            {
                _hasLastCellContext = 1;
                _lastInteriorActive = interiorActive;
                _lastExteriorCell = currentExteriorCell;
                _lastInteriorCellHash = activeInteriorCellHash;
            }
        }

        static bool IsHardMenuPause(in RuntimeShellState shell)
            => shell.PauseMenuOpen != 0
               || shell.ModalOpen != 0
               || shell.SaveLoadBrowserOpen != 0
               || shell.OptionsOpen != 0;

        NativeArray<PlayerInventoryItem> CopyPlayerInventoryItems(ref SystemState state)
        {
            if (_playerInventoryQuery.CalculateEntityCount() != 1)
                return CreateEmptyTempJobArray<PlayerInventoryItem>();

            Entity player = _playerInventoryQuery.GetSingletonEntity();
            var buffer = state.EntityManager.GetBuffer<PlayerInventoryItem>(player, true);
            if (buffer.Length == 0)
                return CreateEmptyTempJobArray<PlayerInventoryItem>();

            var copy = new NativeArray<PlayerInventoryItem>(buffer.Length, Allocator.TempJob);
            for (int i = 0; i < buffer.Length; i++)
                copy[i] = buffer[i];
            return copy;
        }

        static NativeArray<T> CopyEntityBuffer<T>(EntityManager entityManager, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasBuffer<T>(entity))
                return CreateEmptyTempJobArray<T>();

            var buffer = entityManager.GetBuffer<T>(entity, true);
            if (buffer.Length == 0)
                return CreateEmptyTempJobArray<T>();

            var copy = new NativeArray<T>(buffer.Length, Allocator.TempJob);
            for (int i = 0; i < buffer.Length; i++)
                copy[i] = buffer[i];
            return copy;
        }

        NativeArray<MorrowindScriptExternalActorLocalSnapshot> CopyExternalActorLocals(
            ref SystemState state,
            ref NativeList<MorrowindScriptExternalActorLocalSnapshot> snapshots)
        {
            using var profileScope = k_ExternalActorLocalsSnapshot.Auto();
            snapshots.Clear();
            foreach (var (locals, sourceRef) in SystemAPI.Query<DynamicBuffer<MorrowindScriptLocalValue>, RefRO<ActorSpawnSource>>())
            {
                var source = sourceRef.ValueRO;
                if (!source.Definition.IsValid)
                    continue;

                for (int local = 0; local < locals.Length; local++)
                {
                    snapshots.Add(new MorrowindScriptExternalActorLocalSnapshot
                    {
                        ActorHandleValue = source.Definition.Value,
                        LocalIndex = local,
                        Value = locals[local],
                    });
                }
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAiStatusSnapshot> CopyActorAiStatusSnapshots(
            ref SystemState state,
            ref NativeList<MorrowindScriptActorAiStatusSnapshot> snapshots)
        {
            using var profileScope = k_ActorAiStatusSnapshot.Auto();
            snapshots.Clear();
            foreach (var (identityRef, aiStateRef, packages) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorAiState>, DynamicBuffer<ActorAiPackageRuntime>>())
            {
                var identity = identityRef.ValueRO;
                if (identity.Value == 0u)
                    continue;

                var aiState = aiStateRef.ValueRO;
                snapshots.Add(new MorrowindScriptActorAiStatusSnapshot
                {
                    PlacedRefId = identity.Value,
                    Status = aiState.Status,
                    CurrentPackageTypeId = ResolveCurrentAiPackageType(aiState, packages),
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        static int ResolveCurrentAiPackageType(in ActorAiState aiState, DynamicBuffer<ActorAiPackageRuntime> packages)
        {
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return -1;

            return packages[aiState.CurrentPackageIndex].Type switch
            {
                (byte)ActorAiRuntimePackageType.Wander => 0,
                (byte)ActorAiRuntimePackageType.Travel => 1,
                (byte)ActorAiRuntimePackageType.Escort => 2,
                (byte)ActorAiRuntimePackageType.Follow => 3,
                (byte)ActorAiRuntimePackageType.Activate => 4,
                _ => -1,
            };
        }

        NativeArray<MorrowindScriptActorCombatTargetSnapshot> CopyActorCombatTargetSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            ref NativeList<MorrowindScriptActorCombatTargetSnapshot> snapshots)
        {
            using var profileScope = k_ActorCombatTargetSnapshot.Auto();
            snapshots.Clear();
            foreach (var (stateRef, entity) in SystemAPI.Query<RefRO<ActorCombatTargetState>>().WithAll<ActorSpawnSource>().WithEntityAccess())
            {
                uint actorPlacedRefId = entityManager.HasComponent<PlacedRefIdentity>(entity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                    : 0u;

                var targetState = stateRef.ValueRO;
                uint targetPlacedRefId = targetState.TargetPlacedRefId;
                if (targetPlacedRefId == 0u
                    && targetState.TargetEntity != Entity.Null
                    && entityManager.Exists(targetState.TargetEntity)
                    && entityManager.HasComponent<PlacedRefIdentity>(targetState.TargetEntity))
                {
                    targetPlacedRefId = entityManager.GetComponentData<PlacedRefIdentity>(targetState.TargetEntity).Value;
                }

                snapshots.Add(new MorrowindScriptActorCombatTargetSnapshot
                {
                    ActorEntity = entity,
                    ActorPlacedRefId = actorPlacedRefId,
                    TargetEntity = targetState.TargetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    Active = targetState.Active,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptLockStateSnapshot> CopyLockStateSnapshots(
            ref SystemState state,
            ref NativeList<MorrowindScriptLockStateSnapshot> snapshots)
        {
            using var profileScope = k_LockStateSnapshot.Auto();
            snapshots.Clear();
            foreach (var (identityRef, lockStateRef) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<PlacedRefLockState>>())
            {
                var identity = identityRef.ValueRO;
                if (identity.Value == 0u)
                    continue;

                var lockState = lockStateRef.ValueRO;
                snapshots.Add(new MorrowindScriptLockStateSnapshot
                {
                    PlacedRefId = identity.Value,
                    LockLevel = lockState.LockLevel,
                    Locked = lockState.Locked,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorEventSnapshot> CopyActorEventSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorEventSnapshot> snapshots)
        {
            using var profileScope = k_ActorEventSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorVitalSet>(playerEntity))
            {
                var eventState = entityManager.HasComponent<ActorScriptEventState>(playerEntity)
                    ? entityManager.GetComponentData<ActorScriptEventState>(playerEntity)
                    : default;
                snapshots.Add(new MorrowindScriptActorEventSnapshot
                {
                    Entity = playerEntity,
                    PlacedRefId = 0u,
                    Murdered = eventState.Murdered,
                    Attacked = eventState.Attacked,
                    KnockedDownOneFrame = eventState.KnockedDownOneFrame,
                    LastHitAttemptActor = eventState.LastHitAttemptActor,
                    LastHitAttemptActorPlacedRefId = eventState.LastHitAttemptActorPlacedRefId,
                    LastHitAttemptObject = eventState.LastHitAttemptObject,
                    LastHitObject = eventState.LastHitObject,
                });
            }

            foreach (var (identityRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>>().WithAll<ActorVitalSet>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                var eventState = entityManager.HasComponent<ActorScriptEventState>(entity)
                    ? entityManager.GetComponentData<ActorScriptEventState>(entity)
                    : default;
                snapshots.Add(new MorrowindScriptActorEventSnapshot
                {
                    Entity = entity,
                    PlacedRefId = placedRefId,
                    Murdered = eventState.Murdered,
                    Attacked = eventState.Attacked,
                    KnockedDownOneFrame = eventState.KnockedDownOneFrame,
                    LastHitAttemptActor = eventState.LastHitAttemptActor,
                    LastHitAttemptActorPlacedRefId = eventState.LastHitAttemptActorPlacedRefId,
                    LastHitAttemptObject = eventState.LastHitAttemptObject,
                    LastHitObject = eventState.LastHitObject,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorVitalSnapshot> CopyActorVitalSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorVitalSnapshot> snapshots)
        {
            using var profileScope = k_ActorVitalSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorVitalSet>(playerEntity))
            {
                var playerVitals = entityManager.GetComponentData<ActorVitalSet>(playerEntity);
                snapshots.Add(new MorrowindScriptActorVitalSnapshot
                {
                    PlacedRefId = 0u,
                    Health = playerVitals.CurrentHealth,
                    Magicka = playerVitals.CurrentMagicka,
                    Fatigue = playerVitals.CurrentFatigue,
                });
            }

            foreach (var (identityRef, vitalRef) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorVitalSet>>())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                var vitals = vitalRef.ValueRO;
                snapshots.Add(new MorrowindScriptActorVitalSnapshot
                {
                    PlacedRefId = placedRefId,
                    Health = vitals.CurrentHealth,
                    Magicka = vitals.CurrentMagicka,
                    Fatigue = vitals.CurrentFatigue,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAttributeSnapshot> CopyActorAttributeSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorAttributeSnapshot> snapshots)
        {
            using var profileScope = k_ActorAttributeSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorAttributeSet>(playerEntity))
            {
                snapshots.Add(new MorrowindScriptActorAttributeSnapshot
                {
                    PlacedRefId = 0u,
                    Attributes = entityManager.GetComponentData<ActorAttributeSet>(playerEntity),
                });
            }

            foreach (var (identityRef, attributeRef) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorAttributeSet>>())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptActorAttributeSnapshot
                {
                    PlacedRefId = placedRefId,
                    Attributes = attributeRef.ValueRO,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorActiveEffectSnapshot> CopyActorActiveEffectSnapshots(
            ref SystemState state,
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorActiveEffectSnapshot> snapshots)
        {
            using var profileScope = k_ActorActiveEffectSnapshot.Auto();
            snapshots.Clear();
            AppendActiveEffectSnapshots(ref contentBlob, entityManager, playerEntity, 0u, snapshots);

            foreach (var (identityRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>>().WithAll<ActorActiveMagicEffect>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                AppendActiveEffectSnapshots(ref contentBlob, entityManager, entity, placedRefId, snapshots);
            }

            return CopyToTempJobArray(snapshots);
        }

        static void AppendActiveEffectSnapshots(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity actor,
            uint placedRefId,
            NativeList<MorrowindScriptActorActiveEffectSnapshot> snapshots)
        {
            if (actor == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
            {
                return;
            }

            var effects = entityManager.GetBuffer<ActorActiveMagicEffect>(actor, true);
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.Applied == 0
                    || effect.EffectId < 0
                    || (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f))
                {
                    continue;
                }

                SpellDefHandle sourceSpell = default;
                if (effect.SourceIdHash != 0UL
                    && RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref contentBlob, effect.SourceIdHash, out var resolvedSourceSpell)
                    && resolvedSourceSpell.IsValid)
                {
                    sourceSpell = resolvedSourceSpell;
                }

                snapshots.Add(new MorrowindScriptActorActiveEffectSnapshot
                {
                    ActorEntity = actor,
                    PlacedRefId = placedRefId,
                    SourceSpell = sourceSpell,
                    EffectId = effect.EffectId,
                });
            }
        }

        NativeArray<MorrowindScriptActorDiseaseSnapshot> CopyActorDiseaseSnapshots(
            ref SystemState state,
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorDiseaseSnapshot> snapshots)
        {
            using var profileScope = k_ActorDiseaseSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null && entityManager.Exists(playerEntity))
            {
                ResolveDiseaseFlags(ref contentBlob, entityManager, playerEntity, out byte commonDisease, out byte blightDisease);
                snapshots.Add(new MorrowindScriptActorDiseaseSnapshot
                {
                    PlacedRefId = 0u,
                    HasCommonDisease = commonDisease,
                    HasBlightDisease = blightDisease,
                });
            }

            foreach (var (identityRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>>().WithAll<ActorSpawnSource>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                ResolveDiseaseFlags(ref contentBlob, entityManager, entity, out byte commonDisease, out byte blightDisease);
                snapshots.Add(new MorrowindScriptActorDiseaseSnapshot
                {
                    PlacedRefId = placedRefId,
                    HasCommonDisease = commonDisease,
                    HasBlightDisease = blightDisease,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorIdentitySnapshot> CopyActorIdentitySnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorIdentitySnapshot> snapshots)
        {
            using var profileScope = k_ActorIdentitySnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorIdentitySet>(playerEntity))
            {
                var identity = entityManager.GetComponentData<ActorIdentitySet>(playerEntity);
                snapshots.Add(new MorrowindScriptActorIdentitySnapshot
                {
                    ActorEntity = playerEntity,
                    PlacedRefId = 0u,
                    RaceName = identity.RaceName,
                });
            }

            foreach (var (placedRefRef, identityRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorIdentitySet>>().WithEntityAccess())
            {
                uint placedRefId = placedRefRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptActorIdentitySnapshot
                {
                    ActorEntity = entity,
                    PlacedRefId = placedRefId,
                    RaceName = identityRef.ValueRO.RaceName,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAiSettingSnapshot> CopyActorAiSettingSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorAiSettingSnapshot> snapshots)
        {
            using var profileScope = k_ActorAiSettingSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorAiSettingsState>(playerEntity))
            {
                var settings = entityManager.GetComponentData<ActorAiSettingsState>(playerEntity);
                snapshots.Add(new MorrowindScriptActorAiSettingSnapshot
                {
                    ActorEntity = playerEntity,
                    PlacedRefId = 0u,
                    Hello = settings.Hello,
                    Fight = settings.Fight,
                    Flee = settings.Flee,
                    Alarm = settings.Alarm,
                });
            }

            foreach (var (placedRefRef, settingsRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorAiSettingsState>>().WithEntityAccess())
            {
                uint placedRefId = placedRefRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                var settings = settingsRef.ValueRO;
                snapshots.Add(new MorrowindScriptActorAiSettingSnapshot
                {
                    ActorEntity = entity,
                    PlacedRefId = placedRefId,
                    Hello = settings.Hello,
                    Fight = settings.Fight,
                    Flee = settings.Flee,
                    Alarm = settings.Alarm,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorKnownSpellSnapshot> CopyActorKnownSpellSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            ref NativeList<MorrowindScriptActorKnownSpellSnapshot> snapshots)
        {
            using var profileScope = k_ActorKnownSpellSnapshot.Auto();
            snapshots.Clear();
            foreach (var (knownSpells, entity) in SystemAPI.Query<DynamicBuffer<ActorKnownSpell>>().WithEntityAccess())
            {
                uint placedRefId = entityManager.HasComponent<PlacedRefIdentity>(entity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                    : 0u;
                for (int spellIndex = 0; spellIndex < knownSpells.Length; spellIndex++)
                {
                    snapshots.Add(new MorrowindScriptActorKnownSpellSnapshot
                    {
                        ActorEntity = entity,
                        PlacedRefId = placedRefId,
                        Spell = knownSpells[spellIndex].Spell,
                    });
                }
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorDispositionSnapshot> CopyActorDispositionSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity,
            ref NativeList<MorrowindScriptActorDispositionSnapshot> snapshots)
        {
            using var profileScope = k_ActorDispositionSnapshot.Auto();
            snapshots.Clear();
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorDispositionState>(playerEntity))
            {
                var disposition = entityManager.GetComponentData<ActorDispositionState>(playerEntity);
                snapshots.Add(new MorrowindScriptActorDispositionSnapshot
                {
                    ActorEntity = playerEntity,
                    PlacedRefId = 0u,
                    BaseDisposition = disposition.BaseDisposition,
                });
            }

            foreach (var (identityRef, dispositionRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorDispositionState>>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptActorDispositionSnapshot
                {
                    ActorEntity = entity,
                    PlacedRefId = placedRefId,
                    BaseDisposition = dispositionRef.ValueRO.BaseDisposition,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptRunningProgramSnapshot> CopyRunningProgramSnapshots(
            ref SystemState state,
            MorrowindScriptRuntimeCatalog catalog,
            ref NativeList<MorrowindScriptRunningProgramSnapshot> snapshots,
            out MorrowindScriptRequirementMask activeRequirements)
        {
            snapshots.Clear();
            activeRequirements = MorrowindScriptRequirementMask.None;
            foreach (var instanceRef in SystemAPI.Query<RefRO<MorrowindScriptInstance>>())
            {
                var instance = instanceRef.ValueRO;
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    continue;

                if ((uint)instance.ProgramIndex >= (uint)catalog.Programs.Length)
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Running script program index {instance.ProgramIndex} is outside the runtime catalog.");

                activeRequirements |= (MorrowindScriptRequirementMask)catalog.Programs[instance.ProgramIndex].RequirementMask;
            }

            if (!HasRequirement(activeRequirements, MorrowindScriptRequirementMask.RunningPrograms))
                return CreateEmptyTempJobArray<MorrowindScriptRunningProgramSnapshot>();

            foreach (var instanceRef in SystemAPI.Query<RefRO<MorrowindScriptInstance>>().WithAll<MorrowindGlobalScriptInstance>())
            {
                var instance = instanceRef.ValueRO;
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    continue;

                if ((uint)instance.ProgramIndex >= (uint)catalog.Programs.Length)
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Running global script program index {instance.ProgramIndex} is outside the runtime catalog.");

                snapshots.Add(new MorrowindScriptRunningProgramSnapshot
                {
                    ProgramIndex = instance.ProgramIndex,
                    Running = 1,
                });
            }

            return CopyToTempJobArray(snapshots);
        }

        static void ResolveDiseaseFlags(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity actor,
            out byte commonDisease,
            out byte blightDisease)
        {
            commonDisease = 0;
            blightDisease = 0;
            if ( actor == Entity.Null || !entityManager.Exists(actor))
                return;

            if (entityManager.HasBuffer<ActorKnownSpell>(actor))
            {
                var knownSpells = entityManager.GetBuffer<ActorKnownSpell>(actor, true);
                for (int i = 0; i < knownSpells.Length; i++)
                    ResolveDiseaseSpell(ref contentBlob, knownSpells[i].Spell, ref commonDisease, ref blightDisease);
            }

            if (entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
            {
                var activeEffects = entityManager.GetBuffer<ActorActiveMagicEffect>(actor, true);
                for (int i = 0; i < activeEffects.Length; i++)
                {
                    if (activeEffects[i].SourceIdHash != 0UL
                        && RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref contentBlob, activeEffects[i].SourceIdHash, out var sourceSpell)
                        && sourceSpell.IsValid)
                    {
                        ResolveDiseaseSpell(ref contentBlob, sourceSpell, ref commonDisease, ref blightDisease);
                    }
                }
            }
        }

        static void ResolveDiseaseSpell(
            ref RuntimeContentBlob contentBlob,
            SpellDefHandle spellHandle,
            ref byte commonDisease,
            ref byte blightDisease)
        {
            if ( !spellHandle.IsValid)
                return;

            ref var spell = ref RuntimeContentBlobUtility.Get(ref contentBlob, spellHandle);
            if (spell.SpellType == 2)
                blightDisease = 1;
            else if (spell.SpellType == 3)
                commonDisease = 1;
        }

        static NativeArray<T> CreateEmptyTempJobArray<T>()
            where T : unmanaged
            => new(0, Allocator.TempJob);

        static bool HasRequirement(MorrowindScriptRequirementMask activeRequirements, MorrowindScriptRequirementMask requirement)
            => (activeRequirements & requirement) != 0;

        static NativeArray<T> CopyToTempJobArray<T>(NativeList<T> snapshots)
            where T : unmanaged
        {
            if (snapshots.Length == 0)
                return CreateEmptyTempJobArray<T>();

            var result = new NativeArray<T>(snapshots.Length, Allocator.TempJob);
            for (int i = 0; i < snapshots.Length; i++)
                result[i] = snapshots[i];
            return result;
        }

        static bool TryResolveExteriorCellName(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, int2 exteriorCell, out FixedString128Bytes cellName)
        {
            cellName = default;
            if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, exteriorCell, out int cellIndex))
                return false;

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            if (!cell.CellId.IsEmpty)
            {
                cellName = cell.CellId;
                return true;
            }

            if (cell.Environment.RegionIdHash != 0UL
                && RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref contentBlob, cell.Environment.RegionIdHash, out var regionHandle)
                && regionHandle.IsValid)
            {
                ref var region = ref RuntimeContentBlobUtility.Get(ref contentBlob, regionHandle);
                cellName = RuntimeFixedStringUtility.ToFixed128OrDefault(ref region.Name);
                if (cellName.IsEmpty)
                    cellName = RuntimeFixedStringUtility.ToFixed128OrDefault(ref region.Id);
                return !cellName.IsEmpty;
            }

            if (TryGetGameSettingString(ref contentBlob, RuntimeContentKnownHashes.sDefaultCellname, out FixedString128Bytes defaultCellName))
            {
                cellName = defaultCellName;
                return !cellName.IsEmpty;
            }

            return false;
        }

        static bool TryGetGameSettingString(ref RuntimeContentBlob contentBlob, ulong idHash, out FixedString128Bytes value)
        {
            value = default;
            if (!RuntimeContentBlobUtility.TryGetGameSettingHandleByIdHash(ref contentBlob, idHash, out var handle) || !handle.IsValid)
                return false;

            ref var gameSetting = ref RuntimeContentBlobUtility.GetGameSetting(ref contentBlob, handle);
            if (gameSetting.ValueKind != GenericRecordValueKind.String)
                throw new InvalidOperationException($"[VVardenfell][MWScript] GMST hash {idHash} is not a string.");

            value = RuntimeFixedStringUtility.ToFixed128OrDefault(ref gameSetting.Text);
            return true;
        }

        static uint ResolveStandingOnPlacedRefId(EntityManager entityManager, Entity standingOn)
        {
            if (standingOn == Entity.Null || !entityManager.Exists(standingOn))
                return 0u;

            Entity logicalEntity = standingOn;
            if (entityManager.HasComponent<LogicalRefParent>(standingOn))
            {
                logicalEntity = entityManager.GetComponentData<LogicalRefParent>(standingOn).Value;
                if (logicalEntity == Entity.Null || !entityManager.Exists(logicalEntity))
                    return 0u;
            }

            if (!entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                return 0u;

            return entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
        }

        struct ScriptLineOfSightActor
        {
            public Entity Entity;
            public uint PlacedRefId;
            public float3 EyePosition;
        }

        NativeArray<MorrowindScriptActorLineOfSightSnapshot> CopyActorLineOfSightSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            MorrowindScriptRuntimeCatalog catalog,
            Entity playerEntity,
            in LogicalRefLookup logicalRefs,
            in ActiveExplicitRefLookup activeExplicitRefs,
            NativeHashSet<int2> activeExteriorCells,
            ulong activeInteriorCellHash,
            byte interiorActive,
            byte hasActiveExteriorCells,
            ref NativeList<MorrowindScriptActorLineOfSightSnapshot> snapshots,
            ref NativeParallelHashSet<ulong> pairKeys,
            ref NativeParallelHashSet<Entity> pendingScripts)
        {
            using var lineOfSightScope = k_LineOfSightSnapshotPrep.Auto();
            snapshots.Clear();
            pairKeys.Clear();
            pendingScripts.Clear();

            foreach (var (instanceRef, placedRefRef, runtimeStateRef, locationRef, transformRef, entity) in
                     SystemAPI.Query<RefRO<MorrowindScriptInstance>, RefRO<PlacedRefIdentity>, RefRO<PlacedRefRuntimeState>, RefRO<LogicalRefLocation>, RefRO<LocalTransform>>()
                         .WithAll<ActorIdentitySet>()
                         .WithEntityAccess())
            {
                var instance = instanceRef.ValueRO;
                if (!ShouldPrepareLineOfSightForInstance(catalog, instance))
                    continue;
                if (runtimeStateRef.ValueRO.Disabled != 0
                    || !IsScriptLocationActive(locationRef.ValueRO, activeExteriorCells, activeInteriorCellHash, interiorActive, hasActiveExteriorCells))
                {
                    continue;
                }

                var self = new ScriptLineOfSightActor
                {
                    Entity = entity,
                    PlacedRefId = placedRefRef.ValueRO.Value,
                    EyePosition = GetActorEyePosition(transformRef.ValueRO),
                };
                AddLineOfSightInstructionPairs(
                    entityManager,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    catalog,
                    instance.ProgramIndex,
                    entity,
                    self,
                    playerEntity,
                    logicalRefs,
                    activeExplicitRefs,
                    ref snapshots,
                    ref pairKeys,
                    ref pendingScripts);
            }

            foreach (var (instanceRef, globalRef, entity) in SystemAPI.Query<RefRO<MorrowindScriptInstance>, RefRO<MorrowindGlobalScriptInstance>>().WithEntityAccess())
            {
                var instance = instanceRef.ValueRO;
                if (!ShouldPrepareLineOfSightForInstance(catalog, instance))
                    continue;

                if (!TryResolveGlobalScriptSelf(entityManager, globalRef.ValueRO, out var self))
                    continue;

                AddLineOfSightInstructionPairs(
                    entityManager,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    catalog,
                    instance.ProgramIndex,
                    entity,
                    self,
                    playerEntity,
                    logicalRefs,
                    activeExplicitRefs,
                    ref snapshots,
                    ref pairKeys,
                    ref pendingScripts);
            }

            return CopyToTempJobArray(snapshots);
        }

        static bool ShouldPrepareLineOfSightForInstance(
            MorrowindScriptRuntimeCatalog catalog,
            in MorrowindScriptInstance instance)
        {
            if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                return false;
            if ((uint)instance.ProgramIndex >= (uint)catalog.Programs.Length)
                throw new InvalidOperationException($"[VVardenfell][MWScript][LOS] Running script program index {instance.ProgramIndex} is outside the runtime catalog.");

            return HasRequirement((MorrowindScriptRequirementMask)catalog.Programs[instance.ProgramIndex].RequirementMask, MorrowindScriptRequirementMask.ActorLineOfSight);
        }

        static bool IsScriptLocationActive(
            in LogicalRefLocation location,
            NativeHashSet<int2> activeExteriorCells,
            ulong activeInteriorCellHash,
            byte interiorActive,
            byte hasActiveExteriorCells)
        {
            if (interiorActive != 0)
                return location.IsInterior != 0 && location.InteriorCellHash == activeInteriorCellHash;

            if (location.IsInterior != 0 || hasActiveExteriorCells == 0)
                return false;

            return activeExteriorCells.Contains(location.ExteriorCell);
        }

        static bool TryResolveGlobalScriptSelf(
            EntityManager entityManager,
            in MorrowindGlobalScriptInstance global,
            out ScriptLineOfSightActor self)
        {
            self = default;
            Entity entity = global.TargetEntity;
            if (entity == Entity.Null
                || !entityManager.Exists(entity)
                || !entityManager.HasComponent<LocalTransform>(entity))
            {
                return false;
            }

            bool isPlayer = entityManager.HasComponent<PlayerTag>(entity);
            if (!isPlayer && !entityManager.HasComponent<ActorIdentitySet>(entity))
                return false;

            if (entityManager.HasComponent<PlacedRefRuntimeState>(entity)
                && entityManager.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0)
            {
                return false;
            }

            uint placedRefId = global.TargetPlacedRefId;
            if (placedRefId == 0u && entityManager.HasComponent<PlacedRefIdentity>(entity))
                placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(entity).Value;

            self = new ScriptLineOfSightActor
            {
                Entity = entity,
                PlacedRefId = placedRefId,
                EyePosition = GetActorEyePosition(entityManager.GetComponentData<LocalTransform>(entity)),
            };
            return true;
        }

        static void AddLineOfSightInstructionPairs(
            EntityManager entityManager,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            MorrowindScriptRuntimeCatalog catalog,
            int programIndex,
            Entity scriptEntity,
            in ScriptLineOfSightActor self,
            Entity playerEntity,
            in LogicalRefLookup logicalRefs,
            in ActiveExplicitRefLookup activeExplicitRefs,
            ref NativeList<MorrowindScriptActorLineOfSightSnapshot> snapshots,
            ref NativeParallelHashSet<ulong> pairKeys,
            ref NativeParallelHashSet<Entity> pendingScripts)
        {
            var program = catalog.Programs[programIndex];
            int end = program.FirstInstructionIndex + program.InstructionCount;
            for (int i = program.FirstInstructionIndex; i < end; i++)
            {
                var instruction = catalog.Instructions[i];
                if (!IsLineOfSightOpcode((MorrowindScriptOpcode)instruction.Opcode))
                    continue;

                if (!TryResolveLineOfSightActor(
                        entityManager,
                        instruction.Operand0,
                        instruction.Int0,
                        self,
                        playerEntity,
                        logicalRefs,
                        activeExplicitRefs,
                        out var source)
                    || !TryResolveLineOfSightActor(
                        entityManager,
                        (byte)instruction.Operand1,
                        instruction.Int1,
                        self,
                        playerEntity,
                        logicalRefs,
                        activeExplicitRefs,
                        out var target)
                    || source.PlacedRefId == target.PlacedRefId)
                {
                    continue;
                }

                if (!AddLineOfSightSnapshot(entityManager, deferredPhysicsQueueEntity, fixedTick, source, target, ref snapshots, ref pairKeys))
                    pendingScripts.Add(scriptEntity);
            }
        }

        static bool IsLineOfSightOpcode(MorrowindScriptOpcode opcode)
            => opcode == MorrowindScriptOpcode.GetLOS || opcode == MorrowindScriptOpcode.GetDetected;

        static bool TryResolveLineOfSightActor(
            EntityManager entityManager,
            byte targetMode,
            int targetRefKey,
            in ScriptLineOfSightActor self,
            Entity playerEntity,
            in LogicalRefLookup logicalRefs,
            in ActiveExplicitRefLookup activeExplicitRefs,
            out ScriptLineOfSightActor actor)
        {
            actor = default;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Self)
            {
                actor = self;
                return actor.Entity != Entity.Null
                       && (actor.PlacedRefId != 0u || entityManager.HasComponent<PlayerTag>(actor.Entity));
            }

            if (targetMode == (byte)MorrowindScriptRefTargetMode.Player)
                return TryBuildLineOfSightActor(entityManager, playerEntity, 0u, allowPlayer: true, out actor);

            Entity entity = Entity.Null;
            uint placedRefId = 0u;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.PlacedRef)
            {
                placedRefId = unchecked((uint)targetRefKey);
                if (placedRefId == 0u)
                    return false;
                if (!logicalRefs.Map.IsCreated)
                    throw new InvalidOperationException("[VVardenfell][MWScript][LOS] LogicalRefLookup is not initialized.");
                if (!logicalRefs.Map.TryGetValue(placedRefId, out entity))
                    return false;
            }
            else if (targetMode == (byte)MorrowindScriptRefTargetMode.ActiveContentRef)
            {
                if (!activeExplicitRefs.ByContentKey.IsCreated
                    || !activeExplicitRefs.ByContentKey.TryGetValue(targetRefKey, out var target)
                    || target.Ambiguous != 0
                    || target.PlacedRefId == 0u
                    || target.Entity == Entity.Null)
                {
                    return false;
                }

                entity = target.Entity;
                placedRefId = target.PlacedRefId;
            }
            else
            {
                return false;
            }

            return TryBuildLineOfSightActor(entityManager, entity, placedRefId, allowPlayer: false, out actor);
        }

        static bool TryBuildLineOfSightActor(
            EntityManager entityManager,
            Entity entity,
            uint placedRefId,
            bool allowPlayer,
            out ScriptLineOfSightActor actor)
        {
            actor = default;
            if (entity == Entity.Null
                || !entityManager.Exists(entity)
                || !entityManager.HasComponent<LocalTransform>(entity)
                || (!allowPlayer && !entityManager.HasComponent<ActorIdentitySet>(entity)))
            {
                return false;
            }

            if (entityManager.HasComponent<PlacedRefRuntimeState>(entity)
                && entityManager.GetComponentData<PlacedRefRuntimeState>(entity).Disabled != 0)
            {
                return false;
            }

            actor = new ScriptLineOfSightActor
            {
                Entity = entity,
                PlacedRefId = placedRefId,
                EyePosition = GetActorEyePosition(entityManager.GetComponentData<LocalTransform>(entity)),
            };
            return true;
        }

        static bool AddLineOfSightSnapshot(
            EntityManager entityManager,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            in ScriptLineOfSightActor source,
            in ScriptLineOfSightActor target,
            ref NativeList<MorrowindScriptActorLineOfSightSnapshot> snapshots,
            ref NativeParallelHashSet<ulong> pairKeys)
        {
            ulong key = PackLineOfSightPairKey(source.PlacedRefId, target.PlacedRefId);
            if (pairKeys.Contains(key))
                return true;

            if (!TryGetActorLineOfSight(
                    entityManager,
                    deferredPhysicsQueueEntity,
                    fixedTick,
                    source.Entity,
                    target.Entity,
                    source.EyePosition,
                    target.EyePosition,
                    out bool hasLineOfSight))
            {
                return false;
            }

            pairKeys.Add(key);
            snapshots.Add(new MorrowindScriptActorLineOfSightSnapshot
            {
                SourcePlacedRefId = source.PlacedRefId,
                TargetPlacedRefId = target.PlacedRefId,
                HasLineOfSight = hasLineOfSight ? (byte)1 : (byte)0,
            });
            return true;
        }

        static ulong PackLineOfSightPairKey(uint sourcePlacedRefId, uint targetPlacedRefId)
            => ((ulong)sourcePlacedRefId << 32) | targetPlacedRefId;

        static float3 GetActorEyePosition(in LocalTransform transform)
            => transform.Position + new float3(0f, 1.62f, 0f);

        static bool TryGetActorLineOfSight(
            EntityManager entityManager,
            Entity deferredPhysicsQueueEntity,
            uint fixedTick,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target,
            out bool hasLineOfSight)
        {
            hasLineOfSight = false;
            if (math.distancesq(source, target) <= 0.0001f)
            {
                hasLineOfSight = true;
                return true;
            }

            return DeferredPhysicsQueryUtility.TryGetLineOfSightOrRequest(
                entityManager,
                deferredPhysicsQueueEntity,
                fixedTick,
                sourceEntity,
                targetEntity,
                source,
                target,
                InteractionCollisionLayers.LineOfSightQueryFilter,
                DeferredPhysicsQueryUtility.FrameMaxResultAgeTicks,
                out hasLineOfSight);
        }

        [BurstCompile]
        unsafe struct MorrowindScriptInterpretCommon
        {
            [ReadOnly] public NativeArray<MorrowindScriptProgramRuntime> Programs;
            [ReadOnly] public NativeArray<FixedString128Bytes> ProgramIds;
            [ReadOnly] public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
            [ReadOnly] public NativeArray<FixedString512Bytes> Messages;
            [ReadOnly] public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;
            public ulong PreparedRequirements;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindScriptGlobalValue> Globals;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindActorDeathCount> DeathCounts;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindQuestJournalIndex> QuestJournal;
            [ReadOnly] public BufferLookup<ActorInventoryItem> ActorInventories;
            [ReadOnly] public BufferLookup<ContainerSessionItem> ContainerSessionItems;
            [ReadOnly] public NativeParallelHashMap<uint, byte> RefDisabledStates;
            [ReadOnly] public NativeParallelHashMap<uint, Entity> LogicalRefs;
            [ReadOnly] public NativeParallelHashMap<int, ActiveExplicitRefTarget> ActiveExplicitRefs;
            [ReadOnly] public NativeParallelHashMap<int, ActiveExplicitRefTarget> AllExplicitRefs;
            [ReadOnly] public ComponentLookup<LocalTransform> CurrentTransforms;
            [ReadOnly] public ComponentLookup<PlacedRefInitialTransform> InitialTransformLookup;
            [ReadOnly] public ComponentLookup<ActorVitalSet> ActorVitalsLookup;
            [ReadOnly] public ComponentLookup<ActorHitAftermathState> ActorHitAftermathStates;
            [ReadOnly] public ComponentLookup<MorrowindActorDeathCounted> ActorDeathCountedStates;
            [ReadOnly] public ComponentLookup<MorrowindActorOnDeathConsumed> ActorOnDeathConsumedStates;
            public Entity RuntimeEntity;
            public NativeList<MorrowindScriptActiveSource>.ParallelWriter ActiveSources;
            public Entity RefStateRuntimeEntity;
            public Entity TransformRuntimeEntity;
            public Entity AiRuntimeEntity;
            public Entity ShellRuntimeEntity;
            public Entity MovementRuntimeEntity;
            public EntityCommandBuffer.ParallelWriter Ecb;
            public uint AudioSequenceBase;
            public float3 PlayerPosition;
            public Entity PlayerEntity;
            public uint PlayerStandingOnPlacedRefId;
            [ReadOnly] public NativeArray<PlayerInventoryItem> PlayerInventoryItems;
            [ReadOnly] public NativeArray<ActorKnownSpell> ActorKnownSpells;
            [ReadOnly] public NativeArray<PlayerFactionMembership> PlayerFactions;
            public ActorSkillSet PlayerSkills;
            public PlayerCrimeState PlayerCrime;
            public int PlayerCrimeLevel;
            public NativeArray<MorrowindScriptExternalActorLocalSnapshot> ExternalActorLocals;
            [ReadOnly] public NativeArray<MorrowindScriptActorAiStatusSnapshot> ActorAiStatuses;
            [ReadOnly] public NativeArray<MorrowindScriptActorCombatTargetSnapshot> ActorCombatTargets;
            [ReadOnly] public NativeArray<MorrowindScriptLockStateSnapshot> LockStates;
            [ReadOnly] public NativeArray<MorrowindScriptActorEventSnapshot> ActorEvents;
            [ReadOnly] public NativeArray<MorrowindScriptActorVitalSnapshot> ActorVitals;
            [ReadOnly] public NativeArray<MorrowindScriptActorAttributeSnapshot> ActorAttributes;
            [ReadOnly] public NativeArray<MorrowindScriptActorActiveEffectSnapshot> ActorActiveEffects;
            [ReadOnly] public NativeArray<MorrowindScriptActorDiseaseSnapshot> ActorDiseases;
            [ReadOnly] public NativeArray<MorrowindScriptActorIdentitySnapshot> ActorIdentities;
            [ReadOnly] public NativeArray<MorrowindScriptActorAiSettingSnapshot> ActorAiSettings;
            [ReadOnly] public NativeArray<MorrowindScriptActorDispositionSnapshot> ActorDispositions;
            [ReadOnly] public NativeArray<MorrowindScriptActorLineOfSightSnapshot> ActorLineOfSight;
            [ReadOnly] public NativeParallelHashSet<Entity> PendingLineOfSightScripts;
            [ReadOnly] public NativeArray<MorrowindScriptActorKnownSpellSnapshot> ActorKnownSpellSnapshots;
            [ReadOnly] public NativeArray<MorrowindScriptRunningProgramSnapshot> RunningPrograms;
            [ReadOnly] public NativeArray<MorrowindScriptActiveSaySnapshot> ActiveSays;
            [NativeDisableUnsafePtrRestriction] public uint* RandomState;
            [ReadOnly] public NativeArray<ulong> PlayingScriptSoundKeys;
            public byte HasPlayerPosition;
            public byte HasPlayerSkills;
            public byte HasCellChanged;
            public byte CellChanged;
            public byte HasMenuMode;
            public byte MenuMode;
            public byte HasModalButtonPressed;
            public int ModalButtonPressed;
            [NativeDisableUnsafePtrRestriction] public byte* ModalButtonPressedRead;
            public byte HasPlayerSleeping;
            public byte PlayerSleeping;
            public byte HasPlayerCellName;
            public FixedString128Bytes PlayerCellName;
            public byte HasCurrentWeather;
            public int CurrentWeather;
            public float SecondsPassed;
            public Entity InteractionRuntimeEntity;
            public Entity PlaceAtRuntimeEntity;
            public Entity AnimationRuntimeEntity;
            public Entity StartScriptRuntimeEntity;
            public Entity ActorEventRuntimeEntity;
            public Entity InventoryDropRuntimeEntity;
            public Entity WeatherRuntimeEntity;
            public Entity MusicRuntimeEntity;
            public Entity ContainerSessionEntity;
            [ReadOnly] public NativeArray<ScriptActivationEvent> ActivationEvents;

            [BurstCompile]
            public bool TryInterpret(
                int sortKey,
                Entity scriptEntity,
                Entity contextEntity,
                uint placedRefId,
                byte selfDisabled,
                float3 position,
                quaternion rotation,
                ref MorrowindScriptInstance instance,
                DynamicBuffer<MorrowindScriptLocalValue> locals)
            {
                if ((uint)instance.ProgramIndex >= (uint)Programs.Length)
                {
                    Fault(ref instance, FormatScriptFault(default, -1, 0, ScriptFaultReason.InvalidProgramIndex));
                    return false;
                }

                var program = Programs[instance.ProgramIndex];
                FixedString128Bytes programId = ProgramIds[instance.ProgramIndex];
                if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled || program.InstructionCount <= 0)
                    return false;

                ActiveSources.AddNoResize(new MorrowindScriptActiveSource
                {
                    LoopSourceKey = MorrowindScriptOpcodeTable.BuildScriptLoopSourceKey(placedRefId, scriptEntity),
                });

                if (PendingLineOfSightScripts.IsCreated && PendingLineOfSightScripts.Contains(scriptEntity))
                    return false;

                if (locals.Length < program.LocalCount)
                    locals.ResizeUninitialized(program.LocalCount);

                int stackCapacity = math.max(1, program.MaxStack);
                MorrowindScriptStackValue* stack = stackalloc MorrowindScriptStackValue[stackCapacity];

                if (!QuestJournal.HasBuffer(RuntimeEntity))
                {
                    Fault(ref instance, FormatScriptFault(programId, -1, 0, ScriptFaultReason.MissingQuestJournalRuntimeBuffer));
                    return false;
                }

                var globalBuffer = Globals[RuntimeEntity];
                var deathCounts = DeathCounts[RuntimeEntity];
                var questJournal = QuestJournal[RuntimeEntity];
                var context = new MorrowindScriptExecutionContext
                {
                    Entity = contextEntity,
                    Ecb = Ecb,
                    SortKey = sortKey,
                    ProgramCounter = 0,
                    StackLength = 0,
                    StackCapacity = stackCapacity,
                    Stack = stack,
                    Locals = locals.Length == 0 ? null : (MorrowindScriptLocalValue*)locals.GetUnsafePtr(),
                    LocalCount = locals.Length,
                    Globals = globalBuffer.Length == 0 ? null : (MorrowindScriptGlobalValue*)globalBuffer.GetUnsafePtr(),
                    GlobalCount = globalBuffer.Length,
                    DeathCounts = deathCounts.Length == 0 ? null : (MorrowindActorDeathCount*)deathCounts.GetUnsafePtr(),
                    DeathCountCount = deathCounts.Length,
                    QuestJournal = questJournal.Length == 0 ? null : (MorrowindQuestJournalIndex*)questJournal.GetUnsafePtr(),
                    QuestJournalCount = questJournal.Length,
                    PreparedRequirements = PreparedRequirements,
                    RefDisabledStates = RefDisabledStates,
                    LogicalRefs = LogicalRefs,
                    ActiveExplicitRefs = ActiveExplicitRefs,
                    AllExplicitRefs = AllExplicitRefs,
                    CurrentTransforms = CurrentTransforms,
                    InitialTransformLookup = InitialTransformLookup,
                    ActorInventories = ActorInventories,
                    ContainerSessionItems = ContainerSessionItems,
                    ActorVitalsLookup = ActorVitalsLookup,
                    ActorHitAftermathStates = ActorHitAftermathStates,
                    ActorDeathCountedStates = ActorDeathCountedStates,
                    ActorOnDeathConsumedStates = ActorOnDeathConsumedStates,
                    Position = position,
                    Rotation = rotation,
                    PlayerPosition = PlayerPosition,
                    PlayerEntity = PlayerEntity,
                    PlayerStandingOnPlacedRefId = PlayerStandingOnPlacedRefId,
                    PlayerInventory = PlayerInventoryItems.Length == 0 ? null : (PlayerInventoryItem*)PlayerInventoryItems.GetUnsafeReadOnlyPtr(),
                    PlayerInventoryCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.PlayerInventory, PlayerInventoryItems.Length),
                    ActorKnownSpells = ActorKnownSpells.Length == 0 ? null : (ActorKnownSpell*)ActorKnownSpells.GetUnsafeReadOnlyPtr(),
                    ActorKnownSpellCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.PlayerKnownSpells, ActorKnownSpells.Length),
                    PlayerFactions = PlayerFactions.Length == 0 ? null : (PlayerFactionMembership*)PlayerFactions.GetUnsafeReadOnlyPtr(),
                    PlayerFactionCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.PlayerFactions, PlayerFactions.Length),
                    PlayerSkills = PlayerSkills,
                    PlayerCrime = PlayerCrime,
                    PlayerCrimeLevel = PlayerCrimeLevel,
                    ContainerSessionEntity = ContainerSessionEntity,
                    ExternalActorLocals = ExternalActorLocals.Length == 0 ? null : (MorrowindScriptExternalActorLocalSnapshot*)ExternalActorLocals.GetUnsafePtr(),
                    ExternalActorLocalCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ExternalActorLocals, ExternalActorLocals.Length),
                    ActorAiStatuses = ActorAiStatuses.Length == 0 ? null : (MorrowindScriptActorAiStatusSnapshot*)ActorAiStatuses.GetUnsafeReadOnlyPtr(),
                    ActorAiStatusCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorAiStatuses, ActorAiStatuses.Length),
                    ActorCombatTargets = ActorCombatTargets.Length == 0 ? null : (MorrowindScriptActorCombatTargetSnapshot*)ActorCombatTargets.GetUnsafeReadOnlyPtr(),
                    ActorCombatTargetCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorCombatTargets, ActorCombatTargets.Length),
                    LockStates = LockStates.Length == 0 ? null : (MorrowindScriptLockStateSnapshot*)LockStates.GetUnsafeReadOnlyPtr(),
                    LockStateCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.LockStates, LockStates.Length),
                    ActorEvents = ActorEvents.Length == 0 ? null : (MorrowindScriptActorEventSnapshot*)ActorEvents.GetUnsafeReadOnlyPtr(),
                    ActorEventCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorEvents, ActorEvents.Length),
                    ActorVitals = ActorVitals.Length == 0 ? null : (MorrowindScriptActorVitalSnapshot*)ActorVitals.GetUnsafeReadOnlyPtr(),
                    ActorVitalCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorVitals, ActorVitals.Length),
                    ActorAttributes = ActorAttributes.Length == 0 ? null : (MorrowindScriptActorAttributeSnapshot*)ActorAttributes.GetUnsafeReadOnlyPtr(),
                    ActorAttributeCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorAttributes, ActorAttributes.Length),
                    ActorActiveEffects = ActorActiveEffects.Length == 0 ? null : (MorrowindScriptActorActiveEffectSnapshot*)ActorActiveEffects.GetUnsafeReadOnlyPtr(),
                    ActorActiveEffectCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorActiveEffects, ActorActiveEffects.Length),
                    ActorDiseases = ActorDiseases.Length == 0 ? null : (MorrowindScriptActorDiseaseSnapshot*)ActorDiseases.GetUnsafeReadOnlyPtr(),
                    ActorDiseaseCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorDiseases, ActorDiseases.Length),
                    ActorIdentities = ActorIdentities.Length == 0 ? null : (MorrowindScriptActorIdentitySnapshot*)ActorIdentities.GetUnsafeReadOnlyPtr(),
                    ActorIdentityCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorIdentities, ActorIdentities.Length),
                    ActorAiSettings = ActorAiSettings.Length == 0 ? null : (MorrowindScriptActorAiSettingSnapshot*)ActorAiSettings.GetUnsafeReadOnlyPtr(),
                    ActorAiSettingCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorAiSettings, ActorAiSettings.Length),
                    ActorDispositions = ActorDispositions.Length == 0 ? null : (MorrowindScriptActorDispositionSnapshot*)ActorDispositions.GetUnsafeReadOnlyPtr(),
                    ActorDispositionCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorDispositions, ActorDispositions.Length),
                    ActorLineOfSight = ActorLineOfSight.Length == 0 ? null : (MorrowindScriptActorLineOfSightSnapshot*)ActorLineOfSight.GetUnsafeReadOnlyPtr(),
                    ActorLineOfSightCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorLineOfSight, ActorLineOfSight.Length),
                    ActorKnownSpellSnapshots = ActorKnownSpellSnapshots.Length == 0 ? null : (MorrowindScriptActorKnownSpellSnapshot*)ActorKnownSpellSnapshots.GetUnsafeReadOnlyPtr(),
                    ActorKnownSpellSnapshotCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActorKnownSpellSnapshots, ActorKnownSpellSnapshots.Length),
                    RunningPrograms = RunningPrograms.Length == 0 ? null : (MorrowindScriptRunningProgramSnapshot*)RunningPrograms.GetUnsafeReadOnlyPtr(),
                    RunningProgramCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.RunningPrograms, RunningPrograms.Length),
                    ActiveSays = ActiveSays.Length == 0 ? null : (MorrowindScriptActiveSaySnapshot*)ActiveSays.GetUnsafeReadOnlyPtr(),
                    ActiveSayCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActiveSays, ActiveSays.Length),
                    RandomState = RandomState,
                    Messages = Messages.Length == 0 ? null : (FixedString512Bytes*)Messages.GetUnsafeReadOnlyPtr(),
                    MessageCount = Messages.Length,
                    PlayingScriptSoundKeys = PlayingScriptSoundKeys.Length == 0 ? null : (ulong*)PlayingScriptSoundKeys.GetUnsafeReadOnlyPtr(),
                    PlayingScriptSoundKeyCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.PlayingSounds, PlayingScriptSoundKeys.Length),
                    PlacedRefId = placedRefId,
                    AudioSequenceBase = AudioSequenceBase,
                    HasPlayerPosition = HasPlayerPosition,
                    HasPlayerSkills = IsPrepared(PreparedRequirements, MorrowindScriptRequirementMask.PlayerSkills) ? HasPlayerSkills : (byte)0,
                    HasCellChanged = HasCellChanged,
                    CellChanged = CellChanged,
                    HasMenuMode = HasMenuMode,
                    MenuMode = MenuMode,
                    HasModalButtonPressed = HasModalButtonPressed,
                    ModalButtonPressed = ModalButtonPressed,
                    ModalButtonPressedRead = ModalButtonPressedRead,
                    HasPlayerSleeping = HasPlayerSleeping,
                    PlayerSleeping = PlayerSleeping,
                    HasPlayerCellName = HasPlayerCellName,
                    PlayerCellName = PlayerCellName,
                    HasCurrentWeather = HasCurrentWeather,
                    CurrentWeather = CurrentWeather,
                    SecondsPassed = SecondsPassed,
                    InteractionRuntimeEntity = InteractionRuntimeEntity,
                    QuestJournalRuntimeEntity = RuntimeEntity,
                    DialogueRuntimeEntity = RuntimeEntity,
                    MessageBoxRuntimeEntity = RuntimeEntity,
                    ShellRuntimeEntity = ShellRuntimeEntity,
                    MovementRuntimeEntity = MovementRuntimeEntity,
                    PlaceAtRuntimeEntity = PlaceAtRuntimeEntity,
                    AnimationRuntimeEntity = AnimationRuntimeEntity,
                    StartScriptRuntimeEntity = StartScriptRuntimeEntity,
                    StopScriptRuntimeEntity = RuntimeEntity,
                    ActorVitalRuntimeEntity = RuntimeEntity,
                    ActorSpellRuntimeEntity = RuntimeEntity,
                    CastRuntimeEntity = RuntimeEntity,
                    ForceGreetingRuntimeEntity = RuntimeEntity,
                    PlayerReputationRuntimeEntity = RuntimeEntity,
                    ActorAttributeRuntimeEntity = RuntimeEntity,
                    PlayerSkillRuntimeEntity = RuntimeEntity,
                    PlayerFactionRuntimeEntity = RuntimeEntity,
                    ActorFactionRuntimeEntity = RuntimeEntity,
                    InventoryDropRuntimeEntity = InventoryDropRuntimeEntity,
                    WeatherRuntimeEntity = WeatherRuntimeEntity,
                    MusicRuntimeEntity = MusicRuntimeEntity,
                    GlobalMapRevealRuntimeEntity = RuntimeEntity,
                    SayRuntimeEntity = RuntimeEntity,
                    OnDeathRuntimeEntity = RuntimeEntity,
                    ActorEventRuntimeEntity = ActorEventRuntimeEntity,
                    RefStateRuntimeEntity = RefStateRuntimeEntity,
                    TransformRuntimeEntity = TransformRuntimeEntity,
                    AiRuntimeEntity = AiRuntimeEntity,
                    ActivationEvents = ActivationEvents.Length == 0 ? null : (ScriptActivationEvent*)ActivationEvents.GetUnsafeReadOnlyPtr(),
                    ActivationEventCount = CountIfPrepared(PreparedRequirements, MorrowindScriptRequirementMask.ActivationEvents, ActivationEvents.Length),
                    MatchedActivationEventIndex = -1,
                    SelfDisabled = selfDisabled,
                };

                int pc = 0;
                int maxSteps = math.max(16, program.InstructionCount * 4);
                for (int step = 0; step < maxSteps; step++)
                {
                    if ((uint)pc >= (uint)program.InstructionCount)
                        break;

                    var instruction = Instructions[program.FirstInstructionIndex + pc];
                    context.ProgramCounter = 1;
                    context.FaultProgramCounter = pc;
                    context.FaultOpcode = instruction.Opcode;
                    if ((uint)instruction.Opcode >= (uint)OpcodeHandlers.Length)
                    {
                        context.Faulted = 1;
                        break;
                    }

                    OpcodeHandlers[instruction.Opcode].Invoke(&context, &instruction);
                    if (context.Faulted != 0)
                        break;

                    if (context.Halted != 0)
                        break;

                    pc += context.ProgramCounter;
                }

                if (context.Faulted != 0)
                {
                    Fault(ref instance, FormatScriptFault(programId, context.FaultProgramCounter, context.FaultOpcode, ScriptFaultReason.ScriptVmFault));
                    return false;
                }

                if (context.ObservedOnActivate != 0)
                    instance.SuppressActivation = 1;

                if (context.StopRequested != 0)
                {
                    instance.Status = (byte)MorrowindScriptInstanceStatus.Disabled;
                    instance.DisabledReason = BuildStoppedByStopScriptReason();
                }

                instance.ProgramCounter = 0;
                return true;
            }

            static bool IsPrepared(ulong preparedRequirements, MorrowindScriptRequirementMask requirement)
                => (preparedRequirements & (ulong)requirement) != 0UL;

            static int CountIfPrepared(ulong preparedRequirements, MorrowindScriptRequirementMask requirement, int count)
                => IsPrepared(preparedRequirements, requirement) ? count : -1;

            static void Fault(ref MorrowindScriptInstance instance, FixedString128Bytes reason)
            {
                instance.Status = (byte)MorrowindScriptInstanceStatus.Faulted;
                instance.DisabledReason = reason;
            }

            enum ScriptFaultReason : byte
            {
                InvalidProgramIndex,
                MissingQuestJournalRuntimeBuffer,
                ScriptVmFault,
            }

            static FixedString128Bytes FormatScriptFault(FixedString128Bytes programId, int pc, byte opcode, ScriptFaultReason reason)
            {
                var text = default(FixedString128Bytes);
                AppendScriptEquals(ref text);
                if (programId.IsEmpty)
                    AppendUnknown(ref text);
                else
                    text.Append(programId);
                if (pc >= 0)
                {
                    AppendPcEquals(ref text);
                    text.Append(pc);
                    AppendOpEquals(ref text);
                    text.Append(opcode);
                }
                AppendColonSpace(ref text);
                AppendFaultReason(ref text, reason);
                return text;
            }

            static FixedString128Bytes BuildStoppedByStopScriptReason()
            {
                var text = default(FixedString128Bytes);
                AppendStoppedByStopScript(ref text);
                return text;
            }

            static void AppendFaultReason(ref FixedString128Bytes text, ScriptFaultReason reason)
            {
                switch (reason)
                {
                    case ScriptFaultReason.InvalidProgramIndex:
                        AppendInvalidProgramIndex(ref text);
                        break;
                    case ScriptFaultReason.MissingQuestJournalRuntimeBuffer:
                        AppendMissingQuestJournalRuntimeBuffer(ref text);
                        break;
                    case ScriptFaultReason.ScriptVmFault:
                        AppendScriptVmFault(ref text);
                        break;
                }
            }

            static void AppendScriptEquals(ref FixedString128Bytes text)
            {
                text.Append((char)'s');
                text.Append((char)'c');
                text.Append((char)'r');
                text.Append((char)'i');
                text.Append((char)'p');
                text.Append((char)'t');
                text.Append((char)'=');
            }

            static void AppendUnknown(ref FixedString128Bytes text)
            {
                text.Append((char)'<');
                text.Append((char)'u');
                text.Append((char)'n');
                text.Append((char)'k');
                text.Append((char)'n');
                text.Append((char)'o');
                text.Append((char)'w');
                text.Append((char)'n');
                text.Append((char)'>');
            }

            static void AppendPcEquals(ref FixedString128Bytes text)
            {
                text.Append((char)' ');
                text.Append((char)'p');
                text.Append((char)'c');
                text.Append((char)'=');
            }

            static void AppendOpEquals(ref FixedString128Bytes text)
            {
                text.Append((char)' ');
                text.Append((char)'o');
                text.Append((char)'p');
                text.Append((char)'=');
            }

            static void AppendColonSpace(ref FixedString128Bytes text)
            {
                text.Append((char)':');
                text.Append((char)' ');
            }

            static void AppendStoppedByStopScript(ref FixedString128Bytes text)
            {
                text.Append((char)'S');
                text.Append((char)'t');
                text.Append((char)'o');
                text.Append((char)'p');
                text.Append((char)'p');
                text.Append((char)'e');
                text.Append((char)'d');
                text.Append((char)' ');
                text.Append((char)'b');
                text.Append((char)'y');
                text.Append((char)' ');
                text.Append((char)'S');
                text.Append((char)'t');
                text.Append((char)'o');
                text.Append((char)'p');
                text.Append((char)'S');
                text.Append((char)'c');
                text.Append((char)'r');
                text.Append((char)'i');
                text.Append((char)'p');
                text.Append((char)'t');
                text.Append((char)'.');
            }

            static void AppendInvalidProgramIndex(ref FixedString128Bytes text)
            {
                text.Append((char)'I');
                text.Append((char)'n');
                text.Append((char)'v');
                text.Append((char)'a');
                text.Append((char)'l');
                text.Append((char)'i');
                text.Append((char)'d');
                text.Append((char)' ');
                text.Append((char)'s');
                text.Append((char)'c');
                text.Append((char)'r');
                text.Append((char)'i');
                text.Append((char)'p');
                text.Append((char)'t');
                text.Append((char)' ');
                text.Append((char)'p');
                text.Append((char)'r');
                text.Append((char)'o');
                text.Append((char)'g');
                text.Append((char)'r');
                text.Append((char)'a');
                text.Append((char)'m');
                text.Append((char)' ');
                text.Append((char)'i');
                text.Append((char)'n');
                text.Append((char)'d');
                text.Append((char)'e');
                text.Append((char)'x');
                text.Append((char)'.');
            }

            static void AppendMissingQuestJournalRuntimeBuffer(ref FixedString128Bytes text)
            {
                text.Append((char)'M');
                text.Append((char)'i');
                text.Append((char)'s');
                text.Append((char)'s');
                text.Append((char)'i');
                text.Append((char)'n');
                text.Append((char)'g');
                text.Append((char)' ');
                text.Append((char)'q');
                text.Append((char)'u');
                text.Append((char)'e');
                text.Append((char)'s');
                text.Append((char)'t');
                text.Append((char)' ');
                text.Append((char)'j');
                text.Append((char)'o');
                text.Append((char)'u');
                text.Append((char)'r');
                text.Append((char)'n');
                text.Append((char)'a');
                text.Append((char)'l');
                text.Append((char)' ');
                text.Append((char)'r');
                text.Append((char)'u');
                text.Append((char)'n');
                text.Append((char)'t');
                text.Append((char)'i');
                text.Append((char)'m');
                text.Append((char)'e');
                text.Append((char)' ');
                text.Append((char)'b');
                text.Append((char)'u');
                text.Append((char)'f');
                text.Append((char)'f');
                text.Append((char)'e');
                text.Append((char)'r');
                text.Append((char)'.');
            }

            static void AppendScriptVmFault(ref FixedString128Bytes text)
            {
                text.Append((char)'S');
                text.Append((char)'c');
                text.Append((char)'r');
                text.Append((char)'i');
                text.Append((char)'p');
                text.Append((char)'t');
                text.Append((char)' ');
                text.Append((char)'V');
                text.Append((char)'M');
                text.Append((char)' ');
                text.Append((char)'f');
                text.Append((char)'a');
                text.Append((char)'u');
                text.Append((char)'l');
                text.Append((char)'t');
                text.Append((char)'.');
            }
        }

        [BurstCompile]
        unsafe partial struct InterpretObjectScriptsJob : IJobEntity
        {
            public MorrowindScriptInterpretCommon Common;
            [ReadOnly] public NativeHashSet<int2> ActiveExteriorCells;
            public ulong ActiveInteriorCellHash;
            public byte InteriorActive;
            public byte HasActiveExteriorCells;

            void Execute(
                [EntityIndexInQuery] int sortKey,
                Entity entity,
                ref MorrowindScriptInstance instance,
                DynamicBuffer<MorrowindScriptLocalValue> locals,
                in PlacedRefIdentity placedRef,
                in PlacedRefRuntimeState refState,
                in LogicalRefLocation location,
                in LocalTransform transform)
            {
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    return;

                if (refState.Disabled != 0)
                    return;

                if (!IsScriptLocationActive(location))
                    return;

                Common.TryInterpret(sortKey, entity, entity, placedRef.Value, refState.Disabled, transform.Position, transform.Rotation, ref instance, locals);
            }

            bool IsScriptLocationActive(in LogicalRefLocation location)
            {
                if (InteriorActive != 0)
                    return location.IsInterior != 0 && location.InteriorCellHash == ActiveInteriorCellHash;

                if (location.IsInterior != 0 || HasActiveExteriorCells == 0)
                    return false;

                return ActiveExteriorCells.Contains(location.ExteriorCell);
            }

        }

        [BurstCompile]
        unsafe partial struct InterpretGlobalScriptsJob : IJobEntity
        {
            public MorrowindScriptInterpretCommon Common;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> TargetRuntimeStates;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> TargetIdentities;
            [ReadOnly] public ComponentLookup<LocalTransform> TargetTransforms;

            void Execute(
                [EntityIndexInQuery] int sortKey,
                Entity entity,
                ref MorrowindScriptInstance instance,
                ref MorrowindGlobalScriptInstance global,
                DynamicBuffer<MorrowindScriptLocalValue> locals)
            {
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    return;

                Entity contextEntity = Entity.Null;
                uint placedRefId = global.TargetPlacedRefId;
                byte selfDisabled = 0;
                float3 position = default;
                quaternion rotation = quaternion.identity;

                if (global.TargetEntity != Entity.Null && TargetRuntimeStates.HasComponent(global.TargetEntity))
                {
                    contextEntity = global.TargetEntity;
                    selfDisabled = TargetRuntimeStates[global.TargetEntity].Disabled;
                    if (placedRefId == 0u && TargetIdentities.HasComponent(global.TargetEntity))
                        placedRefId = TargetIdentities[global.TargetEntity].Value;
                    if (TargetTransforms.HasComponent(global.TargetEntity))
                    {
                        var transform = TargetTransforms[global.TargetEntity];
                        position = transform.Position;
                        rotation = transform.Rotation;
                    }
                }

                Common.TryInterpret(sortKey, entity, contextEntity, placedRefId, selfDisabled, position, rotation, ref instance, locals);
            }
        }
    }
}
