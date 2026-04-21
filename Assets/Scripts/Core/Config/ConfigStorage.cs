using System.IO;
using UnityEngine;

namespace VVardenfell.Core.Config
{
    public static class ConfigStorage
    {
        public const string FileName = "config.json";

        public static string ConfigPath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool TryLoad(out MorrowindConfig config)
        {
            config = null;
            var path = ConfigPath;
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                config = JsonUtility.FromJson<MorrowindConfig>(json);
                return config != null;
            }
            catch
            {
                return false;
            }
        }

        public static void Save(MorrowindConfig config)
        {
            var json = JsonUtility.ToJson(config, prettyPrint: true);
            File.WriteAllText(ConfigPath, json);
        }
    }
}
