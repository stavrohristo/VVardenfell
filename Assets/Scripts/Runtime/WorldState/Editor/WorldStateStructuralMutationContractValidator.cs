using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VVardenfell.Runtime.WorldState.Editor
{
    static class WorldStateStructuralMutationContractValidator
    {
        static readonly string[] k_ScannedRoots =
        {
            "Assets/Scripts/Runtime/WorldState",
            "Assets/Scripts/Runtime/WorldRefs",
            "Assets/Scripts/Runtime/Interactions",
        };

        static readonly Regex k_ForbiddenStructuralCall = new(
            @"\b(?:EntityManager|entityManager|em)\s*\.\s*(?:CreateEntity|DestroyEntity|Instantiate|AddComponent(?:Data)?|RemoveComponent|AddBuffer|SetName)(?:\s*<[^>]+>)?\s*\(",
            RegexOptions.Compiled);

        [MenuItem("VVardenfell/Validate/Runtime Structural Mutation Contract")]
        public static void ValidateFromMenu()
        {
            if (TryValidate(out var violations))
            {
                Debug.Log("[VVardenfell] Runtime structural mutation contract passed.");
                return;
            }

            foreach (string violation in violations)
                Debug.LogError(violation);
        }

        public static bool TryValidate(out List<string> violations)
        {
            violations = new List<string>();
            foreach (string root in k_ScannedRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (path.Contains($"{Path.DirectorySeparatorChar}Editor{Path.DirectorySeparatorChar}"))
                        continue;

                    string[] lines = File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!k_ForbiddenStructuralCall.IsMatch(lines[i]))
                            continue;

                        violations.Add($"{path}:{i + 1}: structural EntityManager mutation must be queued through EntityCommandBuffer.");
                    }
                }
            }

            return violations.Count == 0;
        }
    }
}
