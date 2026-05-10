using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VVardenfell.Core.Config
{
    [Serializable]
    public sealed class MorrowindContentProfile
    {
        public string ProfileId;
        public string DisplayName;
        public string InstallPath;
        public string[] DataRoots = Array.Empty<string>();
        public string[] ContentFiles = Array.Empty<string>();
        public string[] Archives = Array.Empty<string>();
        public string ProfileCacheKey;

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(ProfileId))
            {
                error = "Content profile id is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(InstallPath) || !Directory.Exists(InstallPath))
            {
                error = $"Content profile install path is missing: {InstallPath}";
                return false;
            }

            if (DataRoots == null || DataRoots.Length == 0)
            {
                error = "Content profile has no data roots.";
                return false;
            }

            for (int i = 0; i < DataRoots.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(DataRoots[i]) || !Directory.Exists(DataRoots[i]))
                {
                    error = $"Content profile data root is missing: {DataRoots[i]}";
                    return false;
                }
            }

            if (ContentFiles == null || ContentFiles.Length == 0)
            {
                error = "Content profile has no content files.";
                return false;
            }

            for (int i = 0; i < ContentFiles.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ContentFiles[i]) || !File.Exists(ContentFiles[i]))
                {
                    error = $"Content profile content file is missing: {ContentFiles[i]}";
                    return false;
                }
            }

            for (int i = 0; i < (Archives?.Length ?? 0); i++)
            {
                if (string.IsNullOrWhiteSpace(Archives[i]) || !File.Exists(Archives[i]))
                {
                    error = $"Content profile archive is missing: {Archives[i]}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public void RefreshCacheKey()
        {
            ProfileCacheKey = BuildCacheKey(this);
        }

        public static string BuildCacheKey(MorrowindContentProfile profile)
        {
            using var sha = SHA256.Create();
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(profile?.ProfileId ?? string.Empty);
                WritePaths(w, profile?.DataRoots);
                WritePaths(w, profile?.ContentFiles);
                WritePaths(w, profile?.Archives);
            }

            byte[] hash = sha.ComputeHash(ms.ToArray());
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        static void WritePaths(BinaryWriter w, string[] paths)
        {
            var values = paths ?? Array.Empty<string>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                string path = values[i] ?? string.Empty;
                w.Write(path.Trim().ToLowerInvariant());
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    w.Write(info.Length);
                    w.Write(info.LastWriteTimeUtc.Ticks);
                }
                else
                {
                    w.Write(0L);
                    w.Write(0L);
                }
            }
        }
    }
}
