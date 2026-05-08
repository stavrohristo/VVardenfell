using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorWeaponAnimationSystem))]
    public partial struct PlayerPreparedMagicCastReleaseSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadWrite<ActorMagicCastState>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadOnly<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<ActorActiveMagicEffect>(),
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<PlayerInventoryItem>(),
                ComponentType.ReadWrite<ActorUsedPower>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<MorrowindMagicRuntimeState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            Entity visual = ResolveActivePlayerVisual(ref systemState, player);
            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(visual))
                throw new InvalidOperationException($"[VVardenfell][Magic] Active player visual entity={visual.Index}:{visual.Version} has no ActorWeaponAnimationState.");

            var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(visual);
            if (weaponState.SpellCastReleasePending == 0)
                return;

            byte sourceKind = weaponState.SpellCastReleaseSourceKind;
            SpellDefHandle spellHandle = weaponState.SpellCastReleaseSpell;
            EnchantmentDefHandle enchantmentHandle = weaponState.SpellCastReleaseEnchantment;
            ContentReference itemContent = weaponState.SpellCastReleaseItemContent;
            int inventoryIndex = weaponState.SpellCastReleaseInventoryIndex;
            weaponState.SpellCastReleasePending = 0;
            weaponState.SpellCastReleaseSourceKind = 0;
            weaponState.SpellCastReleaseSpell = default;
            weaponState.SpellCastReleaseEnchantment = default;
            weaponState.SpellCastReleaseItemContent = default;
            weaponState.SpellCastReleaseInventoryIndex = -1;
            systemState.EntityManager.SetComponentData(visual, weaponState);

            var magicRef = _playerQuery.GetSingletonRW<ActorMagicCastState>();
            ref var castState = ref magicRef.ValueRW;
            if (castState.CastInProgress == 0 || castState.CastingSourceKind != sourceKind)
                throw new InvalidOperationException("[VVardenfell][Magic] Spell release did not match the player's prepared cast state.");

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Prepared spell release requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            if (sourceKind == (byte)RuntimeMagicSourceKind.EnchantedItem)
            {
                if (castState.CastingEnchantment.Value != enchantmentHandle.Value
                    || castState.CastingInventoryIndex != inventoryIndex
                    || castState.CastingItemContent.Kind != itemContent.Kind
                    || castState.CastingItemContent.HandleValue != itemContent.HandleValue)
                {
                    throw new InvalidOperationException("[VVardenfell][Magic] Enchanted item release did not match the player's prepared cast state.");
                }

                FinishCastState(ref castState);
                Entity enchantTarget = ResolveFocusedTarget(out uint enchantTargetPlacedRefId);
                Entity enchantRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
                systemState.EntityManager.GetBuffer<ActorSpellCastRequest>(enchantRuntimeEntity).Add(new ActorSpellCastRequest
                {
                    CasterEntity = player,
                    TargetEntity = enchantTarget,
                    TargetPlacedRefId = enchantTargetPlacedRefId,
                    SourceKind = (byte)RuntimeMagicSourceKind.EnchantedItem,
                    Enchantment = enchantmentHandle,
                    SourceContent = itemContent,
                    SourceInventoryIndex = inventoryIndex,
                });
                return;
            }

            if (sourceKind != (byte)RuntimeMagicSourceKind.Spell || castState.CastingSpell.Value != spellHandle.Value)
                throw new InvalidOperationException("[VVardenfell][Magic] Spell release did not match the player's prepared cast state.");

            RequireKnownCastableSpell(ref systemState, ref content, player, spellHandle);
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);

            bool succeeded = RollSpellSuccess(ref systemState, ref content, player, ref spell);
            FinishCastState(ref castState);
            if (!succeeded)
            {
                QueuePlayerMessage(ref systemState, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicSkillFail));
                return;
            }

            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
                MarkPowerUsed(systemState.EntityManager.GetBuffer<ActorUsedPower>(player), spellHandle, ResolveTotalGameHours(ref systemState));

            Entity target = ResolveFocusedTarget(out uint targetPlacedRefId);

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            systemState.EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity).Add(new ActorSpellCastRequest
            {
                CasterEntity = player,
                CasterPlacedRefId = 0u,
                TargetEntity = target,
                TargetPlacedRefId = targetPlacedRefId,
                SourceKind = (byte)RuntimeMagicSourceKind.Spell,
                Spell = spellHandle,
                Prevalidated = 1,
            });
        }

        Entity ResolveFocusedTarget(out uint targetPlacedRefId)
        {
            targetPlacedRefId = 0u;
            if (SystemAPI.TryGetSingleton<PlayerInteractionFocus>(out var focus) && focus.HasTarget != 0)
            {
                targetPlacedRefId = focus.PlacedRefId;
                return focus.TargetEntity;
            }

            return Entity.Null;
        }

        static void FinishCastState(ref ActorMagicCastState castState)
        {
            castState.CastInProgress = 0;
            castState.CastRequested = 0;
            castState.CastRange = 0;
            castState.CastingSourceKind = 0;
            castState.CastingSpell = default;
            castState.CastingEnchantment = default;
            castState.CastingItemContent = default;
            castState.CastingInventoryIndex = -1;
        }

        Entity ResolveActivePlayerVisual(ref SystemState systemState, Entity player)
        {
            var presentation = _playerQuery.GetSingleton<LocalPlayerPresentationState>();
            Entity visual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (visual == Entity.Null || !systemState.EntityManager.Exists(visual))
                throw new InvalidOperationException($"[VVardenfell][Magic] Player entity={player.Index}:{player.Version} has no active visual for prepared spell release.");
            return visual;
        }

        static void RequireKnownCastableSpell(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity player,
            SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= content.Spells.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Prepared cast references invalid spell handle {spellHandle.Value}.");

            var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(player, true);
            if (!MorrowindActorMagicUtility.HasKnownSpell(knownSpells, spellHandle))
                throw new InvalidOperationException($"[VVardenfell][Magic] Prepared cast references unknown spell handle {spellHandle.Value}.");

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            if (spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell
                && spell.SpellType != MorrowindSpellCostUtility.SpellTypePower)
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Prepared cast references non-castable spell type {spell.SpellType}.");
            }
        }

        bool RollSpellSuccess(ref SystemState systemState, ref RuntimeContentBlob content, Entity player, ref RuntimeSpellDefBlob spell)
        {
            var magicRuntimeRef = SystemAPI.GetSingletonRW<MorrowindMagicRuntimeState>();
            var random = new Unity.Mathematics.Random(magicRuntimeRef.ValueRO.RandomState == 0u ? 0xA5C38F2Du : magicRuntimeRef.ValueRO.RandomState);
            float chance = math.clamp(MorrowindSpellCostUtility.CalculateSuccessChance(
                ref content,
                ref spell,
                systemState.EntityManager.GetComponentData<ActorAttributeSet>(player),
                systemState.EntityManager.GetComponentData<ActorSkillSet>(player),
                systemState.EntityManager.GetComponentData<ActorVitalSet>(player),
                systemState.EntityManager.GetComponentData<ActorDerivedMovementStats>(player),
                systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(player, true),
                checkMagicka: false,
                out _), 0f, 100f);

            bool success = (spell.Flags & MorrowindSpellCostUtility.SpellFlagAlways) != 0
                           || spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell
                           || random.NextInt(0, 100) < chance;
            magicRuntimeRef.ValueRW.RandomState = random.state == 0u ? 0xA5C38F2Du : random.state;
            return success;
        }

        static void MarkPowerUsed(DynamicBuffer<ActorUsedPower> usedPowers, SpellDefHandle spell, float totalHours)
        {
            for (int i = 0; i < usedPowers.Length; i++)
            {
                if (usedPowers[i].Spell.Value == spell.Value)
                {
                    usedPowers[i] = new ActorUsedPower { Spell = spell, LastUsedTotalGameHours = totalHours };
                    return;
                }
            }

            usedPowers.Add(new ActorUsedPower { Spell = spell, LastUsedTotalGameHours = totalHours });
        }

        float ResolveTotalGameHours(ref SystemState systemState)
            => SystemAPI.TryGetSingleton<MorrowindTimeState>(out var time)
                ? (time.DaysPassed * 24f) + time.GameHour
                : throw new InvalidOperationException("[VVardenfell][Magic] Power cooldown requires MorrowindTimeState.");

        void QueuePlayerMessage(ref SystemState systemState, string message)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity).Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(message),
            });
        }
    }
}
