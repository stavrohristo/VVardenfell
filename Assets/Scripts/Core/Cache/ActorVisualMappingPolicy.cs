using System;

namespace VVardenfell.Core.Cache
{
    public static class ActorVisualMappingPolicy
    {
        public static ActorBodyPartMeshPart GetMeshPart(ActorVisualPartReference reference)
        {
            if (TryGetMeshPart(reference, out var meshPart))
                return meshPart;

            throw new InvalidOperationException($"Actor visual part '{reference}' is not associated with a body-part mesh slot.");
        }

        public static bool TryGetMeshPart(ActorVisualPartReference reference, out ActorBodyPartMeshPart meshPart)
        {
            switch (reference)
            {
                case ActorVisualPartReference.Head:
                    meshPart = ActorBodyPartMeshPart.Head;
                    return true;
                case ActorVisualPartReference.Hair:
                    meshPart = ActorBodyPartMeshPart.Hair;
                    return true;
                case ActorVisualPartReference.Neck:
                    meshPart = ActorBodyPartMeshPart.Neck;
                    return true;
                case ActorVisualPartReference.Cuirass:
                    meshPart = ActorBodyPartMeshPart.Chest;
                    return true;
                case ActorVisualPartReference.Groin:
                case ActorVisualPartReference.Skirt:
                    meshPart = ActorBodyPartMeshPart.Groin;
                    return true;
                case ActorVisualPartReference.RightHand:
                case ActorVisualPartReference.LeftHand:
                    meshPart = ActorBodyPartMeshPart.Hand;
                    return true;
                case ActorVisualPartReference.RightWrist:
                case ActorVisualPartReference.LeftWrist:
                    meshPart = ActorBodyPartMeshPart.Wrist;
                    return true;
                case ActorVisualPartReference.RightForearm:
                case ActorVisualPartReference.LeftForearm:
                    meshPart = ActorBodyPartMeshPart.Forearm;
                    return true;
                case ActorVisualPartReference.RightUpperarm:
                case ActorVisualPartReference.LeftUpperarm:
                    meshPart = ActorBodyPartMeshPart.Upperarm;
                    return true;
                case ActorVisualPartReference.RightFoot:
                case ActorVisualPartReference.LeftFoot:
                    meshPart = ActorBodyPartMeshPart.Foot;
                    return true;
                case ActorVisualPartReference.RightAnkle:
                case ActorVisualPartReference.LeftAnkle:
                    meshPart = ActorBodyPartMeshPart.Ankle;
                    return true;
                case ActorVisualPartReference.RightKnee:
                case ActorVisualPartReference.LeftKnee:
                    meshPart = ActorBodyPartMeshPart.Knee;
                    return true;
                case ActorVisualPartReference.RightLeg:
                case ActorVisualPartReference.LeftLeg:
                    meshPart = ActorBodyPartMeshPart.Upperleg;
                    return true;
                case ActorVisualPartReference.RightPauldron:
                case ActorVisualPartReference.LeftPauldron:
                    meshPart = ActorBodyPartMeshPart.Clavicle;
                    return true;
                case ActorVisualPartReference.Tail:
                    meshPart = ActorBodyPartMeshPart.Tail;
                    return true;
                default:
                    meshPart = default;
                    return false;
            }
        }

        public static string GetBoneName(ActorVisualPartReference reference)
        {
            return reference switch
            {
                ActorVisualPartReference.Head => "head",
                ActorVisualPartReference.Hair => "head",
                ActorVisualPartReference.Neck => "neck",
                ActorVisualPartReference.Cuirass => "chest",
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => "groin",
                ActorVisualPartReference.RightHand => "right hand",
                ActorVisualPartReference.LeftHand => "left hand",
                ActorVisualPartReference.RightWrist => "right wrist",
                ActorVisualPartReference.LeftWrist => "left wrist",
                ActorVisualPartReference.Shield => "shield bone",
                ActorVisualPartReference.RightForearm => "right forearm",
                ActorVisualPartReference.LeftForearm => "left forearm",
                ActorVisualPartReference.RightUpperarm => "right upper arm",
                ActorVisualPartReference.LeftUpperarm => "left upper arm",
                ActorVisualPartReference.RightFoot => "right foot",
                ActorVisualPartReference.LeftFoot => "left foot",
                ActorVisualPartReference.RightAnkle => "right ankle",
                ActorVisualPartReference.LeftAnkle => "left ankle",
                ActorVisualPartReference.RightKnee => "right knee",
                ActorVisualPartReference.LeftKnee => "left knee",
                ActorVisualPartReference.RightLeg => "right upper leg",
                ActorVisualPartReference.LeftLeg => "left upper leg",
                ActorVisualPartReference.RightPauldron => "right clavicle",
                ActorVisualPartReference.LeftPauldron => "left clavicle",
                ActorVisualPartReference.Weapon => "weapon bone",
                ActorVisualPartReference.Tail => "tail",
                _ => string.Empty,
            };
        }

        public static string GetMeshFilter(ActorVisualPartReference reference)
            => reference == ActorVisualPartReference.Hair ? "hair" : GetBoneName(reference);

        public static string GetMeshPartFilter(ActorVisualPartReference reference)
        {
            if (!TryGetMeshPart(reference, out var meshPart))
                return string.Empty;

            return meshPart switch
            {
                ActorBodyPartMeshPart.Head => "head",
                ActorBodyPartMeshPart.Hair => "hair",
                ActorBodyPartMeshPart.Neck => "neck",
                ActorBodyPartMeshPart.Chest => "chest",
                ActorBodyPartMeshPart.Groin => "groin",
                ActorBodyPartMeshPart.Hand => "hand",
                ActorBodyPartMeshPart.Wrist => "wrist",
                ActorBodyPartMeshPart.Forearm => "forearm",
                ActorBodyPartMeshPart.Upperarm => "upper arm",
                ActorBodyPartMeshPart.Foot => "foot",
                ActorBodyPartMeshPart.Ankle => "ankle",
                ActorBodyPartMeshPart.Knee => "knee",
                ActorBodyPartMeshPart.Upperleg => "upper leg",
                ActorBodyPartMeshPart.Clavicle => "clavicle",
                ActorBodyPartMeshPart.Tail => "tail",
                _ => string.Empty,
            };
        }

        public static string[] GetMeshFilters(ActorVisualPartReference reference)
            => new[] { GetMeshFilter(reference) };

        public static bool IsAttachmentOnlyPart(ActorVisualPartReference reference)
            => reference is ActorVisualPartReference.Shield
                or ActorVisualPartReference.Weapon;

        public static string[] GetBoneAliases(ActorVisualPartReference reference)
        {
            return reference switch
            {
                ActorVisualPartReference.Head => new[] { "Head", "Bip01 Head" },
                ActorVisualPartReference.Hair => new[] { "Hair", "Head", "Bip01 Head" },
                ActorVisualPartReference.Neck => new[] { "Neck", "Bip01 Neck" },
                ActorVisualPartReference.Cuirass => new[] { "Bip01 Spine1", "Bip01 Spine" },
                ActorVisualPartReference.Groin or ActorVisualPartReference.Skirt => new[] { "Groin", "Bip01 Pelvis", "Pelvis" },
                ActorVisualPartReference.RightHand => new[] { "Right Hand", "Bip01 R Hand" },
                ActorVisualPartReference.LeftHand => new[] { "Left Hand", "Bip01 L Hand" },
                ActorVisualPartReference.RightWrist => new[] { "Right Wrist", "Bip01 R Forearm" },
                ActorVisualPartReference.LeftWrist => new[] { "Left Wrist", "Bip01 L Forearm" },
                ActorVisualPartReference.RightForearm => new[] { "Right Forearm", "Bip01 R Forearm" },
                ActorVisualPartReference.LeftForearm => new[] { "Left Forearm", "Bip01 L Forearm" },
                ActorVisualPartReference.RightUpperarm => new[] { "Right Upper Arm", "Bip01 R UpperArm" },
                ActorVisualPartReference.LeftUpperarm => new[] { "Left Upper Arm", "Bip01 L UpperArm" },
                ActorVisualPartReference.RightPauldron => new[] { "Right Clavicle", "Bip01 R Clavicle", "Bip01 R UpperArm" },
                ActorVisualPartReference.LeftPauldron => new[] { "Left Clavicle", "Bip01 L Clavicle", "Bip01 L UpperArm" },
                ActorVisualPartReference.RightFoot => new[] { "Right Foot", "Bip01 R Foot" },
                ActorVisualPartReference.LeftFoot => new[] { "Left Foot", "Bip01 L Foot" },
                ActorVisualPartReference.RightAnkle => new[] { "Right Ankle", "Bip01 R Calf" },
                ActorVisualPartReference.LeftAnkle => new[] { "Left Ankle", "Bip01 L Calf" },
                ActorVisualPartReference.RightKnee => new[] { "Right Knee", "Bip01 R Calf" },
                ActorVisualPartReference.LeftKnee => new[] { "Left Knee", "Bip01 L Calf" },
                ActorVisualPartReference.RightLeg => new[] { "Right Upper Leg", "Bip01 R Thigh" },
                ActorVisualPartReference.LeftLeg => new[] { "Left Upper Leg", "Bip01 L Thigh" },
                ActorVisualPartReference.Shield => new[] { "Shield Bone", "Bip01 L Forearm" },
                ActorVisualPartReference.Weapon => new[] { "Weapon Bone", "Bip01 R Hand" },
                ActorVisualPartReference.Tail => new[] { "Tail", "Bip01 Tail" },
                _ => Array.Empty<string>(),
            };
        }

        public static string CanonicalizeBoneName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().Replace('_', ' ').Replace('-', ' ');
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

            if (normalized.StartsWith("Bip01 ", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(6);

            if (normalized.StartsWith("R ", StringComparison.OrdinalIgnoreCase))
                normalized = "right " + normalized.Substring(2);
            if (normalized.StartsWith("L ", StringComparison.OrdinalIgnoreCase))
                normalized = "left " + normalized.Substring(2);

            normalized = normalized
                .Replace(" R ", " right ", StringComparison.OrdinalIgnoreCase)
                .Replace(" L ", " left ", StringComparison.OrdinalIgnoreCase)
                .Replace("UpperArm", "Upper Arm", StringComparison.OrdinalIgnoreCase)
                .Replace("UpperLeg", "Upper Leg", StringComparison.OrdinalIgnoreCase);

            return normalized.ToLowerInvariant();
        }
    }
}
