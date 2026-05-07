using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindDialogueSessionSystem))]
    public partial struct MorrowindCrimeDialogueGlobalsSystem : ISystem
    {
        EntityQuery _globalQuery;
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _globalQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<MorrowindScriptGlobalValue>());
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCrimeState>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());
            systemState.RequireForUpdate(_globalQuery);
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Dialogue] Crime dialogue globals require runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            Entity player = _playerQuery.GetSingletonEntity();
            var playerCrime = systemState.EntityManager.GetComponentData<PlayerCrimeState>(player);
            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player, true);
            int playerGold = CountPlayerGold(ref content, inventory);
            int bounty = Math.Max(0, playerCrime.Bounty);
            int discount = CalculateCrimeGold(ref content, bounty, RuntimeContentKnownHashes.fCrimeGoldDiscountMult);
            int turnIn = CalculateCrimeGold(ref content, bounty, RuntimeContentKnownHashes.fCrimeGoldTurnInMult);

            var globals = systemState.EntityManager.GetBuffer<MorrowindScriptGlobalValue>(_globalQuery.GetSingletonEntity());
            SetGlobal(ref content, globals, RuntimeContentKnownHashes.pchascrimegold, bounty <= playerGold ? 1 : 0);
            SetGlobal(ref content, globals, RuntimeContentKnownHashes.pchasgolddiscount, discount <= playerGold ? 1 : 0);
            SetGlobal(ref content, globals, RuntimeContentKnownHashes.crimegolddiscount, discount);
            SetGlobal(ref content, globals, RuntimeContentKnownHashes.crimegoldturnin, turnIn);
            SetGlobal(ref content, globals, RuntimeContentKnownHashes.pchasturnin, turnIn <= playerGold ? 1 : 0);
        }

        static int CountPlayerGold(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, RuntimeContentStableHash.HashId("gold_001"), out ContentReference gold)
                || !RuntimeContentBlobUtility.IsValid(ref content, gold))
            {
                throw new InvalidOperationException("[VVardenfell][Dialogue] Required gold item 'gold_001' was not found in runtime content.");
            }

            int count = 0;
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item.Content.Kind == gold.Kind && item.Content.HandleValue == gold.HandleValue)
                    count += Math.Max(0, item.Count);
            }

            return count;
        }

        static int CalculateCrimeGold(ref RuntimeContentBlob content, int bounty, ulong multiplierHash)
        {
            float multiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, multiplierHash);
            int value = (int)(Math.Max(0, bounty) * multiplier);
            if (bounty > 0 && value < 1)
                value = 1;
            return value;
        }

        static void SetGlobal(ref RuntimeContentBlob content, DynamicBuffer<MorrowindScriptGlobalValue> globals, ulong hash, int value)
        {
            if (!RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref content, hash, out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] Required crime dialogue global hash 0x{hash:X16} is missing.");
            if ((uint)handle.Index >= (uint)globals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] Global buffer is too small for crime dialogue global hash 0x{hash:X16}.");

            ref RuntimeGenericRecordDefBlob global = ref RuntimeContentBlobUtility.GetGlobal(ref content, handle);
            globals[handle.Index] = BuildGlobalValue(value, ResolveGlobalKind(ref global));
        }

        static MorrowindScriptGlobalValue BuildGlobalValue(int value, byte valueKind)
        {
            if (valueKind == (byte)MorrowindScriptValueKind.Float)
            {
                return new MorrowindScriptGlobalValue
                {
                    FloatValue = value,
                    IntValue = value,
                    ValueKind = valueKind,
                };
            }

            return new MorrowindScriptGlobalValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
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
