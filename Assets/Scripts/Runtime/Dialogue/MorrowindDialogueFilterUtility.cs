using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueFilterUtility
    {
        public static bool TryFindFirstMatchingInfo(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle,
            int dialogueIndex,
            int choice,
            out int infoIndex,
            out string unsupportedReason)
        {
            infoIndex = -1;
            unsupportedReason = null;
            if (contentDb == null
                || !speakerHandle.IsValid
                || (uint)dialogueIndex >= (uint)contentDb.DialogueCount)
                return false;

            ref readonly var actor = ref contentDb.Get(speakerHandle);
            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            int end = Math.Min(contentDb.DialogueInfoCount, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref readonly var info = ref contentDb.Data.DialogueInfos[i];
                if (!MatchesStaticFilters(contentDb, entityManager, speakerEntity, actor, info, out unsupportedReason))
                    continue;

                if (!MatchesSelectRules(contentDb, entityManager, speakerEntity, actor, info, choice, out unsupportedReason))
                    continue;

                infoIndex = i;
                return true;
            }

            return false;
        }

        static bool MatchesStaticFilters(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity speakerEntity,
            in ActorDef actor,
            in DialogueInfoDef info,
            out string unsupportedReason)
        {
            unsupportedReason = null;
            bool isCreature = actor.Kind != ActorDefKind.Npc;
            if (!string.IsNullOrWhiteSpace(info.ActorId))
            {
                if (!IdEquals(info.ActorId, actor.Id) && !IdEquals(info.ActorId, actor.OriginalId))
                    return false;
            }
            else if (isCreature)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(info.RaceId))
            {
                if (isCreature)
                    return true;
                if (!IdEquals(info.RaceId, actor.RaceId))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(info.ClassId))
            {
                if (isCreature)
                    return true;
                if (!IdEquals(info.ClassId, actor.ClassId))
                    return false;
            }

            if (info.FactionLess)
            {
                if (isCreature)
                    return true;
                if (!string.IsNullOrWhiteSpace(actor.FactionId))
                    return false;
            }
            else if (!string.IsNullOrWhiteSpace(info.FactionId))
            {
                if (isCreature)
                    return true;
                if (!IdEquals(info.FactionId, actor.FactionId))
                    return false;
                if (actor.Rank < info.Rank)
                    return false;
            }
            else if (info.Rank != -1)
            {
                if (isCreature)
                    return true;
                if (actor.Rank < info.Rank)
                    return false;
            }

            if (!isCreature && info.Gender != -1)
            {
                bool female = (actor.Flags & 0x1u) != 0u;
                int openMwRejectedGender = female ? 0 : 1;
                if (info.Gender == openMwRejectedGender)
                    return false;
            }

            if (!MatchesPlayerFactionAndRank(contentDb, entityManager, actor, info))
                return false;

            if (!string.IsNullOrWhiteSpace(info.CellId) && !MatchesPlayerCell(entityManager, info.CellId))
                return false;

            if (actor.Kind == ActorDefKind.Npc
                && info.DispositionOrJournalIndex > 0
                && ResolveBaseDisposition(entityManager, speakerEntity, actor) < info.DispositionOrJournalIndex)
            {
                return false;
            }

            return true;
        }

        static int ResolveBaseDisposition(EntityManager entityManager, Entity speakerEntity, in ActorDef actor)
        {
            if (entityManager.Exists(speakerEntity) && entityManager.HasComponent<ActorDispositionState>(speakerEntity))
                return entityManager.GetComponentData<ActorDispositionState>(speakerEntity).BaseDisposition;

            return actor.Disposition;
        }

        static bool MatchesPlayerFactionAndRank(RuntimeContentDatabase contentDb, EntityManager entityManager, in ActorDef speaker, in DialogueInfoDef info)
        {
            if (string.IsNullOrWhiteSpace(info.PcFactionId) && info.PcRank == -1)
                return true;

            string requiredFaction = !string.IsNullOrWhiteSpace(info.PcFactionId)
                ? info.PcFactionId
                : speaker.FactionId;

            if (string.IsNullOrWhiteSpace(requiredFaction)
                || contentDb == null
                || !contentDb.TryGetFactionHandle(requiredFaction, out var factionHandle)
                || !factionHandle.IsValid)
            {
                return false;
            }

            if (!TryReadPlayerFaction(entityManager, factionHandle.Index, out var membership)
                || membership.Joined == 0
                || membership.Expelled != 0)
            {
                return false;
            }

            return info.PcRank == -1 || membership.Rank >= info.PcRank;
        }

        static bool TryReadPlayerFaction(EntityManager entityManager, int factionIndex, out PlayerFactionMembership membership)
        {
            membership = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var factions = entityManager.GetBuffer<PlayerFactionMembership>(query.GetSingletonEntity(), true);
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex == factionIndex)
                {
                    membership = factions[i];
                    return true;
                }
            }

            return false;
        }

        static bool MatchesPlayerCell(EntityManager entityManager, string cellPrefix)
        {
            if (string.IsNullOrWhiteSpace(cellPrefix))
                return true;

            if (!TryReadCurrentPlayerCell(entityManager, out string cellName))
                return false;

            return cellName.StartsWith(cellPrefix, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryReadCurrentPlayerCell(EntityManager entityManager, out string cellName)
        {
            cellName = string.Empty;
            using var transitionQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            if (!transitionQuery.IsEmptyIgnoreFilter)
            {
                var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionQuery.GetSingletonEntity());
                if (transition.InteriorActive != 0)
                {
                    cellName = transition.ActiveInteriorCellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            using var streamingQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<StreamingConfig>());
            if (streamingQuery.IsEmptyIgnoreFilter)
                return false;

            var streaming = entityManager.GetComponentData<StreamingConfig>(streamingQuery.GetSingletonEntity());
            if (!WorldResources.Cells.TryGetValue(streaming.CameraCell, out CellData cell) || cell == null)
                return false;

            cellName = cell.CellId ?? string.Empty;
            return !string.IsNullOrWhiteSpace(cellName);
        }

        static bool TryReadActorCell(EntityManager entityManager, Entity actorEntity, out string cellName)
        {
            cellName = string.Empty;
            if (!entityManager.Exists(actorEntity))
                return false;

            if (entityManager.HasComponent<LogicalRefLocation>(actorEntity))
            {
                var location = entityManager.GetComponentData<LogicalRefLocation>(actorEntity);
                if (location.IsInterior != 0)
                {
                    cellName = location.InteriorCellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }

                if (WorldResources.Cells.TryGetValue(location.ExteriorCell, out CellData cell) && cell != null)
                {
                    cellName = cell.CellId ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            if (entityManager.HasComponent<CellLink>(actorEntity))
            {
                var link = entityManager.GetComponentData<CellLink>(actorEntity);
                if (WorldResources.Cells.TryGetValue(link.Value, out CellData cell) && cell != null)
                {
                    cellName = cell.CellId ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            return false;
        }

        static bool MatchesSelectRules(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity speakerEntity,
            in ActorDef actor,
            in DialogueInfoDef info,
            int choice,
            out string unsupportedReason)
        {
            unsupportedReason = null;
            if (info.SelectRuleCount <= 0 || info.FirstSelectRuleIndex < 0)
                return true;

            int end = Math.Min(contentDb.DialogueConditionCount, info.FirstSelectRuleIndex + info.SelectRuleCount);
            for (int i = info.FirstSelectRuleIndex; i < end; i++)
            {
                ref readonly var rule = ref contentDb.Data.DialogueConditions[i];
                if (!TryEvaluateRule(contentDb, entityManager, speakerEntity, actor, rule, choice, out bool matched, out unsupportedReason))
                    return false;

                if (!matched)
                    return false;
            }

            return true;
        }

        static bool TryEvaluateRule(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity speakerEntity,
            in ActorDef actor,
            in DialogueConditionDef rule,
            int choice,
            out bool matched,
            out string unsupportedReason)
        {
            matched = false;
            unsupportedReason = null;
            var function = (DialogueConditionFunction)rule.Function;
            switch (function)
            {
                case DialogueConditionFunction.Global:
                    if (!contentDb.TryGetGlobalHandle(rule.Variable, out var globalHandle) || !globalHandle.IsValid)
                    {
                        matched = true;
                        return true;
                    }
                    if (!TryReadGlobal(entityManager, globalHandle.Index, out float globalValue))
                        return false;
                    matched = Compare(globalValue, rule);
                    return true;

                case DialogueConditionFunction.Local:
                    matched = TryReadLocal(contentDb, entityManager, speakerEntity, actor.ScriptId, rule.Variable, out float localValue)
                        && Compare(localValue, rule);
                    return true;

                case DialogueConditionFunction.NotLocal:
                    matched = !TryReadLocal(contentDb, entityManager, speakerEntity, actor.ScriptId, rule.Variable, out float notLocalValue)
                        || !Compare(notLocalValue, rule);
                    return true;

                case DialogueConditionFunction.Journal:
                    if (!contentDb.TryGetDialogueHandle(rule.Variable, out var journalHandle) || !journalHandle.IsValid)
                        return false;
                    if (!TryReadJournal(entityManager, journalHandle.Index, out int journalIndex))
                        return false;
                    matched = Compare(journalIndex, rule);
                    return true;

                case DialogueConditionFunction.Choice:
                    if (choice < 0)
                        return false;
                    matched = Compare(choice, rule);
                    return true;

                case DialogueConditionFunction.NotId:
                    matched = !IdEquals(rule.Variable, actor.Id) && !IdEquals(rule.Variable, actor.OriginalId);
                    return true;

                case DialogueConditionFunction.NotFaction:
                    matched = !IdEquals(rule.Variable, actor.FactionId);
                    return true;

                case DialogueConditionFunction.NotClass:
                    matched = !IdEquals(rule.Variable, actor.ClassId);
                    return true;

                case DialogueConditionFunction.NotRace:
                    matched = !IdEquals(rule.Variable, actor.RaceId);
                    return true;

                case DialogueConditionFunction.NotCell:
                    if (!TryReadActorCell(entityManager, speakerEntity, out string actorCell))
                        return false;
                    matched = !actorCell.StartsWith(rule.Variable ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    return true;

                case DialogueConditionFunction.PcLevel:
                    if (TryReadPlayerIdentity(entityManager, out var identity))
                    {
                        matched = Compare(identity.Level, rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.PcReputation:
                    if (TryReadPlayerIdentity(entityManager, out var repIdentity))
                    {
                        matched = Compare(repIdentity.Reputation, rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.PcHealth:
                case DialogueConditionFunction.PcMagicka:
                case DialogueConditionFunction.PcFatigue:
                case DialogueConditionFunction.PcHealthPercent:
                    if (!TryReadPlayerVitals(entityManager, out var vitals))
                        return false;
                    matched = Compare(ResolvePlayerVital(function, vitals), rule);
                    return true;

                case DialogueConditionFunction.FacReactionLowest:
                case DialogueConditionFunction.FacReactionHighest:
                    matched = TryResolvePlayerFactionReaction(
                        contentDb,
                        entityManager,
                        actor.FactionId,
                        function == DialogueConditionFunction.FacReactionLowest,
                        out int reaction)
                        && Compare(reaction, rule);
                    return true;
            }

            if (TryResolvePlayerAttributeOrSkill(entityManager, function, out float stat))
            {
                matched = Compare(stat, rule);
                return true;
            }

            unsupportedReason = $"unsupported dialogue select function {function}.";
            return false;
        }

        static bool TryReadGlobal(EntityManager entityManager, int index, out float value)
        {
            value = 0f;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindScriptGlobalValue>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            var globals = entityManager.GetBuffer<MorrowindScriptGlobalValue>(query.GetSingletonEntity(), true);
            if ((uint)index >= (uint)globals.Length)
                return false;
            value = globals[index].ValueKind == (byte)MorrowindScriptValueKind.Float ? globals[index].FloatValue : globals[index].IntValue;
            return true;
        }

        static bool TryReadJournal(EntityManager entityManager, int index, out int value)
        {
            value = 0;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindQuestJournalIndex>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            var journal = entityManager.GetBuffer<MorrowindQuestJournalIndex>(query.GetSingletonEntity(), true);
            if ((uint)index >= (uint)journal.Length)
                return false;
            value = journal[index].Index;
            return true;
        }

        static bool TryReadLocal(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity speakerEntity, string scriptId, string localName, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(scriptId)
                || !contentDb.TryGetMorrowindScriptProgramHandle(scriptId, out var programHandle)
                || !programHandle.IsValid)
                return false;

            var localsDef = contentDb.GetMorrowindScriptLocals(programHandle);
            int localIndex = -1;
            for (int i = 0; i < localsDef.Length; i++)
            {
                if (IdEquals(localsDef[i].Name, localName))
                {
                    localIndex = i;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            if (!entityManager.Exists(speakerEntity) || !entityManager.HasBuffer<MorrowindScriptLocalValue>(speakerEntity))
            {
                value = 0f;
                return true;
            }

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(speakerEntity, true);
            if ((uint)localIndex >= (uint)locals.Length)
            {
                value = 0f;
                return true;
            }

            value = locals[localIndex].ValueKind == (byte)MorrowindScriptValueKind.Float ? locals[localIndex].FloatValue : locals[localIndex].IntValue;
            return true;
        }

        static bool TryReadPlayerIdentity(EntityManager entityManager, out ActorIdentitySet identity)
        {
            identity = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            identity = entityManager.GetComponentData<ActorIdentitySet>(query.GetSingletonEntity());
            return true;
        }

        static bool TryReadPlayerVitals(EntityManager entityManager, out ActorVitalSet vitals)
        {
            vitals = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorVitalSet>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            vitals = entityManager.GetComponentData<ActorVitalSet>(query.GetSingletonEntity());
            return true;
        }

        static bool TryResolvePlayerFactionReaction(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            string speakerFactionId,
            bool lowest,
            out int value)
        {
            value = 0;
            if (contentDb == null
                || string.IsNullOrWhiteSpace(speakerFactionId)
                || !contentDb.TryGetFactionHandle(speakerFactionId, out var speakerFaction)
                || !speakerFaction.IsValid)
            {
                return true;
            }

            using var playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>());
            if (playerQuery.IsEmptyIgnoreFilter)
                return true;

            using var dialogueQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindDialogueState>(), ComponentType.ReadOnly<MorrowindFactionReactionOverride>());
            if (dialogueQuery.IsEmptyIgnoreFilter)
                return true;

            var playerFactions = entityManager.GetBuffer<PlayerFactionMembership>(playerQuery.GetSingletonEntity(), true);
            var overrides = entityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueQuery.GetSingletonEntity(), true);
            for (int i = 0; i < playerFactions.Length; i++)
            {
                if (playerFactions[i].Joined == 0)
                    continue;

                int reaction = MorrowindDialogueUtility.GetFactionReaction(
                    contentDb,
                    overrides,
                    speakerFaction.Index,
                    playerFactions[i].FactionIndex);
                if (lowest ? reaction < value : reaction > value)
                    value = reaction;
            }

            return true;
        }

        static bool TryResolvePlayerAttributeOrSkill(EntityManager entityManager, DialogueConditionFunction function, out float value)
        {
            value = 0f;
            using var attrQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorAttributeSet>());
            using var skillQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorSkillSet>());
            if (!attrQuery.IsEmptyIgnoreFilter)
            {
                var attributes = entityManager.GetComponentData<ActorAttributeSet>(attrQuery.GetSingletonEntity());
                switch (function)
                {
                    case DialogueConditionFunction.PcStrength: value = attributes.Strength; return true;
                    case DialogueConditionFunction.PcIntelligence: value = attributes.Intelligence; return true;
                    case DialogueConditionFunction.PcWillpower: value = attributes.Willpower; return true;
                    case DialogueConditionFunction.PcAgility: value = attributes.Agility; return true;
                    case DialogueConditionFunction.PcSpeed: value = attributes.Speed; return true;
                    case DialogueConditionFunction.PcEndurance: value = attributes.Endurance; return true;
                    case DialogueConditionFunction.PcPersonality: value = attributes.Personality; return true;
                    case DialogueConditionFunction.PcLuck: value = attributes.Luck; return true;
                }
            }

            if (!skillQuery.IsEmptyIgnoreFilter)
            {
                var skills = entityManager.GetComponentData<ActorSkillSet>(skillQuery.GetSingletonEntity());
                switch (function)
                {
                    case DialogueConditionFunction.PcBlock: value = skills.Block; return true;
                    case DialogueConditionFunction.PcArmorer: value = skills.Armorer; return true;
                    case DialogueConditionFunction.PcMediumArmor: value = skills.MediumArmor; return true;
                    case DialogueConditionFunction.PcHeavyArmor: value = skills.HeavyArmor; return true;
                    case DialogueConditionFunction.PcBluntWeapon: value = skills.BluntWeapon; return true;
                    case DialogueConditionFunction.PcLongBlade: value = skills.LongBlade; return true;
                    case DialogueConditionFunction.PcAxe: value = skills.Axe; return true;
                    case DialogueConditionFunction.PcSpear: value = skills.Spear; return true;
                    case DialogueConditionFunction.PcAthletics: value = skills.Athletics; return true;
                    case DialogueConditionFunction.PcEnchant: value = skills.Enchant; return true;
                    case DialogueConditionFunction.PcDestruction: value = skills.Destruction; return true;
                    case DialogueConditionFunction.PcAlteration: value = skills.Alteration; return true;
                    case DialogueConditionFunction.PcIllusion: value = skills.Illusion; return true;
                    case DialogueConditionFunction.PcConjuration: value = skills.Conjuration; return true;
                    case DialogueConditionFunction.PcMysticism: value = skills.Mysticism; return true;
                    case DialogueConditionFunction.PcRestoration: value = skills.Restoration; return true;
                    case DialogueConditionFunction.PcAlchemy: value = skills.Alchemy; return true;
                    case DialogueConditionFunction.PcUnarmored: value = skills.Unarmored; return true;
                    case DialogueConditionFunction.PcSecurity: value = skills.Security; return true;
                    case DialogueConditionFunction.PcSneak: value = skills.Sneak; return true;
                    case DialogueConditionFunction.PcAcrobatics: value = skills.Acrobatics; return true;
                    case DialogueConditionFunction.PcLightArmor: value = skills.LightArmor; return true;
                    case DialogueConditionFunction.PcShortBlade: value = skills.ShortBlade; return true;
                    case DialogueConditionFunction.PcMarksman: value = skills.Marksman; return true;
                    case DialogueConditionFunction.PcMercantile: value = skills.Mercantile; return true;
                    case DialogueConditionFunction.PcSpeechcraft: value = skills.Speechcraft; return true;
                    case DialogueConditionFunction.PcHandToHand: value = skills.HandToHand; return true;
                }
            }

            return false;
        }

        static float ResolvePlayerVital(DialogueConditionFunction function, in ActorVitalSet vitals)
        {
            return function switch
            {
                DialogueConditionFunction.PcHealth => vitals.CurrentHealth,
                DialogueConditionFunction.PcMagicka => vitals.CurrentMagicka,
                DialogueConditionFunction.PcFatigue => vitals.CurrentFatigue,
                DialogueConditionFunction.PcHealthPercent => vitals.ModifiedHealthBase > 0f ? vitals.CurrentHealth / vitals.ModifiedHealthBase * 100f : 0f,
                _ => 0f,
            };
        }

        static bool Compare(float actual, in DialogueConditionDef rule)
        {
            float expected = rule.ValueKind == (byte)MorrowindScriptValueKind.Float ? rule.FloatValue : rule.IntValue;
            return (DialogueConditionComparison)rule.Comparison switch
            {
                DialogueConditionComparison.Equal => Math.Abs(actual - expected) <= 0.0001f,
                DialogueConditionComparison.NotEqual => Math.Abs(actual - expected) > 0.0001f,
                DialogueConditionComparison.Greater => actual > expected,
                DialogueConditionComparison.GreaterOrEqual => actual >= expected,
                DialogueConditionComparison.Less => actual < expected,
                DialogueConditionComparison.LessOrEqual => actual <= expected,
                _ => false,
            };
        }

        static bool IdEquals(string a, string b)
            => string.Equals(ContentId.NormalizeId(a), ContentId.NormalizeId(b), StringComparison.Ordinal);
    }
}
