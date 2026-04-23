using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    public static class ExteriorInteriorCollisionDebug
    {
        const int MaxSamples = 8;

        public static string DescribeExteriorCell(int gridX, int gridY, World world = null)
        {
            var coord = new int2(gridX, gridY);
            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell == null)
                return $"[VVardenfell][CollisionEvidence] exterior ({gridX},{gridY}) is not loaded in WorldResources.";

            world ??= World.DefaultGameObjectInjectionWorld;
            if (world == null)
                return "[VVardenfell][CollisionEvidence] no active world is available.";

            var em = world.EntityManager;
            CompleteReadDependencies(em);

            var streaming = ReadStreamingState(em);
            var live = GatherExteriorLiveCell(em, coord, streaming.LoadedActiveCells);
            var baked = GatherBakedCell(cell, isExterior: true, coord);
            var sb = new StringBuilder(4096);
            AppendCellReport(sb, $"Exterior ({gridX},{gridY})", cell, baked, live, streaming, includeExteriorStreaming: true);
            return sb.ToString();
        }

        public static string DescribeCurrentExterior(World world = null)
        {
            world ??= World.DefaultGameObjectInjectionWorld;
            if (world == null)
                return "[VVardenfell][CollisionEvidence] no active world is available.";

            var em = world.EntityManager;
            CompleteReadDependencies(em);
            var streaming = ReadStreamingState(em);
            return DescribeExteriorCell(streaming.CameraCell.x, streaming.CameraCell.y, world);
        }

        public static string DescribeActiveInterior(World world = null)
        {
            world ??= World.DefaultGameObjectInjectionWorld;
            if (world == null)
                return "[VVardenfell][CollisionEvidence] no active world is available.";

            var em = world.EntityManager;
            CompleteReadDependencies(em);
            var transition = ReadInteriorTransition(em);
            if (transition.InteriorActive == 0 || transition.ActiveInteriorCellId.Length == 0)
                return "[VVardenfell][CollisionEvidence] no active interior is loaded.";

            string cellId = transition.ActiveInteriorCellId.ToString();
            if (!WorldResources.InteriorCells.TryGetValue(cellId, out var cell) || cell == null)
                return $"[VVardenfell][CollisionEvidence] active interior '{cellId}' is not loaded in WorldResources.";

            var live = GatherInteriorLiveCell(em);
            var baked = GatherBakedCell(cell, isExterior: false, default);
            var sb = new StringBuilder(4096);
            AppendCellReport(sb, $"Interior '{cellId}'", cell, baked, live, default, includeExteriorStreaming: false);
            return sb.ToString();
        }

        public static void LogCurrentComparison(World world = null)
        {
            world ??= World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogWarning("[VVardenfell][CollisionEvidence] no active world is available.");
                return;
            }

            var sb = new StringBuilder(8192);
            sb.AppendLine(DescribeCurrentExterior(world));
            sb.AppendLine();
            sb.Append(DescribeActiveInterior(world));
            Debug.Log(sb.ToString());
        }

        static void CompleteReadDependencies(EntityManager em)
        {
            em.CompleteDependencyBeforeRO<StreamingConfig>();
            em.CompleteDependencyBeforeRO<LoadedCellsMap>();
            em.CompleteDependencyBeforeRO<AvailableCells>();
            em.CompleteDependencyBeforeRO<LoadQueue>();
            em.CompleteDependencyBeforeRO<UnloadList>();
            em.CompleteDependencyBeforeRO<CellLink>();
            em.CompleteDependencyBeforeRO<CellCoord>();
            em.CompleteDependencyBeforeRO<InteriorTransitionState>();
            em.CompleteDependencyBeforeRO<InteriorCellMember>();
            em.CompleteDependencyBeforeRO<PlacedRefIdentity>();
            em.CompleteDependencyBeforeRO<RuntimeColliderSource>();
            em.CompleteDependencyBeforeRO<PhysicsCollider>();
            em.CompleteDependencyBeforeRO<MaterialMeshInfo>();
        }

        static StreamingSnapshot ReadStreamingState(EntityManager em)
        {
            var snapshot = new StreamingSnapshot
            {
                CameraCell = default,
                HasStreamingConfig = false,
                LoadedMapCount = -1,
                LoadedActiveCount = -1,
                AvailableCellCount = -1,
                LoadQueueCount = -1,
                UnloadQueueCount = -1,
            };

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<StreamingConfig>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    var cfg = query.GetSingleton<StreamingConfig>();
                    snapshot.HasStreamingConfig = true;
                    snapshot.CameraCell = cfg.CameraCell;
                    snapshot.ViewRadius = cfg.ViewRadius;
                    snapshot.GateTerrainByRadius = cfg.GateTerrainByRadius;
                    snapshot.ExteriorStreamingPaused = cfg.ExteriorStreamingPaused;
                }
            }

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<LoadedCellsMap>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    var loaded = query.GetSingleton<LoadedCellsMap>();
                    if (loaded.Map.IsCreated)
                        snapshot.LoadedMapCount = loaded.Map.Count;
                    if (loaded.Active.IsCreated)
                    {
                        snapshot.LoadedActiveCount = loaded.Active.Count;
                        snapshot.LoadedActiveCells = loaded.Active;
                    }
                }
            }

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<AvailableCells>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    var available = query.GetSingleton<AvailableCells>();
                    if (available.Set.IsCreated)
                        snapshot.AvailableCellCount = available.Set.Count;
                }
            }

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<LoadQueue>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    var queue = query.GetSingleton<LoadQueue>();
                    if (queue.Queue.IsCreated)
                        snapshot.LoadQueueCount = queue.Queue.Count;
                }
            }

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<UnloadList>()))
            {
                if (!query.IsEmptyIgnoreFilter)
                {
                    var unload = query.GetSingleton<UnloadList>();
                    if (unload.PendingEntityDestroy.IsCreated)
                        snapshot.UnloadQueueCount = unload.PendingEntityDestroy.Length;
                }
            }

            return snapshot;
        }

        static InteriorTransitionState ReadInteriorTransition(EntityManager em)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            return query.IsEmptyIgnoreFilter ? default : query.GetSingleton<InteriorTransitionState>();
        }

        static BakedCellCollisionSnapshot GatherBakedCell(CellData cell, bool isExterior, int2 coord)
        {
            var snapshot = new BakedCellCollisionSnapshot
            {
                RefCount = cell.Refs?.Length ?? 0,
                StaticCollider = isExterior
                    ? WorldResources.TryGetStaticCellCollider(coord, out _)
                    : cell.HasStaticCollider,
                TerrainCollider = isExterior
                    ? WorldResources.TryGetTerrainCollider(coord, out _)
                    : cell.HasTerrainCollider,
                HasTerrain = cell.HasTerrain,
                AuditEntryCount = cell.PlacementAudit?.Entries?.Length ?? 0,
                ContentKindCounts = new Dictionary<int, int>(),
                SpawnModeCounts = new Dictionary<int, int>(),
                RefsWithCollisionByPlacedRef = new HashSet<uint>(),
            };

            var refs = cell.Refs;
            if (refs == null)
                return snapshot;

            for (int i = 0; i < refs.Length; i++)
            {
                var entry = refs[i];
                if (entry.CollisionIndex >= 0)
                {
                    snapshot.RefsWithCollision++;
                    if (entry.PlacedRefId != 0u)
                        snapshot.RefsWithCollisionByPlacedRef.Add(entry.PlacedRefId);
                }
                else
                {
                    snapshot.RefsWithoutCollision++;
                }

                AddCount(snapshot.ContentKindCounts, entry.ContentKind);
                AddCount(snapshot.SpawnModeCounts, entry.SpawnModeRaw);
            }

            return snapshot;
        }

        static LiveCellCollisionSnapshot GatherExteriorLiveCell(
            EntityManager em,
            int2 coord,
            NativeHashSet<int2> activeCells)
        {
            var snapshot = new LiveCellCollisionSnapshot
            {
                IsExterior = true,
                IsActiveExteriorCell = activeCells.IsCreated && activeCells.Contains(coord),
                PlacedRefs = new HashSet<uint>(),
                RuntimeColliderKindCounts = new Dictionary<int, int>(),
            };

            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<CellLink>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var links = query.ToComponentDataArray<CellLink>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (!links[i].Value.Equals(coord))
                    continue;

                AccumulateLiveEntity(em, entities[i], ref snapshot, activeCells, links[i].Value);
            }

            return snapshot;
        }

        static LiveCellCollisionSnapshot GatherInteriorLiveCell(EntityManager em)
        {
            var snapshot = new LiveCellCollisionSnapshot
            {
                IsExterior = false,
                IsActiveExteriorCell = false,
                PlacedRefs = new HashSet<uint>(),
                RuntimeColliderKindCounts = new Dictionary<int, int>(),
            };

            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<InteriorCellMember>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
                AccumulateLiveEntity(em, entities[i], ref snapshot, default, default);

            return snapshot;
        }

        static void AccumulateLiveEntity(
            EntityManager em,
            Entity entity,
            ref LiveCellCollisionSnapshot snapshot,
            NativeHashSet<int2> activeCells,
            int2 exteriorCell)
        {
            snapshot.LiveEntityCount++;

            bool hasPlacedRef = em.HasComponent<PlacedRefIdentity>(entity);
            uint placedRefId = 0u;
            if (hasPlacedRef)
            {
                placedRefId = em.GetComponentData<PlacedRefIdentity>(entity).Value;
                if (placedRefId != 0u)
                    snapshot.PlacedRefs.Add(placedRefId);
            }

            bool hasMaterial = em.HasComponent<MaterialMeshInfo>(entity);
            bool materialEnabled = hasMaterial && em.IsComponentEnabled<MaterialMeshInfo>(entity);
            bool hasColliderSource = em.HasComponent<RuntimeColliderSource>(entity);
            bool hasPhysics = em.HasComponent<PhysicsCollider>(entity);
            bool hasWorldIndex = em.HasComponent<PhysicsWorldIndex>(entity);
            bool isTerrain = em.HasComponent<CellCoord>(entity);
            RuntimeColliderSource colliderSource = default;
            if (hasColliderSource)
                colliderSource = em.GetComponentData<RuntimeColliderSource>(entity);

            if (hasPlacedRef)
                snapshot.PlacedRefEntityCount++;
            if (hasMaterial)
                snapshot.MaterialMeshInfoPresentCount++;
            if (materialEnabled)
                snapshot.MaterialMeshInfoEnabledCount++;
            if (hasColliderSource)
            {
                snapshot.RuntimeColliderSourceCount++;
                AddCount(snapshot.RuntimeColliderKindCounts, (int)colliderSource.Kind);
            }
            if (hasPhysics)
                snapshot.PhysicsColliderCount++;
            if (hasWorldIndex)
                snapshot.PhysicsWorldIndexCount++;
            if (isTerrain)
                snapshot.TerrainEntityCount++;
            else if (hasColliderSource && colliderSource.Kind == RuntimeColliderKind.StaticCell)
                snapshot.StaticOrProxyColliderEntityCount++;
            else if (hasColliderSource && colliderSource.Kind == RuntimeColliderKind.ActivationProxy)
                snapshot.StaticOrProxyColliderEntityCount++;

            if (hasColliderSource && !hasPhysics)
                AddSample(snapshot.SourceNoPhysicsSamples, DescribeEntitySample(em, entity, placedRefId));
            if (hasPhysics && !hasWorldIndex)
                AddSample(snapshot.PhysicsNoWorldIndexSamples, DescribeEntitySample(em, entity, placedRefId));
            if (snapshot.IsExterior
                && materialEnabled
                && activeCells.IsCreated
                && !activeCells.Contains(exteriorCell))
            {
                AddSample(snapshot.VisibleInactiveCellSamples, DescribeEntitySample(em, entity, placedRefId));
            }
        }

        static void AppendCellReport(
            StringBuilder sb,
            string label,
            CellData cell,
            BakedCellCollisionSnapshot baked,
            LiveCellCollisionSnapshot live,
            StreamingSnapshot streaming,
            bool includeExteriorStreaming)
        {
            sb.Append("[VVardenfell][CollisionEvidence] ");
            sb.AppendLine(label);

            if (includeExteriorStreaming)
            {
                sb.Append("Streaming: ");
                if (!streaming.HasStreamingConfig)
                {
                    sb.AppendLine("<missing StreamingConfig>");
                }
                else
                {
                    sb.Append("cameraCell=(");
                    sb.Append(streaming.CameraCell.x);
                    sb.Append(",");
                    sb.Append(streaming.CameraCell.y);
                    sb.Append("), viewRadius=");
                    sb.Append(streaming.ViewRadius);
                    sb.Append(", gateTerrain=");
                    sb.Append(streaming.GateTerrainByRadius ? "yes" : "no");
                    sb.Append(", paused=");
                    sb.Append(streaming.ExteriorStreamingPaused ? "yes" : "no");
                    sb.Append(", loadedMap=");
                    AppendCountOrMissing(sb, streaming.LoadedMapCount);
                    sb.Append(", active=");
                    AppendCountOrMissing(sb, streaming.LoadedActiveCount);
                    sb.Append(", available=");
                    AppendCountOrMissing(sb, streaming.AvailableCellCount);
                    sb.Append(", loadQueue=");
                    AppendCountOrMissing(sb, streaming.LoadQueueCount);
                    sb.Append(", unloadQueue=");
                    AppendCountOrMissing(sb, streaming.UnloadQueueCount);
                    sb.AppendLine();
                }
            }

            sb.Append("Baked: refs=");
            sb.Append(baked.RefCount);
            sb.Append(", collisionRefs=");
            sb.Append(baked.RefsWithCollision);
            sb.Append(", noCollisionRefs=");
            sb.Append(baked.RefsWithoutCollision);
            sb.Append(", staticCollider=");
            sb.Append(baked.StaticCollider ? "yes" : "no");
            sb.Append(", terrain=");
            sb.Append(baked.HasTerrain ? "yes" : "no");
            sb.Append(", terrainCollider=");
            sb.Append(baked.TerrainCollider ? "yes" : "no");
            sb.Append(", placementAudit=");
            sb.Append(baked.AuditEntryCount);
            sb.AppendLine();

            sb.Append("Baked content kinds: ");
            AppendContentKindCounts(sb, baked.ContentKindCounts);
            sb.AppendLine();

            sb.Append("Baked spawn modes: ");
            AppendSpawnModeCounts(sb, baked.SpawnModeCounts);
            sb.AppendLine();

            sb.Append("Live: entities=");
            sb.Append(live.LiveEntityCount);
            sb.Append(", placedRefEntities=");
            sb.Append(live.PlacedRefEntityCount);
            sb.Append(", uniquePlacedRefs=");
            sb.Append(live.PlacedRefs?.Count ?? 0);
            sb.Append(", mmiPresent=");
            sb.Append(live.MaterialMeshInfoPresentCount);
            sb.Append(", mmiEnabled=");
            sb.Append(live.MaterialMeshInfoEnabledCount);
            sb.Append(", colliderSources=");
            sb.Append(live.RuntimeColliderSourceCount);
            sb.Append(", physicsColliders=");
            sb.Append(live.PhysicsColliderCount);
            sb.Append(", physicsWorldIndex=");
            sb.Append(live.PhysicsWorldIndexCount);
            sb.Append(", terrainEntities=");
            sb.Append(live.TerrainEntityCount);
            sb.Append(", staticOrProxyEntities=");
            sb.Append(live.StaticOrProxyColliderEntityCount);
            if (live.IsExterior)
            {
                sb.Append(", activeExteriorCell=");
                sb.Append(live.IsActiveExteriorCell ? "yes" : "no");
            }
            sb.AppendLine();

            sb.Append("Live collider kinds: ");
            AppendRuntimeColliderKindCounts(sb, live.RuntimeColliderKindCounts);
            sb.AppendLine();

            AppendBakedLiveMismatchSamples(sb, cell, baked, live);
            AppendSampleList(sb, "collider source without PhysicsCollider", live.SourceNoPhysicsSamples);
            AppendSampleList(sb, "PhysicsCollider without PhysicsWorldIndex", live.PhysicsNoWorldIndexSamples);
            AppendSampleList(sb, "visible entity in inactive exterior cell", live.VisibleInactiveCellSamples);
        }

        static void AppendBakedLiveMismatchSamples(
            StringBuilder sb,
            CellData cell,
            BakedCellCollisionSnapshot baked,
            LiveCellCollisionSnapshot live)
        {
            if (baked.RefsWithCollisionByPlacedRef == null || live.PlacedRefs == null)
                return;

            var samples = new List<string>(MaxSamples);
            foreach (uint placedRefId in baked.RefsWithCollisionByPlacedRef)
            {
                if (live.PlacedRefs.Contains(placedRefId))
                    continue;

                string baseId = FindAuditBaseId(cell, placedRefId);
                samples.Add($"placedRef=0x{placedRefId:X8}, base='{baseId}'");
                if (samples.Count >= MaxSamples)
                    break;
            }

            AppendSampleList(sb, "baked collision ref without live placed-ref entity", samples);
        }

        static string DescribeEntitySample(EntityManager em, Entity entity, uint placedRefId)
        {
            var sb = new StringBuilder(96);
            sb.Append(entity);
            if (placedRefId != 0u)
            {
                sb.Append(", placedRef=0x");
                sb.Append(placedRefId.ToString("X8"));
            }

            if (em.HasComponent<CellLink>(entity))
            {
                var coord = em.GetComponentData<CellLink>(entity).Value;
                sb.Append(", cell=(");
                sb.Append(coord.x);
                sb.Append(",");
                sb.Append(coord.y);
                sb.Append(")");
            }

            if (em.HasComponent<CellCoord>(entity))
                sb.Append(", terrain=yes");
            if (em.HasComponent<InteriorCellMember>(entity))
                sb.Append(", interior=yes");

            return sb.ToString();
        }

        static string FindAuditBaseId(CellData cell, uint placedRefId)
        {
            var entries = cell?.PlacementAudit?.Entries;
            if (entries == null)
                return string.Empty;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].PlacedRefId == placedRefId)
                    return entries[i].BaseId ?? string.Empty;
            }

            return string.Empty;
        }

        static void AddCount(Dictionary<int, int> counts, int key)
        {
            counts.TryGetValue(key, out int value);
            counts[key] = value + 1;
        }

        static void AddSample(List<string> samples, string value)
        {
            if (samples.Count < MaxSamples)
                samples.Add(value);
        }

        static void AppendSampleList(StringBuilder sb, string label, List<string> samples)
        {
            if (samples == null || samples.Count == 0)
                return;

            sb.Append("Samples - ");
            sb.Append(label);
            sb.Append(": ");
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(samples[i]);
            }
            sb.AppendLine();
        }

        static void AppendContentKindCounts(StringBuilder sb, Dictionary<int, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                sb.Append("<none>");
                return;
            }

            bool first = true;
            foreach (var kv in counts)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(ContentKindLabel(kv.Key));
                sb.Append("=");
                sb.Append(kv.Value);
            }
        }

        static void AppendSpawnModeCounts(StringBuilder sb, Dictionary<int, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                sb.Append("<none>");
                return;
            }

            bool first = true;
            foreach (var kv in counts)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(SpawnModeLabel(kv.Key));
                sb.Append("=");
                sb.Append(kv.Value);
            }
        }

        static string ContentKindLabel(int raw)
        {
            return raw switch
            {
                (int)ContentReferenceKind.None => "None",
                (int)ContentReferenceKind.Actor => "Actor",
                (int)ContentReferenceKind.Activator => "Activator",
                (int)ContentReferenceKind.Door => "Door",
                (int)ContentReferenceKind.Container => "Container",
                (int)ContentReferenceKind.Item => "Item",
                (int)ContentReferenceKind.Light => "Light",
                (int)ContentReferenceKind.Static => "Static",
                (int)ContentReferenceKind.LeveledCreature => "LeveledCreature",
                (int)ContentReferenceKind.LeveledItem => "LeveledItem",
                _ => $"Unknown({raw})",
            };
        }

        static string SpawnModeLabel(int raw)
        {
            return raw switch
            {
                (int)RefSpawnMode.RenderShard => "RenderShard",
                (int)RefSpawnMode.ModelPrefab => "ModelPrefab",
                _ => $"Unknown({raw})",
            };
        }

        static void AppendRuntimeColliderKindCounts(StringBuilder sb, Dictionary<int, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                sb.Append("<none>");
                return;
            }

            bool first = true;
            foreach (var kv in counts)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(RuntimeColliderKindLabel(kv.Key));
                sb.Append("=");
                sb.Append(kv.Value);
            }
        }

        static string RuntimeColliderKindLabel(int raw)
        {
            return raw switch
            {
                (int)RuntimeColliderKind.None => "None",
                (int)RuntimeColliderKind.TerrainCell => "TerrainCell",
                (int)RuntimeColliderKind.StaticCell => "StaticCell",
                (int)RuntimeColliderKind.PlacedRef => "PlacedRef",
                (int)RuntimeColliderKind.ActivationProxy => "ActivationProxy",
                (int)RuntimeColliderKind.RuntimeSpawn => "RuntimeSpawn",
                (int)RuntimeColliderKind.Player => "Player",
                _ => $"Unknown({raw})",
            };
        }

        static void AppendCountOrMissing(StringBuilder sb, int count)
        {
            if (count < 0)
                sb.Append("<missing>");
            else
                sb.Append(count);
        }

        struct StreamingSnapshot
        {
            public bool HasStreamingConfig;
            public int2 CameraCell;
            public int ViewRadius;
            public bool GateTerrainByRadius;
            public bool ExteriorStreamingPaused;
            public int LoadedMapCount;
            public int LoadedActiveCount;
            public int AvailableCellCount;
            public int LoadQueueCount;
            public int UnloadQueueCount;
            public NativeHashSet<int2> LoadedActiveCells;
        }

        struct BakedCellCollisionSnapshot
        {
            public int RefCount;
            public int RefsWithCollision;
            public int RefsWithoutCollision;
            public bool StaticCollider;
            public bool TerrainCollider;
            public bool HasTerrain;
            public int AuditEntryCount;
            public Dictionary<int, int> ContentKindCounts;
            public Dictionary<int, int> SpawnModeCounts;
            public HashSet<uint> RefsWithCollisionByPlacedRef;
        }

        struct LiveCellCollisionSnapshot
        {
            public bool IsExterior;
            public bool IsActiveExteriorCell;
            public int LiveEntityCount;
            public int PlacedRefEntityCount;
            public int MaterialMeshInfoPresentCount;
            public int MaterialMeshInfoEnabledCount;
            public int RuntimeColliderSourceCount;
            public int PhysicsColliderCount;
            public int PhysicsWorldIndexCount;
            public int TerrainEntityCount;
            public int StaticOrProxyColliderEntityCount;
            public HashSet<uint> PlacedRefs;
            public Dictionary<int, int> RuntimeColliderKindCounts;
            public List<string> SourceNoPhysicsSamples => _sourceNoPhysicsSamples ??= new List<string>(MaxSamples);
            public List<string> PhysicsNoWorldIndexSamples => _physicsNoWorldIndexSamples ??= new List<string>(MaxSamples);
            public List<string> VisibleInactiveCellSamples => _visibleInactiveCellSamples ??= new List<string>(MaxSamples);
            List<string> _sourceNoPhysicsSamples;
            List<string> _physicsNoWorldIndexSamples;
            List<string> _visibleInactiveCellSamples;
        }
    }
}
