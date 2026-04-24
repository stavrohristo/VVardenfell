using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    /// <summary>
    /// Bake-time bridge for UI movie clips. It owns source resolution, cache reuse,
    /// ffmpeg transcode, ffprobe validation, and the final manifest record shape.
    /// </summary>
    public sealed class MovieTranscodeBridge
    {
        const int ToolTimeoutMs = 120_000;
        static readonly string[] ToolSearchRoots =
        {
            @"C:\Program Files\Topaz Labs LLC\Topaz Video AI",
            @"C:\Program Files\ffmpeg",
            @"C:\Program Files (x86)\ffmpeg",
            @"C:\Tools\ffmpeg",
        };

        readonly Dictionary<string, UiMovieRecord> _previousRecords;
        string _ffmpegPath;
        string _ffprobePath;
        string _videoEncoder;

        public MovieTranscodeBridge(UiCacheManifest previousManifest = null)
        {
            _previousRecords = new Dictionary<string, UiMovieRecord>(StringComparer.OrdinalIgnoreCase);
            if (previousManifest?.Movies == null)
                return;

            for (int i = 0; i < previousManifest.Movies.Length; i++)
            {
                var movie = previousManifest.Movies[i];
                if (!string.IsNullOrWhiteSpace(movie?.Slot))
                    _previousRecords[movie.Slot] = movie;
            }
        }

        public UiMovieRecord CreateRecord(string slot, string configuredSource, string fallbackImageId, string dataFilesRoot)
        {
            var record = new UiMovieRecord
            {
                Slot = slot,
                ConfiguredSource = configuredSource ?? "",
                ResolvedSourcePath = "",
                CachedClipPath = CachePaths.UiMovieFile(slot),
                FallbackImageId = fallbackImageId ?? "",
                TranscodeProfileVersion = UiCacheManifest.MovieTranscodeProfileVersion,
                Flags = UiMovieFlags.None,
            };

            try
            {
                if (string.IsNullOrWhiteSpace(configuredSource))
                {
                    record.Flags = UiMovieFlags.MissingSource;
                    return record;
                }

                string resolvedSource = ResolveDataFilesRelative(dataFilesRoot, configuredSource);
                record.ResolvedSourcePath = resolvedSource;
                if (!File.Exists(resolvedSource))
                {
                    record.Flags = UiMovieFlags.MissingSource;
                    return record;
                }

                var sourceInfo = new FileInfo(resolvedSource);
                record.SourceSize = sourceInfo.Length;
                record.SourceMtimeTicks = sourceInfo.LastWriteTimeUtc.Ticks;
                record.Flags |= UiMovieFlags.SourceAvailable;

                EnsureToolPaths();

                if (TryReuseTranscodedClip(record, out var reusedProbe))
                {
                    ApplyClipMetadata(record, reusedProbe);
                    return record;
                }

                Transcode(record);
                var freshProbe = ProbeClip(record.CachedClipPath);
                ApplyClipMetadata(record, freshProbe);
                return record;
            }
            catch (Exception ex) when ((record.Flags & UiMovieFlags.SourceAvailable) != 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[VVardenfell] skipping movie slot '{slot}' ({record.ResolvedSourcePath}): {ex.Message}");
                ClearCachedClip(record);
                return record;
            }
        }

        public static bool CacheMatches(UiCacheManifest manifest, string installPath, out string error)
        {
            error = null;
            if (manifest == null)
            {
                error = "ui.bin unreadable";
                return false;
            }

            if (!manifest.SourcesMatch(installPath))
            {
                error = "UI source files changed.";
                return false;
            }

            var validator = new MovieTranscodeBridge();
            for (int i = 0; i < manifest.Movies.Length; i++)
            {
                var movie = manifest.Movies[i];
                if ((movie.Flags & UiMovieFlags.SourceAvailable) == 0)
                    continue;

                if (string.IsNullOrWhiteSpace(movie.ResolvedSourcePath) || !File.Exists(movie.ResolvedSourcePath))
                {
                    error = $"UI movie source missing for slot '{movie.Slot}'.";
                    return false;
                }

                var sourceInfo = new FileInfo(movie.ResolvedSourcePath);
                if (sourceInfo.Length != movie.SourceSize || sourceInfo.LastWriteTimeUtc.Ticks != movie.SourceMtimeTicks)
                {
                    error = $"UI movie source changed for slot '{movie.Slot}'.";
                    return false;
                }

                if (movie.TranscodeProfileVersion != UiCacheManifest.MovieTranscodeProfileVersion)
                {
                    error = $"UI movie transcode profile changed for slot '{movie.Slot}'.";
                    return false;
                }

                if ((movie.Flags & UiMovieFlags.TranscodedAvailable) == 0)
                    continue;

                if (string.IsNullOrWhiteSpace(movie.CachedClipPath) || !File.Exists(movie.CachedClipPath))
                {
                    error = $"UI movie cache file missing for slot '{movie.Slot}'.";
                    return false;
                }

                var clipInfo = new FileInfo(movie.CachedClipPath);
                if (clipInfo.Length != movie.CachedClipSize || clipInfo.LastWriteTimeUtc.Ticks != movie.CachedClipMtimeTicks)
                {
                    error = $"UI movie cache file changed for slot '{movie.Slot}'.";
                    return false;
                }

                ClipProbeResult probe;
                try
                {
                    validator.EnsureToolPaths();
                    probe = validator.ProbeClip(movie.CachedClipPath);
                }
                catch (Exception ex)
                {
                    error = $"UI movie validation failed for slot '{movie.Slot}': {ex.Message}";
                    return false;
                }

                if (probe.Width != movie.Width || probe.Height != movie.Height || probe.DurationMs != movie.DurationMs || probe.HasAudio != movie.HasAudio)
                {
                    error = $"UI movie metadata mismatch for slot '{movie.Slot}'.";
                    return false;
                }
            }

            return true;
        }

        public static string ResolveDataFilesRelative(string dataFilesRoot, string configuredSource)
        {
            if (string.IsNullOrWhiteSpace(configuredSource))
                return "";

            string normalized = configuredSource.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
                return normalized;

            if (normalized.StartsWith("Video" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dataFilesRoot, normalized);

            return Path.Combine(dataFilesRoot, "Video", normalized);
        }

        bool TryReuseTranscodedClip(UiMovieRecord record, out ClipProbeResult probe)
        {
            probe = default;
            if (!_previousRecords.TryGetValue(record.Slot, out var previous))
                return false;

            if ((previous.Flags & UiMovieFlags.TranscodedAvailable) == 0)
                return false;
            if (!string.Equals(previous.ConfiguredSource ?? "", record.ConfiguredSource ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(previous.ResolvedSourcePath ?? "", record.ResolvedSourcePath ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
            if (previous.SourceSize != record.SourceSize || previous.SourceMtimeTicks != record.SourceMtimeTicks)
                return false;
            if (previous.TranscodeProfileVersion != UiCacheManifest.MovieTranscodeProfileVersion)
                return false;
            if (string.IsNullOrWhiteSpace(previous.CachedClipPath) || !File.Exists(previous.CachedClipPath))
                return false;

            var clipInfo = new FileInfo(previous.CachedClipPath);
            if (clipInfo.Length != previous.CachedClipSize || clipInfo.LastWriteTimeUtc.Ticks != previous.CachedClipMtimeTicks)
                return false;

            probe = ProbeClip(previous.CachedClipPath);
            record.CachedClipPath = previous.CachedClipPath;
            return true;
        }

        void Transcode(UiMovieRecord record)
        {
            string tempPath = record.CachedClipPath + ".tmp.mp4";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            string args = BuildTranscodeArgs(record.ResolvedSourcePath, tempPath);
            try
            {
                var result = RunTool(GetFfmpegPath(), args);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"ffmpeg failed for slot '{record.Slot}': {FirstNonEmptyLine(result.StandardError, result.StandardOutput)}");

                if (!File.Exists(tempPath))
                    throw new InvalidOperationException($"ffmpeg did not produce an output clip for slot '{record.Slot}'.");

                ProbeClip(tempPath);
                ReplaceAtomically(tempPath, record.CachedClipPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        void ApplyClipMetadata(UiMovieRecord record, ClipProbeResult probe)
        {
            var clipInfo = new FileInfo(record.CachedClipPath);
            record.CachedClipSize = clipInfo.Length;
            record.CachedClipMtimeTicks = clipInfo.LastWriteTimeUtc.Ticks;
            record.Width = probe.Width;
            record.Height = probe.Height;
            record.DurationMs = probe.DurationMs;
            record.HasAudio = probe.HasAudio;
            record.Flags |= UiMovieFlags.TranscodedAvailable;
            if (probe.HasAudio)
                record.Flags |= UiMovieFlags.HasAudio;
        }

        static void ClearCachedClip(UiMovieRecord record)
        {
            record.CachedClipSize = 0;
            record.CachedClipMtimeTicks = 0;
            record.Width = 0;
            record.Height = 0;
            record.DurationMs = 0;
            record.HasAudio = false;
            record.Flags &= ~(UiMovieFlags.TranscodedAvailable | UiMovieFlags.HasAudio);

            if (!string.IsNullOrWhiteSpace(record.CachedClipPath) && File.Exists(record.CachedClipPath))
            {
                try { File.Delete(record.CachedClipPath); }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[VVardenfell] failed deleting stale movie cache '{record.CachedClipPath}': {ex.Message}");
                }
            }
        }

        ClipProbeResult ProbeClip(string clipPath)
        {
            if (string.IsNullOrWhiteSpace(clipPath) || !File.Exists(clipPath))
                throw new FileNotFoundException("Movie clip missing.", clipPath);

            string args = $"-v error -show_entries stream=codec_type,width,height,color_space,color_transfer,color_primaries:format=duration -of default=noprint_wrappers=1:nokey=0 \"{clipPath}\"";
            var result = RunTool(GetFfprobePath(), args);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"ffprobe failed: {FirstNonEmptyLine(result.StandardError, result.StandardOutput)}");

            int width = 0;
            int height = 0;
            bool hasVideo = false;
            bool hasAudio = false;
            long durationMs = 0;
            string currentStreamType = "";
            string colorSpace = "";
            string colorTransfer = "";
            string colorPrimaries = "";

            var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                int sep = lines[i].IndexOf('=');
                if (sep <= 0)
                    continue;

                string key = lines[i].Substring(0, sep).Trim();
                string value = lines[i].Substring(sep + 1).Trim();
                switch (key)
                {
                    case "codec_type":
                        currentStreamType = value;
                        if (value == "video")
                            hasVideo = true;
                        else if (value == "audio")
                            hasAudio = true;
                        break;
                    case "width":
                        if (currentStreamType == "video" && width == 0 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWidth))
                            width = parsedWidth;
                        break;
                    case "height":
                        if (currentStreamType == "video" && height == 0 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHeight))
                            height = parsedHeight;
                        break;
                    case "duration":
                        if (durationMs == 0 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                            durationMs = (long)Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero);
                        break;
                    case "color_space":
                        if (currentStreamType == "video" && string.IsNullOrWhiteSpace(colorSpace))
                            colorSpace = value;
                        break;
                    case "color_transfer":
                        if (currentStreamType == "video" && string.IsNullOrWhiteSpace(colorTransfer))
                            colorTransfer = value;
                        break;
                    case "color_primaries":
                        if (currentStreamType == "video" && string.IsNullOrWhiteSpace(colorPrimaries))
                            colorPrimaries = value;
                        break;
                }
            }

            if (!hasVideo || width <= 0 || height <= 0)
                throw new InvalidDataException($"Movie probe did not return a valid video stream for '{clipPath}'.");
            if (durationMs <= 0)
                throw new InvalidDataException($"Movie probe did not return a valid duration for '{clipPath}'.");
            if (!string.Equals(colorSpace, "bt709", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(colorTransfer, "bt709", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(colorPrimaries, "bt709", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Movie probe returned unsupported color metadata for '{clipPath}' (space='{colorSpace}', transfer='{colorTransfer}', primaries='{colorPrimaries}').");
            }

            return new ClipProbeResult(width, height, durationMs, hasAudio, colorSpace, colorTransfer, colorPrimaries);
        }

        string GetFfmpegPath()
        {
            EnsureToolPaths();
            return _ffmpegPath;
        }

        string GetFfprobePath()
        {
            EnsureToolPaths();
            return _ffprobePath;
        }

        void EnsureToolPaths()
        {
            _ffmpegPath ??= ResolveToolPath("ffmpeg", "VVARDENFELL_FFMPEG_PATH");
            _ffprobePath ??= ResolveSiblingToolPath(_ffmpegPath, "ffprobe.exe")
                             ?? ResolveToolPath("ffprobe", "VVARDENFELL_FFPROBE_PATH");

            _ffmpegPath ??= ResolveSiblingToolPath(_ffprobePath, "ffmpeg.exe")
                            ?? ResolveToolPath("ffmpeg", "VVARDENFELL_FFMPEG_PATH");

            if (string.IsNullOrWhiteSpace(_ffmpegPath))
                throw new InvalidOperationException("Required movie transcode tool 'ffmpeg' was not found on PATH or in known install locations.");
            if (string.IsNullOrWhiteSpace(_ffprobePath))
                throw new InvalidOperationException("Required movie transcode tool 'ffprobe' was not found on PATH or in known install locations.");
            _videoEncoder ??= ResolveVideoEncoder(_ffmpegPath);
        }

        static string ResolveToolPath(string toolName, string overrideEnvVar)
        {
            string envOverride = Environment.GetEnvironmentVariable(overrideEnvVar);
            if (File.Exists(envOverride))
                return envOverride;

            try
            {
                var result = RunTool("where.exe", toolName);
                if (result.ExitCode == 0)
                {
                    var pathFromWhere = result.StandardOutput
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(pathFromWhere))
                        return pathFromWhere;
                }
            }
            catch
            {
            }

            string fileName = toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? toolName : toolName + ".exe";
            for (int i = 0; i < ToolSearchRoots.Length; i++)
            {
                string candidate = Path.Combine(ToolSearchRoots[i], fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        static string ResolveSiblingToolPath(string knownToolPath, string siblingExeName)
        {
            if (string.IsNullOrWhiteSpace(knownToolPath))
                return null;

            string directory = Path.GetDirectoryName(knownToolPath);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            string sibling = Path.Combine(directory, siblingExeName);
            return File.Exists(sibling) ? sibling : null;
        }

        static ProcessResult RunTool(string toolPath, string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            if (!process.Start())
                throw new InvalidOperationException($"Failed starting tool '{toolPath}'.");

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(ToolTimeoutMs))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"Tool '{toolPath}' timed out.");
            }

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        string BuildTranscodeArgs(string sourcePath, string outputPath)
        {
            var builder = new StringBuilder();
            builder.Append("-y -hide_banner -loglevel error ");
            builder.Append("-i \"").Append(sourcePath).Append("\" ");
            builder.Append("-map 0:v:0 -map 0:a? ");
            builder.Append("-c:v ").Append(_videoEncoder).Append(" -pix_fmt yuv420p ");
            builder.Append("-color_primaries bt709 -color_trc bt709 -colorspace bt709 ");
            builder.Append("-bsf:v h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1 ");
            builder.Append("-c:a aac -ar 44100 -ac 2 -b:a 128k ");
            builder.Append("-movflags +faststart+write_colr ");
            builder.Append("-f mp4 ");
            builder.Append("\"").Append(outputPath).Append("\"");
            return builder.ToString();
        }

        static string ResolveVideoEncoder(string ffmpegPath)
        {
            var result = RunTool(ffmpegPath, "-hide_banner -encoders");
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Failed querying ffmpeg encoders: {FirstNonEmptyLine(result.StandardError, result.StandardOutput)}");

            string[] preferred =
            {
                "libx264",
                "h264_mf",
                "h264_qsv",
                "h264_amf",
                "h264_nvenc",
            };

            for (int i = 0; i < preferred.Length; i++)
            {
                if (EncoderExists(result.StandardOutput, preferred[i]))
                    return preferred[i];
            }

            throw new InvalidOperationException("No supported H.264 encoder was found in ffmpeg. Expected one of: libx264, h264_mf, h264_qsv, h264_amf, h264_nvenc.");
        }

        static bool EncoderExists(string encoderListOutput, string encoderName)
        {
            if (string.IsNullOrWhiteSpace(encoderListOutput) || string.IsNullOrWhiteSpace(encoderName))
                return false;

            var lines = encoderListOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || !line.StartsWith("V", StringComparison.Ordinal))
                    continue;

                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && string.Equals(parts[1], encoderName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static void ReplaceAtomically(string tempPath, string finalPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? CachePaths.UiMoviesDir);
            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, finalPath);
        }

        static string FirstNonEmptyLine(string primary, string secondary)
        {
            string message = FirstNonEmptyLine(primary);
            return !string.IsNullOrWhiteSpace(message) ? message : FirstNonEmptyLine(secondary);
        }

        static string FirstNonEmptyLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    return lines[i].Trim();
            }

            return "";
        }

        readonly struct ClipProbeResult
        {
            public ClipProbeResult(
                int width,
                int height,
                long durationMs,
                bool hasAudio,
                string colorSpace,
                string colorTransfer,
                string colorPrimaries)
            {
                Width = width;
                Height = height;
                DurationMs = durationMs;
                HasAudio = hasAudio;
                ColorSpace = colorSpace ?? "";
                ColorTransfer = colorTransfer ?? "";
                ColorPrimaries = colorPrimaries ?? "";
            }

            public int Width { get; }
            public int Height { get; }
            public long DurationMs { get; }
            public bool HasAudio { get; }
            public string ColorSpace { get; }
            public string ColorTransfer { get; }
            public string ColorPrimaries { get; }
        }

        readonly struct ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? "";
                StandardError = standardError ?? "";
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
        }
    }
}
