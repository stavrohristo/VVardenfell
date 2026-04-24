using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial class RuntimeShellBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<RuntimeShellState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.RuntimeShell");
            }

            EnsureComponent(runtimeEntity, new RuntimeShellState
            {
                HudVisible = 1,
                SelectedAction = (byte)RuntimeShellMenuActionId.Resume,
            });
            EnsureComponent(runtimeEntity, new RuntimeShellActionRequest());
            EnsureComponent(runtimeEntity, new SaveLoadBrowserState
            {
                DraftSaveName = new FixedString64Bytes("New Save"),
            });
            EnsureComponent(runtimeEntity, new SaveLoadBrowserRequest());
            EnsureComponent(runtimeEntity, new InventoryWindowState
            {
                NormalizedX = 0.015f,
                NormalizedY = 0.54f,
                NormalizedWidth = 0.45f,
                NormalizedHeight = 0.38f,
                SelectedInventoryIndex = -1,
                ActiveCategory = (byte)InventoryWindowCategory.All,
            });
            EnsureComponent(runtimeEntity, new InventoryWindowRequest());
            EnsureComponent(runtimeEntity, new StatsWindowState
            {
                NormalizedX = 0.015f,
                NormalizedY = 0.015f,
                NormalizedWidth = 0.4275f,
                NormalizedHeight = 0.45f,
            });
            EnsureComponent(runtimeEntity, new StatsWindowRequest());
            EnsureComponent(runtimeEntity, new SpellWindowState
            {
                NormalizedX = 0.63f,
                NormalizedY = 0.39f,
                NormalizedWidth = 0.36f,
                NormalizedHeight = 0.51f,
                SelectedSpellIndex = -1,
            });
            EnsureComponent(runtimeEntity, new SpellWindowRequest());
            EnsureComponent(runtimeEntity, new MapWindowState
            {
                Mode = (byte)MapWindowMode.Local,
                NormalizedX = 0.63f,
                NormalizedY = 0.015f,
                NormalizedWidth = 0.36f,
                NormalizedHeight = 0.37f,
                LocalZoom = 1f,
                GlobalZoom = 1f,
            });
            EnsureComponent(runtimeEntity, new MapWindowRequest());
            EnsureComponent(runtimeEntity, new LocalMapDiscoveryState
            {
                MaskResolution = 64,
                RenderResolution = 256,
                RevealRadiusFraction = 0.17f,
            });
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }
    }
}

