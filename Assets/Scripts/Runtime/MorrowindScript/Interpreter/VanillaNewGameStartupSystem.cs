using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(VVardenfell.Runtime.Player.GameInitializationSystem))]
    public partial struct VanillaNewGameStartupSystem : ISystem
    {
        EntityQuery _globalScriptQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _globalScriptQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindGlobalScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadWrite<MorrowindScriptStackValue>());
            systemState.RequireForUpdate<VanillaNewGameStartupPending>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptGlobalValue>();
            systemState.RequireForUpdate<CharacterGenerationState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            BlobAssetReference<RuntimeContentBlob> contentReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!contentReference.IsCreated)
                throw new InvalidOperationException("[VVardenfell][NewGame] Vanilla startup requires runtime content blob.");

            ref RuntimeContentBlob content = ref contentReference.Value;
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();

            DestroyExistingGlobalScripts(systemState.EntityManager);

            var globals = systemState.EntityManager.GetBuffer<MorrowindScriptGlobalValue>(runtimeEntity);
            ResetGlobals(ref content, globals);
            SetCharGenState(ref content, globals, 1);
            EnsureCharGenStage(systemState.EntityManager, SystemAPI.GetSingletonEntity<CharacterGenerationState>(), SystemAPI.GetSingleton<CharacterGenerationState>(), 1);

            AddStartupScript(systemState.EntityManager, ref content, RuntimeContentStableHash.HashId("main"), "main");
            for (int i = 0; i < content.StartScripts.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob startScript = ref content.StartScripts[i];
                if (startScript.IdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][NewGame] StartScript index {i} has no script id.");

                AddStartupScript(
                    systemState.EntityManager,
                    ref content,
                    startScript.IdHash,
                    startScript.Id.ToString());
            }

            systemState.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<VanillaNewGameStartupPending>());
        }

        void DestroyExistingGlobalScripts(EntityManager entityManager)
        {
            if (_globalScriptQuery.IsEmptyIgnoreFilter)
                return;

            using var entities = _globalScriptQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entityManager.Exists(entities[i]))
                    entityManager.DestroyEntity(entities[i]);
            }
        }

        void AddStartupScript(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            ulong scriptIdHash,
            string scriptId)
        {
            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref content, scriptIdHash, out var programHandle)
                || !programHandle.IsValid)
            {
                throw new InvalidOperationException($"[VVardenfell][NewGame] Startup script '{scriptId}' is missing from compiled script programs.");
            }

            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][NewGame] Startup script '{scriptId}' is disabled: {RuntimeFixedStringUtility.ToFixed128OrDefault(ref program.DisabledReason)}");
            }

            if (FindGlobalScriptEntity(programHandle.Index) != Entity.Null)
                return;

            Entity entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new MorrowindGlobalScriptInstance());
            entityManager.AddComponentData(entity, new MorrowindScriptInstance
            {
                Program = programHandle,
                ProgramIndex = programHandle.Index,
                ProgramCounter = 0,
                Status = (byte)MorrowindScriptInstanceStatus.Running,
            });
            MorrowindScriptRuntimeAuthoringUtility.AddRuntimeScriptBuffers(entityManager, entity, ref content, programHandle);
        }

        Entity FindGlobalScriptEntity(int programIndex)
        {
            using var entities = _globalScriptQuery.ToEntityArray(Allocator.Temp);
            using var instances = _globalScriptQuery.ToComponentDataArray<MorrowindScriptInstance>(Allocator.Temp);
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].ProgramIndex == programIndex)
                    return entities[i];
            }

            return Entity.Null;
        }

        static void ResetGlobals(ref RuntimeContentBlob content, DynamicBuffer<MorrowindScriptGlobalValue> globals)
        {
            if (globals.Length != content.Globals.Length)
                throw new InvalidOperationException($"[VVardenfell][NewGame] Global buffer length {globals.Length} does not match content globals {content.Globals.Length}.");

            for (int i = 0; i < content.Globals.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob global = ref content.Globals[i];
                byte valueKind = ResolveGlobalKind(ref global);
                globals[i] = BuildGlobalValue(valueKind, global.Int0, global.Float0);
            }
        }

        static void SetCharGenState(ref RuntimeContentBlob content, DynamicBuffer<MorrowindScriptGlobalValue> globals, int value)
        {
            if (!RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref content, RuntimeContentStableHash.HashId("chargenstate"), out var handle)
                || !handle.IsValid)
            {
                throw new InvalidOperationException("[VVardenfell][NewGame] Missing required global 'chargenstate'.");
            }

            if ((uint)handle.Index >= (uint)globals.Length)
                throw new InvalidOperationException($"[VVardenfell][NewGame] chargenstate global index {handle.Index} is outside global buffer length {globals.Length}.");

            byte valueKind = globals[handle.Index].ValueKind;
            globals[handle.Index] = BuildGlobalValue(valueKind, value, value);
        }

        static void EnsureCharGenStage(EntityManager entityManager, Entity charGenEntity, in CharacterGenerationState charGen, int globalState)
        {
            var stage = new CharGenStage
            {
                Stage = charGen.Stage,
                Menu = charGen.CurrentMenu,
                Finalized = charGen.Finalized,
                GlobalState = globalState,
            };

            if (entityManager.HasComponent<CharGenStage>(charGenEntity))
                entityManager.SetComponentData(charGenEntity, stage);
            else
                entityManager.AddComponentData(charGenEntity, stage);
        }

        static MorrowindScriptGlobalValue BuildGlobalValue(byte valueKind, int intValue, float floatValue)
        {
            if (valueKind == (byte)MorrowindScriptValueKind.Float)
            {
                return new MorrowindScriptGlobalValue
                {
                    IntValue = (int)floatValue,
                    FloatValue = floatValue,
                    ValueKind = valueKind,
                };
            }

            return new MorrowindScriptGlobalValue
            {
                IntValue = intValue,
                FloatValue = intValue,
                ValueKind = valueKind,
            };
        }

        static byte ResolveGlobalKind(ref RuntimeGenericRecordDefBlob global)
        {
            FixedString64Bytes name = RuntimeFixedStringUtility.ToFixed64OrDefault(ref global.Name);
            if (!name.IsEmpty && name[0] == (byte)'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }
    }
}
