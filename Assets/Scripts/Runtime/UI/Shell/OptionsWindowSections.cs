using System.Collections.Generic;
using UnityEngine;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed partial class OptionsWindowView
    {
        void BuildPreferencesTab(RectTransform root)
        {
            float y = 0f;
            _transparencySlider = CreateRowSlider(root, y, "Menu Transparency", 0.3f, 1f, _config.MenuTransparency,
                formatter: v => $"{v:0.00}",
                onChanged: v =>
                {
                    _config.MenuTransparency = v;
                    _callbacks.MenuTransparency?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _difficultySlider = CreateRowSlider(root, y, "Difficulty", -100f, 100f, _config.Difficulty,
                formatter: v => Mathf.RoundToInt(v).ToString(),
                wholeNumbers: true,
                onChanged: v =>
                {
                    int rounded = Mathf.RoundToInt(v);
                    _config.Difficulty = rounded;
                    _callbacks.Difficulty?.Invoke(rounded);
                });
            y += RowHeight + RowSpacing;

            _crosshairToggle = CreateRowToggle(root, y, "Show Crosshair", _config.ShowCrosshair,
                onChanged: v =>
                {
                    _config.ShowCrosshair = v;
                    _callbacks.ShowCrosshair?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _subtitlesToggle = CreateRowToggle(root, y, "Show Subtitles", _config.ShowSubtitles,
                onChanged: v =>
                {
                    _config.ShowSubtitles = v;
                    _callbacks.ShowSubtitles?.Invoke(v);
                });
        }

        void BuildAudioTab(RectTransform root)
        {
            float y = 0f;
            _masterSlider = CreateRowSlider(root, y, "Master Volume", 0f, 1f, _config.MasterVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.MasterVolume = v;
                    _callbacks.MasterVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _musicSlider = CreateRowSlider(root, y, "Music Volume", 0f, 1f, _config.MusicVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.MusicVolume = v;
                    _callbacks.MusicVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _effectsSlider = CreateRowSlider(root, y, "Effects Volume", 0f, 1f, _config.EffectsVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.EffectsVolume = v;
                    _callbacks.EffectsVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _footstepsSlider = CreateRowSlider(root, y, "Footsteps Volume", 0f, 1f, _config.FootstepsVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.FootstepsVolume = v;
                    _callbacks.FootstepsVolume?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _voiceSlider = CreateRowSlider(root, y, "Voice Volume", 0f, 1f, _config.VoiceVolume,
                formatter: v => $"{Mathf.RoundToInt(v * 100)}%",
                onChanged: v =>
                {
                    _config.VoiceVolume = v;
                    _callbacks.VoiceVolume?.Invoke(v);
                });
        }

        void BuildVideoTab(RectTransform root)
        {
            float y = 0f;
            _uiScaleSlider = CreateRowSlider(root, y, "UI Scale", 0.25f, 4f, _config.UiScale,
                formatter: v => $"{v:0.00}x",
                onChanged: v =>
                {
                    _config.UiScale = v;
                    _callbacks.UiScale?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _hudScaleSlider = CreateRowSlider(root, y, "HUD Scale", 0.5f, 6f, _config.HudScale,
                formatter: v => $"{v:0.00}x",
                onChanged: v =>
                {
                    _config.HudScale = v;
                    _callbacks.HudScale?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _fovSlider = CreateRowSlider(root, y, "Field of View", 30f, 110f, _config.Fov,
                formatter: v => $"{Mathf.RoundToInt(v)}\u00b0",
                onChanged: v =>
                {
                    _config.Fov = v;
                    _callbacks.Fov?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _gammaSlider = CreateRowSlider(root, y, "Gamma", 0.1f, 3f, _config.Gamma,
                formatter: v => $"{v:0.00}",
                onChanged: v =>
                {
                    _config.Gamma = v;
                    _callbacks.Gamma?.Invoke(v);
                });
            y += RowHeight + RowSpacing;

            _resolutions = BuildResolutionList();
            _resolutionStepper = CreateRowStepper(root, y, "Resolution", _resolutions.Count,
                labeler: i =>
                {
                    if (i < 0 || i >= _resolutions.Count) return "\u2014";
                    var r = _resolutions[i];
                    return $"{r.width} \u00d7 {r.height}";
                },
                onChanged: i =>
                {
                    if (i < 0 || i >= _resolutions.Count) return;
                    var r = _resolutions[i];
                    _config.ResolutionWidth = r.width;
                    _config.ResolutionHeight = r.height;
#if UNITY_2022_2_OR_NEWER
                    _config.RefreshRate = Mathf.RoundToInt((float)r.refreshRateRatio.value);
#else
                    _config.RefreshRate = r.refreshRate;
#endif
                    _callbacks.Resolution?.Invoke(r.width, r.height, _config.RefreshRate);
                });
            y += RowHeight + RowSpacing;

            _windowModeStepper = CreateRowStepper(root, y, "Window Mode", count: 3,
                labeler: i => i switch { 0 => "Windowed", 1 => "Fullscreen", 2 => "Borderless", _ => "\u2014" },
                onChanged: i =>
                {
                    _config.WindowMode = i;
                    _callbacks.WindowMode?.Invoke(i);
                });
            y += RowHeight + RowSpacing;

            _vsyncStepper = CreateRowStepper(root, y, "VSync", count: 3,
                labeler: i => i switch { 0 => "Off", 1 => "On", 2 => "Half", _ => "\u2014" },
                onChanged: i =>
                {
                    _config.VSync = i;
                    _callbacks.VSync?.Invoke(i);
                });
        }

        List<Resolution> BuildResolutionList()
        {
            var all = Screen.resolutions;
            var list = new List<Resolution>(all.Length);
            var seen = new HashSet<long>();
            for (int i = all.Length - 1; i >= 0; i--)
            {
                long key = ((long)all[i].width << 32) | (uint)all[i].height;
                if (seen.Add(key))
                    list.Add(all[i]);
            }
            list.Reverse();
            return list;
        }

        void SyncResolutionStepper()
        {
            if (_resolutionStepper == null)
                return;

            int target = -1;
            int w = _config.ResolutionWidth > 0 ? _config.ResolutionWidth : Screen.width;
            int h = _config.ResolutionHeight > 0 ? _config.ResolutionHeight : Screen.height;
            for (int i = 0; i < _resolutions.Count; i++)
            {
                if (_resolutions[i].width == w && _resolutions[i].height == h)
                {
                    target = i;
                    break;
                }
            }
            if (target < 0 && _resolutions.Count > 0)
                target = _resolutions.Count - 1;

            ApplyStepperIndex(_resolutionStepper, Mathf.Max(0, target));
        }
    }
}
