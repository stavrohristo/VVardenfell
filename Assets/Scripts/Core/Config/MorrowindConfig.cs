using System;
using System.IO;

namespace VVardenfell.Core.Config
{
    [Serializable]
    public class MorrowindConfig
    {
        public string InstallPath;

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(InstallPath))
            {
                error = "Install path is empty.";
                return false;
            }
            if (!Directory.Exists(InstallPath))
            {
                error = $"Directory does not exist: {InstallPath}";
                return false;
            }
            var dataFiles = Path.Combine(InstallPath, "Data Files");
            if (!Directory.Exists(dataFiles))
            {
                error = $"No 'Data Files' folder under: {InstallPath}";
                return false;
            }
            var esm = Path.Combine(dataFiles, "Morrowind.esm");
            if (!File.Exists(esm))
            {
                error = $"Morrowind.esm not found at: {esm}";
                return false;
            }
            error = null;
            return true;
        }
    }
}
