using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public sealed class GameplayContentManifest
    {
        public sealed class SourceState
        {
            public string Path;
            public long Size;
            public long MtimeTicks;
        }

        public uint FormatVersion;
        public uint GameplayContentVersion;
        public SourceState[] Sources;
        public int ActorCount;
        public int ActivatorCount;
        public int DoorCount;
        public int ContainerCount;
        public int ItemCount;
        public int LightCount;
        public int SoundCount;
        public int DialogueCount;
        public int DialogueInfoCount;
        public int SpellCount;
        public int EnchantmentCount;
        public int MagicEffectCount;
        public int MagicEffectInstanceCount;
        public int RegionCount;
        public int RegionSoundRefCount;
        public int MusicTrackCount;
        public int AmbientSettingsCount;

        public static GameplayContentManifest FromSources(string[] sourcePaths)
        {
            var sources = sourcePaths ?? Array.Empty<string>();
            var states = new SourceState[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var info = new FileInfo(sources[i]);
                states[i] = new SourceState
                {
                    Path = sources[i],
                    Size = info.Exists ? info.Length : 0L,
                    MtimeTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0L,
                };
            }

            return new GameplayContentManifest
            {
                FormatVersion = CacheFormat.FormatVersion,
                GameplayContentVersion = CacheFormat.GameplayContentVersion,
                Sources = states,
            };
        }

        public bool SourcesMatch(string[] sourcePaths)
        {
            if (FormatVersion != CacheFormat.FormatVersion || GameplayContentVersion != CacheFormat.GameplayContentVersion)
                return false;

            var sources = sourcePaths ?? Array.Empty<string>();
            var states = Sources ?? Array.Empty<SourceState>();
            if (sources.Length != states.Length)
                return false;

            for (int i = 0; i < sources.Length; i++)
            {
                if (!File.Exists(sources[i]))
                    return false;

                var info = new FileInfo(sources[i]);
                var state = states[i];
                if (!string.Equals(state.Path, sources[i], StringComparison.OrdinalIgnoreCase) ||
                    state.Size != info.Length ||
                    state.MtimeTicks != info.LastWriteTimeUtc.Ticks)
                {
                    return false;
                }
            }

            return true;
        }

        public void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(CacheFormat.Magic);
            w.Write(FormatVersion);
            w.Write(GameplayContentVersion);

            int sourceCount = Sources?.Length ?? 0;
            w.Write(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                var source = Sources[i];
                w.Write(source?.Path ?? string.Empty);
                w.Write(source?.Size ?? 0L);
                w.Write(source?.MtimeTicks ?? 0L);
            }

            w.Write(ActorCount);
            w.Write(ActivatorCount);
            w.Write(DoorCount);
            w.Write(ContainerCount);
            w.Write(ItemCount);
            w.Write(LightCount);
            w.Write(SoundCount);
            w.Write(DialogueCount);
            w.Write(DialogueInfoCount);
            w.Write(SpellCount);
            w.Write(EnchantmentCount);
            w.Write(MagicEffectCount);
            w.Write(MagicEffectInstanceCount);
            w.Write(RegionCount);
            w.Write(RegionSoundRefCount);
            w.Write(MusicTrackCount);
            w.Write(AmbientSettingsCount);
        }

        public static bool TryRead(string path, out GameplayContentManifest manifest)
        {
            manifest = null;
            if (!File.Exists(path))
                return false;

            try
            {
                using var fs = File.OpenRead(path);
                using var r = new BinaryReader(fs);
                if (r.ReadUInt32() != CacheFormat.Magic)
                    return false;

                manifest = new GameplayContentManifest
                {
                    FormatVersion = r.ReadUInt32(),
                    GameplayContentVersion = r.ReadUInt32(),
                };

                int sourceCount = r.ReadInt32();
                manifest.Sources = new SourceState[sourceCount];
                for (int i = 0; i < sourceCount; i++)
                {
                    manifest.Sources[i] = new SourceState
                    {
                        Path = r.ReadString(),
                        Size = r.ReadInt64(),
                        MtimeTicks = r.ReadInt64(),
                    };
                }

                manifest.ActorCount = r.ReadInt32();
                manifest.ActivatorCount = r.ReadInt32();
                manifest.DoorCount = r.ReadInt32();
                manifest.ContainerCount = r.ReadInt32();
                manifest.ItemCount = r.ReadInt32();
                manifest.LightCount = r.ReadInt32();
                manifest.SoundCount = r.ReadInt32();
                manifest.DialogueCount = r.ReadInt32();
                manifest.DialogueInfoCount = r.ReadInt32();
                manifest.SpellCount = r.ReadInt32();
                manifest.EnchantmentCount = r.ReadInt32();
                manifest.MagicEffectCount = r.ReadInt32();
                manifest.MagicEffectInstanceCount = r.ReadInt32();
                manifest.RegionCount = r.ReadInt32();
                manifest.RegionSoundRefCount = r.ReadInt32();
                manifest.MusicTrackCount = r.ReadInt32();
                manifest.AmbientSettingsCount = r.ReadInt32();
                return true;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }
    }
}
