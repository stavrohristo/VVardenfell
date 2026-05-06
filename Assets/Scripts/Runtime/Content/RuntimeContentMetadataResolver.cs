using System;
using Unity.Collections;
using Unity.Mathematics;
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

    public readonly struct FixedCarryableMetadata
    {
        public readonly ContentReferenceKind Kind;
        public readonly uint RecordTag;
        public readonly FixedString128Bytes DisplayName;
        public readonly float Weight;
        public readonly int Value;
        public readonly bool HasMagicCategory;

        public FixedCarryableMetadata(
            ContentReferenceKind kind,
            uint recordTag,
            FixedString128Bytes displayName,
            float weight,
            int value,
            bool hasMagicCategory)
        {
            Kind = kind;
            RecordTag = recordTag;
            DisplayName = displayName;
            Weight = weight;
            Value = value;
            HasMagicCategory = hasMagicCategory;
        }
    }

    public readonly struct FixedBookContentMetadata
    {
        public readonly ContentReference Content;
        public readonly FixedString128Bytes Title;
        public readonly bool IsScroll;
        public readonly int SkillId;
        public readonly int EnchantPoints;

        public FixedBookContentMetadata(
            ContentReference content,
            FixedString128Bytes title,
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

        public static bool TryResolveCarryableFixed(
            ref RuntimeContentBlob blob,
            ContentReference content,
            out FixedCarryableMetadata metadata)
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
                    metadata = new FixedCarryableMetadata(
                        ContentReferenceKind.Item,
                        item.RecordTag,
                        ResolveDisplayNameFixed(ref item, FixedDisplayFallback.UnknownItem),
                        item.Float0 > 0f ? item.Float0 : -1f,
                        item.Int0 > 0 ? item.Int0 : -1,
                        HasMagicCategoryFixed(ref item));
                    return true;
                }
                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = content.HandleValue };
                    if (!handle.IsValid)
                        return false;

                    ref RuntimeLightDefBlob light = ref RuntimeContentBlobUtility.Get(ref blob, handle);
                    metadata = new FixedCarryableMetadata(
                        ContentReferenceKind.Light,
                        light.RecordTag,
                        ResolveDisplayNameFixed(ref light),
                        light.Weight > 0f ? light.Weight : -1f,
                        light.Value > 0 ? light.Value : -1,
                        false);
                    return true;
                }
                default:
                    return false;
            }
        }

        public static bool TryResolveBookFixed(
            ref RuntimeContentBlob blob,
            ContentReference content,
            out FixedBookContentMetadata metadata)
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

            metadata = new FixedBookContentMetadata(
                content,
                ResolveDisplayNameFixed(ref item, FixedDisplayFallback.UnknownBook),
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

        public static bool MatchesCategory(in FixedCarryableMetadata metadata, InventoryWindowCategory category)
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

        public static FixedString512Bytes BuildCarryableDetailsFixed(in FixedCarryableMetadata metadata, int count)
        {
            var result = default(FixedString512Bytes);
            if (metadata.DisplayName.IsEmpty)
                AppendSelectItemToInspect(ref result);
            else
                result.Append(metadata.DisplayName);

            if (count > 1)
            {
                result.Append(' ');
                result.Append('x');
                result.Append(count);
            }

            AppendSpaces(ref result, 3);
            result.Append('w');
            result.Append('t');
            result.Append(' ');
            if (metadata.Weight >= 0f)
                AppendFixedOneDecimal(ref result, metadata.Weight * count);
            else
                AppendDashDash(ref result);

            AppendSpaces(ref result, 3);
            result.Append('v');
            result.Append('a');
            result.Append('l');
            result.Append(' ');
            if (metadata.Value >= 0)
                result.Append(metadata.Value * count);
            else
                AppendDashDash(ref result);

            return result;
        }

        public static FixedString512Bytes BuildSelectItemDetailsFixed()
        {
            var result = default(FixedString512Bytes);
            AppendSelectItemToInspect(ref result);
            return result;
        }

        public static FixedString512Bytes BuildContainerEmptyDetailsFixed()
        {
            var result = default(FixedString512Bytes);
            AppendContainerIsEmpty(ref result);
            return result;
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

        public static FixedString128Bytes ResolveDisplayNameFixed(ref RuntimeBaseDefBlob item, FixedDisplayFallback fallback)
        {
            FixedString128Bytes name = RuntimeFixedStringUtility.ToFixed128OrDefault(ref item.Name);
            if (!IsWhiteSpace(name))
                return TrimAscii(name);

            FixedString128Bytes id = RuntimeFixedStringUtility.ToFixed128OrDefault(ref item.Id);
            if (!IsWhiteSpace(id))
                return TrimAscii(id);

            return BuildFallback(fallback);
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

        public static FixedString128Bytes ResolveDisplayNameFixed(ref RuntimeLightDefBlob light)
        {
            FixedString128Bytes name = RuntimeFixedStringUtility.ToFixed128OrDefault(ref light.Name);
            if (!IsWhiteSpace(name))
                return TrimAscii(name);

            FixedString128Bytes id = RuntimeFixedStringUtility.ToFixed128OrDefault(ref light.Id);
            if (!IsWhiteSpace(id))
                return TrimAscii(id);

            return BuildFallback(FixedDisplayFallback.UnknownLight);
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

        public static FixedString128Bytes ResolveActorDisplayNameFixed(ref RuntimeContentBlob blob, ActorDefHandle handle)
        {
            if (!handle.IsValid)
                return BuildNpcFallback();

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref blob, handle);
            FixedString128Bytes name = RuntimeFixedStringUtility.ToFixed128OrDefault(ref actor.Name);
            if (!IsWhiteSpace(name))
                return TrimAscii(name);

            FixedString128Bytes id = RuntimeFixedStringUtility.ToFixed128OrDefault(ref actor.Id);
            if (!IsWhiteSpace(id))
                return TrimAscii(id);

            return BuildNpcFallback();
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
            => RuntimeMagicEffectNameResolver.Resolve(ref blob, effectId);

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

        static bool HasMagicCategoryFixed(ref RuntimeBaseDefBlob item)
        {
            return item.RecordTag == AlchTag
                || item.RecordTag == AppaTag
                || item.RecordTag == IngrTag
                || item.RecordTag == BookRecordTag
                || item.EnchantId.Length != 0;
        }

        public enum FixedDisplayFallback : byte
        {
            UnknownItem,
            UnknownBook,
            UnknownLight,
        }

        static FixedString128Bytes BuildFallback(FixedDisplayFallback fallback)
        {
            var result = default(FixedString128Bytes);
            switch (fallback)
            {
                case FixedDisplayFallback.UnknownBook:
                    AppendUnknownBook(ref result);
                    break;
                case FixedDisplayFallback.UnknownLight:
                    AppendUnknownLight(ref result);
                    break;
                default:
                    AppendUnknownItem(ref result);
                    break;
            }

            return result;
        }

        static FixedString128Bytes TrimAscii(FixedString128Bytes value)
        {
            int start = 0;
            int end = value.Length - 1;
            while (start <= end && IsAsciiWhiteSpace(value[start]))
                start++;
            while (end >= start && IsAsciiWhiteSpace(value[end]))
                end--;

            var result = default(FixedString128Bytes);
            for (int i = start; i <= end; i++)
                result.Append((char)value[i]);
            return result;
        }

        static bool IsWhiteSpace(FixedString128Bytes value)
        {
            if (value.Length == 0)
                return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (!IsAsciiWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }

        static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v';

        static void AppendFixedOneDecimal(ref FixedString512Bytes result, float value)
        {
            if (value < 0f)
            {
                result.Append('-');
                value = -value;
            }

            int scaled = (int)math.round(value * 10f);
            result.Append(scaled / 10);
            result.Append('.');
            result.Append(scaled % 10);
        }

        static void AppendSpaces(ref FixedString512Bytes result, int count)
        {
            for (int i = 0; i < count; i++)
                result.Append(' ');
        }

        static void AppendDashDash(ref FixedString512Bytes result)
        {
            result.Append('-');
            result.Append('-');
        }

        static void AppendSelectItemToInspect(ref FixedString512Bytes result)
        {
            result.Append('S'); result.Append('e'); result.Append('l'); result.Append('e'); result.Append('c'); result.Append('t'); result.Append(' ');
            result.Append('a'); result.Append('n'); result.Append(' '); result.Append('i'); result.Append('t'); result.Append('e'); result.Append('m'); result.Append(' ');
            result.Append('t'); result.Append('o'); result.Append(' '); result.Append('i'); result.Append('n'); result.Append('s'); result.Append('p'); result.Append('e'); result.Append('c'); result.Append('t'); result.Append('.');
        }

        static void AppendContainerIsEmpty(ref FixedString512Bytes result)
        {
            result.Append('C'); result.Append('o'); result.Append('n'); result.Append('t'); result.Append('a'); result.Append('i'); result.Append('n'); result.Append('e'); result.Append('r'); result.Append(' ');
            result.Append('i'); result.Append('s'); result.Append(' '); result.Append('e'); result.Append('m'); result.Append('p'); result.Append('t'); result.Append('y'); result.Append('.');
        }

        static void AppendUnknownItem(ref FixedString128Bytes result)
        {
            result.Append('U'); result.Append('n'); result.Append('k'); result.Append('n'); result.Append('o'); result.Append('w'); result.Append('n'); result.Append(' ');
            result.Append('i'); result.Append('t'); result.Append('e'); result.Append('m');
        }

        static void AppendUnknownBook(ref FixedString128Bytes result)
        {
            result.Append('U'); result.Append('n'); result.Append('k'); result.Append('n'); result.Append('o'); result.Append('w'); result.Append('n'); result.Append(' ');
            result.Append('b'); result.Append('o'); result.Append('o'); result.Append('k');
        }

        static void AppendUnknownLight(ref FixedString128Bytes result)
        {
            result.Append('U'); result.Append('n'); result.Append('k'); result.Append('n'); result.Append('o'); result.Append('w'); result.Append('n'); result.Append(' ');
            result.Append('l'); result.Append('i'); result.Append('g'); result.Append('h'); result.Append('t');
        }

        static FixedString128Bytes BuildNpcFallback()
        {
            var result = default(FixedString128Bytes);
            result.Append('n'); result.Append('p'); result.Append('c');
            return result;
        }

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);
    }
}
