using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    public partial struct BattleSimulatorSpawnSystem : ISystem
    {
        const float FormationSpacingMeters = 1.2f;
        const float FormationStandoffMeters = 8f;
        const int FormationColumns = 32;
        const int PreloadedRadius = 1;

        EntityQuery _unitQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BattleSimulatorUnitTag, PlacedRefIdentity>()
                .Build(ref systemState);

            systemState.RequireForUpdate<BattleSimulatorState>();
            systemState.RequireForUpdate<BattleSimulatorSpawnRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeSpawnState>();
            systemState.RequireForUpdate<RuntimeSpawnedRef>();
            systemState.RequireForUpdate<WorldJournalEntry>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<LoadedCellsMap>();
            systemState.RequireForUpdate<AvailableCells>();
            systemState.RequireForUpdate<StreamingConfig>();
            systemState.RequireForUpdate<InteriorTransitionState>();
            systemState.RequireForUpdate<InteriorSpawnedEntity>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity stateEntity = SystemAPI.GetSingletonEntity<BattleSimulatorState>();
            var simulatorState = SystemAPI.GetComponent<BattleSimulatorState>(stateEntity);
            if (systemState.EntityManager.HasComponent<BattleSimulatorResetRequest>(stateEntity)
                && systemState.EntityManager.IsComponentEnabled<BattleSimulatorResetRequest>(stateEntity))
            {
                Entity resetLookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
                var resetLogicalLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
                DestroyExistingUnits(ref systemState, ref resetLogicalLookup);
                systemState.EntityManager.SetComponentData(resetLookupEntity, resetLogicalLookup);
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(systemState.EntityManager);
                systemState.EntityManager.SetComponentEnabled<BattleSimulatorResetRequest>(stateEntity, false);
                systemState.EntityManager.GetBuffer<BattleSimulatorSpawnRequest>(stateEntity).Clear();
                simulatorState.Phase = (byte)BattleSimulatorPhase.Setup;
                simulatorState.WinningTeam = (byte)BattleSimulatorTeamId.None;
                simulatorState.StartedAt = 0f;
                simulatorState.CompletedAt = 0f;
                simulatorState.GroupATotal = 0;
                simulatorState.GroupBTotal = 0;
                simulatorState.GroupAAlive = 0;
                simulatorState.GroupBAlive = 0;
                simulatorState.Status = new FixedString128Bytes("Build two battle groups, then press Ready.");
                systemState.EntityManager.SetComponentData(stateEntity, simulatorState);
                return;
            }

            if (simulatorState.Phase != (byte)BattleSimulatorPhase.Spawning)
                return;

            DynamicBuffer<BattleSimulatorSpawnRequest> requestBuffer = systemState.EntityManager.GetBuffer<BattleSimulatorSpawnRequest>(stateEntity);
            if (requestBuffer.Length == 0)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] Ready requested without roster entries.");

            NativeArray<BattleSimulatorSpawnRequest> requests = new(requestBuffer.Length, Allocator.Temp);
            for (int i = 0; i < requestBuffer.Length; i++)
                requests[i] = requestBuffer[i];
            requestBuffer.Clear();

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] runtime content blob is unavailable.");

            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            for (int i = 0; i < requests.Length; i++)
                ValidateRosterEntry(ref content, requests[i]);
            int totalA = CountTeamRequests(ref content, requests, (byte)BattleSimulatorTeamId.GroupA);
            int totalB = CountTeamRequests(ref content, requests, (byte)BattleSimulatorTeamId.GroupB);
            if (totalA <= 0 || totalB <= 0)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] both battle groups must contain at least one spawnable actor.");

            var spawnEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            var spawnState = SystemAPI.GetSingleton<RuntimeSpawnState>();
            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            Entity transitionEntity = SystemAPI.GetSingletonEntity<InteriorTransitionState>();
            Entity loadedEntity = SystemAPI.GetSingletonEntity<LoadedCellsMap>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            var available = SystemAPI.GetSingleton<AvailableCells>();
            var config = SystemAPI.GetSingleton<StreamingConfig>();

            DestroyExistingUnits(ref systemState, ref logicalLookup);

            var cell = simulatorState.BattlegroundCell;
            ValidateFormationFootprint(totalA, totalB, cell);
            ValidateFormationPathGrids(ref content, totalA, totalB, cell);

            int spawnedA = SpawnTeam(
                ref systemState,
                ref content,
                requests,
                (byte)BattleSimulatorTeamId.GroupA,
                totalA,
                totalB,
                cell,
                ref spawnState,
                spawnEntity,
                ref logicalLookup,
                transitionEntity,
                ref loaded,
                ref available,
                config);

            int spawnedB = SpawnTeam(
                ref systemState,
                ref content,
                requests,
                (byte)BattleSimulatorTeamId.GroupB,
                totalB,
                totalA,
                cell,
                ref spawnState,
                spawnEntity,
                ref logicalLookup,
                transitionEntity,
                ref loaded,
                ref available,
                config);

            if (spawnedA != totalA || spawnedB != totalB)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] roster expansion did not spawn the expected unit counts.");

            systemState.EntityManager.SetComponentData(spawnEntity, spawnState);
            systemState.EntityManager.SetComponentData(lookupEntity, logicalLookup);
            systemState.EntityManager.SetComponentData(loadedEntity, loaded);
            systemState.EntityManager.SetComponentData(SystemAPI.GetSingletonEntity<AvailableCells>(), available);
            ActiveExplicitRefLookupLifecycleUtility.MarkDirty(systemState.EntityManager);

            simulatorState.Phase = (byte)BattleSimulatorPhase.Running;
            simulatorState.StartedAt = (float)SystemAPI.Time.ElapsedTime;
            simulatorState.CompletedAt = 0f;
            simulatorState.WinningTeam = (byte)BattleSimulatorTeamId.None;
            simulatorState.GroupATotal = totalA;
            simulatorState.GroupBTotal = totalB;
            simulatorState.GroupAAlive = totalA;
            simulatorState.GroupBAlive = totalB;
            simulatorState.Status = new FixedString128Bytes("Battle running.");
            systemState.EntityManager.SetComponentData(stateEntity, simulatorState);
            requests.Dispose();
        }

        void DestroyExistingUnits(ref SystemState systemState, ref LogicalRefLookup logicalLookup)
        {
            if (_unitQuery.IsEmptyIgnoreFilter)
                return;

            using NativeArray<Entity> units = _unitQuery.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < units.Length; i++)
                LogicalRefDestroyUtility.QueueDestroyLogicalRef(systemState.EntityManager, ref ecb, units[i], ref logicalLookup);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static int CountTeamRequests(ref RuntimeContentBlob content, NativeArray<BattleSimulatorSpawnRequest> requests, byte team)
        {
            int total = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.Team != team)
                    continue;

                ValidateRosterEntry(ref content, request);
                total += request.Count;
            }

            return total;
        }

        static int SpawnTeam(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            NativeArray<BattleSimulatorSpawnRequest> requests,
            byte team,
            int teamTotal,
            int opposingTotal,
            int2 battlegroundCell,
            ref RuntimeSpawnState spawnState,
            Entity spawnEntity,
            ref LogicalRefLookup logicalLookup,
            Entity transitionEntity,
            ref LoadedCellsMap loaded,
            ref AvailableCells available,
            StreamingConfig config)
        {
            int spawned = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.Team != team)
                    continue;

                for (int count = 0; count < request.Count; count++)
                {
                    float3 position = ResolveFormationPosition(team, spawned, teamTotal, opposingTotal, battlegroundCell);
                    GroundFormationPosition(ref content, battlegroundCell, ref position);
                    quaternion rotation = team == (byte)BattleSimulatorTeamId.GroupA
                        ? quaternion.LookRotationSafe(new float3(0f, 0f, 1f), math.up())
                        : quaternion.LookRotationSafe(new float3(0f, 0f, -1f), math.up());
                    SpawnUnit(
                        ref systemState,
                        ref content,
                        request.Actor,
                        team,
                        position,
                        rotation,
                        ref spawnState,
                        spawnEntity,
                        ref logicalLookup,
                        transitionEntity,
                        ref loaded,
                        ref available,
                        config);
                    spawned++;
                }
            }

            return spawned;
        }

        static void SpawnUnit(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            ActorDefHandle actor,
            byte team,
            float3 position,
            quaternion rotation,
            ref RuntimeSpawnState spawnState,
            Entity spawnEntity,
            ref LogicalRefLookup logicalLookup,
            Entity transitionEntity,
            ref LoadedCellsMap loaded,
            ref AvailableCells available,
            StreamingConfig config)
        {
            int2 exteriorCell = WorldBootstrap.WorldPositionToCell(position);
            if (!RuntimeContentBlobUtility.TryGetExteriorPathGridHandle(ref content, exteriorCell.x, exteriorCell.y, out var pathGridHandle) || !pathGridHandle.IsValid)
                throw new System.InvalidOperationException($"[VVardenfell][BattleSimulator] formation cell {exteriorCell.x},{exteriorCell.y} has no exterior pathgrid; battle units cannot plan combat movement there.");

            EnsureExteriorCapacity(ref available);
            available.Set.Add(exteriorCell);
            if (!loaded.Active.Contains(exteriorCell))
                throw new System.InvalidOperationException($"[VVardenfell][BattleSimulator] formation cell {exteriorCell.x},{exteriorCell.y} is not active yet.");

            spawnState.NextRuntimeRefId += 1u;
            uint runtimeRefId = RuntimeSpawnRegistryUtility.ComposeRuntimeRefId(spawnState.NextRuntimeRefId);
            var contentReference = new ContentReference
            {
                Kind = ContentReferenceKind.Actor,
                HandleValue = actor.Value,
            };

            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            bool queued = RuntimeSpawnFactory.QueueActorSpawn(
                systemState.EntityManager,
                ref createEcb,
                ref content,
                contentReference,
                runtimeRefId,
                position,
                rotation,
                1f,
                isInterior: false,
                exteriorCell,
                default,
                (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession);
            createEcb.Playback(systemState.EntityManager);
            createEcb.Dispose();

            if (!queued)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] failed to create battle unit logical ref.");

            var materializeEcb = new EntityCommandBuffer(Allocator.Temp);
            Entity logicalEntity = RuntimeSpawnFactory.QueueMaterializeSpawn(
                systemState.EntityManager,
                ref materializeEcb,
                runtimeRefId,
                isInterior: false,
                exteriorCell,
                exteriorActive: true,
                ref logicalLookup,
                transitionEntity);
            materializeEcb.Playback(systemState.EntityManager);
            materializeEcb.Dispose();

            if (logicalEntity == Entity.Null)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] failed to materialize battle unit logical ref.");

            MorrowindScriptAiPackageUtility.EnsureActorAiComponents(ref content, systemState.EntityManager, logicalEntity, runtimeRefId);
            if (!systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(logicalEntity))
                systemState.EntityManager.AddBuffer<ActorAiPackageRuntime>(logicalEntity);

            systemState.EntityManager.AddComponentData(logicalEntity, new BattleSimulatorTeam { Value = team });
            systemState.EntityManager.AddComponent<BattleSimulatorUnitTag>(logicalEntity);
            if (systemState.EntityManager.HasComponent<ActorAiSettingsState>(logicalEntity))
            {
                var ai = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(logicalEntity);
                ai.Fight = 100;
                ai.Flee = 0;
                ai.Alarm = 0;
                systemState.EntityManager.SetComponentData(logicalEntity, ai);
            }

            if (systemState.EntityManager.HasBuffer<ActorCombatTarget>(logicalEntity))
                systemState.EntityManager.GetBuffer<ActorCombatTarget>(logicalEntity).Clear();
            if (systemState.EntityManager.HasComponent<ActorActiveCombatTarget>(logicalEntity))
                systemState.EntityManager.SetComponentEnabled<ActorActiveCombatTarget>(logicalEntity, false);

            var spawnedRef = new RuntimeSpawnedRef
            {
                RuntimeRefId = runtimeRefId,
                Content = contentReference,
                Position = position,
                Rotation = rotation,
                Scale = 1f,
                ExteriorCell = exteriorCell,
                LogicalEntity = logicalEntity,
                IsInterior = 0,
                PersistencePolicy = (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession,
                Alive = 1,
            };

            DynamicBuffer<RuntimeSpawnedRef> spawnedRegistry = systemState.EntityManager.GetBuffer<RuntimeSpawnedRef>(spawnEntity);
            spawnedRegistry.Add(spawnedRef);
            WorldJournalUtility.AppendRuntimeSpawn(systemState.EntityManager, spawnedRef);
        }

        static float3 ResolveFormationPosition(byte team, int index, int teamTotal, int opposingTotal, int2 battlegroundCell)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float3 center = new(
                battlegroundCell.x * cellMeters + cellMeters * 0.5f,
                0f,
                battlegroundCell.y * cellMeters + cellMeters * 0.5f);

            int columns = ResolveFormationColumns(teamTotal);
            int rows = ResolveFormationRows(teamTotal, columns);
            int row = index / columns;
            int col = index - row * columns;
            float x = center.x + (col - (columns - 1) * 0.5f) * FormationSpacingMeters;
            float depth = (rows - 1) * FormationSpacingMeters;
            _ = opposingTotal;

            if (team == (byte)BattleSimulatorTeamId.GroupA)
            {
                float formationCenterZ = center.z - FormationStandoffMeters * 0.5f - depth * 0.5f;
                float z = formationCenterZ + ((rows - 1) * 0.5f - row) * FormationSpacingMeters;
                return new float3(x, 0f, z);
            }
            else
            {
                float formationCenterZ = center.z + FormationStandoffMeters * 0.5f + depth * 0.5f;
                float z = formationCenterZ + (row - (rows - 1) * 0.5f) * FormationSpacingMeters;
                return new float3(x, 0f, z);
            }
        }

        static int ResolveFormationColumns(int count)
        {
            return math.clamp(count, 1, FormationColumns);
        }

        static int ResolveFormationRows(int count, int columns)
        {
            return math.max(1, (count + columns - 1) / columns);
        }

        static void ValidateFormationFootprint(int totalA, int totalB, int2 battlegroundCell)
        {
            int columnsA = ResolveFormationColumns(totalA);
            int rowsA = ResolveFormationRows(totalA, columnsA);
            int columnsB = ResolveFormationColumns(totalB);
            int rowsB = ResolveFormationRows(totalB, columnsB);
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float maxWidth = math.max(columnsA, columnsB) * FormationSpacingMeters;
            float maxDepth = (rowsA + rowsB) * FormationSpacingMeters + FormationStandoffMeters;
            float maxAllowed = cellMeters * (PreloadedRadius * 2 + 1);

            if (maxWidth > maxAllowed || maxDepth > maxAllowed)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] formation footprint exceeds the preloaded 3x3 battleground neighborhood.");

            _ = battlegroundCell;
        }

        static void ValidateFormationPathGrids(ref RuntimeContentBlob content, int totalA, int totalB, int2 battlegroundCell)
        {
            ValidateTeamFormationPathGrids(ref content, (byte)BattleSimulatorTeamId.GroupA, totalA, totalB, battlegroundCell);
            ValidateTeamFormationPathGrids(ref content, (byte)BattleSimulatorTeamId.GroupB, totalB, totalA, battlegroundCell);
        }

        static void ValidateTeamFormationPathGrids(ref RuntimeContentBlob content, byte team, int teamTotal, int opposingTotal, int2 battlegroundCell)
        {
            int lastX = int.MinValue;
            int lastY = int.MinValue;
            for (int i = 0; i < teamTotal; i++)
            {
                float3 position = ResolveFormationPosition(team, i, teamTotal, opposingTotal, battlegroundCell);
                int2 cell = WorldBootstrap.WorldPositionToCell(position);
                if (cell.x == lastX && cell.y == lastY)
                    continue;

                if (!RuntimeContentBlobUtility.TryGetExteriorPathGridHandle(ref content, cell.x, cell.y, out var handle) || !handle.IsValid)
                    throw new System.InvalidOperationException($"[VVardenfell][BattleSimulator] formation cell {cell.x},{cell.y} has no exterior pathgrid; choose a pathgrid-backed battleground.");

                lastX = cell.x;
                lastY = cell.y;
            }
        }

        static void GroundFormationPosition(ref RuntimeContentBlob content, int2 battlegroundCell, ref float3 position)
        {
            int2 cell = WorldBootstrap.WorldPositionToCell(position);
            int dx = math.abs(cell.x - battlegroundCell.x);
            int dz = math.abs(cell.y - battlegroundCell.y);
            if (dx > PreloadedRadius || dz > PreloadedRadius)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] formation position exited the preloaded 3x3 battleground neighborhood.");

            if (!WorldResources.TryGetExteriorCell(cell, out CellData cellData) || cellData == null)
                throw new System.InvalidOperationException($"[VVardenfell][BattleSimulator] terrain data for cell {cell.x},{cell.y} is not loaded.");

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float localX = position.x - cell.x * cellMeters;
            float localZ = position.z - cell.y * cellMeters;
            if (!WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cellData, localX, localZ, out float height))
                throw new System.InvalidOperationException($"[VVardenfell][BattleSimulator] cannot ground battle unit in cell {cell.x},{cell.y}.");

            position.y = height;
            _ = content;
        }

        static void ValidateRosterEntry(ref RuntimeContentBlob content, BattleSimulatorSpawnRequest request)
        {
            if (request.Team != (byte)BattleSimulatorTeamId.GroupA && request.Team != (byte)BattleSimulatorTeamId.GroupB)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] roster entry has an invalid team.");
            if (request.Count <= 0)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] roster entry count must be positive.");
            if (!request.Actor.IsValid || request.Actor.Index < 0 || request.Actor.Index >= content.Actors.Length)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] roster entry actor handle is invalid.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, request.Actor);
            if (actor.Kind != ActorDefKind.Npc && actor.Kind != ActorDefKind.Creature)
                throw new System.InvalidOperationException("[VVardenfell][BattleSimulator] roster entry is not an NPC or creature actor.");
        }

        static void EnsureExteriorCapacity(ref AvailableCells available)
        {
            if (!available.Set.IsCreated)
                return;

            int count = available.Set.Count;
            if (count < available.Set.Capacity)
                return;

            available.Set.Capacity = math.max(available.Set.Capacity * 2, count + 1);
        }
    }
}
