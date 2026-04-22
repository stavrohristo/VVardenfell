using System;
using System.Collections.Generic;
using System.IO;

namespace VVardenfell.Core.Config
{
    /// <summary>
    /// Minimal Morrowind.ini reader for the bootstrap presentation asset set.
    /// Supports only the section/key lookup we need for fonts and movies.
    /// </summary>
    public sealed class MorrowindIniReader
    {
        readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public static MorrowindIniReader Read(string path)
        {
            var reader = new MorrowindIniReader();
            reader.Load(path);
            return reader;
        }

        public bool TryGetValue(string section, string key, out string value)
            => _values.TryGetValue(MakeLookupKey(section, key), out value);

        public string GetValueOrDefault(string section, string key, string fallback = "")
            => TryGetValue(section, key, out var value) ? value : fallback;

        private void Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Morrowind.ini not found", path);

            string currentSection = "";
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                    continue;

                if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                _values[MakeLookupKey(currentSection, key)] = value;
            }
        }

        private static string MakeLookupKey(string section, string key)
            => $"{section ?? ""}:{key ?? ""}";
    }
}
