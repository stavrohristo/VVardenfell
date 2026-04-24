using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;

namespace VVardenfell.Runtime.WorldState
{
    public static partial class WorldSaveStorage
    {
        const uint SlotMagic = 0x53535656u; // VVSS
        const int SlotVersion = 1;
        const string FileName = "continue_save.bin";
        const string SaveExtension = ".vvsave";
        const string LegacyContinueSlotId = "legacy:continue";

        public static string ContinueSavePath => Path.Combine(Application.persistentDataPath, FileName);
        public static string SavesDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        public static bool TryGetContinueAvailability(out string error)
        {
            if (!TryGetLatestSlot(out _, out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "No serialized save payload is available.";
                return false;
            }

            error = null;
            return true;
        }

        public static bool TryWriteContinueSave(EntityManager entityManager, out string error)
            => TryWriteNewSlot(entityManager, BuildDefaultSaveName(), out _, out error);

        public static bool TryWriteNewSlot(EntityManager entityManager, string displayName, out SaveGameSlotSummary summary, out string error)
        {
            summary = default;
            error = null;
            if (!TryBuildPayload(entityManager, out WorldSavePayload payload, out error))
                return false;

            long now = DateTime.UtcNow.Ticks;
            var metadata = BuildMetadata(
                string.Empty,
                string.IsNullOrWhiteSpace(displayName) ? BuildDefaultSaveName() : displayName.Trim(),
                payload,
                now,
                now);
            metadata.SlotId = BuildSlotId(metadata.DisplayName, metadata.LastModifiedUtcTicks);
            string path = Path.Combine(SavesDirectory, metadata.SlotId + SaveExtension);
            return TryWriteSlot(path, metadata, payload, out summary, out error);
        }

        public static bool TryOverwriteSlot(EntityManager entityManager, string slotId, string displayName, out SaveGameSlotSummary summary, out string error)
        {
            summary = default;
            error = null;
            if (string.IsNullOrWhiteSpace(slotId) || string.Equals(slotId, LegacyContinueSlotId, StringComparison.OrdinalIgnoreCase))
            {
                error = "Select a named save slot before overwriting.";
                return false;
            }

            if (!TryBuildPayload(entityManager, out WorldSavePayload payload, out error))
                return false;

            string path = SlotIdToPath(slotId);
            long createdTicks = File.Exists(path) ? File.GetCreationTimeUtc(path).Ticks : DateTime.UtcNow.Ticks;
            long modifiedTicks = DateTime.UtcNow.Ticks;
            var metadata = BuildMetadata(
                slotId,
                string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(path) : displayName.Trim(),
                payload,
                createdTicks,
                modifiedTicks);
            return TryWriteSlot(path, metadata, payload, out summary, out error);
        }

        public static bool TryDeleteSlot(string slotId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                error = "No save slot is selected.";
                return false;
            }

            if (string.Equals(slotId, LegacyContinueSlotId, StringComparison.OrdinalIgnoreCase))
            {
                error = "The legacy continue payload cannot be deleted from the save browser.";
                return false;
            }

            try
            {
                string path = SlotIdToPath(slotId);
                if (!File.Exists(path))
                {
                    error = "Save slot no longer exists.";
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed deleting save slot: {ex.Message}";
                return false;
            }
        }

        public static bool TryLoadContinueSave(out WorldSavePayload payload, out string error)
        {
            payload = default;
            if (!TryGetLatestSlot(out var summary, out error))
                return false;

            return TryLoadSlot(summary.SlotId, out payload, out error);
        }

        public static bool TryLoadSlot(string slotId, out WorldSavePayload payload, out string error)
        {
            payload = default;
            error = null;

            try
            {
                string path = string.Equals(slotId, LegacyContinueSlotId, StringComparison.OrdinalIgnoreCase)
                    ? ContinueSavePath
                    : SlotIdToPath(slotId);
                if (!File.Exists(path))
                {
                    error = "No serialized save payload is available.";
                    return false;
                }

                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                payload = ReadPayloadFromAnySave(fs, out _);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Save slot unreadable: {ex.Message}";
                return false;
            }
        }

        public static bool TryGetLatestSlot(out SaveGameSlotSummary summary, out string error)
        {
            summary = default;
            error = null;
            var slots = EnumerateSlots();
            SaveGameSlotSummary best = default;
            bool found = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsValid)
                    continue;

                if (!found || slots[i].LastModifiedUtcTicks > best.LastModifiedUtcTicks)
                {
                    best = slots[i];
                    found = true;
                }
            }

            if (!found)
            {
                error = "No serialized save payload is available.";
                return false;
            }

            summary = best;
            return true;
        }

        public static SaveGameSlotSummary[] EnumerateSlots()
        {
            var result = new List<SaveGameSlotSummary>();
            if (Directory.Exists(SavesDirectory))
            {
                string[] files = Directory.GetFiles(SavesDirectory, "*" + SaveExtension, SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                    result.Add(ReadSlotSummary(files[i], isLegacy: false));
            }

            if (File.Exists(ContinueSavePath))
                result.Add(ReadSlotSummary(ContinueSavePath, isLegacy: true));

            result.Sort((a, b) => b.LastModifiedUtcTicks.CompareTo(a.LastModifiedUtcTicks));
            return result.ToArray();
        }

        static bool TryWriteSlot(string path, SaveGameSlotMetadata metadata, in WorldSavePayload payload, out SaveGameSlotSummary summary, out string error)
        {
            summary = default;
            error = null;
            string tempPath = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? SavesDirectory);

                byte[] bytes;
                using (var memory = new MemoryStream())
                using (var w = new BinaryWriter(memory))
                {
                    w.Write(SlotMagic);
                    w.Write(SlotVersion);
                    WriteMetadata(w, metadata);
                    WritePayload(w, payload);
                    w.Flush();
                    bytes = memory.ToArray();
                }

                tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tempPath, bytes);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                        tempPath = null;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                        tempPath = null;
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                    tempPath = null;
                }

                File.SetCreationTimeUtc(path, new DateTime(metadata.CreatedUtcTicks, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(path, new DateTime(metadata.LastModifiedUtcTicks, DateTimeKind.Utc));
                summary = ToSummary(metadata, path, isLegacy: false, isValid: true, error: null);
                return true;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }

                error = $"Failed writing save slot: {ex.Message}";
                return false;
            }
        }

        static SaveGameSlotSummary ReadSlotSummary(string path, bool isLegacy)
        {
            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                WorldSavePayload payload = default;
                SaveGameSlotMetadata metadata = default;
                if (isLegacy)
                {
                    payload = ReadPayloadFromAnySave(fs, out metadata);
                }
                else if (!TryReadSlotMetadataSummary(fs, out metadata, out string metadataError))
                {
                    throw new InvalidDataException(metadataError);
                }

                if (string.IsNullOrWhiteSpace(metadata.SlotId))
                {
                    long modified = File.GetLastWriteTimeUtc(path).Ticks;
                    if (!isLegacy && fs.CanSeek)
                    {
                        fs.Position = 0L;
                        payload = ReadPayloadFromAnySave(fs, out _);
                    }

                    metadata = BuildMetadata(
                        isLegacy ? LegacyContinueSlotId : Path.GetFileNameWithoutExtension(path),
                        isLegacy ? "Legacy Continue" : Path.GetFileNameWithoutExtension(path),
                        payload,
                        File.GetCreationTimeUtc(path).Ticks,
                        modified);
                }

                return ToSummary(metadata, path, isLegacy, isValid: true, error: null);
            }
            catch (Exception ex)
            {
                return new SaveGameSlotSummary
                {
                    SlotId = isLegacy ? LegacyContinueSlotId : Path.GetFileNameWithoutExtension(path),
                    DisplayName = isLegacy ? "Legacy Continue" : Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    LastModifiedUtcTicks = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L,
                    IsLegacy = isLegacy,
                    IsValid = false,
                    Error = ex.Message,
                };
            }
        }

        static bool TryReadSlotMetadataSummary(Stream stream, out SaveGameSlotMetadata metadata, out string error)
        {
            metadata = default;
            error = null;
            using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            long start = stream.Position;
            try
            {
                uint magic = r.ReadUInt32();
                if (magic == SlotMagic)
                {
                    int slotVersion = r.ReadInt32();
                    if (slotVersion != SlotVersion)
                    {
                        error = $"unsupported save slot version {slotVersion}";
                        return false;
                    }

                    metadata = ReadMetadata(r);
                    ValidatePayloadHeader(r);
                    if (string.IsNullOrWhiteSpace(metadata.SlotId))
                        metadata.SlotId = Path.GetFileNameWithoutExtension(stream is FileStream fileStream ? fileStream.Name : string.Empty);
                    return true;
                }

                if (magic == PayloadMagic)
                {
                    stream.Position = start;
                    return true;
                }

                error = "unexpected save slot magic";
                return false;
            }
            catch (EndOfStreamException)
            {
                error = "save slot is truncated";
                return false;
            }
        }

        static SaveGameSlotMetadata BuildMetadata(
            string slotId,
            string displayName,
            in WorldSavePayload payload,
            long createdTicks,
            long modifiedTicks)
        {
            string cell = payload.InteriorActive
                ? payload.ActiveInteriorCellId ?? string.Empty
                : FormatExteriorCell(payload.PlayerPosition);
            string location = string.IsNullOrWhiteSpace(cell)
                ? "Unknown"
                : cell;
            return new SaveGameSlotMetadata
            {
                SlotId = slotId ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Save" : displayName.Trim(),
                CreatedUtcTicks = createdTicks,
                LastModifiedUtcTicks = modifiedTicks,
                CharacterName = payload.PlayerIdentity.CharacterName.ToString(),
                PlayerLevel = Math.Max(1, payload.PlayerIdentity.Level),
                LocationName = location,
                CellName = cell,
                PayloadVersion = PayloadVersion,
            };
        }

        static SaveGameSlotSummary ToSummary(SaveGameSlotMetadata metadata, string path, bool isLegacy, bool isValid, string error)
        {
            return new SaveGameSlotSummary
            {
                SlotId = isLegacy ? LegacyContinueSlotId : metadata.SlotId,
                DisplayName = string.IsNullOrWhiteSpace(metadata.DisplayName) ? "Save" : metadata.DisplayName,
                FilePath = path,
                CreatedUtcTicks = metadata.CreatedUtcTicks,
                LastModifiedUtcTicks = metadata.LastModifiedUtcTicks,
                CharacterName = string.IsNullOrWhiteSpace(metadata.CharacterName) ? "Player" : metadata.CharacterName,
                PlayerLevel = Math.Max(1, metadata.PlayerLevel),
                LocationName = metadata.LocationName ?? string.Empty,
                CellName = metadata.CellName ?? string.Empty,
                PayloadVersion = metadata.PayloadVersion,
                IsLegacy = isLegacy,
                IsValid = isValid,
                Error = error ?? string.Empty,
            };
        }

        static void WriteMetadata(BinaryWriter w, SaveGameSlotMetadata value)
        {
            w.Write(value.SlotId ?? string.Empty);
            w.Write(value.DisplayName ?? string.Empty);
            w.Write(value.CreatedUtcTicks);
            w.Write(value.LastModifiedUtcTicks);
            w.Write(value.CharacterName ?? string.Empty);
            w.Write(value.PlayerLevel);
            w.Write(value.LocationName ?? string.Empty);
            w.Write(value.CellName ?? string.Empty);
            w.Write(value.PayloadVersion);
        }

        static SaveGameSlotMetadata ReadMetadata(BinaryReader r)
        {
            return new SaveGameSlotMetadata
            {
                SlotId = r.ReadString(),
                DisplayName = r.ReadString(),
                CreatedUtcTicks = r.ReadInt64(),
                LastModifiedUtcTicks = r.ReadInt64(),
                CharacterName = r.ReadString(),
                PlayerLevel = r.ReadInt32(),
                LocationName = r.ReadString(),
                CellName = r.ReadString(),
                PayloadVersion = r.ReadInt32(),
            };
        }

        static string SlotIdToPath(string slotId)
        {
            string safe = SanitizeFileStem(slotId);
            return Path.Combine(SavesDirectory, safe + SaveExtension);
        }

        static string BuildSlotId(string displayName, long ticks)
            => $"{SanitizeFileStem(displayName)}-{ticks}";

        static string SanitizeFileStem(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "save";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c))
                    chars[i] = '-';
            }

            string safe = new string(chars).Trim('-');
            if (safe.Length > 48)
                safe = safe.Substring(0, 48).Trim('-');
            return string.IsNullOrWhiteSpace(safe) ? "save" : safe;
        }

        static string BuildDefaultSaveName()
            => $"Save {DateTime.Now:yyyy-MM-dd HH-mm-ss}";

        static string FormatExteriorCell(float3 position)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            if (cellMeters <= 0f)
                return "Exterior cell 0, 0";

            int x = (int)math.floor(position.x / cellMeters);
            int y = (int)math.floor(position.z / cellMeters);
            return $"Exterior cell {x}, {y}";
        }
    }
}
