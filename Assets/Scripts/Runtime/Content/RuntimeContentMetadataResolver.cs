using System;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Content
{
    public readonly struct CarryableMetadata
    {
        public readonly ContentReferenceKind Kind;
        public readonly uint RecordTag;
        public readonly string DisplayName;
        public readonly string IconPath;
        public readonly float Weight;
        public readonly int Value;
        public readonly bool HasMagicCategory;

        public CarryableMetadata(
            ContentReferenceKind kind,
            uint recordTag,
            string displayName,
            string iconPath,
            float weight,
            int value,
            bool hasMagicCategory)
        {
            Kind = kind;
            RecordTag = recordTag;
            DisplayName = displayName;
            IconPath = iconPath;
            Weight = weight;
            Value = value;
            HasMagicCategory = hasMagicCategory;
        }
    }

    public readonly struct BookContentMetadata
    {
        public readonly ContentReference Content;
        public readonly string Title;
        public readonly bool IsScroll;
        public readonly int SkillId;
        public readonly int EnchantPoints;

        public BookContentMetadata(
            ContentReference content,
            string title,
            bool isScroll,
            int skillId,
            int enchantPoints)
        {
            Content = content;
            Title = title;
            IsScroll = isScroll;
            SkillId = skillId;
            EnchantPoints = enchantPoints;
        }
    }

    public static class RuntimeContentMetadataResolver
    {
        public const int AttributeCount = 8;
        public const int SkillCount = 27;

        public static readonly uint WeapTag = MakeTag('W', 'E', 'A', 'P');
        public static readonly uint ArmoTag = MakeTag('A', 'R', 'M', 'O');
        public static readonly uint ClotTag = MakeTag('C', 'L', 'O', 'T');
        public static readonly uint BookRecordTag = MakeTag('B', 'O', 'O', 'K');
        public static readonly uint AlchTag = MakeTag('A', 'L', 'C', 'H');
        public static readonly uint AppaTag = MakeTag('A', 'P', 'P', 'A');
        public static readonly uint IngrTag = MakeTag('I', 'N', 'G', 'R');

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

        public static bool TryResolveCarryable(
            ref RuntimeContentBlob blob,
            ContentReference content,
            out CarryableMetadata metadata)
        {
            metadata = default;
            if (!content.IsValid)
                return false;

            switch (content.Kind)
            {
                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = content.HandleValue };
                    if (!handle.IsValid)
                        return false;

                    ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref blob, handle);
                    metadata = new CarryableMetadata(
                        ContentReferenceKind.Item,
                        item.RecordTag,
                        ResolveDisplayName(ref item, "Unknown item"),
                        item.Icon.ToString(),
                        item.Float0 > 0f ? item.Float0 : -1f,
                        item.Int0 > 0 ? item.Int0 : -1,
                        HasMagicCategory(ref item));
                    return true;
                }
                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = content.HandleValue };
                    if (!handle.IsValid)
                        return false;

                    ref RuntimeLightDefBlob light = ref RuntimeContentBlobUtility.Get(ref blob, handle);
                    metadata = new CarryableMetadata(
                        ContentReferenceKind.Light,
                        light.RecordTag,
                        ResolveDisplayName(ref light),
                        light.Icon.ToString(),
                        light.Weight > 0f ? light.Weight : -1f,
                        light.Value > 0 ? light.Value : -1,
                        false);
                    return true;
                }
                default:
                    return false;
            }
        }

        public static bool TryResolveBook(
            ref RuntimeContentBlob blob,
            ContentReference content,
            out BookContentMetadata metadata)
        {
            metadata = default;
            if (content.Kind != ContentReferenceKind.Item || content.HandleValue <= 0)
                return false;

            var handle = new ItemDefHandle { Value = content.HandleValue };
            if (!handle.IsValid)
                return false;

            ref RuntimeBaseDefBlob item = ref RuntimeContentBlobUtility.Get(ref blob, handle);
            if (!IsBook(ref item))
                return false;

            metadata = new BookContentMetadata(
                content,
                ResolveDisplayName(ref item, "Unknown book"),
                false,
                -1,
                0);
            return true;
        }

        public static bool IsBook(ref RuntimeBaseDefBlob item) => item.RecordTag == BookRecordTag;

        public static bool MatchesCategory(in CarryableMetadata metadata, InventoryWindowCategory category)
        {
            if (metadata.Kind == ContentReferenceKind.Light)
                return category == InventoryWindowCategory.All || category == InventoryWindowCategory.Misc;

            return category switch
            {
                InventoryWindowCategory.All => true,
                InventoryWindowCategory.Weapons => metadata.RecordTag == WeapTag,
                InventoryWindowCategory.Apparel => metadata.RecordTag == ArmoTag || metadata.RecordTag == ClotTag,
                InventoryWindowCategory.Magic => metadata.HasMagicCategory,
                InventoryWindowCategory.Misc => metadata.RecordTag != WeapTag
                    && metadata.RecordTag != ArmoTag
                    && metadata.RecordTag != ClotTag
                    && !metadata.HasMagicCategory,
                _ => true,
            };
        }

        public static string BuildCarryableDetails(in CarryableMetadata metadata, int count)
        {
            string countLabel = count > 1 ? $" x{count}" : string.Empty;
            string weightLabel = metadata.Weight >= 0f ? (metadata.Weight * count).ToString("0.0") : "--";
            string valueLabel = metadata.Value >= 0 ? (metadata.Value * count).ToString() : "--";
            return $"{metadata.DisplayName}{countLabel}   wt {weightLabel}   val {valueLabel}";
        }

        public static string ResolveDisplayName(ref RuntimeBaseDefBlob item, string fallback)
        {
            string name = item.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            string id = item.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
            return fallback;
        }

        public static string ResolveDisplayName(ref RuntimeLightDefBlob light)
        {
            string name = light.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            string id = light.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
            return "Unknown light";
        }

        public static string ResolveDoorDisplayName(ref RuntimeContentBlob blob, DoorDefHandle handle, string fallback = "door")
            => handle.IsValid ? ResolveDisplayName(ref RuntimeContentBlobUtility.Get(ref blob, handle), fallback) : fallback;

        public static string ResolveItemDisplayName(ref RuntimeContentBlob blob, ItemDefHandle handle, string fallback = "item")
            => handle.IsValid ? ResolveDisplayName(ref RuntimeContentBlobUtility.Get(ref blob, handle), fallback) : fallback;

        public static string ResolveContainerDisplayName(ref RuntimeContentBlob blob, ContainerDefHandle handle, string fallback = "container")
            => handle.IsValid ? ResolveDisplayName(ref RuntimeContentBlobUtility.Get(ref blob, handle), fallback) : fallback;

        public static string ResolveActivatorDisplayName(ref RuntimeContentBlob blob, ActivatorDefHandle handle, string fallback = "activator")
            => handle.IsValid ? ResolveDisplayName(ref RuntimeContentBlobUtility.Get(ref blob, handle), fallback) : fallback;

        public static string ResolveActorDisplayName(ref RuntimeContentBlob blob, ActorDefHandle handle, string fallback = "npc")
        {
            if (!handle.IsValid)
                return fallback;

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref blob, handle);
            string name = actor.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            string id = actor.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();
            return fallback;
        }

        public static string ResolveRaceDisplayName(ref RuntimeContentBlob blob, FixedString64Bytes raceId, string fallback = "--")
        {
            string id = raceId.ToString();
            if (!string.IsNullOrWhiteSpace(id)
                && RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref blob, RuntimeContentStableHash.HashId(id), out var handle)
                && handle.IsValid)
            {
                ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref blob, handle);
                string name = race.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            return ToDisplay(raceId, fallback);
        }

        public static string ResolveClassDisplayName(ref RuntimeContentBlob blob, FixedString64Bytes classId, string fallback = "--")
        {
            string id = classId.ToString();
            if (!string.IsNullOrWhiteSpace(id)
                && RuntimeContentBlobUtility.TryGetClassHandleByIdHash(ref blob, RuntimeContentStableHash.HashId(id), out var handle)
                && handle.IsValid)
            {
                ref RuntimeClassDefBlob classDef = ref RuntimeContentBlobUtility.GetClass(ref blob, handle);
                string name = classDef.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            return ToDisplay(classId, fallback);
        }

        public static bool TryResolveClassHandle(ref RuntimeContentBlob blob, FixedString64Bytes classId, out GenericRecordDefHandle handle)
        {
            handle = default;
            string id = classId.ToString();
            return !string.IsNullOrWhiteSpace(id)
                   && RuntimeContentBlobUtility.TryGetClassHandleByIdHash(ref blob, RuntimeContentStableHash.HashId(id), out handle)
                   && handle.IsValid;
        }

        public static string ResolveFactionDisplayName(ref RuntimeFactionDefBlob faction, string fallback)
        {
            string name = faction.Name.ToString();
            return !string.IsNullOrWhiteSpace(name) ? name.Trim() : fallback;
        }

        public static string ResolveFactionRankName(ref RuntimeContentBlob blob, ref RuntimeFactionDefBlob faction, int rank)
        {
            RuntimeContentBlobUtility.RequireRange(faction.FirstRankNameIndex, faction.RankNameCount, blob.FactionRankNames.Length, "faction rank name");
            if (rank >= 0 && rank < faction.RankNameCount)
            {
                string rankName = blob.FactionRankNames[faction.FirstRankNameIndex + rank].Value.ToString();
                if (!string.IsNullOrWhiteSpace(rankName))
                    return rankName.Trim();
            }

            return rank >= 0 ? rank.ToString() : "--";
        }

        public static string ResolveAttributeName(int attribute)
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

        public static string ResolveSkillName(int skill)
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

        public static string ResolveSchoolName(ref RuntimeContentBlob blob, int school)
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
            if (!string.IsNullOrWhiteSpace(gmstId))
            {
                string gmstName = RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref blob, RuntimeContentStableHash.HashId(gmstId));
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

        public static string ResolveSpellName(ref RuntimeSpellDefBlob spell)
        {
            string name = spell.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            string id = spell.Id.ToString();
            return string.IsNullOrWhiteSpace(id) ? "--" : id.Trim();
        }

        public static string ResolveSpellTypeName(int type)
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

        public static string ResolveMagicEffectName(ref RuntimeContentBlob blob, short effectId)
        {
            if (effectId >= 0 && effectId < s_MagicEffectNames.Length)
            {
                var name = s_MagicEffectNames[effectId];
                if (!string.IsNullOrWhiteSpace(name.Gmst))
                    return RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref blob, RuntimeContentStableHash.HashId(name.Gmst)).Trim();
                if (!string.IsNullOrWhiteSpace(name.Fallback))
                    return name.Fallback;
            }

            return $"Effect {effectId}";
        }

        public static string ResolveMagicEffectIconPath(ref RuntimeContentBlob blob, short effectId)
        {
            if (RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref blob, effectId, out var handle))
            {
                ref RuntimeMagicEffectDefBlob def = ref RuntimeContentBlobUtility.Get(ref blob, handle);
                return def.Icon.ToString();
            }

            return string.Empty;
        }

        public static bool TryGetMagicEffectFlags(ref RuntimeContentBlob blob, short effectId, out int flags)
        {
            if (RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref blob, effectId, out var handle))
            {
                ref RuntimeMagicEffectDefBlob def = ref RuntimeContentBlobUtility.Get(ref blob, handle);
                flags = def.Flags;
                return true;
            }

            flags = 0;
            return false;
        }

        public static string ResolveGameSettingString(ref RuntimeContentBlob blob, string id, string fallback)
        {
            string value = RuntimeContentBlobUtility.RequireGameSettingStringAllowEmptyByIdHash(ref blob, RuntimeContentStableHash.HashId(id));
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static string ToDisplay(FixedString64Bytes value, string fallback)
        {
            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        }

        static bool HasMagicCategory(ref RuntimeBaseDefBlob item)
        {
            string enchantId = item.EnchantId.ToString();
            return item.RecordTag == AlchTag
                || item.RecordTag == AppaTag
                || item.RecordTag == IngrTag
                || item.RecordTag == BookRecordTag
                || !string.IsNullOrWhiteSpace(enchantId);
        }

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);
    }
}
