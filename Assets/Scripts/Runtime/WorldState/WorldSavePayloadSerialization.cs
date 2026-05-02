using System;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldState
{
    public static partial class WorldSaveStorage
    {
        const uint PayloadMagic = 0x53575656u; // VVWS
        const int PayloadVersion = 19;
        const int PreviousPayloadVersion = 18;
        const int PreviousCrimePayloadVersion = 18;
        const int PreviousPlayerFactionPayloadVersion = 16;
        const int ActorDeathCountPayloadVersion = 18;
        const int DialogueFactionReactionPayloadVersion = 17;
        const int PlayerFactionJoinedPayloadVersion = 16;
        const int PlayerFactionPayloadVersion = 15;
        const int DialoguePayloadVersion = 14;
        const int QuestJournalDatePayloadVersion = 13;
        const int QuestJournalPayloadVersion = 12;
        const int WeatherPayloadVersion = 10;
        const int LegacyPayloadVersion = 9;

        static void ValidatePayloadHeader(BinaryReader r)
        {
            uint magic = r.ReadUInt32();
            if (magic != PayloadMagic)
                throw new InvalidDataException("unexpected save magic");

            int version = r.ReadInt32();
            if (version != PayloadVersion
                && version != PreviousPayloadVersion
                && version != PreviousPlayerFactionPayloadVersion
                && version != DialoguePayloadVersion
                && version != QuestJournalDatePayloadVersion
                && version != QuestJournalPayloadVersion
                && version != WeatherPayloadVersion
                && version != LegacyPayloadVersion)
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
            WritePlayerCrime(w, payload.PlayerCrime);
            w.Write(payload.PlayerFactions?.Length ?? 0);
            if (payload.PlayerFactions != null)
            {
                for (int i = 0; i < payload.PlayerFactions.Length; i++)
                    WritePlayerFaction(w, payload.PlayerFactions[i]);
            }
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

            WriteQuestJournalPayload(w, payload.QuestJournal);
            WriteDialoguePayload(w, payload.Dialogue);
            WriteActorDeathCounts(w, payload.ActorDeathCounts);
            WriteTimePayload(w, payload.Time);
            WriteWeatherPayload(w, payload.Weather);
        }

        static WorldSavePayload ReadPayload(BinaryReader r)
        {
            uint magic = r.ReadUInt32();
            if (magic != PayloadMagic)
                throw new InvalidDataException("unexpected save magic");

            int version = r.ReadInt32();
            if (version != PayloadVersion
                && version != PreviousPayloadVersion
                && version != PreviousPlayerFactionPayloadVersion
                && version != DialoguePayloadVersion
                && version != QuestJournalDatePayloadVersion
                && version != QuestJournalPayloadVersion
                && version != WeatherPayloadVersion
                && version != LegacyPayloadVersion)
                throw new InvalidDataException($"unsupported save version {version}");

            var payload = new WorldSavePayload
            {
                PlayerPosition = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerRotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                PlayerPitchDegrees = r.ReadSingle(),
                ActorStats = ReadActorStats(r),
            };

            payload.PlayerIdentity = ReadActorIdentity(r);
            payload.PlayerCrime = version >= PayloadVersion ? ReadPlayerCrime(r) : default;

            if (version >= PlayerFactionPayloadVersion)
            {
                int factionCount = ReadCount(r, "player faction");
                payload.PlayerFactions = new PlayerFactionMembership[factionCount];
                for (int i = 0; i < factionCount; i++)
                    payload.PlayerFactions[i] = ReadPlayerFaction(r, version);
            }
            else
            {
                payload.PlayerFactions = null;
            }

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

            payload.QuestJournal = version >= QuestJournalPayloadVersion
                ? ReadQuestJournalPayload(r, version)
                : new MorrowindQuestJournalSavePayload
                {
                    NextEntrySequence = 1u,
                    States = Array.Empty<MorrowindQuestJournalStateSavePayload>(),
                    Entries = Array.Empty<MorrowindQuestJournalEntrySavePayload>(),
                };

            payload.Dialogue = version >= DialoguePayloadVersion
                ? ReadDialoguePayload(r, version)
                : new MorrowindDialogueSavePayload
                {
                    NextTopicEntrySequence = 1u,
                    KnownTopicDialogueIndices = Array.Empty<int>(),
                    TopicEntries = Array.Empty<MorrowindTopicJournalEntrySavePayload>(),
                    FactionReactions = Array.Empty<MorrowindFactionReactionSavePayload>(),
                };

            payload.ActorDeathCounts = version >= ActorDeathCountPayloadVersion
                ? ReadActorDeathCounts(r)
                : Array.Empty<int>();

            if (version >= WeatherPayloadVersion)
            {
                payload.Time = ReadTimePayload(r);
                payload.Weather = ReadWeatherPayload(r, version);
            }
            else
            {
                payload.Time = ToSavePayload(MorrowindTimeBootstrapSystem.CreateDefaultTime());
                payload.Weather = ToSavePayload(MorrowindTimeBootstrapSystem.CreateDefaultWeather());
            }

            return payload;
        }

        static void WriteActorDeathCounts(BinaryWriter w, int[] counts)
        {
            w.Write(counts?.Length ?? 0);
            if (counts == null)
                return;

            for (int i = 0; i < counts.Length; i++)
                w.Write(counts[i]);
        }

        static int[] ReadActorDeathCounts(BinaryReader r)
        {
            int count = ReadCount(r, "actor death count");
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = r.ReadInt32();
            return result;
        }

        static void WriteTimePayload(BinaryWriter w, in MorrowindTimeSavePayload value)
        {
            w.Write(value.GameHour);
            w.Write(value.DaysPassed);
            w.Write(value.Day);
            w.Write(value.Month);
            w.Write(value.Year);
            w.Write(value.TimeScale);
            w.Write(value.SimulationTimeScale);
        }

        static MorrowindTimeSavePayload ReadTimePayload(BinaryReader r)
        {
            return new MorrowindTimeSavePayload
            {
                GameHour = r.ReadSingle(),
                DaysPassed = r.ReadInt32(),
                Day = r.ReadInt32(),
                Month = r.ReadInt32(),
                Year = r.ReadInt32(),
                TimeScale = r.ReadSingle(),
                SimulationTimeScale = r.ReadSingle(),
            };
        }

        static void WriteWeatherPayload(BinaryWriter w, in MorrowindWeatherSavePayload value)
        {
            w.Write(value.CurrentWeather);
            w.Write(value.NextWeather);
            w.Write(value.QueuedWeather);
            w.Write(value.Transition);
            w.Write(value.TransitionFactor);
            w.Write(value.TransitionDelta);
            w.Write(value.HoursUntilNextChange);
            w.Write(value.WeatherUpdateHoursRemaining);
            w.Write(value.RegionHandleValue);
            w.Write(value.RandomState);
            w.Write(value.ForcedWeather);
            w.Write(value.SecondsUntilThunder);
            w.Write(value.LightningBrightness);
            w.Write(value.ThunderSequence);
            w.Write(value.LastThunderSoundIndex);
            w.Write(value.Initialized);
            w.Write(value.Transitioning);
            w.Write(value.RegionWeather?.Length ?? 0);
            if (value.RegionWeather != null)
            {
                for (int i = 0; i < value.RegionWeather.Length; i++)
                {
                    w.Write(value.RegionWeather[i].RegionHandleValue);
                    w.Write(value.RegionWeather[i].Weather);
                }
            }
        }

        static MorrowindWeatherSavePayload ReadWeatherPayload(BinaryReader r, int version)
        {
            if (version == WeatherPayloadVersion)
            {
                var legacy = new MorrowindWeatherSavePayload
                {
                    CurrentWeather = r.ReadInt32(),
                    NextWeather = r.ReadInt32(),
                    Transition = r.ReadSingle(),
                    TransitionDelta = r.ReadSingle(),
                    HoursUntilNextChange = r.ReadSingle(),
                    RegionHandleValue = r.ReadInt32(),
                    RandomState = r.ReadUInt32(),
                    ForcedWeather = r.ReadInt32(),
                    SecondsUntilThunder = r.ReadSingle(),
                    LightningBrightness = r.ReadSingle(),
                    ThunderSequence = r.ReadUInt32(),
                    LastThunderSoundIndex = r.ReadInt32(),
                    Initialized = r.ReadByte(),
                    Transitioning = r.ReadByte(),
                };
                legacy.QueuedWeather = -1;
                legacy.TransitionFactor = legacy.Transitioning != 0 ? 1f - legacy.Transition : 0f;
                legacy.WeatherUpdateHoursRemaining = legacy.HoursUntilNextChange;
                legacy.RegionWeather = System.Array.Empty<MorrowindRegionWeatherCacheSavePayload>();
                return legacy;
            }

            var payload = new MorrowindWeatherSavePayload
            {
                CurrentWeather = r.ReadInt32(),
                NextWeather = r.ReadInt32(),
                QueuedWeather = r.ReadInt32(),
                Transition = r.ReadSingle(),
                TransitionFactor = r.ReadSingle(),
                TransitionDelta = r.ReadSingle(),
                HoursUntilNextChange = r.ReadSingle(),
                WeatherUpdateHoursRemaining = r.ReadSingle(),
                RegionHandleValue = r.ReadInt32(),
                RandomState = r.ReadUInt32(),
                ForcedWeather = r.ReadInt32(),
                SecondsUntilThunder = r.ReadSingle(),
                LightningBrightness = r.ReadSingle(),
                ThunderSequence = r.ReadUInt32(),
                LastThunderSoundIndex = r.ReadInt32(),
                Initialized = r.ReadByte(),
                Transitioning = r.ReadByte(),
            };
            int count = ReadCount(r, "region weather cache");
            payload.RegionWeather = new MorrowindRegionWeatherCacheSavePayload[count];
            for (int i = 0; i < count; i++)
            {
                payload.RegionWeather[i] = new MorrowindRegionWeatherCacheSavePayload
                {
                    RegionHandleValue = r.ReadInt32(),
                    Weather = r.ReadInt32(),
                };
            }
            return payload;
        }

        static MorrowindTimeSavePayload ToSavePayload(MorrowindTimeState time)
        {
            return new MorrowindTimeSavePayload
            {
                GameHour = time.GameHour,
                DaysPassed = time.DaysPassed,
                Day = time.Day,
                Month = time.Month,
                Year = time.Year,
                TimeScale = time.TimeScale,
                SimulationTimeScale = time.SimulationTimeScale,
            };
        }

        static MorrowindWeatherSavePayload ToSavePayload(MorrowindWeatherState weather)
        {
            return new MorrowindWeatherSavePayload
            {
                CurrentWeather = weather.CurrentWeather,
                NextWeather = weather.NextWeather,
                QueuedWeather = weather.QueuedWeather,
                Transition = weather.Transition,
                TransitionFactor = weather.TransitionFactor,
                TransitionDelta = weather.TransitionDelta,
                HoursUntilNextChange = weather.HoursUntilNextChange,
                WeatherUpdateHoursRemaining = weather.WeatherUpdateHoursRemaining,
                RegionHandleValue = weather.RegionHandleValue,
                RandomState = weather.RandomState,
                ForcedWeather = weather.ForcedWeather,
                SecondsUntilThunder = weather.SecondsUntilThunder,
                LightningBrightness = weather.LightningBrightness,
                ThunderSequence = weather.ThunderSequence,
                LastThunderSoundIndex = weather.LastThunderSoundIndex,
                Initialized = weather.Initialized,
                Transitioning = weather.Transitioning,
                RegionWeather = System.Array.Empty<MorrowindRegionWeatherCacheSavePayload>(),
            };
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

        static void WritePlayerFaction(BinaryWriter w, in PlayerFactionMembership value)
        {
            w.Write(value.FactionIndex);
            w.Write(value.Rank);
            w.Write(value.Reputation);
            w.Write(value.Joined);
            w.Write(value.Expelled);
        }

        static PlayerFactionMembership ReadPlayerFaction(BinaryReader r, int version)
        {
            return new PlayerFactionMembership
            {
                FactionIndex = r.ReadInt32(),
                Rank = r.ReadInt32(),
                Reputation = r.ReadInt32(),
                Joined = version >= PlayerFactionJoinedPayloadVersion ? r.ReadByte() : (byte)1,
                Expelled = r.ReadByte(),
            };
        }

        static void WriteQuestJournalPayload(BinaryWriter w, in MorrowindQuestJournalSavePayload value)
        {
            w.Write(value.NextEntrySequence);
            w.Write(value.States?.Length ?? 0);
            if (value.States != null)
            {
                for (int i = 0; i < value.States.Length; i++)
                {
                    w.Write(value.States[i].DialogueIndex);
                    w.Write(value.States[i].Index);
                    w.Write(value.States[i].Started);
                    w.Write(value.States[i].Finished);
                }
            }

            w.Write(value.Entries?.Length ?? 0);
            if (value.Entries != null)
            {
                for (int i = 0; i < value.Entries.Length; i++)
                {
                    w.Write(value.Entries[i].Sequence);
                    w.Write(value.Entries[i].DialogueIndex);
                    w.Write(value.Entries[i].InfoIndex);
                    w.Write(value.Entries[i].JournalIndex);
                    w.Write(value.Entries[i].Day);
                    w.Write(value.Entries[i].Month);
                    w.Write(value.Entries[i].DayOfMonth);
                    w.Write(value.Entries[i].QuestStatus);
                }
            }
        }

        static void WriteDialoguePayload(BinaryWriter w, in MorrowindDialogueSavePayload value)
        {
            w.Write(value.NextTopicEntrySequence);
            w.Write(value.KnownTopicDialogueIndices?.Length ?? 0);
            if (value.KnownTopicDialogueIndices != null)
            {
                for (int i = 0; i < value.KnownTopicDialogueIndices.Length; i++)
                    w.Write(value.KnownTopicDialogueIndices[i]);
            }

            w.Write(value.TopicEntries?.Length ?? 0);
            if (value.TopicEntries != null)
            {
                for (int i = 0; i < value.TopicEntries.Length; i++)
                {
                    w.Write(value.TopicEntries[i].Sequence);
                    w.Write(value.TopicEntries[i].DialogueIndex);
                    w.Write(value.TopicEntries[i].InfoIndex);
                    w.Write(value.TopicEntries[i].ActorPlacedRefId);
                    w.Write(value.TopicEntries[i].ActorId ?? string.Empty);
                    w.Write(value.TopicEntries[i].Day);
                    w.Write(value.TopicEntries[i].Month);
                    w.Write(value.TopicEntries[i].DayOfMonth);
                }
            }

            w.Write(value.FactionReactions?.Length ?? 0);
            if (value.FactionReactions != null)
            {
                for (int i = 0; i < value.FactionReactions.Length; i++)
                {
                    w.Write(value.FactionReactions[i].SourceFactionIndex);
                    w.Write(value.FactionReactions[i].TargetFactionIndex);
                    w.Write(value.FactionReactions[i].Reaction);
                }
            }
        }

        static MorrowindQuestJournalSavePayload ReadQuestJournalPayload(BinaryReader r, int version)
        {
            var payload = new MorrowindQuestJournalSavePayload
            {
                NextEntrySequence = r.ReadUInt32(),
            };

            int stateCount = ReadCount(r, "quest journal state");
            payload.States = new MorrowindQuestJournalStateSavePayload[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                payload.States[i] = new MorrowindQuestJournalStateSavePayload
                {
                    DialogueIndex = r.ReadInt32(),
                    Index = r.ReadInt32(),
                    Started = r.ReadByte(),
                    Finished = r.ReadByte(),
                };
            }

            int entryCount = ReadCount(r, "quest journal entry");
            payload.Entries = new MorrowindQuestJournalEntrySavePayload[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                payload.Entries[i] = new MorrowindQuestJournalEntrySavePayload
                {
                    Sequence = r.ReadUInt32(),
                    DialogueIndex = r.ReadInt32(),
                    InfoIndex = r.ReadInt32(),
                    JournalIndex = r.ReadInt32(),
                    Day = version >= QuestJournalDatePayloadVersion ? r.ReadInt32() : 0,
                    Month = version >= QuestJournalDatePayloadVersion ? r.ReadInt32() : 0,
                    DayOfMonth = version >= QuestJournalDatePayloadVersion ? r.ReadInt32() : 0,
                    QuestStatus = r.ReadByte(),
                };
            }

            return payload;
        }

        static MorrowindDialogueSavePayload ReadDialoguePayload(BinaryReader r, int version)
        {
            var payload = new MorrowindDialogueSavePayload
            {
                NextTopicEntrySequence = r.ReadUInt32(),
            };

            int knownCount = ReadCount(r, "known dialogue topic");
            payload.KnownTopicDialogueIndices = new int[knownCount];
            for (int i = 0; i < knownCount; i++)
                payload.KnownTopicDialogueIndices[i] = r.ReadInt32();

            int entryCount = ReadCount(r, "dialogue topic journal entry");
            payload.TopicEntries = new MorrowindTopicJournalEntrySavePayload[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                payload.TopicEntries[i] = new MorrowindTopicJournalEntrySavePayload
                {
                    Sequence = r.ReadUInt32(),
                    DialogueIndex = r.ReadInt32(),
                    InfoIndex = r.ReadInt32(),
                    ActorPlacedRefId = r.ReadUInt32(),
                    ActorId = r.ReadString(),
                    Day = r.ReadInt32(),
                    Month = r.ReadInt32(),
                    DayOfMonth = r.ReadInt32(),
                };
            }

            if (version >= DialogueFactionReactionPayloadVersion)
            {
                int factionReactionCount = ReadCount(r, "dialogue faction reaction");
                payload.FactionReactions = new MorrowindFactionReactionSavePayload[factionReactionCount];
                for (int i = 0; i < factionReactionCount; i++)
                {
                    payload.FactionReactions[i] = new MorrowindFactionReactionSavePayload
                    {
                        SourceFactionIndex = r.ReadInt32(),
                        TargetFactionIndex = r.ReadInt32(),
                        Reaction = r.ReadInt32(),
                    };
                }
            }
            else
            {
                payload.FactionReactions = Array.Empty<MorrowindFactionReactionSavePayload>();
            }

            return payload;
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
            uint sequence = r.ReadUInt32();
            byte kind = r.ReadByte();
            uint placedRefId = r.ReadUInt32();
            uint runtimeRefId = r.ReadUInt32();
            ContentReference content = ReadContentReference(r);
            int deltaCount = r.ReadInt32();
            float3 position = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            quaternion rotation = new quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float scale = r.ReadSingle();
            int2 exteriorCell = new int2(r.ReadInt32(), r.ReadInt32());
            FixedString128Bytes interiorCellId = RuntimeFixedStringUtility.ToFixed128OrDefaultWhiteSpace(r.ReadString());
            return new WorldJournalEntry
            {
                Sequence = sequence,
                Kind = kind,
                PlacedRefId = placedRefId,
                RuntimeRefId = runtimeRefId,
                Content = content,
                DeltaCount = deltaCount,
                Position = position,
                Rotation = rotation,
                Scale = scale,
                ExteriorCell = exteriorCell,
                InteriorCellId = interiorCellId,
                InteriorCellHash = InteriorCellIdHash.Hash(interiorCellId),
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
                CharacterName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
                Level = r.ReadInt32(),
                RaceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
                ClassName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
                BirthSignName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
                Reputation = r.ReadInt32(),
            };
        }

        static void WritePlayerCrime(BinaryWriter w, in PlayerCrimeState value)
            => w.Write(value.Bounty);

        static PlayerCrimeState ReadPlayerCrime(BinaryReader r)
        {
            return new PlayerCrimeState
            {
                Bounty = Math.Max(0, r.ReadInt32()),
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
                SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
                SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(r.ReadString()),
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

        static int ReadCount(BinaryReader r, string label)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"invalid {label} count {count}");
            return count;
        }
    }
}
