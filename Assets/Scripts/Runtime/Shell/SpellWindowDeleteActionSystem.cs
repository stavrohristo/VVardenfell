using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    [UpdateBefore(typeof(SpellWindowStateSystem))]
    public partial struct SpellWindowDeleteActionSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorIdentitySet>(),
                ComponentType.ReadOnly<PlayerRaceAppearance>(),
                ComponentType.ReadWrite<ActorKnownSpell>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<SpellWindowState>();
            systemState.RequireForUpdate<SpellWindowRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<SpellWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<SpellWindowRequest>().ValueRW;

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Spell delete requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            if (state.DeleteConfirmSpell.IsValid && shell.ModalButtonPressedValid != 0)
            {
                SpellDefHandle confirmedSpell = state.DeleteConfirmSpell;
                state.DeleteConfirmSpell = default;
                int button = shell.ModalButtonPressed;
                shell.ModalButtonPressedValid = 0;
                shell.ModalButtonPressed = -1;
                if (button == 0)
                    DeleteSpell(ref systemState, ref content, ref state, confirmedSpell);
            }
            else if (state.DeleteConfirmSpell.IsValid && shell.ModalOpen == 0)
            {
                state.DeleteConfirmSpell = default;
            }

            if (request.PendingDeleteSource != 0)
            {
                request.PendingDeleteSource = 0;
                if ((RuntimeMagicSourceKind)request.DeleteSourceKind == RuntimeMagicSourceKind.EnchantedItem)
                    return;
                if ((RuntimeMagicSourceKind)request.DeleteSourceKind != RuntimeMagicSourceKind.Spell || !request.DeleteSpell.IsValid)
                    return;

                BeginDelete(ref systemState, ref content, ref shell, ref state, request.DeleteSpell);
                request.DeleteSpell = default;
                request.DeleteSourceKind = 0;
                return;
            }

            if (request.PendingDeleteSelected == 0)
                return;

            request.PendingDeleteSelected = 0;
            if (state.SelectedSourceKind != (byte)RuntimeMagicSourceKind.Spell || !state.SelectedSpell.IsValid)
                return;

            BeginDelete(ref systemState, ref content, ref shell, ref state, state.SelectedSpell);
        }

        void BeginDelete(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            ref RuntimeShellState shell,
            ref SpellWindowState state,
            SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= content.Spells.Length)
                throw new InvalidOperationException("[VVardenfell][MagicUI] Delete requested an invalid spell handle.");

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            if (spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell || IsInherentSpell(ref systemState, ref content, spellHandle, ref spell))
            {
                ShowMagicModal(ref content, ref shell, RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentStableHash.HashId("sDeleteSpellError")), false);
                return;
            }

            string question = RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentStableHash.HashId("sQuestionDeleteSpell"));
            string spellName = RuntimeContentMetadataResolver.ResolveSpellName(ref spell);
            if (string.IsNullOrWhiteSpace(question))
                throw new InvalidOperationException("[VVardenfell][MagicUI] GMST sQuestionDeleteSpell is empty.");
            question = question.Contains("%s", StringComparison.Ordinal)
                ? question.Replace("%s", spellName)
                : string.Format(question, spellName);

            state.DeleteConfirmSpell = spellHandle;
            ShowMagicModal(ref content, ref shell, question, true);
        }

        void DeleteSpell(ref SystemState systemState, ref RuntimeContentBlob content, ref SpellWindowState state, SpellDefHandle spellHandle)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(player);
            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(player);
            MorrowindActorMagicUtility.RemoveKnownSpell(ref content, knownSpells, activeEffects, spellHandle);
            if (state.SelectedSourceKind == (byte)RuntimeMagicSourceKind.Spell && state.SelectedSpell.Value == spellHandle.Value)
            {
                state.SelectedSourceKind = (byte)RuntimeMagicSourceKind.None;
                state.SelectedSpell = default;
                state.SelectedSpellIndex = -1;
            }
        }

        bool IsInherentSpell(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            SpellDefHandle spellHandle,
            ref RuntimeSpellDefBlob spell)
        {
            if (spell.SpellType == MorrowindSpellCostUtility.SpellTypePower)
                return true;

            Entity player = _playerQuery.GetSingletonEntity();
            var appearance = systemState.EntityManager.GetComponentData<PlayerRaceAppearance>(player);
            if (IsRacePower(ref content, appearance.RaceId, spell.IdHash))
                return true;

            var identity = systemState.EntityManager.GetComponentData<ActorIdentitySet>(player);
            return IsBirthsignPower(ref content, identity.BirthSignName, spell.IdHash);
        }

        static bool IsRacePower(ref RuntimeContentBlob content, FixedString64Bytes raceId, ulong spellIdHash)
        {
            if (raceId.IsEmpty)
                return false;
            if (!RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref content, RuntimeContentStableHash.HashId(raceId.ToString()), out var raceHandle) || !raceHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Player race '{raceId.ToString()}' could not be resolved for spell delete.");

            ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
            RuntimeContentBlobUtility.RequireRange(race.FirstPowerSpellIdIndex, race.PowerSpellIdCount, content.RacePowerSpellIds.Length, "race power spell");
            for (int i = 0; i < race.PowerSpellIdCount; i++)
            {
                if (RuntimeContentStableHash.HashId(content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value.ToString()) == spellIdHash)
                    return true;
            }

            return false;
        }

        static bool IsBirthsignPower(ref RuntimeContentBlob content, FixedString64Bytes birthsignId, ulong spellIdHash)
        {
            if (birthsignId.IsEmpty)
                return false;
            if (!RuntimeContentBlobUtility.TryGetBirthsignHandleByIdHash(ref content, RuntimeContentStableHash.HashId(birthsignId.ToString()), out var birthsignHandle) || !birthsignHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][MagicUI] Player birthsign '{birthsignId.ToString()}' could not be resolved for spell delete.");

            ref RuntimeGenericRecordDefBlob birthsign = ref RuntimeContentBlobUtility.GetBirthsign(ref content, birthsignHandle);
            RuntimeContentBlobUtility.RequireRange(birthsign.FirstPowerSpellIdIndex, birthsign.PowerSpellIdCount, content.GenericRecordPowerSpellIds.Length, "birthsign power spell");
            for (int i = 0; i < birthsign.PowerSpellIdCount; i++)
            {
                if (RuntimeContentStableHash.HashId(content.GenericRecordPowerSpellIds[birthsign.FirstPowerSpellIdIndex + i].Value.ToString()) == spellIdHash)
                    return true;
            }

            return false;
        }

        static void ShowMagicModal(ref RuntimeContentBlob content, ref RuntimeShellState shell, string body, bool confirmation)
        {
            string title = RuntimeContentMetadataResolver.ResolveGameSettingString(ref content, "sMagicItem", "Magic");
            shell.ModalOpen = 1;
            shell.ModalButtonPressedValid = 0;
            shell.ModalButtonPressed = -1;
            shell.ModalTitle = RuntimeFixedStringUtility.ToFixed128OrDefault(title);
            shell.ModalBody = RuntimeFixedStringUtility.ToFixed512OrDefault(body);
            if (!confirmation)
            {
                shell.ModalButtonCount = 0;
                shell.ModalButton0 = default;
                shell.ModalButton1 = default;
                return;
            }

            shell.ModalButtonCount = 2;
            shell.ModalButton0 = RuntimeFixedStringUtility.ToFixed128OrDefault(RuntimeContentMetadataResolver.ResolveGameSettingString(ref content, "sYes", "Yes"));
            shell.ModalButton1 = RuntimeFixedStringUtility.ToFixed128OrDefault(RuntimeContentMetadataResolver.ResolveGameSettingString(ref content, "sNo", "No"));
        }
    }
}
