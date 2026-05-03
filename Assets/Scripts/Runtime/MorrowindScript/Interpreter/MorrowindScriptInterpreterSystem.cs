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
            state.RequireForUpdate<PhysicsWorldSingleton>();
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

            var playerInventoryItems = CopySingletonBuffer<PlayerInventoryItem>(state.EntityManager);
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
            var externalActorLocals = CopyExternalActorLocals(state.EntityManager);
            var actorAiStatuses = CopyActorAiStatusSnapshots(state.EntityManager);
            var actorCombatTargets = CopyActorCombatTargetSnapshots(state.EntityManager);
            var refTransforms = CopyRefTransformSnapshots(state.EntityManager);
            var initialTransforms = CopyInitialTransformSnapshots(state.EntityManager);
            var lockStates = CopyLockStateSnapshots(state.EntityManager);
            var inventoryCounts = CopyInventoryCountSnapshots(state.EntityManager);
            var actorDeaths = CopyActorDeathSnapshots(state.EntityManager);
            var actorEvents = CopyActorEventSnapshots(state.EntityManager, playerEntity);
            var actorVitals = CopyActorVitalSnapshots(state.EntityManager, playerEntity);
            var actorAttributes = CopyActorAttributeSnapshots(state.EntityManager, playerEntity);
            var actorActiveEffects = CopyActorActiveEffectSnapshots(RuntimeContentDatabase.Active, state.EntityManager, playerEntity);
            var actorDiseases = CopyActorDiseaseSnapshots(RuntimeContentDatabase.Active, state.EntityManager, playerEntity);
            var actorIdentities = CopyActorIdentitySnapshots(state.EntityManager, playerEntity);
            var actorAiSettings = CopyActorAiSettingSnapshots(state.EntityManager, playerEntity);
            var actorDispositions = CopyActorDispositionSnapshots(state.EntityManager, playerEntity);
            var actorLineOfSight = CopyActorLineOfSightSnapshots(state.EntityManager, playerEntity, SystemAPI.GetSingleton<PhysicsWorldSingleton>());
            var actorKnownSpellSnapshots = CopyActorKnownSpellSnapshots(state.EntityManager);
            var runningPrograms = CopyRunningProgramSnapshots(state.EntityManager);

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
                Instructions = catalog.Instructions,
                Messages = catalog.Messages,
                OpcodeHandlers = catalog.OpcodeHandlers,
                Globals = _globalsLookup,
                DeathCounts = _deathCountsLookup,
                QuestJournal = _questJournalLookup,
                RefDisabledStates = placedRefRuntimeStates.DisabledByPlacedRef,
                ActiveExplicitRefs = activeExplicitRefs.ByContentKey,
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

        static NativeArray<T> CopySingletonBuffer<T>(EntityManager entityManager)
            where T : unmanaged, IBufferElementData
        {
            Entity owner = WorldStateEntityQueryUtility.GetSingletonBufferOwner<T>(entityManager);
            if (owner == Entity.Null || !entityManager.HasBuffer<T>(owner))
                return CreateEmptyTempJobArray<T>();

            var buffer = entityManager.GetBuffer<T>(owner, true);
            if (buffer.Length == 0)
                return CreateEmptyTempJobArray<T>();

            var copy = new NativeArray<T>(buffer.Length, Allocator.TempJob);
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

        static NativeArray<MorrowindScriptExternalActorLocalSnapshot> CopyExternalActorLocals(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadOnly<MorrowindScriptLocalValue>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptExternalActorLocalSnapshot>();

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var sources = query.ToComponentDataArray<ActorSpawnSource>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptExternalActorLocalSnapshot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!sources[i].Definition.IsValid || !entityManager.HasBuffer<MorrowindScriptLocalValue>(entities[i]))
                    continue;

                var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(entities[i], true);
                for (int local = 0; local < locals.Length; local++)
                {
                    snapshots.Add(new MorrowindScriptExternalActorLocalSnapshot
                    {
                        ActorHandleValue = sources[i].Definition.Value,
                        LocalIndex = local,
                        Value = locals[local],
                    });
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptExternalActorLocalSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptExternalActorLocalSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorAiStatusSnapshot> CopyActorAiStatusSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorAiState>(),
                ComponentType.ReadOnly<ActorAiPackageRuntime>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptActorAiStatusSnapshot>();

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var aiStates = query.ToComponentDataArray<ActorAiState>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptActorAiStatusSnapshot>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].Value == 0u)
                    continue;

                var packages = entityManager.GetBuffer<ActorAiPackageRuntime>(entities[i], true);
                snapshots.Add(new MorrowindScriptActorAiStatusSnapshot
                {
                    PlacedRefId = identities[i].Value,
                    Status = aiStates[i].Status,
                    CurrentPackageTypeId = ResolveCurrentAiPackageType(aiStates[i], packages),
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorAiStatusSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorAiStatusSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static int ResolveCurrentAiPackageType(in ActorAiState aiState, DynamicBuffer<ActorAiPackageRuntime> packages)
        {
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return -1;

            return packages[aiState.CurrentPackageIndex].Type switch
            {
                (byte)ActorAiRuntimePackageType.Wander => 0,
                (byte)ActorAiRuntimePackageType.Travel => 1,
                (byte)ActorAiRuntimePackageType.Follow => 3,
                _ => -1,
            };
        }

        static NativeArray<MorrowindScriptActorCombatTargetSnapshot> CopyActorCombatTargetSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ActorCombatTargetState>(),
                ComponentType.ReadOnly<ActorSpawnSource>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptActorCombatTargetSnapshot>();

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var states = query.ToComponentDataArray<ActorCombatTargetState>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptActorCombatTargetSnapshot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                uint actorPlacedRefId = entityManager.HasComponent<PlacedRefIdentity>(entities[i])
                    ? entityManager.GetComponentData<PlacedRefIdentity>(entities[i]).Value
                    : 0u;

                var state = states[i];
                uint targetPlacedRefId = state.TargetPlacedRefId;
                if (targetPlacedRefId == 0u
                    && state.TargetEntity != Entity.Null
                    && entityManager.Exists(state.TargetEntity)
                    && entityManager.HasComponent<PlacedRefIdentity>(state.TargetEntity))
                {
                    targetPlacedRefId = entityManager.GetComponentData<PlacedRefIdentity>(state.TargetEntity).Value;
                }

                snapshots.Add(new MorrowindScriptActorCombatTargetSnapshot
                {
                    ActorEntity = entities[i],
                    ActorPlacedRefId = actorPlacedRefId,
                    TargetEntity = state.TargetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    Active = state.Active,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorCombatTargetSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorCombatTargetSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptRefTransformSnapshot> CopyRefTransformSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LocalTransform>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptRefTransformSnapshot>();

            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptRefTransformSnapshot>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].Value == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptRefTransformSnapshot
                {
                    PlacedRefId = identities[i].Value,
                    Position = transforms[i].Position,
                    Rotation = transforms[i].Rotation,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptRefTransformSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptRefTransformSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptInitialTransformSnapshot> CopyInitialTransformSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<PlacedRefInitialTransform>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptInitialTransformSnapshot>();

            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<PlacedRefInitialTransform>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptInitialTransformSnapshot>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].Value == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptInitialTransformSnapshot
                {
                    PlacedRefId = identities[i].Value,
                    Position = transforms[i].Position,
                    Rotation = transforms[i].Rotation,
                    Scale = transforms[i].Scale,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptInitialTransformSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptInitialTransformSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptLockStateSnapshot> CopyLockStateSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<PlacedRefLockState>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptLockStateSnapshot>();

            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var lockStates = query.ToComponentDataArray<PlacedRefLockState>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptLockStateSnapshot>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].Value == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptLockStateSnapshot
                {
                    PlacedRefId = identities[i].Value,
                    LockLevel = lockStates[i].LockLevel,
                    Locked = lockStates[i].Locked,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptLockStateSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptLockStateSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptInventoryCountSnapshot> CopyInventoryCountSnapshots(EntityManager entityManager)
        {
            var snapshots = new NativeList<MorrowindScriptInventoryCountSnapshot>(Allocator.Temp);
            using (var actorQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorInventoryItem>()))
            {
                if (!actorQuery.IsEmptyIgnoreFilter)
                {
                    using var entities = actorQuery.ToEntityArray(Allocator.Temp);
                    using var identities = actorQuery.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u || !entityManager.HasBuffer<ActorInventoryItem>(entities[i]))
                            continue;

                        var inventory = entityManager.GetBuffer<ActorInventoryItem>(entities[i], true);
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
                }
            }

            Entity containerOwner = WorldStateEntityQueryUtility.GetSingletonBufferOwner<ContainerSessionItem>(entityManager);
            if (containerOwner != Entity.Null && entityManager.HasBuffer<ContainerSessionItem>(containerOwner))
            {
                var containerItems = entityManager.GetBuffer<ContainerSessionItem>(containerOwner, true);
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

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptInventoryCountSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptInventoryCountSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorDeathSnapshot> CopyActorDeathSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorVitalSet>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptActorDeathSnapshot>();

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var vitals = query.ToComponentDataArray<ActorVitalSet>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptActorDeathSnapshot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptActorDeathSnapshot
                {
                    Entity = entities[i],
                    PlacedRefId = placedRefId,
                    Died = vitals[i].CurrentHealth <= 0f || entityManager.HasComponent<MorrowindActorDeathCounted>(entities[i]) ? (byte)1 : (byte)0,
                    Consumed = entityManager.HasComponent<MorrowindActorOnDeathConsumed>(entities[i]) ? (byte)1 : (byte)0,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorDeathSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorDeathSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorEventSnapshot> CopyActorEventSnapshots(EntityManager entityManager, Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorEventSnapshot>(Allocator.Temp);
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<ActorVitalSet>(playerEntity))
            {
                var state = entityManager.HasComponent<ActorScriptEventState>(playerEntity)
                    ? entityManager.GetComponentData<ActorScriptEventState>(playerEntity)
                    : default;
                snapshots.Add(new MorrowindScriptActorEventSnapshot
                {
                    Entity = playerEntity,
                    PlacedRefId = 0u,
                    Murdered = state.Murdered,
                    Attacked = state.Attacked,
                    KnockedDownOneFrame = state.KnockedDownOneFrame,
                    LastHitObject = state.LastHitObject,
                });
            }

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorVitalSet>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        var state = entityManager.HasComponent<ActorScriptEventState>(entities[i])
                            ? entityManager.GetComponentData<ActorScriptEventState>(entities[i])
                            : default;
                        snapshots.Add(new MorrowindScriptActorEventSnapshot
                        {
                            Entity = entities[i],
                            PlacedRefId = placedRefId,
                            Murdered = state.Murdered,
                            Attacked = state.Attacked,
                            KnockedDownOneFrame = state.KnockedDownOneFrame,
                            LastHitObject = state.LastHitObject,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorEventSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorEventSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorVitalSnapshot> CopyActorVitalSnapshots(EntityManager entityManager, Entity playerEntity)
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorVitalSet>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var vitals = query.ToComponentDataArray<ActorVitalSet>(Allocator.Temp);
                    for (int i = 0; i < identities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        snapshots.Add(new MorrowindScriptActorVitalSnapshot
                        {
                            PlacedRefId = placedRefId,
                            Health = vitals[i].CurrentHealth,
                            Magicka = vitals[i].CurrentMagicka,
                            Fatigue = vitals[i].CurrentFatigue,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorVitalSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorVitalSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorAttributeSnapshot> CopyActorAttributeSnapshots(EntityManager entityManager, Entity playerEntity)
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorAttributeSet>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var attributes = query.ToComponentDataArray<ActorAttributeSet>(Allocator.Temp);
                    for (int i = 0; i < identities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        snapshots.Add(new MorrowindScriptActorAttributeSnapshot
                        {
                            PlacedRefId = placedRefId,
                            Attributes = attributes[i],
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorAttributeSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorAttributeSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorActiveEffectSnapshot> CopyActorActiveEffectSnapshots(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity playerEntity)
        {
            var snapshots = new NativeList<MorrowindScriptActorActiveEffectSnapshot>(Allocator.Temp);
            AppendActiveEffectSnapshots(contentDb, entityManager, playerEntity, 0u, snapshots);

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        AppendActiveEffectSnapshots(contentDb, entityManager, entities[i], placedRefId, snapshots);
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorActiveEffectSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorActiveEffectSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
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
                string sourceId = effect.SourceId.ToString();
                if (!string.IsNullOrWhiteSpace(sourceId)
                    && contentDb != null
                    && contentDb.TryGetSpellHandle(sourceId, out var resolvedSourceSpell)
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

        static NativeArray<MorrowindScriptActorDiseaseSnapshot> CopyActorDiseaseSnapshots(
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorSpawnSource>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        ResolveDiseaseFlags(contentDb, entityManager, entities[i], out byte commonDisease, out byte blightDisease);
                        snapshots.Add(new MorrowindScriptActorDiseaseSnapshot
                        {
                            PlacedRefId = placedRefId,
                            HasCommonDisease = commonDisease,
                            HasBlightDisease = blightDisease,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorDiseaseSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorDiseaseSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorIdentitySnapshot> CopyActorIdentitySnapshots(
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorIdentitySet>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var placedRefs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var identities = query.ToComponentDataArray<ActorIdentitySet>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = placedRefs[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        snapshots.Add(new MorrowindScriptActorIdentitySnapshot
                        {
                            ActorEntity = entities[i],
                            PlacedRefId = placedRefId,
                            RaceName = identities[i].RaceName,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorIdentitySnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorIdentitySnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorAiSettingSnapshot> CopyActorAiSettingSnapshots(
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorAiSettingsState>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var placedRefs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var settings = query.ToComponentDataArray<ActorAiSettingsState>(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        uint placedRefId = placedRefs[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        snapshots.Add(new MorrowindScriptActorAiSettingSnapshot
                        {
                            ActorEntity = entities[i],
                            PlacedRefId = placedRefId,
                            Hello = settings[i].Hello,
                            Fight = settings[i].Fight,
                            Flee = settings[i].Flee,
                            Alarm = settings[i].Alarm,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorAiSettingSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorAiSettingSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorKnownSpellSnapshot> CopyActorKnownSpellSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ActorKnownSpell>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptActorKnownSpellSnapshot>();

            using var entities = query.ToEntityArray(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptActorKnownSpellSnapshot>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!entityManager.HasBuffer<ActorKnownSpell>(entity))
                    continue;

                uint placedRefId = entityManager.HasComponent<PlacedRefIdentity>(entity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                    : 0u;
                var knownSpells = entityManager.GetBuffer<ActorKnownSpell>(entity, true);
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

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorKnownSpellSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorKnownSpellSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptActorDispositionSnapshot> CopyActorDispositionSnapshots(EntityManager entityManager, Entity playerEntity)
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

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<ActorDispositionState>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var entities = query.ToEntityArray(Allocator.Temp);
                    using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var dispositions = query.ToComponentDataArray<ActorDispositionState>(Allocator.Temp);
                    for (int i = 0; i < identities.Length; i++)
                    {
                        uint placedRefId = identities[i].Value;
                        if (placedRefId == 0u)
                            continue;

                        snapshots.Add(new MorrowindScriptActorDispositionSnapshot
                        {
                            ActorEntity = entities[i],
                            PlacedRefId = placedRefId,
                            BaseDisposition = dispositions[i].BaseDisposition,
                        });
                    }
                }
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptActorDispositionSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptActorDispositionSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
        }

        static NativeArray<MorrowindScriptRunningProgramSnapshot> CopyRunningProgramSnapshots(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindScriptInstance>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptRunningProgramSnapshot>();

            using var instances = query.ToComponentDataArray<MorrowindScriptInstance>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptRunningProgramSnapshot>(Allocator.Temp);
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].Status != (byte)MorrowindScriptInstanceStatus.Running)
                    continue;

                snapshots.Add(new MorrowindScriptRunningProgramSnapshot
                {
                    ProgramIndex = instances[i].ProgramIndex,
                    Running = 1,
                });
            }

            if (snapshots.Count == 0)
            {
                snapshots.Dispose();
                return CreateEmptyTempJobArray<MorrowindScriptRunningProgramSnapshot>();
            }

            var result = new NativeArray<MorrowindScriptRunningProgramSnapshot>(snapshots.Count, Allocator.TempJob);
            for (int i = 0; i < snapshots.Count; i++)
                result[i] = snapshots[i];
            snapshots.Dispose();
            return result;
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
                    string sourceId = activeEffects[i].SourceId.ToString();
                    if (!string.IsNullOrWhiteSpace(sourceId)
                        && contentDb.TryGetSpellHandle(sourceId, out var sourceSpell)
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
            public uint PlacedRefId;
            public float3 EyePosition;
        }

        static NativeArray<MorrowindScriptActorLineOfSightSnapshot> CopyActorLineOfSightSnapshots(
            EntityManager entityManager,
            Entity playerEntity,
            PhysicsWorldSingleton physicsWorld)
        {
            var actors = new NativeList<ScriptLineOfSightActor>(Allocator.Temp);
            if (playerEntity != Entity.Null
                && entityManager.Exists(playerEntity)
                && entityManager.HasComponent<LocalTransform>(playerEntity))
            {
                actors.Add(new ScriptLineOfSightActor
                {
                    PlacedRefId = 0u,
                    EyePosition = GetActorEyePosition(entityManager.GetComponentData<LocalTransform>(playerEntity)),
                });
            }

            using (var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<PlacedRefRuntimeState>(),
                ComponentType.ReadOnly<ActorIdentitySet>(),
                ComponentType.ReadOnly<LocalTransform>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    using var placedRefs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
                    using var runtimeStates = query.ToComponentDataArray<PlacedRefRuntimeState>(Allocator.Temp);
                    using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    for (int i = 0; i < placedRefs.Length; i++)
                    {
                        if (placedRefs[i].Value == 0u || runtimeStates[i].Disabled != 0)
                            continue;

                        actors.Add(new ScriptLineOfSightActor
                        {
                            PlacedRefId = placedRefs[i].Value,
                            EyePosition = GetActorEyePosition(transforms[i]),
                        });
                    }
                }
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
                        HasLineOfSight = HasActorLineOfSight(physicsWorld, source.EyePosition, target.EyePosition) ? (byte)1 : (byte)0,
                    };
                }
            }

            actors.Dispose();
            return result;
        }

        static float3 GetActorEyePosition(in LocalTransform transform)
            => transform.Position + new float3(0f, 1.62f, 0f);

        static bool HasActorLineOfSight(PhysicsWorldSingleton physicsWorld, float3 source, float3 target)
        {
            if (math.distancesq(source, target) <= 0.0001f)
                return true;

            var input = new RaycastInput
            {
                Start = source,
                End = target,
                Filter = InteractionCollisionLayers.LineOfSightQueryFilter,
            };
            return !physicsWorld.CastRay(input);
        }

        [BurstCompile]
        unsafe struct MorrowindScriptInterpretCommon
        {
            [ReadOnly] public NativeArray<MorrowindScriptProgramRuntime> Programs;
            [ReadOnly] public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
            [ReadOnly] public NativeArray<FixedString512Bytes> Messages;
            [ReadOnly] public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindScriptGlobalValue> Globals;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindActorDeathCount> DeathCounts;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindQuestJournalIndex> QuestJournal;
            [ReadOnly] public NativeParallelHashMap<uint, byte> RefDisabledStates;
            [ReadOnly] public NativeParallelHashMap<int, ActiveExplicitRefTarget> ActiveExplicitRefs;
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
                    Fault(ref instance, "Invalid script program index.");
                    return false;
                }

                var program = Programs[instance.ProgramIndex];
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
                    Fault(ref instance, "Missing quest journal runtime buffer.");
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
                    Fault(ref instance, "Script VM fault.");
                    return false;
                }

                if (context.ObservedOnActivate != 0)
                    instance.SuppressActivation = 1;

                if (context.StopRequested != 0)
                {
                    instance.Status = (byte)MorrowindScriptInstanceStatus.Disabled;
                    instance.DisabledReason = "Stopped by StopScript.";
                }

                instance.ProgramCounter = 0;
                return true;
            }

            static void Fault(ref MorrowindScriptInstance instance, FixedString128Bytes reason)
            {
                instance.Status = (byte)MorrowindScriptInstanceStatus.Faulted;
                instance.DisabledReason = reason;
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
