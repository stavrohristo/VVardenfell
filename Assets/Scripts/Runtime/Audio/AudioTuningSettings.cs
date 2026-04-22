using System.IO;
using System.Text;
using UnityEngine;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Audio
{
    [CreateAssetMenu(menuName = "VVardenfell/Audio Tuning Settings", fileName = "VVAudioTuningSettings")]
    public sealed class AudioTuningSettings : ScriptableObject
    {
        public enum LoadSource
        {
            ResourcesAsset = 0,
            EditorAsset = 1,
            RuntimeDefaults = 2,
        }

        [System.Serializable]
        public sealed class MusicSection
        {
            [Range(0f, 1f)] public float GlobalVolume = 0.3f;
            [Min(0f)] public float MenuSpecialScalar = 1f;
            [Min(0f)] public float ExploreScalar = 1f;
            [Min(0f)] public float BattleScalar = 1f;
        }

        [System.Serializable]
        public sealed class InteriorAmbienceSection
        {
            [Min(0f)] public float VolumeMultiplier = 1f;
            [Range(0f, 1f)] public float FallbackBaseVolume = 1f;
            [Min(0f)] public float MinDistanceMultiplier = 1f;
            [Min(0f)] public float MaxDistanceMultiplier = 1f;
        }

        [System.Serializable]
        public sealed class ExteriorAmbienceSection
        {
            [Min(0f)] public float VolumeMultiplier = 1f;
            [Range(0f, 1f)] public float FallbackBaseVolume = 0.75f;
            [Min(0f)] public float MinIntervalMultiplier = 1f;
            [Min(0f)] public float MaxIntervalMultiplier = 1f;
            [Min(0.05f)] public float FallbackMinSeconds = 1f;
            [Min(0.05f)] public float FallbackMaxSeconds = 5f;
        }

        [System.Serializable]
        public sealed class InteractionSfxSection
        {
            [Min(0f)] public float VolumeMultiplier = 1f;
            [Range(0f, 1f)] public float FallbackBaseVolume = 1f;
            [Min(0f)] public float MinDistanceMultiplier = 1f;
            [Min(0f)] public float MaxDistanceMultiplier = 1f;
        }

        public const string AssetPath = "Assets/Resources/VVAudioTuningSettings.asset";
        public const string ResourcesPath = "VVAudioTuningSettings";

        public MusicSection Music = new();
        public InteriorAmbienceSection InteriorAmbience = new();
        public ExteriorAmbienceSection ExteriorAmbience = new();
        public InteractionSfxSection InteractionSfx = new();

        public AudioTuningState BuildRuntimeState()
        {
            Validate();
            return new AudioTuningState
            {
                MusicGlobalVolume = Music.GlobalVolume,
                MusicMenuSpecialScalar = Music.MenuSpecialScalar,
                MusicExploreScalar = Music.ExploreScalar,
                MusicBattleScalar = Music.BattleScalar,
                InteriorAmbientVolumeMultiplier = InteriorAmbience.VolumeMultiplier,
                InteriorAmbientFallbackBaseVolume = InteriorAmbience.FallbackBaseVolume,
                InteriorAmbientMinDistanceMultiplier = InteriorAmbience.MinDistanceMultiplier,
                InteriorAmbientMaxDistanceMultiplier = InteriorAmbience.MaxDistanceMultiplier,
                ExteriorAmbientVolumeMultiplier = ExteriorAmbience.VolumeMultiplier,
                ExteriorAmbientFallbackBaseVolume = ExteriorAmbience.FallbackBaseVolume,
                ExteriorAmbientMinIntervalMultiplier = ExteriorAmbience.MinIntervalMultiplier,
                ExteriorAmbientMaxIntervalMultiplier = ExteriorAmbience.MaxIntervalMultiplier,
                ExteriorAmbientFallbackMinSeconds = ExteriorAmbience.FallbackMinSeconds,
                ExteriorAmbientFallbackMaxSeconds = ExteriorAmbience.FallbackMaxSeconds,
                InteractionVolumeMultiplier = InteractionSfx.VolumeMultiplier,
                InteractionFallbackBaseVolume = InteractionSfx.FallbackBaseVolume,
                InteractionMinDistanceMultiplier = InteractionSfx.MinDistanceMultiplier,
                InteractionMaxDistanceMultiplier = InteractionSfx.MaxDistanceMultiplier,
            };
        }

        public static AudioTuningSettings LoadRuntimeOrDefault()
        {
            return LoadRuntimeOrDefault(out _);
        }

        public static AudioTuningSettings LoadRuntimeOrDefault(out LoadSource loadSource)
        {
            var settings = Resources.Load<AudioTuningSettings>(ResourcesPath);
            loadSource = settings != null ? LoadSource.ResourcesAsset : LoadSource.RuntimeDefaults;
#if UNITY_EDITOR
            if (settings == null)
            {
                settings = LoadOrCreate();
                if (settings != null)
                    loadSource = LoadSource.EditorAsset;
            }
#endif
            if (settings == null)
            {
                settings = CreateInstance<AudioTuningSettings>();
                settings.ResetToDefaults();
                loadSource = LoadSource.RuntimeDefaults;
            }

            settings.Validate();
            return settings;
        }

        public static string Describe(in AudioTuningState state, LoadSource loadSource = LoadSource.ResourcesAsset)
        {
            var builder = new StringBuilder();
            builder.Append("[VVardenfell][Audio] tuning profile resolved")
                .Append(" (source=")
                .Append(Describe(loadSource))
                .Append(')');
            builder.Append("\n  Music: global=").Append(state.MusicGlobalVolume.ToString("0.###"))
                .Append(", menu=").Append(state.MusicMenuSpecialScalar.ToString("0.###"))
                .Append(", explore=").Append(state.MusicExploreScalar.ToString("0.###"))
                .Append(", battle=").Append(state.MusicBattleScalar.ToString("0.###"));
            builder.Append("\n  InteriorAmbience: volume=").Append(state.InteriorAmbientVolumeMultiplier.ToString("0.###"))
                .Append(", fallback=").Append(state.InteriorAmbientFallbackBaseVolume.ToString("0.###"))
                .Append(", minRange=").Append(state.InteriorAmbientMinDistanceMultiplier.ToString("0.###"))
                .Append(", maxRange=").Append(state.InteriorAmbientMaxDistanceMultiplier.ToString("0.###"));
            builder.Append("\n  ExteriorAmbience: volume=").Append(state.ExteriorAmbientVolumeMultiplier.ToString("0.###"))
                .Append(", fallback=").Append(state.ExteriorAmbientFallbackBaseVolume.ToString("0.###"))
                .Append(", minInterval=").Append(state.ExteriorAmbientMinIntervalMultiplier.ToString("0.###"))
                .Append(", maxInterval=").Append(state.ExteriorAmbientMaxIntervalMultiplier.ToString("0.###"))
                .Append(", fallbackMinSeconds=").Append(state.ExteriorAmbientFallbackMinSeconds.ToString("0.###"))
                .Append(", fallbackMaxSeconds=").Append(state.ExteriorAmbientFallbackMaxSeconds.ToString("0.###"));
            builder.Append("\n  InteractionSfx: volume=").Append(state.InteractionVolumeMultiplier.ToString("0.###"))
                .Append(", fallback=").Append(state.InteractionFallbackBaseVolume.ToString("0.###"))
                .Append(", minRange=").Append(state.InteractionMinDistanceMultiplier.ToString("0.###"))
                .Append(", maxRange=").Append(state.InteractionMaxDistanceMultiplier.ToString("0.###"));
            return builder.ToString();
        }

        static string Describe(LoadSource loadSource)
        {
            return loadSource switch
            {
                LoadSource.ResourcesAsset => $"Resources:{ResourcesPath}",
                LoadSource.EditorAsset => AssetPath,
                LoadSource.RuntimeDefaults => "runtime-defaults",
                _ => "unknown",
            };
        }

        void Reset()
        {
            ResetToDefaults();
        }

        void OnValidate()
        {
            Validate();
        }

        void ResetToDefaults()
        {
            Music = new MusicSection();
            InteriorAmbience = new InteriorAmbienceSection();
            ExteriorAmbience = new ExteriorAmbienceSection();
            InteractionSfx = new InteractionSfxSection();
        }

        void Validate()
        {
            Music ??= new MusicSection();
            InteriorAmbience ??= new InteriorAmbienceSection();
            ExteriorAmbience ??= new ExteriorAmbienceSection();
            InteractionSfx ??= new InteractionSfxSection();

            Music.GlobalVolume = Mathf.Clamp01(Music.GlobalVolume);
            Music.MenuSpecialScalar = Mathf.Max(0f, Music.MenuSpecialScalar);
            Music.ExploreScalar = Mathf.Max(0f, Music.ExploreScalar);
            Music.BattleScalar = Mathf.Max(0f, Music.BattleScalar);

            InteriorAmbience.VolumeMultiplier = Mathf.Max(0f, InteriorAmbience.VolumeMultiplier);
            InteriorAmbience.FallbackBaseVolume = Mathf.Clamp01(InteriorAmbience.FallbackBaseVolume);
            InteriorAmbience.MinDistanceMultiplier = Mathf.Max(0f, InteriorAmbience.MinDistanceMultiplier);
            InteriorAmbience.MaxDistanceMultiplier = Mathf.Max(0f, InteriorAmbience.MaxDistanceMultiplier);

            ExteriorAmbience.VolumeMultiplier = Mathf.Max(0f, ExteriorAmbience.VolumeMultiplier);
            ExteriorAmbience.FallbackBaseVolume = Mathf.Clamp01(ExteriorAmbience.FallbackBaseVolume);
            ExteriorAmbience.MinIntervalMultiplier = Mathf.Max(0f, ExteriorAmbience.MinIntervalMultiplier);
            ExteriorAmbience.MaxIntervalMultiplier = Mathf.Max(0f, ExteriorAmbience.MaxIntervalMultiplier);
            ExteriorAmbience.FallbackMinSeconds = Mathf.Max(0.05f, ExteriorAmbience.FallbackMinSeconds);
            ExteriorAmbience.FallbackMaxSeconds = Mathf.Max(ExteriorAmbience.FallbackMinSeconds, ExteriorAmbience.FallbackMaxSeconds);

            InteractionSfx.VolumeMultiplier = Mathf.Max(0f, InteractionSfx.VolumeMultiplier);
            InteractionSfx.FallbackBaseVolume = Mathf.Clamp01(InteractionSfx.FallbackBaseVolume);
            InteractionSfx.MinDistanceMultiplier = Mathf.Max(0f, InteractionSfx.MinDistanceMultiplier);
            InteractionSfx.MaxDistanceMultiplier = Mathf.Max(0f, InteractionSfx.MaxDistanceMultiplier);
        }

#if UNITY_EDITOR
        public static AudioTuningSettings LoadOrCreate()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioTuningSettings>(AssetPath);
            if (settings != null)
            {
                settings.Validate();
                return settings;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath) ?? "Assets/Resources");
            settings = CreateInstance<AudioTuningSettings>();
            settings.ResetToDefaults();
            UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
            UnityEditor.EditorUtility.SetDirty(settings);
            UnityEditor.AssetDatabase.SaveAssets();
            return settings;
        }
#endif
    }
}
