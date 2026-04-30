using System;

namespace VVardenfell.Core.Cache
{
    public static class MorrowindAmbientScriptUtility
    {
        static readonly string[] LoopCommands =
        {
            "PlayLoopSound3DVP",
            "PlayLoopSound3D",
        };

        public static bool TryGetLoopingSoundId(string scriptText, out string soundId)
        {
            soundId = null;
            if (string.IsNullOrWhiteSpace(scriptText))
                return false;

            for (int i = 0; i < LoopCommands.Length; i++)
            {
                int commandIndex = scriptText.IndexOf(LoopCommands[i], StringComparison.OrdinalIgnoreCase);
                if (commandIndex < 0)
                    continue;

                int quoteStart = scriptText.IndexOf('"', commandIndex + LoopCommands[i].Length);
                if (quoteStart < 0)
                    return false;

                int quoteEnd = scriptText.IndexOf('"', quoteStart + 1);
                if (quoteEnd <= quoteStart + 1)
                    return false;

                soundId = scriptText.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                return !string.IsNullOrWhiteSpace(soundId);
            }

            return false;
        }
    }
}
