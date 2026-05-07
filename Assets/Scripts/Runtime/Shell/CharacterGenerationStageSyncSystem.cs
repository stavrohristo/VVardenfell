using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(CharacterGenerationActionSystem))]
    public partial struct CharacterGenerationStageSyncSystem : ISystem
    {
        static readonly ulong k_CharGenStateHash = RuntimeContentStableHash.HashId("chargenstate");

        EntityQuery _globalQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _globalQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindScriptGlobalValue>());
            systemState.RequireForUpdate<CharacterGenerationState>();
            systemState.RequireForUpdate<MorrowindScriptGlobalValue>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            BlobAssetReference<RuntimeContentBlob> contentReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!contentReference.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CharGen] Stage sync requires runtime content blob.");

            ref RuntimeContentBlob content = ref contentReference.Value;
            int globalState = ReadCharGenState(ref content, systemState.EntityManager.GetBuffer<MorrowindScriptGlobalValue>(_globalQuery.GetSingletonEntity()));
            Entity charGenEntity = SystemAPI.GetSingletonEntity<CharacterGenerationState>();
            CharacterGenerationState charGen = SystemAPI.GetSingleton<CharacterGenerationState>();

            bool active = charGen.Finalized == 0
                && (globalState > 0
                    || charGen.CurrentMenu != (byte)CharacterGenerationMenu.None
                    || charGen.Stage != (byte)CharacterGenerationStage.NotStarted);

            if (!active)
            {
                if (systemState.EntityManager.HasComponent<CharGenStage>(charGenEntity))
                    systemState.EntityManager.RemoveComponent<CharGenStage>(charGenEntity);
                return;
            }

            var stage = new CharGenStage
            {
                Stage = charGen.Stage,
                Menu = charGen.CurrentMenu,
                Finalized = charGen.Finalized,
                GlobalState = globalState,
            };

            if (systemState.EntityManager.HasComponent<CharGenStage>(charGenEntity))
                systemState.EntityManager.SetComponentData(charGenEntity, stage);
            else
                systemState.EntityManager.AddComponentData(charGenEntity, stage);
        }

        static int ReadCharGenState(ref RuntimeContentBlob content, DynamicBuffer<MorrowindScriptGlobalValue> globals)
        {
            if (!RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref content, k_CharGenStateHash, out GenericRecordDefHandle handle)
                || !handle.IsValid)
            {
                throw new InvalidOperationException("[VVardenfell][CharGen] Missing required global 'chargenstate'.");
            }

            if ((uint)handle.Index >= (uint)globals.Length)
                throw new InvalidOperationException($"[VVardenfell][CharGen] chargenstate global index {handle.Index} is outside global buffer length {globals.Length}.");

            MorrowindScriptGlobalValue value = globals[handle.Index];
            return value.ValueKind == (byte)MorrowindScriptValueKind.Float
                ? (int)value.FloatValue
                : value.IntValue;
        }
    }
}
