using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Config;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial struct RuntimeShellBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeShellBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, RuntimeShellState>(
                systemState.EntityManager,
                "VVardenfell.RuntimeShell");

            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeShellState
            {
                HudVisible = 0,
                SelectedAction = (byte)RuntimeShellMenuActionId.Resume,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeShellActionRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new CharacterGenerationState());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new CharacterGenerationRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeSubtitleState());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, LoadHudPreferences());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeEnemyHealthBarState());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new SaveLoadBrowserState
            {
                DraftSaveName = new FixedString64Bytes("New Save"),
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new SaveLoadBrowserRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InventoryWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.015f,
                    Y = 0.6422f,
                    Width = 0.3125f,
                    Height = 0.2778f,
                },
                SelectedInventoryIndex = -1,
                ActiveCategory = (byte)InventoryWindowCategory.All,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InventoryWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InventoryItemActionRequest());
            RuntimeBootstrapUtility.EnsureBuffer<InventoryItemActionRequestElement>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InventoryHeldItemState
            {
                InventoryIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new StatsWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.015f,
                    Y = 0.015f,
                    Width = 0.4275f,
                    Height = 0.45f,
                },
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new StatsWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new SpellWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.63f,
                    Y = 0.39f,
                    Width = 0.36f,
                    Height = 0.51f,
                },
                SelectedSpellIndex = -1,
                SelectedInventoryIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new SpellWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MapWindowState
            {
                Mode = (byte)MapWindowMode.Local,
                Rect = new RuntimeWindowRect
                {
                    X = 0.63f,
                    Y = 0.015f,
                    Width = 0.36f,
                    Height = 0.37f,
                },
                LocalZoom = 1f,
                GlobalZoom = 1f,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MapWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new JournalWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.12f,
                    Y = 0.08f,
                    Width = 0.76f,
                    Height = 0.78f,
                },
                SelectedDialogueIndex = -1,
                Page = -1,
                QuestScrollY = 1f,
                EntryScrollY = 1f,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new JournalWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MorrowindDialogueSession
            {
                SelectedTopicDialogueIndex = -1,
                ChoiceDialogueIndex = -1,
                LastInfoIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MorrowindDialogueResponseRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MorrowindDialogueServiceWindowState
            {
                BarterOffer = 0,
            });
            RuntimeBootstrapUtility.EnsureBuffer<MorrowindDialogueSessionLine>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureBuffer<MorrowindDialogueChoice>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureBuffer<MorrowindDialogueBarterStagedItem>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new PlayerTravelingState());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new LocalMapDiscoveryState
            {
                MaskResolution = 64,
                RenderResolution = 256,
                RevealRadiusFraction = 0.17f,
            });
            RuntimeBootstrapRequestUtility.Consume<RuntimeShellBootstrapRequest>(systemState.EntityManager);
        }

        static RuntimeHudPreferences LoadHudPreferences()
        {
            if (ConfigStorage.TryLoad(out var config) && config != null)
            {
                return new RuntimeHudPreferences
                {
                    ShowCrosshair = config.ShowCrosshair ? (byte)1 : (byte)0,
                    ShowSubtitles = config.ShowSubtitles ? (byte)1 : (byte)0,
                };
            }

            return new RuntimeHudPreferences
            {
                ShowCrosshair = 1,
                ShowSubtitles = 1,
            };
        }
    }
}
