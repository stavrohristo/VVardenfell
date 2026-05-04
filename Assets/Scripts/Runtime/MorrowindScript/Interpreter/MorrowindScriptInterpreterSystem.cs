using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    public unsafe partial struct MorrowindScriptInterpreterSystem : ISystem
    {
        EntityQuery _scriptQuery;
        EntityQuery _globalScriptQuery;
        BufferLookup<MorrowindScriptGlobalValue> _globalsLookup;
        BufferLookup<MorrowindActorDeathCount> _deathCountsLookup;
        BufferLookup<MorrowindQuestJournalIndex> _questJournalLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefStateLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefIdentityLookup;
        ComponentLookup<LocalTransform> _transformLookup;
        byte _hasLastCellContext;
        byte _lastInteriorActive;
        int2 _lastExteriorCell;
        ulong _lastInteriorCellHash;

        public void OnCreate(ref SystemState state)
        {
            _scriptQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadWrite<MorrowindScriptStackValue>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<PlacedRefRuntimeState>(),
                ComponentType.ReadOnly<LogicalRefLocation>(),
                ComponentType.ReadOnly<LocalTransform>());
            _globalScriptQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindGlobalScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadWrite<MorrowindScriptStackValue>());
            _globalsLookup = state.GetBufferLookup<MorrowindScriptGlobalValue>(false);
            _deathCountsLookup = state.GetBufferLookup<MorrowindActorDeathCount>(false);
            _questJournalLookup = state.GetBufferLookup<MorrowindQuestJournalIndex>(false);
            _placedRefStateLookup = state.GetComponentLookup<PlacedRefRuntimeState>(true);
            _placedRefIdentityLookup = state.GetComponentLookup<PlacedRefIdentity>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
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
            state.RequireForUpdate<PlacedRefRuntimeStateLookup>();
            state.RequireForUpdate<ActiveExplicitRefLookup>();
            state.RequireForUpdate<DeferredPhysicsQueryQueueTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var catalog = WorldResources.MorrowindScriptCatalog;
            bool objectScriptsEmpty = _scriptQuery.IsEmptyIgnoreFilter;
            bool globalScriptsEmpty = _globalScriptQuery.IsEmptyIgnoreFilter;
            if (catalog == null || !catalog.IsCreated || (objectScriptsEmpty && globalScriptsEmpty))
                return;
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
            _placedRefStateLookup.Update(ref state);
            _placedRefIdentityLookup.Update(ref state);
            _transformLookup.Update(ref state);
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

            var playingBuffer = state.EntityManager.GetBuffer<MorrowindScriptPlayingSound>(runtimeEntity);
            var playingSoundKeys = new NativeArray<ulong>(playingBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < playingBuffer.Length; i++)
                playingSoundKeys[i] = playingBuffer[i].LoopKey;

            var activeSayBuffer = state.EntityManager.GetBuffer<MorrowindScriptActiveSay>(runtimeEntity);
            var activeSays = new NativeArray<MorrowindScriptActiveSaySnapshot>(activeSayBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < activeSayBuffer.Length; i++)
            {
                activeSays[i] = new MorrowindScriptActiveSaySnapshot
                {
                    SourceEntity = activeSayBuffer[i].SourceEntity,
                    SourcePlacedRefId = activeSayBuffer[i].SourcePlacedRefId,
                };
            }

            var activationBuffer = state.EntityManager.GetBuffer<ScriptActivationEvent>(interactionRuntimeEntity);
            var activationEvents = new NativeArray<ScriptActivationEvent>(activationBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < activationBuffer.Length; i++)
                activationEvents[i] = activationBuffer[i];

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
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

            var playerInventoryItems = CopyPlayerInventoryItems(ref state);
            var actorKnownSpells = CopyEntityBuffer<ActorKnownSpell>(state.EntityManager, playerEntity);
            var playerFactions = CopyEntityBuffer<PlayerFactionMembership>(state.EntityManager, playerEntity);
            byte hasPlayerSkills = 0;
            ActorSkillSet playerSkills = default;
            if (playerEntity != Entity.Null && state.EntityManager.HasComponent<ActorSkillSet>(playerEntity))
            {
                playerSkills = state.EntityManager.GetComponentData<ActorSkillSet>(playerEntity);
                hasPlayerSkills = 1;
            }

            var playerCrime = PlayerCrimeState.Default;
            if (playerEntity != Entity.Null && state.EntityManager.HasComponent<PlayerCrimeState>(playerEntity))
                playerCrime = state.EntityManager.GetComponentData<PlayerCrimeState>(playerEntity);
            var externalActorLocals = CopyExternalActorLocals(ref state);
            var actorAiStatuses = CopyActorAiStatusSnapshots(ref state);
            var actorCombatTargets = CopyActorCombatTargetSnapshots(ref state, state.EntityManager);
            var refTransforms = CopyRefTransformSnapshots(ref state);
            var initialTransforms = CopyInitialTransformSnapshots(ref state);
            var lockStates = CopyLockStateSnapshots(ref state);
            var inventoryCounts = CopyInventoryCountSnapshots(ref state, state.EntityManager);
            var actorDeaths = CopyActorDeathSnapshots(ref state, state.EntityManager);
            var actorEvents = CopyActorEventSnapshots(ref state, state.EntityManager, playerEntity);
            var actorVitals = CopyActorVitalSnapshots(ref state, state.EntityManager, playerEntity);
            var actorAttributes = CopyActorAttributeSnapshots(ref state, state.EntityManager, playerEntity);
            var actorActiveEffects = CopyActorActiveEffectSnapshots(ref state, RuntimeContentDatabase.Active, state.EntityManager, playerEntity);
            var actorDiseases = CopyActorDiseaseSnapshots(ref state, RuntimeContentDatabase.Active, state.EntityManager, playerEntity);
            var actorIdentities = CopyActorIdentitySnapshots(ref state, state.EntityManager, playerEntity);
            var actorAiSettings = CopyActorAiSettingSnapshots(ref state, state.EntityManager, playerEntity);
            var actorKnownSpellSnapshots = CopyActorKnownSpellSnapshots(ref state, state.EntityManager);
            var runningPrograms = CopyRunningProgramSnapshots(ref state);
            var actorDispositions = CopyActorDispositionSnapshots(ref state, state.EntityManager, playerEntity);
            var actorLineOfSight = RunningProgramsNeedLineOfSight(catalog, runningPrograms)
                ? CopyActorLineOfSightSnapshots(ref state, state.EntityManager, playerEntity)
                : CreateEmptyTempJobArray<MorrowindScriptActorLineOfSightSnapshot>();

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
                hasPlayerCellName = TryResolveExteriorCellName(currentExteriorCell, out playerCellName) ? (byte)1 : (byte)0;
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
                Globals = _globalsLookup,
                DeathCounts = _deathCountsLookup,
                QuestJournal = _questJournalLookup,
                RefDisabledStates = placedRefRuntimeStates.DisabledByPlacedRef,
                ActiveExplicitRefs = activeExplicitRefs.ByContentKey,
                AllExplicitRefs = activeExplicitRefs.AllByContentKey,
                RuntimeEntity = runtimeEntity,
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
                ExternalActorLocals = externalActorLocals,
                ActorAiStatuses = actorAiStatuses,
                ActorCombatTargets = actorCombatTargets,
                RefTransforms = refTransforms,
                InitialTransforms = initialTransforms,
                LockStates = lockStates,
                InventoryCounts = inventoryCounts,
                ActorDeaths = actorDeaths,
                ActorEvents = actorEvents,
                ActorVitals = actorVitals,
                ActorAttributes = actorAttributes,
                ActorActiveEffects = actorActiveEffects,
                ActorDiseases = actorDiseases,
                ActorIdentities = actorIdentities,
                ActorAiSettings = actorAiSettings,
                ActorDispositions = actorDispositions,
                ActorLineOfSight = actorLineOfSight,
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
            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            runtimeState.RandomState = scriptRandomState.Value == 0u ? 0x6E624EB7u : scriptRandomState.Value;
            if (modalButtonPressedRead.Value != 0 && SystemAPI.TryGetSingletonRW<RuntimeShellState>(out var shellRef))
            {
                shellRef.ValueRW.ModalButtonPressedValid = 0;
                shellRef.ValueRW.ModalButtonPressed = -1;
            }

            scriptRandomState.Dispose();
            modalButtonPressedRead.Dispose();
            playingSoundKeys.Dispose();
            activeSays.Dispose();
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
            if (refTransforms.IsCreated)
                refTransforms.Dispose();
            if (initialTransforms.IsCreated)
                initialTransforms.Dispose();
            if (lockStates.IsCreated)
                lockStates.Dispose();
            if (inventoryCounts.IsCreated)
                inventoryCounts.Dispose();
            if (actorDeaths.IsCreated)
                actorDeaths.Dispose();
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
            if (!SystemAPI.TryGetSingletonBuffer<PlayerInventoryItem>(out var buffer, true) || buffer.Length == 0)
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

        NativeArray<MorrowindScriptExternalActorLocalSnapshot> CopyExternalActorLocals(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptExternalActorLocalSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAiStatusSnapshot> CopyActorAiStatusSnapshots(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptActorAiStatusSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
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

        NativeArray<MorrowindScriptActorCombatTargetSnapshot> CopyActorCombatTargetSnapshots(ref SystemState state, EntityManager entityManager)
        {
            var snapshots = new NativeList<MorrowindScriptActorCombatTargetSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptRefTransformSnapshot> CopyRefTransformSnapshots(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptRefTransformSnapshot>(Allocator.Temp);
            foreach (var (identityRef, transformRef) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<LocalTransform>>())
            {
                var identity = identityRef.ValueRO;
                if (identity.Value == 0u)
                    continue;

                var transform = transformRef.ValueRO;
                snapshots.Add(new MorrowindScriptRefTransformSnapshot
                {
                    PlacedRefId = identity.Value,
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                });
            }

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptInitialTransformSnapshot> CopyInitialTransformSnapshots(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptInitialTransformSnapshot>(Allocator.Temp);
            foreach (var (identityRef, transformRef) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<PlacedRefInitialTransform>>())
            {
                var identity = identityRef.ValueRO;
                if (identity.Value == 0u)
                    continue;

                var transform = transformRef.ValueRO;
                snapshots.Add(new MorrowindScriptInitialTransformSnapshot
                {
                    PlacedRefId = identity.Value,
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Scale = transform.Scale,
                });
            }

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptLockStateSnapshot> CopyLockStateSnapshots(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptLockStateSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptInventoryCountSnapshot> CopyInventoryCountSnapshots(ref SystemState state, EntityManager entityManager)
        {
            var snapshots = new NativeList<MorrowindScriptInventoryCountSnapshot>(Allocator.Temp);
            foreach (var (identityRef, inventory) in SystemAPI.Query<RefRO<PlacedRefIdentity>, DynamicBuffer<ActorInventoryItem>>())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                for (int item = 0; item < inventory.Length; item++)
                {
                    var entry = inventory[item];
                    if (!entry.Content.IsValid || entry.Count <= 0)
                        continue;

                    snapshots.Add(new MorrowindScriptInventoryCountSnapshot
                    {
                        PlacedRefId = placedRefId,
                        Content = entry.Content,
                        Count = entry.Count,
                    });
                }
            }

            if (SystemAPI.TryGetSingletonBuffer<ContainerSessionItem>(out var containerItems, true))
            {
                for (int i = 0; i < containerItems.Length; i++)
                {
                    var entry = containerItems[i];
                    if (entry.PlacedRefId == 0u || !entry.Content.IsValid || entry.Count <= 0)
                        continue;

                    snapshots.Add(new MorrowindScriptInventoryCountSnapshot
                    {
                        PlacedRefId = entry.PlacedRefId,
                        Content = entry.Content,
                        Count = entry.Count,
                    });
                }
            }

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorDeathSnapshot> CopyActorDeathSnapshots(ref SystemState state, EntityManager entityManager)
        {
            var snapshots = new NativeList<MorrowindScriptActorDeathSnapshot>(Allocator.Temp);
            foreach (var (identityRef, vitalRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<ActorVitalSet>>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                byte died = entityManager.HasComponent<MorrowindActorDeathCounted>(entity) ? (byte)1 : (byte)0;
                if (entityManager.HasComponent<ActorHitAftermathState>(entity))
                {
                    var aftermath = entityManager.GetComponentData<ActorHitAftermathState>(entity);
                    if (aftermath.Dead != 0)
                        died = 1;
                }
                else if (vitalRef.ValueRO.CurrentHealth <= 0f)
                {
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Actor ref={placedRefId} reached zero health without ActorHitAftermathState.");
                }

                snapshots.Add(new MorrowindScriptActorDeathSnapshot
                {
                    Entity = entity,
                    PlacedRefId = placedRefId,
                    Died = died,
                    Consumed = entityManager.HasComponent<MorrowindActorOnDeathConsumed>(entity) ? (byte)1 : (byte)0,
                });
            }

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorEventSnapshot> CopyActorEventSnapshots(ref SystemState state, EntityManager entityManager, Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorEventSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorVitalSnapshot> CopyActorVitalSnapshots(ref SystemState state, EntityManager entityManager, Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorVitalSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAttributeSnapshot> CopyActorAttributeSnapshots(ref SystemState state, EntityManager entityManager, Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorAttributeSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorActiveEffectSnapshot> CopyActorActiveEffectSnapshots(
            ref SystemState state,
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorActiveEffectSnapshot>(Allocator.Temp);
            AppendActiveEffectSnapshots(contentDb, entityManager, playerEntity, 0u, snapshots);

            foreach (var (identityRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>>().WithAll<ActorActiveMagicEffect>().WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                AppendActiveEffectSnapshots(contentDb, entityManager, entity, placedRefId, snapshots);
            }

            return MoveToTempJobArray(snapshots);
        }

        static void AppendActiveEffectSnapshots(
            RuntimeContentDatabase contentDb,
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
                if (!effect.SourceId.IsEmpty
                    && contentDb != null
                    && contentDb.TryGetSpellHandle(effect.SourceId.ToString(), out var resolvedSourceSpell)
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
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorDiseaseSnapshot>(Allocator.Temp);
            if (playerEntity != Entity.Null && entityManager.Exists(playerEntity))
            {
                ResolveDiseaseFlags(contentDb, entityManager, playerEntity, out byte commonDisease, out byte blightDisease);
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

                ResolveDiseaseFlags(contentDb, entityManager, entity, out byte commonDisease, out byte blightDisease);
                snapshots.Add(new MorrowindScriptActorDiseaseSnapshot
                {
                    PlacedRefId = placedRefId,
                    HasCommonDisease = commonDisease,
                    HasBlightDisease = blightDisease,
                });
            }

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorIdentitySnapshot> CopyActorIdentitySnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorIdentitySnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorAiSettingSnapshot> CopyActorAiSettingSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorAiSettingSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorKnownSpellSnapshot> CopyActorKnownSpellSnapshots(ref SystemState state, EntityManager entityManager)
        {
            var snapshots = new NativeList<MorrowindScriptActorKnownSpellSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptActorDispositionSnapshot> CopyActorDispositionSnapshots(ref SystemState state, EntityManager entityManager, Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorDispositionSnapshot>(Allocator.Temp);
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

            return MoveToTempJobArray(snapshots);
        }

        NativeArray<MorrowindScriptRunningProgramSnapshot> CopyRunningProgramSnapshots(ref SystemState state)
        {
            var snapshots = new NativeList<MorrowindScriptRunningProgramSnapshot>(Allocator.Temp);
            foreach (var instanceRef in SystemAPI.Query<RefRO<MorrowindScriptInstance>>())
            {
                var instance = instanceRef.ValueRO;
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    continue;

                snapshots.Add(new MorrowindScriptRunningProgramSnapshot
                {
                    ProgramIndex = instance.ProgramIndex,
                    Running = 1,
                });
            }

            return MoveToTempJobArray(snapshots);
        }

        static void ResolveDiseaseFlags(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            out byte commonDisease,
            out byte blightDisease)
        {
            commonDisease = 0;
            blightDisease = 0;
            if (contentDb == null || actor == Entity.Null || !entityManager.Exists(actor))
                return;

            if (entityManager.HasBuffer<ActorKnownSpell>(actor))
            {
                var knownSpells = entityManager.GetBuffer<ActorKnownSpell>(actor, true);
                for (int i = 0; i < knownSpells.Length; i++)
                    ResolveDiseaseSpell(contentDb, knownSpells[i].Spell, ref commonDisease, ref blightDisease);
            }

            if (entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
            {
                var activeEffects = entityManager.GetBuffer<ActorActiveMagicEffect>(actor, true);
                for (int i = 0; i < activeEffects.Length; i++)
                {
                    if (!activeEffects[i].SourceId.IsEmpty
                        && contentDb.TryGetSpellHandle(activeEffects[i].SourceId.ToString(), out var sourceSpell)
                        && sourceSpell.IsValid)
                    {
                        ResolveDiseaseSpell(contentDb, sourceSpell, ref commonDisease, ref blightDisease);
                    }
                }
            }
        }

        static void ResolveDiseaseSpell(
            RuntimeContentDatabase contentDb,
            SpellDefHandle spellHandle,
            ref byte commonDisease,
            ref byte blightDisease)
        {
            if (contentDb == null || !spellHandle.IsValid)
                return;

            ref readonly var spell = ref contentDb.Get(spellHandle);
            if (spell.SpellType == 2)
                blightDisease = 1;
            else if (spell.SpellType == 3)
                commonDisease = 1;
        }

        static NativeArray<T> CreateEmptyTempJobArray<T>()
            where T : unmanaged
            => new(0, Allocator.TempJob);

        static NativeArray<T> MoveToTempJobArray<T>(NativeList<T> snapshots)
            where T : unmanaged
        {
            if (snapshots.Length == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<T>();
            }

            var result = new NativeArray<T>(snapshots.Length, Allocator.TempJob);
            for (int i = 0; i < snapshots.Length; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static bool TryResolveExteriorCellName(int2 exteriorCell, out FixedString128Bytes cellName)
        {
            cellName = default;
            if (!WorldResources.Cells.TryGetValue(exteriorCell, out var cell) || cell == null)
                return false;

            if (!string.IsNullOrWhiteSpace(cell.CellId))
            {
                cellName = RuntimeFixedStringUtility.ToFixed128OrDefault(cell.CellId);
                return !cellName.IsEmpty;
            }

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb != null
                && !string.IsNullOrWhiteSpace(cell.Environment.RegionId)
                && contentDb.TryGetRegionHandle(cell.Environment.RegionId, out var regionHandle)
                && regionHandle.IsValid)
            {
                ref readonly var region = ref contentDb.Get(regionHandle);
                cellName = RuntimeFixedStringUtility.ToFixed128OrDefault(!string.IsNullOrWhiteSpace(region.Name) ? region.Name : region.Id);
                return !cellName.IsEmpty;
            }

            if (contentDb != null && contentDb.TryGetGameSettingString("sDefaultCellname", out string defaultCellName))
            {
                cellName = RuntimeFixedStringUtility.ToFixed128OrDefault(defaultCellName);
                return !cellName.IsEmpty;
            }

            return false;
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

        static bool RunningProgramsNeedLineOfSight(
            MorrowindScriptRuntimeCatalog catalog,
            NativeArray<MorrowindScriptRunningProgramSnapshot> runningPrograms)
        {
            if (runningPrograms.Length == 0)
                return false;

            for (int i = 0; i < runningPrograms.Length; i++)
            {
                int programIndex = runningPrograms[i].ProgramIndex;
                if ((uint)programIndex >= (uint)catalog.Programs.Length)
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Running script program index {programIndex} is outside the runtime catalog.");

                var program = catalog.Programs[programIndex];
                if (program.InstructionCount <= 0)
                    continue;

                if (program.FirstInstructionIndex < 0
                    || program.InstructionCount < 0
                    || program.FirstInstructionIndex > catalog.Instructions.Length - program.InstructionCount)
                {
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Running script program index {programIndex} has an invalid instruction range.");
                }

                int end = program.FirstInstructionIndex + program.InstructionCount;
                for (int instructionIndex = program.FirstInstructionIndex; instructionIndex < end; instructionIndex++)
                {
                    byte opcode = catalog.Instructions[instructionIndex].Opcode;
                    if (opcode == (byte)MorrowindScriptOpcode.GetLOS
                        || opcode == (byte)MorrowindScriptOpcode.GetDetected)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        NativeArray<MorrowindScriptActorLineOfSightSnapshot> CopyActorLineOfSightSnapshots(
            ref SystemState state,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var actors = new NativeList<ScriptLineOfSightActor>(Allocator.Temp);
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<LocalTransform>(playerEntity))
            {
                actors.Add(new ScriptLineOfSightActor
                {
                    Entity = playerEntity,
                    PlacedRefId = 0u,
                    EyePosition = GetActorEyePosition(entityManager.GetComponentData<LocalTransform>(playerEntity)),
                });
            }

            foreach (var (placedRefRef, runtimeStateRef, transformRef, entity) in SystemAPI.Query<RefRO<PlacedRefIdentity>, RefRO<PlacedRefRuntimeState>, RefRO<LocalTransform>>().WithAll<ActorIdentitySet>().WithEntityAccess())
            {
                var placedRef = placedRefRef.ValueRO;
                if (placedRef.Value == 0u || runtimeStateRef.ValueRO.Disabled != 0)
                    continue;

                actors.Add(new ScriptLineOfSightActor
                {
                    Entity = entity,
                    PlacedRefId = placedRef.Value,
                    EyePosition = GetActorEyePosition(transformRef.ValueRO),
                });
            }

            if (actors.Length < 2)
            {
                actors.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorLineOfSightSnapshot>();
            }

            int pairCount = actors.Length * (actors.Length - 1);
            var result = new NativeArray<MorrowindScriptActorLineOfSightSnapshot>(pairCount, Allocator.TempJob);
            int index = 0;
            for (int sourceIndex = 0; sourceIndex < actors.Length; sourceIndex++)
            {
                var source = actors[sourceIndex];
                for (int targetIndex = 0; targetIndex < actors.Length; targetIndex++)
                {
                    if (sourceIndex == targetIndex)
                        continue;

                    var target = actors[targetIndex];
                    result[index++] = new MorrowindScriptActorLineOfSightSnapshot
                    {
                        SourcePlacedRefId = source.PlacedRefId,
                        TargetPlacedRefId = target.PlacedRefId,
                        HasLineOfSight = HasActorLineOfSight(
                            entityManager,
                            source.Entity,
                            target.Entity,
                            source.EyePosition,
                            target.EyePosition) ? (byte)1 : (byte)0,
                    };
                }
            }

            actors.Dispose();
            return result;
        }

        static float3 GetActorEyePosition(in LocalTransform transform)
            => transform.Position + new float3(0f, 1.62f, 0f);

        static bool HasActorLineOfSight(
            EntityManager entityManager,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target)
        {
            if (math.distancesq(source, target) <= 0.0001f)
                return true;

            return DeferredPhysicsQueryUtility.TryGetLineOfSightOrRequest(
                       entityManager,
                       sourceEntity,
                       targetEntity,
                       source,
                       target,
                       InteractionCollisionLayers.LineOfSightQueryFilter,
                       DeferredPhysicsQueryUtility.DefaultMaxResultAgeTicks,
                       out bool hasLineOfSight)
                   && hasLineOfSight;
        }

        [BurstCompile]
        unsafe struct MorrowindScriptInterpretCommon
        {
            [ReadOnly] public NativeArray<MorrowindScriptProgramRuntime> Programs;
            [ReadOnly] public NativeArray<FixedString128Bytes> ProgramIds;
            [ReadOnly] public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
            [ReadOnly] public NativeArray<FixedString512Bytes> Messages;
            [ReadOnly] public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindScriptGlobalValue> Globals;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindActorDeathCount> DeathCounts;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindQuestJournalIndex> QuestJournal;
            [ReadOnly] public NativeParallelHashMap<uint, byte> RefDisabledStates;
            [ReadOnly] public NativeParallelHashMap<int, ActiveExplicitRefTarget> ActiveExplicitRefs;
            [ReadOnly] public NativeParallelHashMap<int, ActiveExplicitRefTarget> AllExplicitRefs;
            public Entity RuntimeEntity;
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
            [ReadOnly] public NativeArray<MorrowindScriptRefTransformSnapshot> RefTransforms;
            [ReadOnly] public NativeArray<MorrowindScriptInitialTransformSnapshot> InitialTransforms;
            [ReadOnly] public NativeArray<MorrowindScriptLockStateSnapshot> LockStates;
            [ReadOnly] public NativeArray<MorrowindScriptInventoryCountSnapshot> InventoryCounts;
            [ReadOnly] public NativeArray<MorrowindScriptActorDeathSnapshot> ActorDeaths;
            [ReadOnly] public NativeArray<MorrowindScriptActorEventSnapshot> ActorEvents;
            [ReadOnly] public NativeArray<MorrowindScriptActorVitalSnapshot> ActorVitals;
            [ReadOnly] public NativeArray<MorrowindScriptActorAttributeSnapshot> ActorAttributes;
            [ReadOnly] public NativeArray<MorrowindScriptActorActiveEffectSnapshot> ActorActiveEffects;
            [ReadOnly] public NativeArray<MorrowindScriptActorDiseaseSnapshot> ActorDiseases;
            [ReadOnly] public NativeArray<MorrowindScriptActorIdentitySnapshot> ActorIdentities;
            [ReadOnly] public NativeArray<MorrowindScriptActorAiSettingSnapshot> ActorAiSettings;
            [ReadOnly] public NativeArray<MorrowindScriptActorDispositionSnapshot> ActorDispositions;
            [ReadOnly] public NativeArray<MorrowindScriptActorLineOfSightSnapshot> ActorLineOfSight;
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
            [ReadOnly] public NativeArray<ScriptActivationEvent> ActivationEvents;

            public bool TryInterpret(
                int sortKey,
                Entity scriptEntity,
                Entity contextEntity,
                uint placedRefId,
                byte selfDisabled,
                float3 position,
                quaternion rotation,
                ref MorrowindScriptInstance instance,
                DynamicBuffer<MorrowindScriptLocalValue> locals,
                DynamicBuffer<MorrowindScriptStackValue> stack)
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

                Ecb.AppendToBuffer(sortKey, RuntimeEntity, new MorrowindScriptActiveSource
                {
                    LoopSourceKey = MorrowindScriptOpcodeTable.BuildScriptLoopSourceKey(placedRefId, scriptEntity),
                });

                if (locals.Length < program.LocalCount)
                    locals.ResizeUninitialized(program.LocalCount);

                int stackCapacity = math.max(1, program.MaxStack);
                if (stack.Length < stackCapacity)
                    stack.ResizeUninitialized(stackCapacity);

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
                    Stack = (MorrowindScriptStackValue*)stack.GetUnsafePtr(),
                    Locals = locals.Length == 0 ? null : (MorrowindScriptLocalValue*)locals.GetUnsafePtr(),
                    LocalCount = locals.Length,
                    Globals = globalBuffer.Length == 0 ? null : (MorrowindScriptGlobalValue*)globalBuffer.GetUnsafePtr(),
                    GlobalCount = globalBuffer.Length,
                    DeathCounts = deathCounts.Length == 0 ? null : (MorrowindActorDeathCount*)deathCounts.GetUnsafePtr(),
                    DeathCountCount = deathCounts.Length,
                    QuestJournal = questJournal.Length == 0 ? null : (MorrowindQuestJournalIndex*)questJournal.GetUnsafePtr(),
                    QuestJournalCount = questJournal.Length,
                    RefDisabledStates = RefDisabledStates,
                    ActiveExplicitRefs = ActiveExplicitRefs,
                    AllExplicitRefs = AllExplicitRefs,
                    Position = position,
                    Rotation = rotation,
                    PlayerPosition = PlayerPosition,
                    PlayerEntity = PlayerEntity,
                    PlayerStandingOnPlacedRefId = PlayerStandingOnPlacedRefId,
                    PlayerInventory = PlayerInventoryItems.Length == 0 ? null : (PlayerInventoryItem*)PlayerInventoryItems.GetUnsafeReadOnlyPtr(),
                    PlayerInventoryCount = PlayerInventoryItems.Length,
                    ActorKnownSpells = ActorKnownSpells.Length == 0 ? null : (ActorKnownSpell*)ActorKnownSpells.GetUnsafeReadOnlyPtr(),
                    ActorKnownSpellCount = ActorKnownSpells.Length,
                    PlayerFactions = PlayerFactions.Length == 0 ? null : (PlayerFactionMembership*)PlayerFactions.GetUnsafeReadOnlyPtr(),
                    PlayerFactionCount = PlayerFactions.Length,
                    PlayerSkills = PlayerSkills,
                    PlayerCrime = PlayerCrime,
                    PlayerCrimeLevel = PlayerCrimeLevel,
                    ExternalActorLocals = ExternalActorLocals.Length == 0 ? null : (MorrowindScriptExternalActorLocalSnapshot*)ExternalActorLocals.GetUnsafePtr(),
                    ExternalActorLocalCount = ExternalActorLocals.Length,
                    ActorAiStatuses = ActorAiStatuses.Length == 0 ? null : (MorrowindScriptActorAiStatusSnapshot*)ActorAiStatuses.GetUnsafeReadOnlyPtr(),
                    ActorAiStatusCount = ActorAiStatuses.Length,
                    ActorCombatTargets = ActorCombatTargets.Length == 0 ? null : (MorrowindScriptActorCombatTargetSnapshot*)ActorCombatTargets.GetUnsafeReadOnlyPtr(),
                    ActorCombatTargetCount = ActorCombatTargets.Length,
                    RefTransforms = RefTransforms.Length == 0 ? null : (MorrowindScriptRefTransformSnapshot*)RefTransforms.GetUnsafeReadOnlyPtr(),
                    RefTransformCount = RefTransforms.Length,
                    InitialTransforms = InitialTransforms.Length == 0 ? null : (MorrowindScriptInitialTransformSnapshot*)InitialTransforms.GetUnsafeReadOnlyPtr(),
                    InitialTransformCount = InitialTransforms.Length,
                    LockStates = LockStates.Length == 0 ? null : (MorrowindScriptLockStateSnapshot*)LockStates.GetUnsafeReadOnlyPtr(),
                    LockStateCount = LockStates.Length,
                    InventoryCounts = InventoryCounts.Length == 0 ? null : (MorrowindScriptInventoryCountSnapshot*)InventoryCounts.GetUnsafeReadOnlyPtr(),
                    InventoryCountCount = InventoryCounts.Length,
                    ActorDeaths = ActorDeaths.Length == 0 ? null : (MorrowindScriptActorDeathSnapshot*)ActorDeaths.GetUnsafeReadOnlyPtr(),
                    ActorDeathCount = ActorDeaths.Length,
                    ActorEvents = ActorEvents.Length == 0 ? null : (MorrowindScriptActorEventSnapshot*)ActorEvents.GetUnsafeReadOnlyPtr(),
                    ActorEventCount = ActorEvents.Length,
                    ActorVitals = ActorVitals.Length == 0 ? null : (MorrowindScriptActorVitalSnapshot*)ActorVitals.GetUnsafeReadOnlyPtr(),
                    ActorVitalCount = ActorVitals.Length,
                    ActorAttributes = ActorAttributes.Length == 0 ? null : (MorrowindScriptActorAttributeSnapshot*)ActorAttributes.GetUnsafeReadOnlyPtr(),
                    ActorAttributeCount = ActorAttributes.Length,
                    ActorActiveEffects = ActorActiveEffects.Length == 0 ? null : (MorrowindScriptActorActiveEffectSnapshot*)ActorActiveEffects.GetUnsafeReadOnlyPtr(),
                    ActorActiveEffectCount = ActorActiveEffects.Length,
                    ActorDiseases = ActorDiseases.Length == 0 ? null : (MorrowindScriptActorDiseaseSnapshot*)ActorDiseases.GetUnsafeReadOnlyPtr(),
                    ActorDiseaseCount = ActorDiseases.Length,
                    ActorIdentities = ActorIdentities.Length == 0 ? null : (MorrowindScriptActorIdentitySnapshot*)ActorIdentities.GetUnsafeReadOnlyPtr(),
                    ActorIdentityCount = ActorIdentities.Length,
                    ActorAiSettings = ActorAiSettings.Length == 0 ? null : (MorrowindScriptActorAiSettingSnapshot*)ActorAiSettings.GetUnsafeReadOnlyPtr(),
                    ActorAiSettingCount = ActorAiSettings.Length,
                    ActorDispositions = ActorDispositions.Length == 0 ? null : (MorrowindScriptActorDispositionSnapshot*)ActorDispositions.GetUnsafeReadOnlyPtr(),
                    ActorDispositionCount = ActorDispositions.Length,
                    ActorLineOfSight = ActorLineOfSight.Length == 0 ? null : (MorrowindScriptActorLineOfSightSnapshot*)ActorLineOfSight.GetUnsafeReadOnlyPtr(),
                    ActorLineOfSightCount = ActorLineOfSight.Length,
                    ActorKnownSpellSnapshots = ActorKnownSpellSnapshots.Length == 0 ? null : (MorrowindScriptActorKnownSpellSnapshot*)ActorKnownSpellSnapshots.GetUnsafeReadOnlyPtr(),
                    ActorKnownSpellSnapshotCount = ActorKnownSpellSnapshots.Length,
                    RunningPrograms = RunningPrograms.Length == 0 ? null : (MorrowindScriptRunningProgramSnapshot*)RunningPrograms.GetUnsafeReadOnlyPtr(),
                    RunningProgramCount = RunningPrograms.Length,
                    ActiveSays = ActiveSays.Length == 0 ? null : (MorrowindScriptActiveSaySnapshot*)ActiveSays.GetUnsafeReadOnlyPtr(),
                    ActiveSayCount = ActiveSays.Length,
                    RandomState = RandomState,
                    Messages = Messages.Length == 0 ? null : (FixedString512Bytes*)Messages.GetUnsafeReadOnlyPtr(),
                    MessageCount = Messages.Length,
                    PlayingScriptSoundKeys = PlayingScriptSoundKeys.Length == 0 ? null : (ulong*)PlayingScriptSoundKeys.GetUnsafeReadOnlyPtr(),
                    PlayingScriptSoundKeyCount = PlayingScriptSoundKeys.Length,
                    PlacedRefId = placedRefId,
                    AudioSequenceBase = AudioSequenceBase,
                    HasPlayerPosition = HasPlayerPosition,
                    HasPlayerSkills = HasPlayerSkills,
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
                    ActivationEventCount = ActivationEvents.Length,
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
                DynamicBuffer<MorrowindScriptStackValue> stack,
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

                Common.TryInterpret(sortKey, entity, entity, placedRef.Value, refState.Disabled, transform.Position, transform.Rotation, ref instance, locals, stack);
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
                DynamicBuffer<MorrowindScriptLocalValue> locals,
                DynamicBuffer<MorrowindScriptStackValue> stack)
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

                Common.TryInterpret(sortKey, entity, contextEntity, placedRefId, selfDisabled, position, rotation, ref instance, locals, stack);
            }
        }
    }
}
