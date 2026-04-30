using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    public unsafe partial struct MorrowindScriptInterpreterSystem : ISystem
    {
        EntityQuery _scriptQuery;
        BufferLookup<MorrowindScriptGlobalValue> _globalsLookup;
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
                ComponentType.ReadOnly<LogicalRefLocation>(),
                ComponentType.ReadOnly<LocalTransform>());
            _globalsLookup = state.GetBufferLookup<MorrowindScriptGlobalValue>(false);
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<LoadedCellsMap>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var catalog = WorldResources.MorrowindScriptCatalog;
            if (catalog == null || !catalog.IsCreated || _scriptQuery.IsEmptyIgnoreFilter)
                return;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            ref var runtimeState = ref SystemAPI.GetSingletonRW<MorrowindScriptRuntimeState>().ValueRW;
            uint sequenceBase = runtimeState.NextAudioRequestSequence;
            int scriptCount = _scriptQuery.CalculateEntityCount();
            runtimeState.NextAudioRequestSequence += (uint)math.max(1, scriptCount + 1);
            _globalsLookup.Update(ref state);
            var activeSources = state.EntityManager.GetBuffer<MorrowindScriptActiveSource>(runtimeEntity);
            activeSources.Clear();

            var playingBuffer = state.EntityManager.GetBuffer<MorrowindScriptPlayingSound>(runtimeEntity);
            var playingSoundKeys = new NativeArray<ulong>(playingBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < playingBuffer.Length; i++)
                playingSoundKeys[i] = playingBuffer[i].LoopKey;

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
            }

            byte hasPlayerPosition = 0;
            float3 playerPosition = default;
            if (SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)
                && SystemAPI.HasComponent<LocalTransform>(playerEntity))
            {
                playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
                hasPlayerPosition = 1;
            }

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
                hasCellChanged = 1;
                cellChanged = _hasLastCellContext == 0
                    || _lastInteriorActive != 0
                    || math.any(currentExteriorCell != _lastExteriorCell)
                        ? (byte)1
                        : (byte)0;
            }

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var job = new InterpretObjectScriptsJob
            {
                Programs = catalog.Programs,
                Instructions = catalog.Instructions,
                OpcodeHandlers = catalog.OpcodeHandlers,
                Globals = _globalsLookup,
                ActiveExteriorCells = loadedCells.Active,
                ActiveInteriorCellHash = activeInteriorCellHash,
                InteriorActive = interiorActive,
                HasActiveExteriorCells = loadedCells.Active.IsCreated ? (byte)1 : (byte)0,
                RuntimeEntity = runtimeEntity,
                Ecb = ecb.AsParallelWriter(),
                AudioSequenceBase = sequenceBase,
                PlayerPosition = playerPosition,
                HasPlayerPosition = hasPlayerPosition,
                PlayingScriptSoundKeys = playingSoundKeys,
                HasCellChanged = hasCellChanged,
                CellChanged = cellChanged,
                HasMenuMode = hasMenuMode,
                MenuMode = menuMode,
            };

            state.Dependency = job.Schedule(_scriptQuery, state.Dependency);
            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            playingSoundKeys.Dispose();

            if (hasCellChanged != 0)
            {
                _hasLastCellContext = 1;
                _lastInteriorActive = interiorActive;
                _lastExteriorCell = currentExteriorCell;
                _lastInteriorCellHash = activeInteriorCellHash;
            }
        }

        [BurstCompile]
        unsafe partial struct InterpretObjectScriptsJob : IJobEntity
        {
            [ReadOnly] public NativeArray<MorrowindScriptProgramRuntime> Programs;
            [ReadOnly] public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
            [ReadOnly] public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;
            [NativeDisableParallelForRestriction] public BufferLookup<MorrowindScriptGlobalValue> Globals;
            [ReadOnly] public NativeHashSet<int2> ActiveExteriorCells;
            public ulong ActiveInteriorCellHash;
            public byte InteriorActive;
            public byte HasActiveExteriorCells;
            public Entity RuntimeEntity;
            public EntityCommandBuffer.ParallelWriter Ecb;
            public uint AudioSequenceBase;
            public float3 PlayerPosition;
            [ReadOnly] public NativeArray<ulong> PlayingScriptSoundKeys;
            public byte HasPlayerPosition;
            public byte HasCellChanged;
            public byte CellChanged;
            public byte HasMenuMode;
            public byte MenuMode;

            void Execute(
                [EntityIndexInQuery] int sortKey,
                Entity entity,
                ref MorrowindScriptInstance instance,
                DynamicBuffer<MorrowindScriptLocalValue> locals,
                DynamicBuffer<MorrowindScriptStackValue> stack,
                in PlacedRefIdentity placedRef,
                in LogicalRefLocation location,
                in LocalTransform transform)
            {
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Running)
                    return;

                if (!IsScriptLocationActive(location))
                    return;

                if ((uint)instance.ProgramIndex >= (uint)Programs.Length)
                {
                    Fault(ref instance, "Invalid script program index.");
                    return;
                }

                var program = Programs[instance.ProgramIndex];
                if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled || program.InstructionCount <= 0)
                    return;

                Ecb.AppendToBuffer(sortKey, RuntimeEntity, new MorrowindScriptActiveSource
                {
                    LoopSourceKey = MorrowindScriptOpcodeTable.BuildScriptLoopSourceKey(placedRef.Value, entity),
                });

                if (locals.Length < program.LocalCount)
                    locals.ResizeUninitialized(program.LocalCount);

                int stackCapacity = math.max(1, program.MaxStack);
                if (stack.Length < stackCapacity)
                    stack.ResizeUninitialized(stackCapacity);

                var globalBuffer = Globals[RuntimeEntity];
                var context = new MorrowindScriptExecutionContext
                {
                    Entity = entity,
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
                    Position = transform.Position,
                    PlayerPosition = PlayerPosition,
                    PlayingScriptSoundKeys = PlayingScriptSoundKeys.Length == 0 ? null : (ulong*)PlayingScriptSoundKeys.GetUnsafeReadOnlyPtr(),
                    PlayingScriptSoundKeyCount = PlayingScriptSoundKeys.Length,
                    PlacedRefId = placedRef.Value,
                    AudioSequenceBase = AudioSequenceBase,
                    HasPlayerPosition = HasPlayerPosition,
                    HasCellChanged = HasCellChanged,
                    CellChanged = CellChanged,
                    HasMenuMode = HasMenuMode,
                    MenuMode = MenuMode,
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
                    return;
                }

                instance.ProgramCounter = 0;
            }

            bool IsScriptLocationActive(in LogicalRefLocation location)
            {
                if (InteriorActive != 0)
                    return location.IsInterior != 0 && location.InteriorCellHash == ActiveInteriorCellHash;

                if (location.IsInterior != 0 || HasActiveExteriorCells == 0)
                    return false;

                return ActiveExteriorCells.Contains(location.ExteriorCell);
            }

            static void Fault(ref MorrowindScriptInstance instance, FixedString128Bytes reason)
            {
                instance.Status = (byte)MorrowindScriptInstanceStatus.Faulted;
                instance.DisabledReason = reason;
            }
        }
    }
}
