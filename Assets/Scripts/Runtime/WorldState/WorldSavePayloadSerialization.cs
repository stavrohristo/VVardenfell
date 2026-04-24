using System;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;

namespace VVardenfell.Runtime.WorldState
{
    public static partial class WorldSaveStorage
    {
        const uint PayloadMagic = 0x53575656u; // VVWS
        const int PayloadVersion = 9;

        static void ValidatePayloadHeader(BinaryReader r)
        {
            uint magic = r.ReadUInt32();
            if (magic != PayloadMagic)
                throw new InvalidDataException("unexpected save magic");

            int version = r.ReadInt32();
            if (version != PayloadVersion)
                throw new InvalidDataException($"unsupported save version {version}");
        }

        static WorldSavePayload ReadPayloadFromAnySave(Stream stream, out SaveGameSlotMetadata metadata)
        {
            metadata = default;
            using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            long start = stream.Position;
            uint magic = r.ReadUInt32();
            if (magic == SlotMagic)
            {
                int slotVersion = r.ReadInt32();
                if (slotVersion != SlotVersion)
                    throw new InvalidDataException($"unsupported save slot version {slotVersion}");

                metadata = ReadMetadata(r);
                return ReadPayload(r);
            }

            stream.Position = start;
            return ReadPayload(r);
        }

        static void WritePayload(BinaryWriter w, in WorldSavePayload payload)
        {
            w.Write(PayloadMagic);
            w.Write(PayloadVersion);
            w.Write(payload.PlayerPosition.x);
            w.Write(payload.PlayerPosition.y);
            w.Write(payload.PlayerPosition.z);
            w.Write(payload.PlayerRotation.value.x);
            w.Write(payload.PlayerRotation.value.y);
            w.Write(payload.PlayerRotation.value.z);
            w.Write(payload.PlayerRotation.value.w);
            w.Write(payload.PlayerPitchDegrees);

            WriteActorStats(w, payload.ActorStats);
            WriteActorIdentity(w, payload.PlayerIdentity);
            w.Write(payload.KnownSpells?.Length ?? 0);
            if (payload.KnownSpells != null)
            {
                for (int i = 0; i < payload.KnownSpells.Length; i++)
                    WriteKnownSpell(w, payload.KnownSpells[i]);
            }
            w.Write(payload.ActiveMagicEffects?.Length ?? 0);
            if (payload.ActiveMagicEffects != null)
            {
                for (int i = 0; i < payload.ActiveMagicEffects.Length; i++)
                    WriteActiveMagicEffect(w, payload.ActiveMagicEffects[i]);
            }
            w.Write(payload.ExteriorMapDiscovery?.Length ?? 0);
            if (payload.ExteriorMapDiscovery != null)
            {
                for (int i = 0; i < payload.ExteriorMapDiscovery.Length; i++)
                    WriteMapDiscoveryTile(w, payload.ExteriorMapDiscovery[i]);
            }
            WriteGlobalMapOverlay(w, payload.GlobalMapOverlay);

            w.Write(payload.InteriorActive);
            w.Write(payload.ActiveInteriorCellId ?? string.Empty);
            w.Write(payload.NextJournalSequence);
            w.Write(payload.NextRuntimeRefId);

            w.Write(payload.Inventory?.Length ?? 0);
            if (payload.Inventory != null)
            {
                for (int i = 0; i < payload.Inventory.Length; i++)
                    WriteInventoryEntry(w, payload.Inventory[i]);
            }

            w.Write(payload.JournalEntries?.Length ?? 0);
            if (payload.JournalEntries != null)
            {
                for (int i = 0; i < payload.JournalEntries.Length; i++)
                    WriteJournalEntry(w, payload.JournalEntries[i]);
            }
        }

        static WorldSavePayload ReadPayload(BinaryReader r)
        {
            uint magic = r.ReadUInt32();
            if (magic != PayloadMagic)
                throw new InvalidDataException("unexpected save magic");

            int version = r.ReadInt32();
            if (version != PayloadVersion)
                throw new InvalidDataException($"unsupported save version {version}");

            var payload = new WorldSavePayload
            {
                PlayerPosition = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerRotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerPitchDegrees = r.ReadSingle(),
                ActorStats = ReadActorStats(r),
            };

            payload.PlayerIdentity = ReadActorIdentity(r);

            int knownSpellCount = ReadCount(r, "known spell");
            payload.KnownSpells = new PlayerKnownSpell[knownSpellCount];
            for (int i = 0; i < knownSpellCount; i++)
                payload.KnownSpells[i] = ReadKnownSpell(r);

            int activeEffectCount = ReadCount(r, "active magic effect");
            payload.ActiveMagicEffects = new ActorActiveMagicEffect[activeEffectCount];
            for (int i = 0; i < activeEffectCount; i++)
                payload.ActiveMagicEffects[i] = ReadActiveMagicEffect(r);

            int mapTileCount = ReadCount(r, "map discovery tile");
            payload.ExteriorMapDiscovery = new LocalMapDiscoveryTilePayload[mapTileCount];
            for (int i = 0; i < mapTileCount; i++)
                payload.ExteriorMapDiscovery[i] = ReadMapDiscoveryTile(r);

            payload.GlobalMapOverlay = ReadGlobalMapOverlay(r);

            payload.InteriorActive = r.ReadBoolean();
            payload.ActiveInteriorCellId = r.ReadString();
            payload.NextJournalSequence = r.ReadUInt32();
            payload.NextRuntimeRefId = r.ReadUInt32();

            int inventoryCount = ReadCount(r, "inventory");
            payload.Inventory = new PlayerInventoryItem[inventoryCount];
            for (int i = 0; i < inventoryCount; i++)
                payload.Inventory[i] = ReadInventoryEntry(r);

            int journalCount = ReadCount(r, "journal");
            payload.JournalEntries = new WorldJournalEntry[journalCount];
            for (int i = 0; i < journalCount; i++)
                payload.JournalEntries[i] = ReadJournalEntry(r);

            return payload;
        }

        static void WriteInventoryEntry(BinaryWriter w, in PlayerInventoryItem value)
        {
            WriteContentReference(w, value.Content);
            w.Write(value.Count);
        }

        static PlayerInventoryItem ReadInventoryEntry(BinaryReader r)
        {
            return new PlayerInventoryItem
            {
                Content = ReadContentReference(r),
                Count = r.ReadInt32(),
            };
        }

        static void WriteJournalEntry(BinaryWriter w, in WorldJournalEntry value)
        {
            w.Write(value.Sequence);
            w.Write(value.Kind);
            w.Write(value.PlacedRefId);
            w.Write(value.RuntimeRefId);
            WriteContentReference(w, value.Content);
            w.Write(value.DeltaCount);
            w.Write(value.Position.x);
            w.Write(value.Position.y);
            w.Write(value.Position.z);
            w.Write(value.Rotation.value.x);
            w.Write(value.Rotation.value.y);
            w.Write(value.Rotation.value.z);
            w.Write(value.Rotation.value.w);
            w.Write(value.Scale);
            w.Write(value.ExteriorCell.x);
            w.Write(value.ExteriorCell.y);
            w.Write(value.InteriorCellId.ToString());
            w.Write(value.IsInterior);
            w.Write(value.PersistencePolicy);
        }

        static WorldJournalEntry ReadJournalEntry(BinaryReader r)
        {
            return new WorldJournalEntry
            {
                Sequence = r.ReadUInt32(),
                Kind = r.ReadByte(),
                PlacedRefId = r.ReadUInt32(),
                RuntimeRefId = r.ReadUInt32(),
                Content = ReadContentReference(r),
                DeltaCount = r.ReadInt32(),
                Position = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Rotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Scale = r.ReadSingle(),
                ExteriorCell = new int2(r.ReadInt32(), r.ReadInt32()),
                InteriorCellId = ToFixed128(r.ReadString()),
                IsInterior = r.ReadByte(),
                PersistencePolicy = r.ReadByte(),
            };
        }

        static void WriteActorStats(BinaryWriter w, in ActorRuntimeStatSeed value)
        {
            w.Write(value.Attributes.Strength);
            w.Write(value.Attributes.Intelligence);
            w.Write(value.Attributes.Willpower);
            w.Write(value.Attributes.Agility);
            w.Write(value.Attributes.Speed);
            w.Write(value.Attributes.Endurance);
            w.Write(value.Attributes.Personality);
            w.Write(value.Attributes.Luck);
            w.Write(value.Skills.Block);
            w.Write(value.Skills.Armorer);
            w.Write(value.Skills.MediumArmor);
            w.Write(value.Skills.HeavyArmor);
            w.Write(value.Skills.BluntWeapon);
            w.Write(value.Skills.LongBlade);
            w.Write(value.Skills.Axe);
            w.Write(value.Skills.Spear);
            w.Write(value.Skills.Athletics);
            w.Write(value.Skills.Enchant);
            w.Write(value.Skills.Destruction);
            w.Write(value.Skills.Alteration);
            w.Write(value.Skills.Illusion);
            w.Write(value.Skills.Conjuration);
            w.Write(value.Skills.Mysticism);
            w.Write(value.Skills.Restoration);
            w.Write(value.Skills.Alchemy);
            w.Write(value.Skills.Unarmored);
            w.Write(value.Skills.Security);
            w.Write(value.Skills.Sneak);
            w.Write(value.Skills.Acrobatics);
            w.Write(value.Skills.LightArmor);
            w.Write(value.Skills.ShortBlade);
            w.Write(value.Skills.Marksman);
            w.Write(value.Skills.Mercantile);
            w.Write(value.Skills.Speechcraft);
            w.Write(value.Skills.HandToHand);
            w.Write(value.Vitals.CurrentHealth);
            w.Write(value.Vitals.ModifiedHealthBase);
            w.Write(value.Vitals.CurrentMagicka);
            w.Write(value.Vitals.ModifiedMagickaBase);
            w.Write(value.Vitals.CurrentFatigue);
            w.Write(value.Vitals.ModifiedFatigueBase);
            w.Write(value.EffectModifiers.JumpMagnitude);
            w.Write(value.EffectModifiers.FeatherMagnitude);
            w.Write(value.EffectModifiers.BurdenMagnitude);
        }

        static ActorRuntimeStatSeed ReadActorStats(BinaryReader r)
        {
            var result = new ActorRuntimeStatSeed
            {
                Attributes = new ActorAttributeSet
                {
                    Strength = r.ReadSingle(),
                    Intelligence = r.ReadSingle(),
                    Willpower = r.ReadSingle(),
                    Agility = r.ReadSingle(),
                    Speed = r.ReadSingle(),
                    Endurance = r.ReadSingle(),
                    Personality = r.ReadSingle(),
                    Luck = r.ReadSingle(),
                },
                Skills = new ActorSkillSet
                {
                    Block = r.ReadSingle(),
                    Armorer = r.ReadSingle(),
                    MediumArmor = r.ReadSingle(),
                    HeavyArmor = r.ReadSingle(),
                    BluntWeapon = r.ReadSingle(),
                    LongBlade = r.ReadSingle(),
                    Axe = r.ReadSingle(),
                    Spear = r.ReadSingle(),
                    Athletics = r.ReadSingle(),
                    Enchant = r.ReadSingle(),
                    Destruction = r.ReadSingle(),
                    Alteration = r.ReadSingle(),
                    Illusion = r.ReadSingle(),
                    Conjuration = r.ReadSingle(),
                    Mysticism = r.ReadSingle(),
                    Restoration = r.ReadSingle(),
                    Alchemy = r.ReadSingle(),
                    Unarmored = r.ReadSingle(),
                    Security = r.ReadSingle(),
                    Sneak = r.ReadSingle(),
                    Acrobatics = r.ReadSingle(),
                    LightArmor = r.ReadSingle(),
                    ShortBlade = r.ReadSingle(),
                    Marksman = r.ReadSingle(),
                    Mercantile = r.ReadSingle(),
                    Speechcraft = r.ReadSingle(),
                    HandToHand = r.ReadSingle(),
                },
                EffectModifiers = new ActorEffectStatModifiers
                {
                },
            };

            result.Vitals = new ActorVitalSet
            {
                CurrentHealth = r.ReadSingle(),
                ModifiedHealthBase = r.ReadSingle(),
                CurrentMagicka = r.ReadSingle(),
                ModifiedMagickaBase = r.ReadSingle(),
                CurrentFatigue = r.ReadSingle(),
                ModifiedFatigueBase = r.ReadSingle(),
            };

            result.EffectModifiers = new ActorEffectStatModifiers
            {
                JumpMagnitude = r.ReadSingle(),
                FeatherMagnitude = r.ReadSingle(),
                BurdenMagnitude = r.ReadSingle(),
            };
            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, result.Attributes, ref result.Vitals, initializeMissingCurrents: true);
            return result;
        }

        static void WriteActorIdentity(BinaryWriter w, in ActorIdentitySet value)
        {
            w.Write(value.CharacterName.ToString());
            w.Write(value.Level);
            w.Write(value.RaceName.ToString());
            w.Write(value.ClassName.ToString());
            w.Write(value.BirthSignName.ToString());
            w.Write(value.Reputation);
        }

        static ActorIdentitySet ReadActorIdentity(BinaryReader r)
        {
            return new ActorIdentitySet
            {
                CharacterName = ToFixed64(r.ReadString()),
                Level = r.ReadInt32(),
                RaceName = ToFixed64(r.ReadString()),
                ClassName = ToFixed64(r.ReadString()),
                BirthSignName = ToFixed64(r.ReadString()),
                Reputation = r.ReadInt32(),
            };
        }

        static void WriteKnownSpell(BinaryWriter w, in PlayerKnownSpell value)
            => w.Write(value.Spell.Value);

        static PlayerKnownSpell ReadKnownSpell(BinaryReader r)
        {
            return new PlayerKnownSpell
            {
                Spell = new SpellDefHandle { Value = r.ReadInt32() },
            };
        }

        static void WriteActiveMagicEffect(BinaryWriter w, in ActorActiveMagicEffect value)
        {
            w.Write(value.EffectId);
            w.Write(value.Skill);
            w.Write(value.Attribute);
            w.Write(value.Magnitude);
            w.Write(value.DurationSeconds);
            w.Write(value.TimeLeftSeconds);
            w.Write(value.Applied);
            w.Write((byte)value.SourceKind);
            w.Write(value.SourceName.ToString());
            w.Write(value.SourceId.ToString());
        }

        static ActorActiveMagicEffect ReadActiveMagicEffect(BinaryReader r)
        {
            return new ActorActiveMagicEffect
            {
                EffectId = r.ReadInt16(),
                Skill = r.ReadSByte(),
                Attribute = r.ReadSByte(),
                Magnitude = r.ReadSingle(),
                DurationSeconds = r.ReadSingle(),
                TimeLeftSeconds = r.ReadSingle(),
                Applied = r.ReadByte(),
                SourceKind = (ActorActiveMagicEffectSourceKind)r.ReadByte(),
                SourceName = ToFixed64(r.ReadString()),
                SourceId = ToFixed64(r.ReadString()),
            };
        }

        static void WriteMapDiscoveryTile(BinaryWriter w, in LocalMapDiscoveryTilePayload value)
        {
            w.Write(value.Cell.x);
            w.Write(value.Cell.y);
            w.Write(value.Resolution);
            w.Write(value.Alpha?.Length ?? 0);
            if (value.Alpha == null)
                return;

            w.Write(value.Alpha);
        }

        static LocalMapDiscoveryTilePayload ReadMapDiscoveryTile(BinaryReader r)
        {
            var payload = new LocalMapDiscoveryTilePayload
            {
                Cell = new int2(r.ReadInt32(), r.ReadInt32()),
                Resolution = r.ReadInt32(),
            };
            int byteCount = ReadCount(r, "map discovery byte");
            payload.Alpha = r.ReadBytes(byteCount);
            if (payload.Alpha.Length != byteCount)
                throw new EndOfStreamException("truncated map discovery payload");
            return payload;
        }

        static void WriteGlobalMapOverlay(BinaryWriter w, in GlobalMapOverlayPayload value)
        {
            w.Write(value.MinCell.x);
            w.Write(value.MinCell.y);
            w.Write(value.MaxCell.x);
            w.Write(value.MaxCell.y);
            w.Write(value.CellPixelSize);
            w.Write(value.Width);
            w.Write(value.Height);
            w.Write(value.VisitedCells?.Length ?? 0);
            if (value.VisitedCells != null)
            {
                for (int i = 0; i < value.VisitedCells.Length; i++)
                {
                    w.Write(value.VisitedCells[i].x);
                    w.Write(value.VisitedCells[i].y);
                }
            }
            w.Write(value.PngBytes?.Length ?? 0);
            if (value.PngBytes != null && value.PngBytes.Length > 0)
                w.Write(value.PngBytes);
        }

        static GlobalMapOverlayPayload ReadGlobalMapOverlay(BinaryReader r)
        {
            var payload = new GlobalMapOverlayPayload
            {
                MinCell = new int2(r.ReadInt32(), r.ReadInt32()),
                MaxCell = new int2(r.ReadInt32(), r.ReadInt32()),
                CellPixelSize = r.ReadInt32(),
                Width = r.ReadInt32(),
                Height = r.ReadInt32(),
            };
            int visitedCount = ReadCount(r, "global map visited cell");
            payload.VisitedCells = new int2[visitedCount];
            for (int i = 0; i < visitedCount; i++)
                payload.VisitedCells[i] = new int2(r.ReadInt32(), r.ReadInt32());
            int byteCount = ReadCount(r, "global map overlay byte");
            payload.PngBytes = r.ReadBytes(byteCount);
            if (payload.PngBytes.Length != byteCount)
                throw new EndOfStreamException("truncated global map overlay payload");
            return payload;
        }

        static void WriteContentReference(BinaryWriter w, in ContentReference value)
        {
            w.Write((byte)value.Kind);
            w.Write(value.HandleValue);
        }

        static ContentReference ReadContentReference(BinaryReader r)
        {
            return new ContentReference
            {
                Kind = (ContentReferenceKind)r.ReadByte(),
                HandleValue = r.ReadInt32(),
            };
        }

        static FixedString64Bytes ToFixed64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        static FixedString128Bytes ToFixed128(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        static int ReadCount(BinaryReader r, string label)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"invalid {label} count {count}");
            return count;
        }
    }
}
