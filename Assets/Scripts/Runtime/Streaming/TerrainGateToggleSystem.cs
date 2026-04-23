using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Debug/runtime toggle for terrain visibility mode.
    ///
    /// `F8` flips <see cref="StreamingConfig.GateTerrainByRadius"/> at runtime:
    ///   - Off: terrain stays visible for every loaded cell, refs still stream by radius.
    ///   - On: terrain visibility matches the active cell ring.
    ///
    /// The transition is applied immediately so the mode switch is visually consistent.
    /// Terrain count is tiny (~1400), so a main-thread sync pass is fine here.
    /// </summary>
    [UpdateInGroup(typeof(CellStreamingSystemGroup))]
    [UpdateAfter(typeof(CellLoadWorkerSystem))]
    public partial class TerrainGateToggleSystem : SystemBase
    {
        private static readonly System.Type KeyboardType = System.Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
        private static readonly System.Reflection.PropertyInfo KeyboardCurrentProperty = KeyboardType?.GetProperty("current");
        private static readonly System.Reflection.PropertyInfo F8KeyProperty = KeyboardType?.GetProperty("f8Key");
        private static readonly System.Reflection.PropertyInfo WasPressedThisFrameProperty =
            F8KeyProperty?.PropertyType.GetProperty("wasPressedThisFrame");

        private EntityQuery _singletonQuery;
        private EntityQuery _terrainQuery;
        private bool _initialized;
        private bool _lastGateTerrainByRadius;

        protected override void OnCreate()
        {
            _singletonQuery = SystemAPI.QueryBuilder()
                .WithAllRW<StreamingConfig>()
                .WithAll<LoadedCellsMap>()
                .Build();
            _terrainQuery = SystemAPI.QueryBuilder()
                .WithAll<CellCoord>()
                .WithPresent<MaterialMeshInfo>()
                .Build();
            RequireForUpdate(_singletonQuery);
            RequireForUpdate(_terrainQuery);
        }

        protected override void OnUpdate()
        {
            var singleton = _singletonQuery.GetSingletonEntity();
            var cfg = EntityManager.GetComponentData<StreamingConfig>(singleton);

            bool changed = false;
            if (!_initialized)
            {
                _initialized = true;
                _lastGateTerrainByRadius = cfg.GateTerrainByRadius;
                changed = true;
            }

            if (WasTogglePressed())
            {
                cfg.GateTerrainByRadius = !cfg.GateTerrainByRadius;
                EntityManager.SetComponentData(singleton, cfg);
                changed = true;
                Debug.Log($"[VVardenfell] Terrain radius gating {(cfg.GateTerrainByRadius ? "enabled" : "disabled")} (F8)");
            }

            if (!changed && cfg.GateTerrainByRadius == _lastGateTerrainByRadius)
                return;

            var loaded = EntityManager.GetComponentData<LoadedCellsMap>(singleton);
            SyncTerrainVisibility(cfg.GateTerrainByRadius, loaded);
            _lastGateTerrainByRadius = cfg.GateTerrainByRadius;
        }

        private void SyncTerrainVisibility(bool gateTerrainByRadius, LoadedCellsMap loaded)
        {
            if (!gateTerrainByRadius)
            {
                EntityManager.SetComponentEnabled<MaterialMeshInfo>(_terrainQuery, true);
                return;
            }

            using var terrains = _terrainQuery.ToEntityArray(Allocator.Temp);
            using var coords = _terrainQuery.ToComponentDataArray<CellCoord>(Allocator.Temp);
            for (int i = 0; i < terrains.Length; i++)
            {
                bool shouldBeVisible = loaded.Active.Contains(coords[i].Value);
                EntityManager.SetComponentEnabled<MaterialMeshInfo>(terrains[i], shouldBeVisible);
            }
        }

        private static bool WasTogglePressed()
        {
            if (KeyboardCurrentProperty != null && F8KeyProperty != null && WasPressedThisFrameProperty != null)
            {
                object keyboard = KeyboardCurrentProperty.GetValue(null);
                if (keyboard != null)
                {
                    object f8Key = F8KeyProperty.GetValue(keyboard);
                    if (f8Key != null)
                        return (bool)WasPressedThisFrameProperty.GetValue(f8Key);
                }
            }

            try
            {
                return Input.GetKeyDown(KeyCode.F8);
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }
    }
}
