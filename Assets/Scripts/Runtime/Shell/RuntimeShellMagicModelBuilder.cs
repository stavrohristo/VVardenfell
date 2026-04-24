using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        const int MagicEffectFlagTargetSkill = 0x1;
        const int MagicEffectFlagTargetAttribute = 0x2;
        const int MagicEffectFlagNoMagnitude = 0x8;

        static readonly (string Gmst, string Fallback)[] s_MagicEffectNames =
        {
            ("sEffectWaterBreathing", "Water Breathing"),
            ("sEffectSwiftSwim", "Swift Swim"),
            ("sEffectWaterWalking", "Water Walking"),
            ("sEffectShield", "Shield"),
            ("sEffectFireShield", "Fire Shield"),
            ("sEffectLightningShield", "Lightning Shield"),
            ("sEffectFrostShield", "Frost Shield"),
            ("sEffectBurden", "Burden"),
            ("sEffectFeather", "Feather"),
            ("sEffectJump", "Jump"),
            ("sEffectLevitate", "Levitate"),
            ("sEffectSlowFall", "SlowFall"),
            ("sEffectLock", "Lock"),
            ("sEffectOpen", "Open"),
            ("sEffectFireDamage", "Fire Damage"),
            ("sEffectShockDamage", "Shock Damage"),
            ("sEffectFrostDamage", "Frost Damage"),
            ("sEffectDrainAttribute", "Drain Attribute"),
            ("sEffectDrainHealth", "Drain Health"),
            ("sEffectDrainSpellpoints", "Drain Magicka"),
            ("sEffectDrainFatigue", "Drain Fatigue"),
            ("sEffectDrainSkill", "Drain Skill"),
            ("sEffectDamageAttribute", "Damage Attribute"),
            ("sEffectDamageHealth", "Damage Health"),
            ("sEffectDamageMagicka", "Damage Magicka"),
            ("sEffectDamageFatigue", "Damage Fatigue"),
            ("sEffectDamageSkill", "Damage Skill"),
            ("sEffectPoison", "Poison"),
            ("sEffectWeaknessToFire", "Weakness to Fire"),
            ("sEffectWeaknessToFrost", "Weakness to Frost"),
            ("sEffectWeaknessToShock", "Weakness to Shock"),
            ("sEffectWeaknessToMagicka", "Weakness to Magicka"),
            ("sEffectWeaknessToCommonDisease", "Weakness to Common Disease"),
            ("sEffectWeaknessToBlightDisease", "Weakness to Blight Disease"),
            ("sEffectWeaknessToCorprusDisease", "Weakness to Corprus Disease"),
            ("sEffectWeaknessToPoison", "Weakness to Poison"),
            ("sEffectWeaknessToNormalWeapons", "Weakness to Normal Weapons"),
            ("sEffectDisintegrateWeapon", "Disintegrate Weapon"),
            ("sEffectDisintegrateArmor", "Disintegrate Armor"),
            ("sEffectInvisibility", "Invisibility"),
            ("sEffectChameleon", "Chameleon"),
            ("sEffectLight", "Light"),
            ("sEffectSanctuary", "Sanctuary"),
            ("sEffectNightEye", "Night Eye"),
            ("sEffectCharm", "Charm"),
            ("sEffectParalyze", "Paralyze"),
            ("sEffectSilence", "Silence"),
            ("sEffectBlind", "Blind"),
            ("sEffectSound", "Sound"),
            ("sEffectCalmHumanoid", "Calm Humanoid"),
            ("sEffectCalmCreature", "Calm Creature"),
            ("sEffectFrenzyHumanoid", "Frenzy Humanoid"),
            ("sEffectFrenzyCreature", "Frenzy Creature"),
            ("sEffectDemoralizeHumanoid", "Demoralize Humanoid"),
            ("sEffectDemoralizeCreature", "Demoralize Creature"),
            ("sEffectRallyHumanoid", "Rally Humanoid"),
            ("sEffectRallyCreature", "Rally Creature"),
            ("sEffectDispel", "Dispel"),
            ("sEffectSoultrap", "Soultrap"),
            ("sEffectTelekinesis", "Telekinesis"),
            ("sEffectMark", "Mark"),
            ("sEffectRecall", "Recall"),
            ("sEffectDivineIntervention", "Divine Intervention"),
            ("sEffectAlmsiviIntervention", "Almsivi Intervention"),
            ("sEffectDetectAnimal", "Detect Animal"),
            ("sEffectDetectEnchantment", "Detect Enchantment"),
            ("sEffectDetectKey", "Detect Key"),
            ("sEffectSpellAbsorption", "Spell Absorption"),
            ("sEffectReflect", "Reflect"),
            ("sEffectCureCommonDisease", "Cure Common Disease"),
            ("sEffectCureBlightDisease", "Cure Blight Disease"),
            ("sEffectCureCorprusDisease", "Cure Corprus Disease"),
            ("sEffectCurePoison", "Cure Poison"),
            ("sEffectCureParalyzation", "Cure Paralyzation"),
            ("sEffectRestoreAttribute", "Restore Attribute"),
            ("sEffectRestoreHealth", "Restore Health"),
            ("sEffectRestoreSpellPoints", "Restore Magicka"),
            ("sEffectRestoreFatigue", "Restore Fatigue"),
            ("sEffectRestoreSkill", "Restore Skill"),
            ("sEffectFortifyAttribute", "Fortify Attribute"),
            ("sEffectFortifyHealth", "Fortify Health"),
            ("sEffectFortifySpellpoints", "Fortify Magicka"),
            ("sEffectFortifyFatigue", "Fortify Fatigue"),
            ("sEffectFortifySkill", "Fortify Skill"),
            ("sEffectFortifyMagickaMultiplier", "Fortify Maximum Magicka"),
            ("sEffectAbsorbAttribute", "Absorb Attribute"),
            ("sEffectAbsorbHealth", "Absorb Health"),
            ("sEffectAbsorbSpellPoints", "Absorb Magicka"),
            ("sEffectAbsorbFatigue", "Absorb Fatigue"),
            ("sEffectAbsorbSkill", "Absorb Skill"),
            ("sEffectResistFire", "Resist Fire"),
            ("sEffectResistFrost", "Resist Frost"),
            ("sEffectResistShock", "Resist Shock"),
            ("sEffectResistMagicka", "Resist Magicka"),
            ("sEffectResistCommonDisease", "Resist Common Disease"),
            ("sEffectResistBlightDisease", "Resist Blight Disease"),
            ("sEffectResistCorprusDisease", "Resist Corprus Disease"),
            ("sEffectResistPoison", "Resist Poison"),
            ("sEffectResistNormalWeapons", "Resist Normal Weapons"),
            ("sEffectResistParalysis", "Resist Paralysis"),
            ("sEffectRemoveCurse", "Remove Curse"),
            ("sEffectTurnUndead", "Turn Undead"),
            ("sEffectSummonScamp", "Summon Scamp"),
            ("sEffectSummonClannfear", "Summon Clannfear"),
            ("sEffectSummonDaedroth", "Summon Daedroth"),
            ("sEffectSummonDremora", "Summon Dremora"),
            ("sEffectSummonAncestralGhost", "Summon Ancestral Ghost"),
            ("sEffectSummonSkeletalMinion", "Summon Skeletal Minion"),
            ("sEffectSummonLeastBonewalker", "Summon Bonewalker"),
            ("sEffectSummonGreaterBonewalker", "Summon Greater Bonewalker"),
            ("sEffectSummonBonelord", "Summon Bonelord"),
            ("sEffectSummonWingedTwilight", "Summon Winged Twilight"),
            ("sEffectSummonHunger", "Summon Hunger"),
            ("sEffectSummonGoldensaint", "Summon Golden Saint"),
            ("sEffectSummonFlameAtronach", "Summon Flame Atronach"),
            ("sEffectSummonFrostAtronach", "Summon Frost Atronach"),
            ("sEffectSummonStormAtronach", "Summon Storm Atronach"),
            ("sEffectFortifyAttackBonus", "Fortify Attack"),
            ("sEffectCommandCreatures", "Command Creature"),
            ("sEffectCommandHumanoids", "Command Humanoid"),
            ("sEffectBoundDagger", "Bound Dagger"),
            ("sEffectBoundLongsword", "Bound Longsword"),
            ("sEffectBoundMace", "Bound Mace"),
            ("sEffectBoundBattleAxe", "Bound Battle Axe"),
            ("sEffectBoundSpear", "Bound Spear"),
            ("sEffectBoundLongbow", "Bound Longbow"),
            ("sEffectExtraSpell", "EXTRA SPELL"),
            ("sEffectBoundCuirass", "Bound Cuirass"),
            ("sEffectBoundHelm", "Bound Helm"),
            ("sEffectBoundBoots", "Bound Boots"),
            ("sEffectBoundShield", "Bound Shield"),
            ("sEffectBoundGloves", "Bound Gloves"),
            ("sEffectCorpus", "Corprus"),
            ("sEffectVampirism", "Vampirism"),
            ("sEffectSummonCenturionSphere", "Summon Centurion Sphere"),
            ("sEffectSunDamage", "Sun Damage"),
            ("sEffectStuntedMagicka", "Stunted Magicka"),
            ("sEffectSummonFabricant", "Summon Fabricant"),
            ("sEffectSummonCreature01", "Call Wolf"),
            ("sEffectSummonCreature02", "Call Bear"),
            ("sEffectSummonCreature03", "Summon Bonewolf"),
            ("sEffectSummonCreature04", "sEffectSummonCreature04"),
            ("sEffectSummonCreature05", "sEffectSummonCreature05"),
        };

        SpellWindowViewModel BuildSpellModel(RuntimeContentDatabase contentDb, in SpellWindowState state, in PlayerPresentationStats playerStats)
        {
            int spellCount = contentDb?.SpellCount ?? 0;
            var model = new SpellWindowViewModel
            {
                NormalizedRect = new Rect(state.NormalizedX, state.NormalizedY, state.NormalizedWidth, state.NormalizedHeight),
                Title = "Magic",
                FilterText = state.FilterText.ToString(),
                FooterButtonText = "Delete",
                EmptyStateText = "No known spells",
                SpellSummaryText = $"Known spells: 0   Cached definitions: {spellCount}",
                EffectSummaryText = "No selected spell",
                ActiveEffects = BuildActiveEffectIcons(contentDb, playerStats),
            };

            if (!playerStats.HasPlayer || contentDb == null || !EntityManager.Exists(playerStats.PlayerEntity) || !EntityManager.HasBuffer<PlayerKnownSpell>(playerStats.PlayerEntity))
                return model;

            var knownSpells = EntityManager.GetBuffer<PlayerKnownSpell>(playerStats.PlayerEntity);
            var entries = new List<SpellWindowEntryViewModel>(knownSpells.Length);
            int selectedIndex = knownSpells.Length == 0 ? -1 : Math.Clamp(state.SelectedSpellIndex, 0, knownSpells.Length - 1);
            string filter = state.FilterText.ToString();
            for (int i = 0; i < knownSpells.Length; i++)
            {
                var spellHandle = knownSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentDb.Data.Spells.Length)
                    continue;

                ref readonly var spell = ref contentDb.Get(spellHandle);
                if (!MatchesSpellFilter(spell, filter))
                    continue;

                entries.Add(new SpellWindowEntryViewModel
                {
                    SpellIndex = i,
                    Name = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim(),
                    CostText = spell.Cost.ToString(),
                    TypeText = ResolveSpellTypeName(spell.SpellType),
                    EffectTooltipText = BuildSpellEffectTooltip(contentDb, spell),
                    SpellTooltip = BuildSpellTooltip(contentDb, spell),
                    Selected = i == selectedIndex,
                });
            }

            model.Entries = entries.ToArray();
            model.SpellSummaryText = $"Known spells: {entries.Count}   Cached definitions: {spellCount}";
            model.EmptyStateText = entries.Count == 0 && !string.IsNullOrWhiteSpace(filter)
                ? $"No spells match \"{filter.Trim()}\""
                : "No known spells";
            if (selectedIndex >= 0 && selectedIndex < knownSpells.Length)
            {
                var spellHandle = knownSpells[selectedIndex].Spell;
                if (spellHandle.IsValid && spellHandle.Index >= 0 && spellHandle.Index < contentDb.Data.Spells.Length)
                {
                    ref readonly var spell = ref contentDb.Get(spellHandle);
                    model.EffectSummaryText = $"{ResolveSpellName(spell)}   Cost {spell.Cost}   {ResolveSpellTypeName(spell.SpellType)}";
                    model.Effects = BuildSpellEffectRows(contentDb, spell);
                }
            }

            return model;
        }

        static bool MatchesSpellFilter(in SpellDef spell, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            if (!string.IsNullOrWhiteSpace(spell.Name) && spell.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrWhiteSpace(spell.Id) && spell.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return ResolveSpellTypeName(spell.SpellType).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static SpellWindowEffectRow[] BuildSpellEffectRows(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return Array.Empty<SpellWindowEffectRow>();

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new SpellWindowEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new SpellWindowEffectRow
                {
                    EffectId = effect.EffectId,
                    Name = ResolveMagicEffectName(contentDb, effect.EffectId),
                    DetailText = BuildEffectDetail(contentDb, effect),
                    IconPath = ResolveMagicEffectIconPath(contentDb, effect.EffectId),
                };
            }

            return rows;
        }

        RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(RuntimeContentDatabase contentDb, in PlayerPresentationStats playerStats)
        {
            if (!playerStats.HasPlayer
                || contentDb == null
                || !EntityManager.Exists(playerStats.PlayerEntity)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity))
            {
                return Array.Empty<RuntimeMagicEffectIconViewModel>();
            }

            return BuildActiveEffectIcons(contentDb, EntityManager.GetBuffer<ActorActiveMagicEffect>(playerStats.PlayerEntity, true));
        }

        static RuntimeMagicEffectIconViewModel[] BuildActiveEffectIcons(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            if (contentDb == null || activeEffects.Length == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            var ordered = new List<short>();
            var groups = new Dictionary<short, ActiveEffectIconGroup>();
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var active = activeEffects[i];
                if (active.Applied == 0)
                    continue;
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds <= 0f)
                    continue;

                if (!groups.TryGetValue(active.EffectId, out var group))
                {
                    group = new ActiveEffectIconGroup(contentDb, active.EffectId);
                    groups.Add(active.EffectId, group);
                    ordered.Add(active.EffectId);
                }

                group.Add(active);
            }

            if (ordered.Count == 0)
                return Array.Empty<RuntimeMagicEffectIconViewModel>();

            float fadeTime = 1f;
            if (contentDb.TryGetGameSettingFloat("fMagicStartIconBlink", out float gmstFadeTime) && gmstFadeTime > 0f)
                fadeTime = gmstFadeTime;

            var result = new RuntimeMagicEffectIconViewModel[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                short effectId = ordered[i];
                var group = groups[effectId];
                string displayName = ResolveMagicEffectName(contentDb, effectId);
                var descriptionLines = group.BuildDescriptionLines(displayName);
                string iconPath = ResolveMagicEffectIconPath(contentDb, effectId);
                result[i] = new RuntimeMagicEffectIconViewModel
                {
                    EffectId = effectId,
                    IconPath = iconPath,
                    DisplayName = displayName,
                    TooltipText = BuildActiveEffectPlainTooltip(displayName, descriptionLines),
                    Tooltip = new RuntimeMagicEffectTooltipViewModel
                    {
                        IconPath = iconPath,
                        DisplayName = displayName,
                        DescriptionLines = descriptionLines,
                    },
                    Alpha = group.ComputeAlpha(fadeTime),
                    SourceLines = descriptionLines,
                };
            }

            return result;
        }

        sealed class ActiveEffectIconGroup
        {
            readonly RuntimeContentDatabase _contentDb;
            readonly short _effectId;
            readonly List<string> _sourceLines = new();
            float _lowestFadeTimeLeft = float.PositiveInfinity;

            public ActiveEffectIconGroup(RuntimeContentDatabase contentDb, short effectId)
            {
                _contentDb = contentDb;
                _effectId = effectId;
            }

            public void Add(in ActorActiveMagicEffect active)
            {
                string source = active.SourceName.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = active.SourceId.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    source = $"Effect {_effectId}";

                _sourceLines.Add(BuildActiveEffectSourceLine(_contentDb, _effectId, active, source));
                if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                    _lowestFadeTimeLeft = Math.Min(_lowestFadeTimeLeft, active.TimeLeftSeconds);
            }

            public float ComputeAlpha(float fadeTime)
            {
                if (fadeTime <= 0f || float.IsPositiveInfinity(_lowestFadeTimeLeft))
                    return 1f;

                return Math.Clamp(_lowestFadeTimeLeft / fadeTime, 0f, 1f);
            }

            public string[] BuildDescriptionLines(string displayName)
                => CollapseRedundantDescriptionLines(_sourceLines, displayName);
        }

        static string ResolveSpellName(in SpellDef spell)
            => string.IsNullOrWhiteSpace(spell.Name) ? spell.Id ?? "--" : spell.Name.Trim();

        static string ResolveSpellTypeName(int type)
        {
            return type switch
            {
                0 => "Spell",
                1 => "Ability",
                2 => "Blight",
                3 => "Disease",
                4 => "Curse",
                5 => "Power",
                _ => "Spell",
            };
        }

        static string ResolveMagicEffectName(RuntimeContentDatabase contentDb, short effectId)
        {
            if (effectId >= 0 && effectId < s_MagicEffectNames.Length)
            {
                var name = s_MagicEffectNames[effectId];
                if (contentDb != null
                    && !string.IsNullOrWhiteSpace(name.Gmst)
                    && contentDb.TryGetGameSettingString(name.Gmst, out string gmstName))
                    return gmstName.Trim();
                if (!string.IsNullOrWhiteSpace(name.Fallback))
                    return name.Fallback;
            }

            return $"Effect {effectId}";
        }

        static RuntimeSpellTooltipViewModel BuildSpellTooltip(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            string title = ResolveSpellName(spell);
            var effects = BuildSpellTooltipEffects(contentDb, spell);
            return new RuntimeSpellTooltipViewModel
            {
                Title = title,
                SchoolText = spell.SpellType == 0 ? BuildSpellSchoolText(contentDb, spell) : null,
                Effects = effects,
            };
        }

        static RuntimeSpellTooltipEffectRow[] BuildSpellTooltipEffects(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return Array.Empty<RuntimeSpellTooltipEffectRow>();

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = Math.Min(spell.EffectCount, available);
            var rows = new RuntimeSpellTooltipEffectRow[count];
            for (int i = 0; i < count; i++)
            {
                var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new RuntimeSpellTooltipEffectRow
                {
                    EffectId = effect.EffectId,
                    IconPath = ResolveMagicEffectIconPath(contentDb, effect.EffectId),
                    Text = BuildSpellTooltipEffectText(contentDb, effect),
                };
            }

            return rows;
        }

        static string BuildSpellSchoolText(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0 || contentDb?.Data.MagicEffectInstances == null)
                return null;

            int available = Math.Max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            if (available <= 0)
                return null;

            short effectId = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex].EffectId;
            int school = -1;
            if (contentDb.TryGetMagicEffectHandle(effectId, out var handle))
            {
                ref readonly var def = ref contentDb.Get(handle);
                school = def.School;
            }

            string schoolName = ResolveSchoolName(contentDb, school);
            string schoolLabel = ResolveGameSettingString(contentDb, "sSchool", "School");
            return string.IsNullOrWhiteSpace(schoolName) ? null : $"{schoolLabel}: {schoolName}";
        }

        static string ResolveMagicEffectIconPath(RuntimeContentDatabase contentDb, short effectId)
        {
            if (contentDb != null && contentDb.TryGetMagicEffectHandle(effectId, out var handle))
            {
                ref readonly var def = ref contentDb.Get(handle);
                return def.Icon ?? string.Empty;
            }

            return string.Empty;
        }

        static string BuildEffectDetail(RuntimeContentDatabase contentDb, in MagicEffectInstanceDef effect)
        {
            var parts = new List<string>(4);
            if (effect.MagnitudeMin != 0 || effect.MagnitudeMax != 0)
                parts.Add(effect.MagnitudeMin == effect.MagnitudeMax
                    ? $"{effect.MagnitudeMin} {Pluralize(effect.MagnitudeMin, "pt", "pts")}"
                    : $"{effect.MagnitudeMin} to {effect.MagnitudeMax} pts");
            if (effect.Duration > 0)
                parts.Add($"for {effect.Duration} {Pluralize(effect.Duration, "sec", "secs")}");
            if (effect.Area > 0)
                parts.Add($"in {effect.Area} ft");
            parts.Add(effect.Range switch
            {
                0 => "on Self",
                1 => "on Touch",
                2 => "on Target",
                _ => "range ?",
            });
            return string.Join(" ", parts);
        }

        static string BuildSpellTooltipEffectText(RuntimeContentDatabase contentDb, in MagicEffectInstanceDef effect)
        {
            string name = ResolveMagicEffectName(contentDb, effect.EffectId);
            string argument = ResolveEffectArgumentName(effect);
            if (!string.IsNullOrWhiteSpace(argument))
                name = $"{name} {argument}";

            string detail = BuildEffectDetail(contentDb, effect);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}";
        }

        static string BuildSpellEffectTooltip(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            var effects = BuildSpellEffectRows(contentDb, spell);
            if (effects.Length == 0)
                return string.Empty;

            var lines = new List<string>(effects.Length);
            for (int i = 0; i < effects.Length; i++)
            {
                string name = string.IsNullOrWhiteSpace(effects[i].Name) ? "Effect" : effects[i].Name.Trim();
                string detail = string.IsNullOrWhiteSpace(effects[i].DetailText) ? string.Empty : effects[i].DetailText.Trim();
                lines.Add(string.IsNullOrEmpty(detail) ? name : $"{name} {detail}");
            }

            return string.Join("\n", lines);
        }

        static string ResolveSchoolName(RuntimeContentDatabase contentDb, int school)
        {
            string gmstId = school switch
            {
                0 => "sSkillAlteration",
                1 => "sSkillConjuration",
                2 => "sSkillDestruction",
                3 => "sSkillIllusion",
                4 => "sSkillMysticism",
                5 => "sSkillRestoration",
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(gmstId)
                && contentDb != null
                && contentDb.TryGetGameSettingString(gmstId, out string gmstName))
            {
                return gmstName.Trim();
            }

            return school switch
            {
                0 => "Alteration",
                1 => "Conjuration",
                2 => "Destruction",
                3 => "Illusion",
                4 => "Mysticism",
                5 => "Restoration",
                _ => string.Empty,
            };
        }

        static string ResolveGameSettingString(RuntimeContentDatabase contentDb, string id, string fallback)
            => contentDb != null && contentDb.TryGetGameSettingString(id, out string value)
                ? value.Trim()
                : fallback;

        static string ResolveEffectArgumentName(in MagicEffectInstanceDef effect)
        {
            if (effect.Attribute >= 0)
                return ResolveAttributeName(effect.Attribute);
            if (effect.Skill >= 0)
                return ResolveSkillName(effect.Skill);
            return string.Empty;
        }

        static string ResolveAttributeName(int attribute)
        {
            return attribute switch
            {
                0 => "Strength",
                1 => "Intelligence",
                2 => "Willpower",
                3 => "Agility",
                4 => "Speed",
                5 => "Endurance",
                6 => "Personality",
                7 => "Luck",
                _ => string.Empty,
            };
        }

        static string ResolveSkillName(int skill)
        {
            return skill switch
            {
                0 => "Block",
                1 => "Armorer",
                2 => "Medium Armor",
                3 => "Heavy Armor",
                4 => "Blunt Weapon",
                5 => "Long Blade",
                6 => "Axe",
                7 => "Spear",
                8 => "Athletics",
                9 => "Enchant",
                10 => "Destruction",
                11 => "Alteration",
                12 => "Illusion",
                13 => "Conjuration",
                14 => "Mysticism",
                15 => "Restoration",
                16 => "Alchemy",
                17 => "Unarmored",
                18 => "Security",
                19 => "Sneak",
                20 => "Acrobatics",
                21 => "Light Armor",
                22 => "Short Blade",
                23 => "Marksman",
                24 => "Mercantile",
                25 => "Speechcraft",
                26 => "Hand-to-hand",
                _ => string.Empty,
            };
        }

        static string Pluralize(int value, string singular, string plural)
            => Math.Abs(value) == 1 ? singular : plural;

        static string BuildActiveEffectSourceLine(
            RuntimeContentDatabase contentDb,
            short effectId,
            in ActorActiveMagicEffect active,
            string source)
        {
            string displayName = ResolveMagicEffectName(contentDb, effectId);
            string line = string.IsNullOrWhiteSpace(source)
                ? displayName
                : source.Trim();
            string sourceLabel = line;
            string detail = string.Empty;

            if (TryGetMagicEffectDef(contentDb, effectId, out var def))
            {
                if ((def.Flags & MagicEffectFlagTargetSkill) != 0 && active.Skill >= 0)
                {
                    string skillName = ResolveSkillName(active.Skill);
                    if (!string.IsNullOrWhiteSpace(skillName))
                        line += $" ({skillName})";
                }

                if ((def.Flags & MagicEffectFlagTargetAttribute) != 0 && active.Attribute >= 0)
                {
                    string attributeName = ResolveAttributeName(active.Attribute);
                    if (!string.IsNullOrWhiteSpace(attributeName))
                        line += $" ({attributeName})";
                }

                sourceLabel = line;
                detail = FormatActiveEffectMagnitude(contentDb, effectId, active.Magnitude, def.Flags).TrimStart(':', ' ');
            }
            else if (Math.Abs(active.Magnitude) > 0.0001f)
            {
                detail = $"{(int)active.Magnitude} {ResolveGameSettingString(contentDb, Math.Abs((int)active.Magnitude) == 1 ? "sPoint" : "sPoints", "pts")}";
            }

            if (active.DurationSeconds >= 0f && active.TimeLeftSeconds >= 0f)
                detail = string.IsNullOrWhiteSpace(detail)
                    ? BuildActiveEffectDuration(contentDb, active.TimeLeftSeconds).Trim()
                    : $"{detail} {BuildActiveEffectDuration(contentDb, active.TimeLeftSeconds).Trim()}";

            if (string.Equals(sourceLabel, displayName, StringComparison.OrdinalIgnoreCase))
                return detail;
            return string.IsNullOrWhiteSpace(detail) ? sourceLabel : $"{sourceLabel}: {detail}";
        }

        static string[] CollapseRedundantDescriptionLines(List<string> lines, string displayName)
        {
            if (lines == null || lines.Count == 0)
                return Array.Empty<string>();

            var result = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i]?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (string.Equals(line, displayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(line);
            }

            return result.ToArray();
        }

        static string BuildActiveEffectPlainTooltip(string displayName, string[] descriptionLines)
        {
            var lines = new List<string>(1 + (descriptionLines?.Length ?? 0));
            if (!string.IsNullOrWhiteSpace(displayName))
                lines.Add(displayName.Trim());
            if (descriptionLines != null)
            {
                for (int i = 0; i < descriptionLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(descriptionLines[i]))
                        lines.Add(descriptionLines[i].Trim());
                }
            }

            return string.Join("\n", lines);
        }

        static string FormatActiveEffectMagnitude(RuntimeContentDatabase contentDb, short effectId, float magnitude, int flags)
        {
            var displayType = ResolveMagnitudeDisplayType(effectId, flags);
            if (displayType == ActiveEffectMagnitudeDisplayType.None)
                return string.Empty;

            int integerMagnitude = (int)magnitude;
            if (displayType == ActiveEffectMagnitudeDisplayType.TimesInt)
            {
                string unit = ResolveGameSettingString(contentDb, "sXTimesINT", "x INT");
                return $" {(integerMagnitude / 10f):0.0}{unit}";
            }

            string result = $": {integerMagnitude}";
            if (displayType != ActiveEffectMagnitudeDisplayType.Percentage)
                result += " ";

            result += displayType switch
            {
                ActiveEffectMagnitudeDisplayType.Feet => ResolveGameSettingString(contentDb, "sFeet", "ft"),
                ActiveEffectMagnitudeDisplayType.Level => ResolveGameSettingString(contentDb, Math.Abs(integerMagnitude) == 1 ? "sLevel" : "sLevels", "levels"),
                ActiveEffectMagnitudeDisplayType.Percentage => ResolveGameSettingString(contentDb, "sPercent", "%"),
                _ => ResolveGameSettingString(contentDb, Math.Abs(integerMagnitude) == 1 ? "sPoint" : "sPoints", "pts"),
            };

            return result;
        }

        static string BuildActiveEffectDuration(RuntimeContentDatabase contentDb, float timeLeftSeconds)
        {
            int seconds = Math.Max(0, (int)Math.Ceiling(timeLeftSeconds));
            string durationLabel = ResolveGameSettingString(contentDb, "sDuration", "Duration");
            return $" {durationLabel}: {seconds}";
        }

        static bool TryGetMagicEffectDef(RuntimeContentDatabase contentDb, short effectId, out MagicEffectDef def)
        {
            if (contentDb != null && contentDb.TryGetMagicEffectHandle(effectId, out var handle))
            {
                def = contentDb.Get(handle);
                return true;
            }

            def = default;
            return false;
        }

        enum ActiveEffectMagnitudeDisplayType
        {
            None,
            Feet,
            Level,
            Percentage,
            Points,
            TimesInt,
        }

        static ActiveEffectMagnitudeDisplayType ResolveMagnitudeDisplayType(short effectId, int flags)
        {
            if ((flags & MagicEffectFlagNoMagnitude) != 0)
                return ActiveEffectMagnitudeDisplayType.None;
            if (effectId == 84)
                return ActiveEffectMagnitudeDisplayType.TimesInt;
            if (effectId == 59 || effectId is >= 64 and <= 66)
                return ActiveEffectMagnitudeDisplayType.Feet;
            if (effectId == 118 || effectId == 119)
                return ActiveEffectMagnitudeDisplayType.Level;
            if (effectId is >= 28 and <= 36
                || effectId is >= 90 and <= 99
                || effectId == 40
                || effectId == 47
                || effectId == 57
                || effectId == 68)
            {
                return ActiveEffectMagnitudeDisplayType.Percentage;
            }

            return ActiveEffectMagnitudeDisplayType.Points;
        }
    }
}

