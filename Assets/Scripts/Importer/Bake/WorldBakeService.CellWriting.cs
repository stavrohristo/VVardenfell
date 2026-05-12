using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        private static IEnumerator AuthorCellSectionsIncremental(
            List<StagedCellData> dirtyCells,
            BakeProgress progress,
            float cellMeters,
            ModelPrefabCatalogData modelPrefabCatalog,
            TextureBakery textures,
            MaterialBakery materials,
            GameplayContentData gameplayContent,
            CollisionBakery collisions)
        {
            var info = new CellWriteProgressInfo { Subphase = "Authoring cell sections" };
            UpdateCellWriteProgress(progress, dirtyCells.Count, info);
            yield return null;

            if (dirtyCells.Count == 0)
                yield break;

            using var scratch = new CellWriteScratch();
            using var runtimeContentBlob = RuntimeContentBlobBuilder.Build(gameplayContent);
            var pendingStaticJobs = new List<PendingStaticColliderJob>();
            int nextIndex = 0;
            int completed = 0;
            int maxInFlightStaticJobs = Math.Max(1, Math.Min(Math.Max(1, Environment.ProcessorCount - 1), dirtyCells.Count));

            try
            {
                while (completed < dirtyCells.Count)
                {
                    while (nextIndex < dirtyCells.Count && pendingStaticJobs.Count < maxInFlightStaticJobs)
                    {
                        var staged = dirtyCells[nextIndex++];
                        if (HasStaticCollision(staged))
                        {
                            pendingStaticJobs.Add(ScheduleStaticColliderBlobJob(staged));
                        }
                        else
                        {
                            AuthorCellSection(staged, cellMeters, scratch, default, modelPrefabCatalog, textures, materials, runtimeContentBlob, collisions);
                            completed++;
                            info.LastCompletedCellKey = staged.WorkItem.Key;
                            if ((completed & 3) == 3)
                                break;
                        }
                    }

                    for (int i = pendingStaticJobs.Count - 1; i >= 0; i--)
                    {
                        var pending = pendingStaticJobs[i];
                        if (!pending.Handle.IsCompleted)
                            continue;

                        var staticCollider = CompleteStaticColliderBlobJob(pending);
                        pendingStaticJobs.RemoveAt(i);
                        try
                        {
                            AuthorCellSection(pending.Staged, cellMeters, scratch, staticCollider, modelPrefabCatalog, textures, materials, runtimeContentBlob, collisions);
                        }
                        finally
                        {
                            if (staticCollider.IsCreated)
                                staticCollider.Dispose();
                        }

                        completed++;
                        info.LastCompletedCellKey = pending.Staged.WorkItem.Key;
                    }

                    info.ClaimedCount = nextIndex;
                    info.CompletedCount = completed;
                    UpdateCellWriteProgress(progress, dirtyCells.Count, info);
                    yield return null;
                }

                info.ClaimedCount = dirtyCells.Count;
                info.CompletedCount = dirtyCells.Count;
                UpdateCellWriteProgress(progress, dirtyCells.Count, info);
                yield return null;
            }
            finally
            {
                for (int i = 0; i < pendingStaticJobs.Count; i++)
                {
                    pendingStaticJobs[i].Handle.Complete();
                    pendingStaticJobs[i].Dispose();
                }
            }
        }


        private static void AuthorCellSection(
            StagedCellData staged,
            float cellMeters,
            CellWriteScratch scratch,
            Unity.Entities.BlobAssetReference<Unity.Physics.Collider> staticCollider,
            ModelPrefabCatalogData modelPrefabCatalog,
            TextureBakery textures,
            MaterialBakery materials,
            Unity.Entities.BlobAssetReference<RuntimeContentBlob> runtimeContentBlob,
            CollisionBakery collisions)
        {
            Unity.Entities.BlobAssetReference<Unity.Physics.Collider> terrainCollider = default;
            try
            {
                terrainCollider = CreateTerrainColliderBlob(staged, cellMeters, scratch);
                WriteRuntimeCellSection(staged, terrainCollider, staticCollider, modelPrefabCatalog, textures, materials, runtimeContentBlob, collisions);
            }
            finally
            {
                if (terrainCollider.IsCreated)
                    terrainCollider.Dispose();
            }
        }


        private static Unity.Entities.BlobAssetReference<Unity.Physics.Collider> CreateTerrainColliderBlob(
            StagedCellData staged,
            float cellMeters,
            CellWriteScratch scratch)
        {
            if (!HasTerrain(staged))
                return default;

            const int n = LandRecord.Size;
            for (int i = 0; i < n * n; i++)
                scratch.TerrainHeights[i] = staged.Land.Heights[i] * WorldScale.MwUnitsToMeters;

            float spacing = cellMeters / (n - 1);
            var terrainScale = new Unity.Mathematics.float3(spacing, 1f, spacing);
            return Unity.Physics.TerrainCollider.Create(
                scratch.TerrainHeights,
                new Unity.Mathematics.int2(n, n),
                terrainScale,
                Unity.Physics.TerrainCollider.CollisionMethod.VertexSamples);
        }


        private static PendingStaticColliderJob ScheduleStaticColliderBlobJob(StagedCellData staged)
        {
            try
            {
                int vertexCount = staged.StaticCollision.Vertices.Length;
                int triangleCount = staged.StaticCollision.Indices.Length / 3;

                var points = new Unity.Collections.NativeArray<Unity.Mathematics.float3>(vertexCount, Unity.Collections.Allocator.Persistent);
                var triangles = new Unity.Collections.NativeArray<Unity.Mathematics.int3>(triangleCount, Unity.Collections.Allocator.Persistent);
                var output = new Unity.Collections.NativeArray<Unity.Entities.BlobAssetReference<Unity.Physics.Collider>>(1, Unity.Collections.Allocator.Persistent);

                for (int i = 0; i < vertexCount; i++)
                {
                    var vertex = staged.StaticCollision.Vertices[i];
                    points[i] = new Unity.Mathematics.float3(vertex.x, vertex.y, vertex.z);
                }

                for (int t = 0; t < triangleCount; t++)
                {
                    triangles[t] = new Unity.Mathematics.int3(
                        staged.StaticCollision.Indices[t * 3 + 0],
                        staged.StaticCollision.Indices[t * 3 + 1],
                        staged.StaticCollision.Indices[t * 3 + 2]);
                }

                var pending = new PendingStaticColliderJob
                {
                    Staged = staged,
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                };

                pending.Handle = new CreateStaticColliderBlobJob
                {
                    Points = points,
                    Triangles = triangles,
                    Output = output,
                }.Schedule();

                Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                return pending;
            }
            finally
            {
            }
        }


        private static Unity.Entities.BlobAssetReference<Unity.Physics.Collider> CompleteStaticColliderBlobJob(PendingStaticColliderJob pending)
        {
            pending.Handle.Complete();
            try
            {
                var blob = pending.Output[0];
                if (!blob.IsCreated)
                    throw new InvalidDataException($"{pending.Staged.WorkItem.Key} static collider job produced no collider.");
                pending.Output[0] = default;
                return blob;
            }
            finally
            {
                pending.Dispose();
            }
        }


        private static void UpdateCellWriteProgress(BakeProgress progress, int total, CellWriteProgressInfo info)
        {
            progress.Stage = "Cell Writes";
            progress.Total = total;
            progress.Current = Math.Min(total, info.ClaimedCount);
            progress.Label = total == 0
                ? $"No cells for {info.Subphase?.ToLowerInvariant() ?? "cell writes"}"
                : $"{info.Subphase} {info.CompletedCount}/{total}";
        }


        private static Dictionary<string, ContentReference> BuildGameplayContentLookup(GameplayContentData gameplayContent)
        {
            return GameplayContentReferenceIndex.BuildPlaceableIndex(gameplayContent);
        }


        private static ContentReference ResolveGameplayContentReference(Dictionary<string, ContentReference> lookup, string baseId)
        {
            return GameplayContentReferenceIndex.TryResolvePlaceable(lookup, baseId, out var contentReference)
                ? contentReference
                : default;
        }


        private static BakeManifest.BakedCellState BuildCellState(StagedCellData staged)
        {
            if (staged.BakedRefs == null && staged.PreviousState != null)
            {
                return new BakeManifest.BakedCellState
                {
                    Key = staged.WorkItem.Key,
                    SectionPath = BuildCellSectionPath(staged.WorkItem),
                    Fingerprint = staged.Fingerprint,
                    PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                    IsInterior = staged.WorkItem.IsInterior,
                    GridX = staged.WorkItem.Cell.GridX,
                    GridY = staged.WorkItem.Cell.GridY,
                    InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                    MeshIndices = staged.PreviousState.MeshIndices ?? Array.Empty<int>(),
                    MaterialIndices = staged.PreviousState.MaterialIndices ?? Array.Empty<int>(),
                    TextureIndices = staged.PreviousState.TextureIndices ?? Array.Empty<int>(),
                    CollisionIndices = staged.PreviousState.CollisionIndices ?? Array.Empty<int>(),
                    TerrainLayerIndices = staged.PreviousState.TerrainLayerIndices ?? Array.Empty<int>(),
                };
            }

            return new BakeManifest.BakedCellState
            {
                Key = staged.WorkItem.Key,
                SectionPath = BuildCellSectionPath(staged.WorkItem),
                Fingerprint = staged.Fingerprint,
                PipelineVersion = CacheFormat.WorldBakePipelineVersion,
                IsInterior = staged.WorkItem.IsInterior,
                GridX = staged.WorkItem.Cell.GridX,
                GridY = staged.WorkItem.Cell.GridY,
                InteriorId = staged.WorkItem.Cell.Name ?? string.Empty,
                MeshIndices = staged.GlobalMeshIndices ?? Array.Empty<int>(),
                MaterialIndices = staged.GlobalMaterialIndices ?? Array.Empty<int>(),
                TextureIndices = staged.GlobalTextureIndices ?? Array.Empty<int>(),
                CollisionIndices = staged.GlobalCollisionIndices ?? Array.Empty<int>(),
                TerrainLayerIndices = staged.GlobalTerrainLayerIndices ?? Array.Empty<int>(),
            };
        }


        private static string BuildCellSectionPath(CellBakeWorkItem workItem)
        {
            return workItem.IsInterior
                ? CachePaths.InteriorCellSectionFile(workItem.Cell.Name ?? string.Empty)
                : CachePaths.ExteriorCellSectionFile(workItem.Cell.GridX, workItem.Cell.GridY);
        }


        private static int[] ToSortedArray(HashSet<int> set)
        {
            var values = new int[set.Count];
            set.CopyTo(values);
            Array.Sort(values);
            return values;
        }


        private static string ComputeFingerprint(
            CellBakeWorkItem workItem,
            List<CellReference> refs,
            LandRecord land,
            RecordIndex recordIndex,
            Dictionary<string, Dictionary<int, string>> ltexMapsBySource,
            bool bakeCombinedCellRenderChunks,
            CombinedStaticExclusionData combinedStaticExclusions)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(workItem.IsInterior);
                w.Write(workItem.Cell.Name ?? string.Empty);
                w.Write(workItem.Cell.GridX);
                w.Write(workItem.Cell.GridY);
                w.Write(workItem.Cell.Environment.HasMood);
                w.Write(workItem.Cell.Environment.HasWater);
                w.Write(workItem.Cell.Environment.AmbientColorRgba);
                w.Write(workItem.Cell.Environment.DirectionalColorRgba);
                w.Write(workItem.Cell.Environment.FogColorRgba);
                w.Write(workItem.Cell.Environment.FogDensity);
                w.Write(workItem.Cell.Environment.WaterHeight);
                w.Write(workItem.Cell.Environment.RegionId ?? string.Empty);
                w.Write(bakeCombinedCellRenderChunks);
                w.Write(CombinedCellRenderPolicyVersion);
                w.Write(refs.Count);
                for (int i = 0; i < refs.Count; i++)
                {
                    var reference = refs[i];
                    w.Write(reference.FormId);
                    w.Write(reference.BaseId ?? string.Empty);
                    w.Write(reference.PosX);
                    w.Write(reference.PosY);
                    w.Write(reference.PosZ);
                    w.Write(reference.RotX);
                    w.Write(reference.RotY);
                    w.Write(reference.RotZ);
                    w.Write(reference.Scale);
                    w.Write(reference.Deleted);
                    w.Write(reference.IsDoor);
                    w.Write(reference.DoorDestCell ?? string.Empty);
                    w.Write(reference.DoorDestX);
                    w.Write(reference.DoorDestY);
                    w.Write(reference.DoorDestZ);
                    w.Write(reference.DoorDestRotX);
                    w.Write(reference.DoorDestRotY);
                    w.Write(reference.DoorDestRotZ);
                    w.Write(reference.SoulId ?? string.Empty);
                    w.Write(IsMutablePlacedOrScriptedStaticRef(reference.FormId, reference.BaseId, combinedStaticExclusions));
                    if (recordIndex.TryGet(reference.BaseId, out var rec))
                    {
                        w.Write(rec.Tag);
                        w.Write(rec.Model ?? string.Empty);
                    }
                    else
                    {
                        w.Write(0u);
                        w.Write(string.Empty);
                    }
                }

                bool hasLand = land != null && land.HasHeights;
                w.Write(hasLand);
                if (hasLand)
                {
                    w.Write(land.GridX);
                    w.Write(land.GridY);
                    w.Write(land.Heights.Length);
                    for (int i = 0; i < land.Heights.Length; i++)
                        w.Write(land.Heights[i]);

                    bool hasNormals = land.Normals != null;
                    w.Write(hasNormals);
                    if (hasNormals)
                    {
                        w.Write(land.Normals.Length);
                        for (int i = 0; i < land.Normals.Length; i++)
                            w.Write(land.Normals[i]);
                    }

                    bool hasVtex = land.VtexIndices != null;
                    w.Write(hasVtex);
                    if (hasVtex)
                    {
                        w.Write(land.VtexIndices.Length);
                        for (int i = 0; i < land.VtexIndices.Length; i++)
                        {
                            ushort vtex = land.VtexIndices[i];
                            w.Write(vtex);
                            w.Write(LtexIndex.ResolveVtexRequired(
                                vtex,
                                ltexMapsBySource,
                                workItem.LandSourcePath,
                                $"{workItem.Key} terrain VTEX slot {i}") ?? string.Empty);
                        }
                    }

                    bool hasWorldMap = land.WorldMap != null;
                    w.Write(hasWorldMap);
                    if (hasWorldMap)
                    {
                        w.Write(land.WorldMap.Length);
                        for (int i = 0; i < land.WorldMap.Length; i++)
                            w.Write(land.WorldMap[i]);
                    }
                }
            }

            ms.Position = 0;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(ms.ToArray());
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }


        private static void AppendTransformed(
            CollisionPayload src,
            Vector3 pos,
            Quaternion rot,
            float scale,
            Vector3 cellOrigin,
            List<Vector3> outVerts,
            List<int> outIndices)
        {
            int baseVert = outVerts.Count;
            for (int i = 0; i < src.Vertices.Length; i++)
                outVerts.Add(pos + rot * (src.Vertices[i] * scale) - cellOrigin);

            for (int i = 0; i < src.Indices.Length; i++)
                outIndices.Add(baseVert + src.Indices[i]);
        }


        }
    }
