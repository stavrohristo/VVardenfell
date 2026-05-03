using System;

namespace VVardenfell.Core
{
    public static class MorrowindActorSkillTextUtility
    {
        public const byte MutationSet = 1;
        public const byte MutationMod = 2;

        public static bool TryResolveSkillCommand(string command, out byte skill, out byte mutation)
        {
            skill = 0;
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

            return TryResolveSkillName(command.Substring(prefix.Length), out skill);
        }

        public static bool TryResolveGetSkillExpression(string command, out byte skill)
        {
            skill = 0;
            if (string.IsNullOrWhiteSpace(command)
                || !command.StartsWith("get", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryResolveSkillName(command.Substring("get".Length), out skill);
        }

        public static bool TryResolveSkillName(string skillName, out byte skill)
        {
            skill = NormalizeSkillName(skillName) switch
            {
                "block" => 1,
                "armorer" => 2,
                "mediumarmor" => 3,
                "heavyarmor" => 4,
                "bluntweapon" => 5,
                "longblade" => 6,
                "axe" => 7,
                "spear" => 8,
                "athletics" => 9,
                "enchant" => 10,
                "destruction" => 11,
                "alteration" => 12,
                "illusion" => 13,
                "conjuration" => 14,
                "mysticism" => 15,
                "restoration" => 16,
                "alchemy" => 17,
                "unarmored" => 18,
                "security" => 19,
                "sneak" => 20,
                "acrobatics" => 21,
                "lightarmor" => 22,
                "shortblade" => 23,
                "marksman" => 24,
                "mercantile" => 25,
                "speechcraft" => 26,
                "handtohand" => 27,
                _ => 0,
            };
            return skill != 0;
        }

        static string NormalizeSkillName(string skillName)
            => (skillName ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
    }
}
