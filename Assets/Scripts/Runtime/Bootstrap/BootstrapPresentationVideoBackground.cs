using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Video;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Audio;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Bootstrap
{
    public sealed partial class BootstrapPresentationView
    {
        void BuildVideoPlayer()
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.loopPointReached += OnVideoLoopPointReached;
        }


        void SetBackgroundImage(UiImageAsset image, bool stretchToFill)
        {
            _activeBackgroundImage = image;
            if (image?.Texture == null)
            {
                _backgroundImage.texture = null;
                _backgroundImage.color = Color.clear;
                RuntimeUiFactory.Stretch(_backgroundImage.rectTransform);
                return;
            }

            _backgroundImage.texture = image.Texture;
            _backgroundImage.color = Color.white;
            _backgroundImage.uvRect = new Rect(0f, 1f, 1f, -1f);
            ApplyTextureLayout(_backgroundImage.rectTransform, image.Texture.width, image.Texture.height, stretchToFill);
        }

        UiImageAsset PickSplashImage()
        {
            if (_theme.SplashImages.Count == 0)
                return null;

            int index = UnityEngine.Random.Range(0, _theme.SplashImages.Count);
            return _theme.SplashImages[index];
        }

        bool TryPlayMovie(string slot, bool loop)
        {
            var movie = _theme.GetMovie(slot);
            if (movie == null || !movie.HasPlayableClip)
            {
                ApplyMovieFallback(movie);
                StopMovie();
                return false;
            }

            if (IsSameMovieRequest(movie, loop))
                return true;

            if (_movieState != MoviePlaybackState.Idle)
            {
                Debug.LogWarning(
                    $"[VVardenfell] restarting bootstrap movie playback with slot '{slot}' while '{_activeMovieSlot ?? "<none>"}' was still {_movieState}.");
            }

            EnsureVideoTexture();
            ResetMoviePlaybackObjects();

            _activeMovie = movie;
            _activeMovieSlot = slot;
            _activeMoviePath = movie.CachedClipPath;
            _activeMovieLoop = loop;
            _activeMovieOwnsPhase = !loop && (_phase == PresentationPhase.IntroCompany || _phase == PresentationPhase.IntroLogo);
            _movieState = MoviePlaybackState.Preparing;
            _moviePrepareStartTime = Time.unscaledTime;
            _moviePrepareWarningLogged = false;
            _movieStartQueued = false;
            _videoImage.enabled = false;
            _videoImage.texture = null;

            using (k_MoviePrepare.Auto())
            {
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = movie.CachedClipPath;
                _videoPlayer.isLooping = loop;
                _videoPlayer.targetTexture = _videoTexture;
                ConfigureMovieAudio(movie.HasAudio);
                _videoPlayer.Prepare();
            }

            return true;
        }

        void StopMovie()
        {
            using (k_MovieStop.Auto())
            {
                ResetMoviePlaybackObjects();
            }

            _activeMovie = null;
            _activeMovieSlot = null;
            _activeMoviePath = null;
            _activeMovieLoop = false;
            _activeMovieOwnsPhase = false;
            _movieState = MoviePlaybackState.Idle;
            _moviePrepareWarningLogged = false;
            _movieStartQueued = false;
        }

        void OnVideoPrepared(VideoPlayer source)
        {
            if (source == null || source != _videoPlayer)
                return;

            if (_movieState != MoviePlaybackState.Preparing
                || _videoTexture == null
                || _activeMovie == null
                || !PathMatchesActiveMovie(source.url))
            {
                Debug.LogWarning(
                    $"[VVardenfell] stale bootstrap movie prepare callback ignored for slot '{_activeMovieSlot ?? "<none>"}' in state {_movieState}.");
                return;
            }

            _videoImage.texture = _videoTexture;
            _videoImage.enabled = true;
            source.targetTexture = _videoTexture;
            ConfigureMovieAudio(_activeMovie.HasAudio);
            UpdateVideoLayout();
            _movieState = MoviePlaybackState.Prepared;
            _movieStartQueued = true;
        }

        void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning(
                $"[VVardenfell] bootstrap movie error for slot '{_activeMovieSlot ?? _activeMovie?.Slot ?? "<none>"}' in state {_movieState}: {message}");
            ApplyMovieFallback(_activeMovie);
            StopMovie();
            BeginTimedIntroFallbackIfNeeded();
        }

        void OnVideoLoopPointReached(VideoPlayer source)
        {
            if (source == null || source != _videoPlayer || !_activeMovieOwnsPhase || _movieState != MoviePlaybackState.Playing)
                return;

            AdvanceFromCurrentIntroPhase();
        }

        void ApplyMovieFallback(UiMovieRuntimeInfo movie)
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.FallbackImageId))
                return;

            var fallback = _theme.GetImage(movie.FallbackImageId);
            if (fallback != null)
                SetBackgroundImage(fallback, stretchToFill: ShouldStretchBackgroundForCurrentPhase());
        }

        void EnsureVideoTexture()
        {
            int width = Mathf.Max(2, Screen.width);
            int height = Mathf.Max(2, Screen.height);
            if (_videoTexture != null && _videoTexture.width == width && _videoTexture.height == height)
                return;

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }

            _videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "VV.BootstrapVideo",
            };
        }

        void UpdateMoviePlaybackState()
        {
            if (_movieState == MoviePlaybackState.Preparing
                && !_moviePrepareWarningLogged
                && Time.unscaledTime - _moviePrepareStartTime >= MoviePrepareWarningSeconds)
            {
                _moviePrepareWarningLogged = true;
                Debug.LogWarning(
                    $"[VVardenfell] bootstrap movie prepare is taking longer than expected for slot '{_activeMovieSlot ?? "<none>"}' in state {_movieState}.");
            }

            if (_movieState == MoviePlaybackState.Prepared && _movieStartQueued)
                StartPreparedMovie();
        }

        void StartPreparedMovie()
        {
            if (_videoPlayer == null || _activeMovie == null || _movieState != MoviePlaybackState.Prepared)
                return;

            using (k_MovieStart.Auto())
            {
                _videoPlayer.targetTexture = _videoTexture;
                ConfigureMovieAudio(_activeMovie.HasAudio);
                _videoPlayer.Play();
            }

            _movieStartQueued = false;
            _movieState = MoviePlaybackState.Playing;
        }

        void ResetMoviePlaybackObjects()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.targetTexture = null;
                _videoPlayer.url = string.Empty;
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            }

            if (_videoAudio != null)
                _videoAudio.Stop();

            if (_videoImage != null)
            {
                _videoImage.texture = null;
                _videoImage.enabled = false;
                RuntimeUiFactory.Stretch(_videoImage.rectTransform);
            }
        }

        bool IsSameMovieRequest(UiMovieRuntimeInfo movie, bool loop)
        {
            return movie != null
                && _activeMovie != null
                && _movieState != MoviePlaybackState.Idle
                && string.Equals(_activeMovieSlot, movie.Slot, StringComparison.Ordinal)
                && string.Equals(_activeMoviePath, movie.CachedClipPath, StringComparison.OrdinalIgnoreCase)
                && _activeMovieLoop == loop;
        }

        bool PathMatchesActiveMovie(string path)
        {
            return string.Equals(_activeMoviePath ?? string.Empty, path ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        void BeginIntroMoviePhase(string slot)
        {
            _phaseWaitingForMovieCompletion = TryPlayMovie(slot, loop: false);
            if (!_phaseWaitingForMovieCompletion)
                BeginTimedIntroFallback();
        }

        void ConfigureMovieAudio(bool hasAudio)
        {
            if (_videoPlayer == null)
                return;

            if (!hasAudio)
            {
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                if (_videoAudio != null)
                    _videoAudio.Stop();
                return;
            }

            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.SetDirectAudioMute(0, false);
            _videoPlayer.SetDirectAudioVolume(0, 1f);
        }

        static BootstrapAudioPhase ToAudioPhase(PresentationPhase phase)
        {
            return phase switch
            {
                PresentationPhase.IntroCompany => BootstrapAudioPhase.IntroCompany,
                PresentationPhase.Loading => BootstrapAudioPhase.Loading,
                PresentationPhase.IntroLogo => BootstrapAudioPhase.IntroLogo,
                PresentationPhase.Menu => BootstrapAudioPhase.Menu,
                PresentationPhase.Dismissed => BootstrapAudioPhase.Dismissed,
                _ => BootstrapAudioPhase.None,
            };
        }

    }
}
