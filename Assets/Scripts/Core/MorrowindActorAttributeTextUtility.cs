using System;

namespace VVardenfell.Core
{
    public static class MorrowindActorAttributeTextUtility
    {
        public const byte MutationSet = 1;
        public const byte MutationMod = 2;

        public static bool TryResolveAttributeCommand(string command, out byte attribute, out byte mutation)
        {
            attribute = 0;
            mutation = 0;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string prefix;
            if (command.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "mod";
                mutation = MutationMod;
            }
            else if (command.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "set";
                mutation = MutationSet;
            }
            else
            {
                return false;
            }

            return TryResolveAttributeName(command.Substring(prefix.Length), out attribute);
        }

        public static bool TryResolveGetAttributeExpression(string command, out byte attribute)
        {
            attribute = 0;
            if (string.IsNullOrWhiteSpace(command)
                || !command.StartsWith("get", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryResolveAttributeName(command.Substring("get".Length), out attribute);
        }

        public static bool TryResolveAttributeName(string attributeName, out byte attribute)
        {
            attribute = NormalizeAttributeName(attributeName) switch
            {
                "strength" => 1,
                "intelligence" => 2,
                "willpower" => 3,
                "agility" => 4,
                "speed" => 5,
                "endurance" => 6,
                "personality" => 7,
                "luck" => 8,
                _ => 0,
            };
            return attribute != 0;
        }

        static string NormalizeAttributeName(string attributeName)
            => (attributeName ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
    }
}
