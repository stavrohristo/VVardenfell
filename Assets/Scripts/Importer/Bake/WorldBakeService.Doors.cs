using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{

    internal static partial class WorldBakeService
    {
        private static void BuildDoorEntry(in CellReference reference, out DoorRefEntry doorEntry)
        {
            CellBakery.ToUnityTransformRaw(
                reference.DoorDestX,
                reference.DoorDestY,
                reference.DoorDestZ,
                reference.DoorDestRotX,
                reference.DoorDestRotY,
                reference.DoorDestRotZ,
                out var destPos,
                out var destRot);

            uint flags = 0u;
            string destinationCellId = string.Empty;
            if (reference.IsDoor)
            {
                flags |= DoorRefEntry.FlagTeleport;
                destinationCellId = reference.DoorDestCell ?? string.Empty;
            }

            doorEntry = new DoorRefEntry
            {
                PlacedRefId = reference.FormId,
                Flags = flags,
                DestPosX = destPos.x,
                DestPosY = destPos.y,
                DestPosZ = destPos.z,
                DestRotX = destRot.x,
                DestRotY = destRot.y,
                DestRotZ = destRot.z,
                DestRotW = destRot.w,
                DestinationCellId = destinationCellId,
            };
        }


        private static string BuildExteriorKey(int gridX, int gridY) => $"ext:{gridX},{gridY}";


        private static string BuildInteriorKey(string interiorId) => $"int:{(interiorId ?? string.Empty).Trim().ToLowerInvariant()}";


        private static void PruneOrphans(string directory, HashSet<string> expectedOutputs)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!expectedOutputs.Contains(file))
                    File.Delete(file);
            }
        }

        }
    }
