using System;
using System.Collections.Generic;

namespace VVardenfell.Core.Cache
{
    public static class ActorVisualContentRules
    {
        const int BeastRaceFlag = 0x02;

        public static string NormalizeModelPath(string modelPath, bool lowerInvariant = false)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return string.Empty;

            string trimmed = modelPath.Trim().Replace('/', '\\');
            while (trimmed.Contains("\\\\", StringComparison.Ordinal))
                trimmed = trimmed.Replace("\\\\", "\\", StringComparison.Ordinal);

            if (!trimmed.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase))
                trimmed = "meshes\\" + trimmed;

            return lowerInvariant ? trimmed.ToLowerInvariant() : trimmed;
        }

        public static string BuildCompanionKfPath(string modelPath, bool lowerInvariant = false)
        {
            string normalized = NormalizeModelPath(modelPath, lowerInvariant);
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;

            int dot = normalized.LastIndexOf('.');
            return dot < 0
                ? normalized + ".kf"
                : normalized.Substring(0, dot) + ".kf";
        }

        public static string BuildPrefixedActorModelPath(string modelPath, bool lowerInvariant = false)
        {
            string normalized = NormalizeModelPath(modelPath, lowerInvariant);
            int slash = normalized.LastIndexOf('\\');
            return slash >= 0
                ? normalized.Substring(0, slash + 1) + "x" + normalized.Substring(slash + 1)
                : "x" + normalized;
        }

        public static string ResolveNpcSkeletonModel(bool firstPerson, bool female, bool beast, bool lowerInvariant = false)
        {
            string path = firstPerson
                ? beast ? "meshes\\base_animkna.1st.nif"
                    : female ? "meshes\\base_anim_female.1st.nif"
                    : "meshes\\xbase_anim.1st.nif"
                : beast ? "meshes\\base_animkna.nif"
                    : female ? "meshes\\base_anim_female.nif"
                    : "meshes\\base_anim.nif";

            return NormalizeModelPath(path, lowerInvariant);
        }

        public static bool IsBeastRace(string raceId, Dictionary<string, RaceDef> races)
            => !string.IsNullOrWhiteSpace(raceId)
               && races != null
               && races.TryGetValue(raceId, out var race)
               && IsBeastRaceFlags(race.Flags);

        public static bool IsBeastRaceFlags(int raceFlags)
            => (raceFlags & BeastRaceFlag) != 0;

        public static uint PartMask(ActorVisualPartReference reference)
        {
            int bit = (int)reference;
            return (uint)bit < 32u ? 1u << bit : 0u;
        }

        public static bool IsPlayerActor(ActorDef actor)
            => actor.Kind == ActorDefKind.Npc
               && string.Equals(actor.Id, "player", StringComparison.OrdinalIgnoreCase);

        public static bool TryResolveNpcRaceBodyPart(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            ActorVisualPartReference partReference,
            bool female,
            bool firstPerson,
            out ActorBodyPartDef result)
        {
            result = default;
            var table = BuildNpcRaceBodyPartTable(bodyParts, raceId, female, firstPerson, werewolf: false);
            int index = (int)partReference;
            if ((uint)index >= (uint)table.Length || string.IsNullOrWhiteSpace(table[index].Id))
                return false;

            result = table[index];
            return true;
        }

        public static ActorBodyPartDef[] BuildNpcRaceBodyPartTable(
            ActorBodyPartDef[] bodyParts,
            string raceId,
            bool female,
            bool firstPerson,
            bool werewolf)
        {
            var parts = new ActorBodyPartDef[(int)ActorVisualPartReference.Count];
            if (werewolf)
                return parts;

            bodyParts ??= Array.Empty<ActorBodyPartDef>();
            for (int i = 0; i < bodyParts.Length; i++)
            {
                var bodyPart = bodyParts[i];
                if (bodyPart.Type != ActorBodyPartMeshType.Skin
                    || bodyPart.Vampire != 0
                    || bodyPart.NotPlayable != 0
                    || !string.Equals(bodyPart.RaceId, raceId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool partFirstPerson = bodyPart.FirstPerson != 0;
                bool partFemale = bodyPart.Female != 0;
                bool isHand = IsFirstPersonMeshPart(bodyPart.Part);
                bool isSameGender = partFemale == female;

                if (firstPerson && isHand && !partFirstPerson)
                {
                    foreach (var reference in GetPartReferences(bodyPart.Part))
                    {
                        int partIndex = (int)reference;
                        if (string.IsNullOrWhiteSpace(parts[partIndex].Id) && isSameGender)
                            parts[partIndex] = bodyPart;
                        else if (isSameGender && parts[partIndex].Female != bodyPart.Female)
                            parts[partIndex] = bodyPart;
                        else if (string.IsNullOrWhiteSpace(parts[partIndex].Id) && female)
                            parts[partIndex] = bodyPart;
                    }

                    continue;
                }

                if (partFirstPerson != firstPerson)
                    continue;

                if (female && !partFemale)
                {
                    foreach (var reference in GetPartReferences(bodyPart.Part))
                    {
                        int partIndex = (int)reference;
                        if (string.IsNullOrWhiteSpace(parts[partIndex].Id))
                            parts[partIndex] = bodyPart;
                        else if (isHand && parts[partIndex].FirstPerson == 0 && partFirstPerson)
                            parts[partIndex] = bodyPart;
                    }

                    continue;
                }

                if (female != partFemale)
                    continue;

                foreach (var reference in GetPartReferences(bodyPart.Part))
                    parts[(int)reference] = bodyPart;
            }

            return parts;
        }

        public static ActorVisualPartReference[] GetPartReferences(ActorBodyPartMeshPart part)
        {
            return part switch
            {
                ActorBodyPartMeshPart.Head => new[] { ActorVisualPartReference.Head },
                ActorBodyPartMeshPart.Hair => new[] { ActorVisualPartReference.Hair },
                ActorBodyPartMeshPart.Neck => new[] { ActorVisualPartReference.Neck },
                ActorBodyPartMeshPart.Chest => new[] { ActorVisualPartReference.Cuirass },
                ActorBodyPartMeshPart.Groin => new[] { ActorVisualPartReference.Groin },
                ActorBodyPartMeshPart.Hand => new[] { ActorVisualPartReference.RightHand, ActorVisualPartReference.LeftHand },
                ActorBodyPartMeshPart.Wrist => new[] { ActorVisualPartReference.RightWrist, ActorVisualPartReference.LeftWrist },
                ActorBodyPartMeshPart.Forearm => new[] { ActorVisualPartReference.RightForearm, ActorVisualPartReference.LeftForearm },
                ActorBodyPartMeshPart.Upperarm => new[] { ActorVisualPartReference.RightUpperarm, ActorVisualPartReference.LeftUpperarm },
                ActorBodyPartMeshPart.Foot => new[] { ActorVisualPartReference.RightFoot, ActorVisualPartReference.LeftFoot },
                ActorBodyPartMeshPart.Ankle => new[] { ActorVisualPartReference.RightAnkle, ActorVisualPartReference.LeftAnkle },
                ActorBodyPartMeshPart.Knee => new[] { ActorVisualPartReference.RightKnee, ActorVisualPartReference.LeftKnee },
                ActorBodyPartMeshPart.Upperleg => new[] { ActorVisualPartReference.RightLeg, ActorVisualPartReference.LeftLeg },
                ActorBodyPartMeshPart.Clavicle => new[] { ActorVisualPartReference.RightPauldron, ActorVisualPartReference.LeftPauldron },
                ActorBodyPartMeshPart.Tail => new[] { ActorVisualPartReference.Tail },
                _ => Array.Empty<ActorVisualPartReference>(),
            };
        }

        public static int ResolveNpcRaceBodyPartScore(
            bool firstPerson,
            bool female,
            bool isFirstPersonArmPart,
            bool partFirstPerson,
            bool partFemale)
        {
            if (partFirstPerson == firstPerson && partFemale == female)
                return 0;

            if (firstPerson && isFirstPersonArmPart && !partFirstPerson && partFemale == female)
                return 10;

            if (female && partFirstPerson == firstPerson && !partFemale)
                return 20;

            if (firstPerson && isFirstPersonArmPart && female && !partFirstPerson && !partFemale)
                return 30;

            return int.MaxValue;
        }

        public static bool IsBaseSkinPartReference(ActorVisualPartReference type)
            => type is ActorVisualPartReference.Neck
                or ActorVisualPartReference.Cuirass
                or ActorVisualPartReference.Groin
                or ActorVisualPartReference.RightHand
                or ActorVisualPartReference.LeftHand
                or ActorVisualPartReference.RightWrist
                or ActorVisualPartReference.LeftWrist
                or ActorVisualPartReference.RightForearm
                or ActorVisualPartReference.LeftForearm
                or ActorVisualPartReference.RightUpperarm
                or ActorVisualPartReference.LeftUpperarm
                or ActorVisualPartReference.RightFoot
                or ActorVisualPartReference.LeftFoot
                or ActorVisualPartReference.RightAnkle
                or ActorVisualPartReference.LeftAnkle
                or ActorVisualPartReference.RightKnee
                or ActorVisualPartReference.LeftKnee
                or ActorVisualPartReference.RightLeg
                or ActorVisualPartReference.LeftLeg
                or ActorVisualPartReference.Tail;

        public static bool IsFirstPersonPartReference(ActorVisualPartReference type)
            => type is ActorVisualPartReference.RightHand
                or ActorVisualPartReference.LeftHand
                or ActorVisualPartReference.RightWrist
                or ActorVisualPartReference.LeftWrist
                or ActorVisualPartReference.RightForearm
                or ActorVisualPartReference.LeftForearm
                or ActorVisualPartReference.RightUpperarm
                or ActorVisualPartReference.LeftUpperarm;

        public static bool IsFirstPersonMeshPart(ActorBodyPartMeshPart part)
            => part is ActorBodyPartMeshPart.Hand
                or ActorBodyPartMeshPart.Wrist
                or ActorBodyPartMeshPart.Forearm
                or ActorBodyPartMeshPart.Upperarm;
    }
}
