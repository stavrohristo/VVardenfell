using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class BattleSimulatorPresentationSystem : SystemBase
    {
        BattleSimulatorWindowView _view;
        BattleSimulatorWindowView.CatalogEntry[] _catalog = Array.Empty<BattleSimulatorWindowView.CatalogEntry>();
        BattleSimulatorWindowView.RosterEntry[] _pendingGroupA;
        BattleSimulatorWindowView.RosterEntry[] _pendingGroupB;
        EntityQuery _setupUiQuery;
        bool _hasPendingReady;
        bool _hasPendingReset;
        bool _cameraCenteredForRunningBattle;
        byte _lastPhase;

        protected override void OnCreate()
        {
            _setupUiQuery = GetEntityQuery(ComponentType.ReadOnly<BattleSimulatorSetupUiActive>());
            RequireForUpdate<BattleSimulatorState>();
            RequireForUpdate<BattleSimulatorSpawnRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnDestroy()
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view.gameObject);
            SetSetupUiActive(false);
            _view = null;
        }

        protected override void OnUpdate()
        {
            Entity stateEntity = SystemAPI.GetSingletonEntity<BattleSimulatorState>();
            ApplyPendingRequests(stateEntity);

            if (_view == null)
                CreateView(stateEntity);

            if (_view == null)
                return;

            var state = EntityManager.GetComponentData<BattleSimulatorState>(stateEntity);
            SyncBattleCamera(state);
            _view.SetState(state, (float)SystemAPI.Time.ElapsedTime);
            SetSetupUiActive(state.Phase == (byte)BattleSimulatorPhase.Setup || state.Phase == (byte)BattleSimulatorPhase.Complete);
            _lastPhase = state.Phase;
        }

        void CreateView(Entity stateEntity)
        {
            var contentReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] runtime content blob is unavailable.");

            ref RuntimeContentBlob content = ref contentReference.Blob.Value;
            _catalog = BuildCatalog(ref content);
            var state = EntityManager.GetComponentData<BattleSimulatorState>(stateEntity);
            _view = BattleSimulatorWindowView.Create(
                _catalog,
                state.BattlegroundCell,
                OnReadyRequested,
                OnResetRequested);
        }

        void ApplyPendingRequests(Entity stateEntity)
        {
            if (_hasPendingReset)
            {
                if (!EntityManager.HasComponent<BattleSimulatorResetRequest>(stateEntity))
                    throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] reset request component is missing.");
                EntityManager.SetComponentEnabled<BattleSimulatorResetRequest>(stateEntity, true);
                _hasPendingReset = false;
            }

            if (!_hasPendingReady)
                return;

            var buffer = EntityManager.GetBuffer<BattleSimulatorSpawnRequest>(stateEntity);
            buffer.Clear();
            AppendRoster(buffer, _pendingGroupA, (byte)BattleSimulatorTeamId.GroupA);
            AppendRoster(buffer, _pendingGroupB, (byte)BattleSimulatorTeamId.GroupB);

            ClearShellBlockingState();

            var state = EntityManager.GetComponentData<BattleSimulatorState>(stateEntity);
            state.Phase = (byte)BattleSimulatorPhase.Spawning;
            state.WinningTeam = (byte)BattleSimulatorTeamId.None;
            state.StartedAt = 0f;
            state.CompletedAt = 0f;
            state.GroupATotal = 0;
            state.GroupBTotal = 0;
            state.GroupAAlive = 0;
            state.GroupBAlive = 0;
            state.Status = new Unity.Collections.FixedString128Bytes("Spawning battle groups.");
            EntityManager.SetComponentData(stateEntity, state);

            _pendingGroupA = null;
            _pendingGroupB = null;
            _hasPendingReady = false;
            _cameraCenteredForRunningBattle = false;
            SetSetupUiActive(false);
        }

        void SyncBattleCamera(in BattleSimulatorState state)
        {
            if (_lastPhase != state.Phase && state.Phase != (byte)BattleSimulatorPhase.Running)
                _cameraCenteredForRunningBattle = false;
            if (state.Phase != (byte)BattleSimulatorPhase.Running || _cameraCenteredForRunningBattle)
                return;

            Camera camera = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            var freeCamera = camera.GetComponent<UnityEngine.Rendering.FreeCamera>();
            if (freeCamera == null || !freeCamera.enabled)
                throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] Main Camera must have enabled FreeCamera when battle starts.");

            Vector3 center = ResolveBattlegroundCenter(state.BattlegroundCell);
            Vector3 cameraPosition = center + new Vector3(0f, 58f, -46f);
            Vector3 lookTarget = center + new Vector3(0f, 1.6f, 0f);
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation((lookTarget - cameraPosition).normalized, Vector3.up));
            _cameraCenteredForRunningBattle = true;
        }

        static Vector3 ResolveBattlegroundCenter(Unity.Mathematics.int2 cell)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float x = cell.x * cellMeters + cellMeters * 0.5f;
            float z = cell.y * cellMeters + cellMeters * 0.5f;
            if (!WorldResources.TryGetExteriorCell(cell, out CellData cellData) || cellData == null)
                throw new InvalidOperationException($"[VVardenfell][BattleSimulatorUI] terrain data for battle cell {cell.x},{cell.y} is not loaded.");
            if (!WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cellData, cellMeters * 0.5f, cellMeters * 0.5f, out float height))
                throw new InvalidOperationException($"[VVardenfell][BattleSimulatorUI] cannot sample terrain height for battle cell {cell.x},{cell.y}.");

            return new Vector3(x, height, z);
        }

        void SetSetupUiActive(bool active)
        {
            bool exists = !_setupUiQuery.IsEmptyIgnoreFilter;
            if (active == exists)
                return;

            if (active)
            {
                Entity entity = EntityManager.CreateEntity(typeof(BattleSimulatorSetupUiActive));
                EntityManager.SetName(entity, "VVardenfell.BattleSimulatorSetupUiActive");
                return;
            }

            EntityManager.DestroyEntity(_setupUiQuery);
        }

        void ClearShellBlockingState()
        {
            if (!SystemAPI.TryGetSingletonEntity<RuntimeShellState>(out Entity shellEntity))
                return;

            var shell = EntityManager.GetComponentData<RuntimeShellState>(shellEntity);
            shell.InventoryOpen = 0;
            shell.ContainerOpen = 0;
            shell.PauseMenuOpen = 0;
            shell.ModalOpen = 0;
            shell.SaveLoadBrowserOpen = 0;
            shell.OptionsOpen = 0;
            shell.JournalOpen = 0;
            shell.DialogueOpen = 0;
            RuntimeShellStateUtility.CloseRestMenu(ref shell);
            RuntimeShellStateUtility.ClearModal(ref shell);
            EntityManager.SetComponentData(shellEntity, shell);

            if (SystemAPI.TryGetSingletonEntity<SaveLoadBrowserState>(out Entity saveLoadEntity))
            {
                var saveLoad = EntityManager.GetComponentData<SaveLoadBrowserState>(saveLoadEntity);
                saveLoad.Visible = 0;
                saveLoad.ConfirmAction = 0;
                saveLoad.ConfirmationText = default;
                EntityManager.SetComponentData(saveLoadEntity, saveLoad);
            }
        }

        static void AppendRoster(
            DynamicBuffer<BattleSimulatorSpawnRequest> buffer,
            BattleSimulatorWindowView.RosterEntry[] roster,
            byte team)
        {
            if (roster == null || roster.Length == 0)
                throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] submitted roster is empty.");

            for (int i = 0; i < roster.Length; i++)
            {
                if (!roster[i].Actor.IsValid || roster[i].Count <= 0)
                    throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] submitted roster entry is invalid.");

                buffer.Add(new BattleSimulatorSpawnRequest
                {
                    Team = team,
                    Actor = roster[i].Actor,
                    Count = roster[i].Count,
                });
            }
        }

        void OnReadyRequested(BattleSimulatorWindowView.RosterEntry[] groupA, BattleSimulatorWindowView.RosterEntry[] groupB)
        {
            _pendingGroupA = groupA;
            _pendingGroupB = groupB;
            _hasPendingReady = true;
        }

        void OnResetRequested()
        {
            _hasPendingReset = true;
        }

        static BattleSimulatorWindowView.CatalogEntry[] BuildCatalog(ref RuntimeContentBlob content)
        {
            var entries = new List<BattleSimulatorWindowView.CatalogEntry>(content.Actors.Length);
            for (int i = 0; i < content.Actors.Length; i++)
            {
                var handle = ActorDefHandle.FromIndex(i);
                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, handle);
                if (!SandboxWorldFixtures.CanSpawnBattleSimulatorActor(ref content, ref actor))
                    continue;

                string id = actor.Id.ToString().Trim();
                string displayName = RuntimeContentMetadataResolver.ResolveActorDisplayName(ref content, handle, id);
                string label = string.IsNullOrWhiteSpace(id)
                    ? displayName
                    : $"{displayName} [{id}]";
                entries.Add(new BattleSimulatorWindowView.CatalogEntry(handle, id, label));
            }

            if (entries.Count == 0)
                throw new InvalidOperationException("[VVardenfell][BattleSimulatorUI] no spawnable NPC or creature actors were found in runtime content.");

            return entries.ToArray();
        }
    }
}
