using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
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
            state.RequireForUpdate<MorrowindScriptTransformRequest>();
            state.RequireForUpdate<MorrowindScriptActorVitalRequest>();
            state.RequireForUpdate<MorrowindQuestJournalRequest>();
            state.RequireForUpdate<MorrowindDialogueRequest>();
            state.RequireForUpdate<MorrowindScriptMessageBoxRequest>();
            state.RequireForUpdate<MorrowindScriptOnDeathConsumeRequest>();
            state.RequireForUpdate<InteractionActivationRequest>();
            state.RequireForUpdate<LoadedCellsMap>();
            state.RequireForUpdate<PlacedRefRuntimeStateLookup>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var catalog = WorldResources.MorrowindScriptCatalog;
            bool objectScriptsEmpty = _scriptQuery.IsEmptyIgnoreFilter;
            bool globalScriptsEmpty = _globalScriptQuery.IsEmptyIgnoreFilter;
            if (catalog == null || !catalog.IsCreated || (objectScriptsEmpty && globalScriptsEmpty))
                return;

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
            var transformRequests = state.EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            transformRequests.Clear();
            var questJournalRequests = state.EntityManager.GetBuffer<MorrowindQuestJournalRequest>(runtimeEntity);
            questJournalRequests.Clear();
            var dialogueRequests = state.EntityManager.GetBuffer<MorrowindDialogueRequest>(runtimeEntity);
            dialogueRequests.Clear();
            var inventoryMutationRequests = state.EntityManager.GetBuffer<MorrowindScriptInventoryMutationRequest>(runtimeEntity);
            inventoryMutationRequests.Clear();
            var actorVitalRequests = state.EntityManager.GetBuffer<MorrowindScriptActorVitalRequest>(runtimeEntity);
            actorVitalRequests.Clear();
            var messageBoxRequests = state.EntityManager.GetBuffer<MorrowindScriptMessageBoxRequest>(runtimeEntity);
            messageBoxRequests.Clear();
            var onDeathConsumeRequests = state.EntityManager.GetBuffer<MorrowindScriptOnDeathConsumeRequest>(runtimeEntity);
            onDeathConsumeRequests.Clear();

            var playingBuffer = state.EntityManager.GetBuffer<MorrowindScriptPlayingSound>(runtimeEntity);
            var playingSoundKeys = new NativeArray<ulong>(playingBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < playingBuffer.Length; i++)
                playingSoundKeys[i] = playingBuffer[i].LoopKey;

            var activationBuffer = state.EntityManager.GetBuffer<ScriptActivationEvent>(interactionRuntimeEntity);
            var activationEvents = new NativeArray<ScriptActivationEvent>(activationBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < activationBuffer.Length; i++)
                activationEvents[i] = activationBuffer[i];

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            var placedRefRuntimeStates = SystemAPI.GetSingleton<PlacedRefRuntimeStateLookup>();
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
            if (SystemAPI.TryGetSingletonEntity<PlayerTag>(out playerEntity)
                && SystemAPI.HasComponent<LocalTransform>(playerEntity))
            {
                playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
                hasPlayerPosition = 1;
            }

            var playerInventoryItems = CopySingletonBuffer<PlayerInventoryItem>(state.EntityManager);
            var playerKnownSpells = CopyEntityBuffer<PlayerKnownSpell>(state.EntityManager, playerEntity);
            var playerFactions = CopyEntityBuffer<PlayerFactionMembership>(state.EntityManager, playerEntity);
            int playerCrimeLevel = 0;
            if (playerEntity != Entity.Null && state.EntityManager.HasComponent<PlayerCrimeState>(playerEntity))
                playerCrimeLevel = state.EntityManager.GetComponentData<PlayerCrimeState>(playerEntity).Bounty;
            var externalActorLocals = CopyExternalActorLocals(state.EntityManager);
            var actorAiStatuses = CopyActorAiStatusSnapshots(state.EntityManager);
            var refTransforms = CopyRefTransformSnapshots(state.EntityManager);
            var inventoryCounts = CopyInventoryCountSnapshots(state.EntityManager);
            var actorDeaths = CopyActorDeathSnapshots(state.EntityManager);

            byte hasMenuMode = 0;
            byte menuMode = 0;
            if (SystemAPI.TryGetSingleton<RuntimeShellState>(out var shell))
            {
                hasMenuMode = 1;
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
                RuntimeEntity = runtimeEntity,
                RefStateRuntimeEntity = runtimeEntity,
                TransformRuntimeEntity = runtimeEntity,
                AiRuntimeEntity = runtimeEntity,
                Ecb = ecb.AsParallelWriter(),
                AudioSequenceBase = sequenceBase,
                PlayerPosition = playerPosition,
                PlayerEntity = playerEntity,
                PlayerInventoryItems = playerInventoryItems,
                PlayerKnownSpells = playerKnownSpells,
                PlayerFactions = playerFactions,
                PlayerCrimeLevel = playerCrimeLevel,
                ExternalActorLocals = externalActorLocals,
                ActorAiStatuses = actorAiStatuses,
                RefTransforms = refTransforms,
                InventoryCounts = inventoryCounts,
                ActorDeaths = actorDeaths,
                HasPlayerPosition = hasPlayerPosition,
                PlayingScriptSoundKeys = playingSoundKeys,
                HasCellChanged = hasCellChanged,
                CellChanged = cellChanged,
                HasMenuMode = hasMenuMode,
                MenuMode = menuMode,
                HasPlayerCellName = hasPlayerCellName,
                PlayerCellName = playerCellName,
                SecondsPassed = math.max(0f, SystemAPI.Time.DeltaTime),
                InteractionRuntimeEntity = interactionRuntimeEntity,
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
            playingSoundKeys.Dispose();
            activationEvents.Dispose();
            if (playerInventoryItems.IsCreated)
                playerInventoryItems.Dispose();
            if (playerKnownSpells.IsCreated)
                playerKnownSpells.Dispose();
            if (playerFactions.IsCreated)
                playerFactions.Dispose();
            if (externalActorLocals.IsCreated)
                externalActorLocals.Dispose();
            if (actorAiStatuses.IsCreated)
                actorAiStatuses.Dispose();
            if (refTransforms.IsCreated)
                refTransforms.Dispose();
            if (inventoryCounts.IsCreated)
                inventoryCounts.Dispose();
            if (actorDeaths.IsCreated)
                actorDeaths.Dispose();

            if (hasCellChanged != 0)
            {
                _hasLastCellContext = 1;
                _lastInteriorActive = interiorActive;
                _lastExteriorCell = currentExteriorCell;
                _lastInteriorCellHash = activeInteriorCellHash;
            }
        }

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
                ComponentType.ReadOnly<ActorAiState>());
            if (query.IsEmptyIgnoreFilter)
                return CreateEmptyTempJobArray<MorrowindScriptActorAiStatusSnapshot>();

            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var aiStates = query.ToComponentDataArray<ActorAiState>(Allocator.Temp);
            var snapshots = new NativeList<MorrowindScriptActorAiStatusSnapshot>(Allocator.Temp);
            for (int i = 0; i < identities.Length; i++)
            {
                if (identities[i].Value == 0u)
                    continue;

                snapshots.Add(new MorrowindScriptActorAiStatusSnapshot
                {
                    PlacedRefId = identities[i].Value,
                    Status = aiStates[i].Status,
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
            public Entity RuntimeEntity;
            public Entity RefStateRuntimeEntity;
            public Entity TransformRuntimeEntity;
            public Entity AiRuntimeEntity;
            public EntityCommandBuffer.ParallelWriter Ecb;
            public uint AudioSequenceBase;
            public float3 PlayerPosition;
            public Entity PlayerEntity;
            [ReadOnly] public NativeArray<PlayerInventoryItem> PlayerInventoryItems;
            [ReadOnly] public NativeArray<PlayerKnownSpell> PlayerKnownSpells;
            [ReadOnly] public NativeArray<PlayerFactionMembership> PlayerFactions;
            public int PlayerCrimeLevel;
            public NativeArray<MorrowindScriptExternalActorLocalSnapshot> ExternalActorLocals;
            [ReadOnly] public NativeArray<MorrowindScriptActorAiStatusSnapshot> ActorAiStatuses;
            [ReadOnly] public NativeArray<MorrowindScriptRefTransformSnapshot> RefTransforms;
            [ReadOnly] public NativeArray<MorrowindScriptInventoryCountSnapshot> InventoryCounts;
            [ReadOnly] public NativeArray<MorrowindScriptActorDeathSnapshot> ActorDeaths;
            [ReadOnly] public NativeArray<ulong> PlayingScriptSoundKeys;
            public byte HasPlayerPosition;
            public byte HasCellChanged;
            public byte CellChanged;
            public byte HasMenuMode;
            public byte MenuMode;
            public byte HasPlayerCellName;
            public FixedString128Bytes PlayerCellName;
            public float SecondsPassed;
            public Entity InteractionRuntimeEntity;
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
                    Position = position,
                    Rotation = rotation,
                    PlayerPosition = PlayerPosition,
                    PlayerEntity = PlayerEntity,
                    PlayerInventory = PlayerInventoryItems.Length == 0 ? null : (PlayerInventoryItem*)PlayerInventoryItems.GetUnsafeReadOnlyPtr(),
                    PlayerInventoryCount = PlayerInventoryItems.Length,
                    PlayerKnownSpells = PlayerKnownSpells.Length == 0 ? null : (PlayerKnownSpell*)PlayerKnownSpells.GetUnsafeReadOnlyPtr(),
                    PlayerKnownSpellCount = PlayerKnownSpells.Length,
                    PlayerFactions = PlayerFactions.Length == 0 ? null : (PlayerFactionMembership*)PlayerFactions.GetUnsafeReadOnlyPtr(),
                    PlayerFactionCount = PlayerFactions.Length,
                    PlayerCrimeLevel = PlayerCrimeLevel,
                    ExternalActorLocals = ExternalActorLocals.Length == 0 ? null : (MorrowindScriptExternalActorLocalSnapshot*)ExternalActorLocals.GetUnsafePtr(),
                    ExternalActorLocalCount = ExternalActorLocals.Length,
                    ActorAiStatuses = ActorAiStatuses.Length == 0 ? null : (MorrowindScriptActorAiStatusSnapshot*)ActorAiStatuses.GetUnsafeReadOnlyPtr(),
                    ActorAiStatusCount = ActorAiStatuses.Length,
                    RefTransforms = RefTransforms.Length == 0 ? null : (MorrowindScriptRefTransformSnapshot*)RefTransforms.GetUnsafeReadOnlyPtr(),
                    RefTransformCount = RefTransforms.Length,
                    InventoryCounts = InventoryCounts.Length == 0 ? null : (MorrowindScriptInventoryCountSnapshot*)InventoryCounts.GetUnsafeReadOnlyPtr(),
                    InventoryCountCount = InventoryCounts.Length,
                    ActorDeaths = ActorDeaths.Length == 0 ? null : (MorrowindScriptActorDeathSnapshot*)ActorDeaths.GetUnsafeReadOnlyPtr(),
                    ActorDeathCount = ActorDeaths.Length,
                    Messages = Messages.Length == 0 ? null : (FixedString512Bytes*)Messages.GetUnsafeReadOnlyPtr(),
                    MessageCount = Messages.Length,
                    PlayingScriptSoundKeys = PlayingScriptSoundKeys.Length == 0 ? null : (ulong*)PlayingScriptSoundKeys.GetUnsafeReadOnlyPtr(),
                    PlayingScriptSoundKeyCount = PlayingScriptSoundKeys.Length,
                    PlacedRefId = placedRefId,
                    AudioSequenceBase = AudioSequenceBase,
                    HasPlayerPosition = HasPlayerPosition,
                    HasCellChanged = HasCellChanged,
                    CellChanged = CellChanged,
                    HasMenuMode = HasMenuMode,
                    MenuMode = MenuMode,
                    HasPlayerCellName = HasPlayerCellName,
                    PlayerCellName = PlayerCellName,
                    SecondsPassed = SecondsPassed,
                    InteractionRuntimeEntity = InteractionRuntimeEntity,
                    QuestJournalRuntimeEntity = RuntimeEntity,
                    DialogueRuntimeEntity = RuntimeEntity,
                    MessageBoxRuntimeEntity = RuntimeEntity,
                    ActorVitalRuntimeEntity = RuntimeEntity,
                    OnDeathRuntimeEntity = RuntimeEntity,
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
