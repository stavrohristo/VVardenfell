using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
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
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, RuntimeShellState>(
                EntityManager,
                "VVardenfell.RuntimeShell");

            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeShellState
            {
                HudVisible = 1,
                SelectedAction = (byte)RuntimeShellMenuActionId.Resume,
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeShellActionRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeSubtitleState());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeEnemyHealthBarState());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new SaveLoadBrowserState
            {
                DraftSaveName = new FixedString64Bytes("New Save"),
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new SaveLoadBrowserRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InventoryWindowState
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
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InventoryWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InventoryItemActionRequest());
            RuntimeBootstrapUtility.EnsureBuffer<InventoryItemActionRequestElement>(EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InventoryHeldItemState
            {
                InventoryIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new StatsWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.015f,
                    Y = 0.015f,
                    Width = 0.4275f,
                    Height = 0.45f,
                },
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new StatsWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new SpellWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.63f,
                    Y = 0.39f,
                    Width = 0.36f,
                    Height = 0.51f,
                },
                SelectedSpellIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new SpellWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new MapWindowState
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
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new MapWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new JournalWindowState
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
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new JournalWindowRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new MorrowindDialogueSession
            {
                SelectedTopicDialogueIndex = -1,
                ChoiceDialogueIndex = -1,
                LastInfoIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new MorrowindDialogueResponseRequest());
            RuntimeBootstrapUtility.EnsureBuffer<MorrowindDialogueSessionLine>(EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureBuffer<MorrowindDialogueChoice>(EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new LocalMapDiscoveryState
            {
                MaskResolution = 64,
                RenderResolution = 256,
                RevealRadiusFraction = 0.17f,
            });
            RuntimeBootstrapRequestUtility.Consume<RuntimeShellBootstrapRequest>(EntityManager);
        }
    }
}

