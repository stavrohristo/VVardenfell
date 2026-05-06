using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;

namespace VVardenfell.Runtime.UI.Shell
{
    public static class RuntimeShellRequestBridge
    {
        delegate void RequestMutation<T>(ref T request)
            where T : unmanaged, IComponentData;

        delegate void SaveLoadRequestMutation(ref SaveLoadBrowserRequest request, in SaveLoadBrowserState state);

        public static bool TryRequestAction(RuntimeShellMenuActionId action, out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                (ref RuntimeShellActionRequest request) =>
                {
                    request.Pending = 1;
                    request.DismissModal = 0;
                    request.Action = (byte)action;
                },
                out error);
        }

        public static bool TryDismissModal(out string error)
            => TryDismissModal(-1, out error);

        public static bool TryDismissModal(int buttonIndex, out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                (ref RuntimeShellActionRequest request) =>
                {
                    request.DismissModal = 1;
                    request.DismissModalButton = buttonIndex;
                },
                out error);
        }

        public static bool TryCloseOptions(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.CloseOptions = 1;
                },
                out error);
        }

        public static bool TryCloseJournal(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.CloseJournal = 1;
                },
                out error);
        }

        public static bool TryCloseDialogue(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.CloseDialogue = 1;
                },
                out error);
        }

        public static bool TryCloseMovie(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.CloseMovie = 1;
                },
                out error);
        }

        public static bool TrySetRestMenuHours(int hours, out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                (ref RuntimeShellActionRequest request) =>
                {
                    request.RestMenuAction = (byte)RuntimeShellRestMenuActionId.SetHours;
                    request.RestMenuHours = hours;
                },
                out error);
        }

        public static bool TryStartRestMenu(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.RestMenuAction = (byte)RuntimeShellRestMenuActionId.Start;
                    request.RestMenuHours = 0;
                },
                out error);
        }

        public static bool TryStartRestUntilHealed(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.RestMenuAction = (byte)RuntimeShellRestMenuActionId.UntilHealed;
                    request.RestMenuHours = 0;
                },
                out error);
        }

        public static bool TryCancelRestMenu(out string error)
        {
            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                static (ref RuntimeShellActionRequest request) =>
                {
                    request.RestMenuAction = (byte)RuntimeShellRestMenuActionId.Cancel;
                    request.RestMenuHours = 0;
                },
                out error);
        }

        public static bool TrySelectDialogueTopic(int dialogueIndex, out string error)
        {
            return TryMutateRequest<MorrowindDialogueResponseRequest>(
                "Dialogue response request state",
                (ref MorrowindDialogueResponseRequest request) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)MorrowindDialogueResponseAction.SelectTopic;
                    request.DialogueIndex = dialogueIndex;
                },
                out error);
        }

        public static bool TryDialogueGoodbye(out string error)
        {
            return TryMutateRequest<MorrowindDialogueResponseRequest>(
                "Dialogue response request state",
                static (ref MorrowindDialogueResponseRequest request) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)MorrowindDialogueResponseAction.Goodbye;
                    request.DialogueIndex = -1;
                },
                out error);
        }

        public static bool TryAnswerDialogueChoice(int choiceValue, out string error)
        {
            return TryMutateRequest<MorrowindDialogueResponseRequest>(
                "Dialogue response request state",
                (ref MorrowindDialogueResponseRequest request) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)MorrowindDialogueResponseAction.AnswerChoice;
                    request.DialogueIndex = -1;
                    request.ChoiceValue = choiceValue;
                },
                out error);
        }

        /// <summary>
        /// Flip the MW_Window_Pinnable state for one of the inventory-group
        /// subwindows. Pinned windows stay visible on the HUD layer after the
        /// inventory group closes; unpinned ones hide with it. Matches vanilla
        /// MW behavior.
        /// </summary>
        public static bool TryTogglePinnedWindow(RuntimeShellPinnableWindow window, out string error)
        {
            if (window == RuntimeShellPinnableWindow.None)
            {
                error = "TryTogglePinnedWindow called with None.";
                return false;
            }

            return TryMutateRequest<RuntimeShellActionRequest>(
                "Runtime shell request state",
                (ref RuntimeShellActionRequest request) =>
                {
                    request.PendingPinToggle = 1;
                    request.PinWindow = (byte)window;
                },
                out error);
        }

        public static bool TrySaveLoadSelectSlot(string slotId, out string error)
        {
            return TryMutateSaveLoadRequest(
                (ref SaveLoadBrowserRequest request, in SaveLoadBrowserState state) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)SaveLoadBrowserPendingAction.SelectSlot;
                    request.SlotId = RuntimeFixedStringUtility.ToFixed128OrDefault(slotId);
                },
                out error);
        }

        public static bool TrySaveLoadSetName(string value, out string error)
        {
            return TryMutateSaveLoadRequest(
                (ref SaveLoadBrowserRequest request, in SaveLoadBrowserState state) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)SaveLoadBrowserPendingAction.SetName;
                    request.SaveName = RuntimeFixedStringUtility.ToFixed64OrDefault(value);
                },
                out error);
        }

        public static bool TrySaveLoadAction(SaveLoadBrowserPendingAction action, out string error)
        {
            return TryMutateSaveLoadRequest(
                (ref SaveLoadBrowserRequest request, in SaveLoadBrowserState state) =>
                {
                    request.Pending = 1;
                    request.Action = (byte)action;
                    request.SlotId = state.SelectedSlotId;
                    request.SaveName = state.DraftSaveName;
                },
                out error);
        }

        public static bool TrySelectInventoryItem(int inventoryIndex, out string error)
        {
            return TryMutateRequest<InventoryWindowRequest>(
                "Inventory window request state",
                (ref InventoryWindowRequest request) =>
                {
                    request.PendingSelectionChange = 1;
                    request.SelectedInventoryIndex = inventoryIndex;
                },
                out error);
        }

        public static bool TrySetInventoryCategory(InventoryWindowCategory category, out string error)
        {
            return TryMutateRequest<InventoryWindowRequest>(
                "Inventory window request state",
                (ref InventoryWindowRequest request) =>
                {
                    request.PendingCategoryChange = 1;
                    request.ActiveCategory = (byte)category;
                },
                out error);
        }

        public static bool TrySetInventoryFilterText(string text, out string error)
        {
            if (text != null && text.Length > 63)
                text = text.Substring(0, 63);

            return TryMutateRequest<InventoryWindowRequest>(
                "Inventory window request state",
                (ref InventoryWindowRequest request) =>
                {
                    request.PendingFilterTextChange = 1;
                    request.FilterText = string.IsNullOrEmpty(text) ? default : text;
                },
                out error);
        }

        public static bool TrySetInventoryWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<InventoryWindowRequest>(
                "Inventory window request state",
                (ref InventoryWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        public static bool TryBeginInventoryItemDrag(int inventoryIndex, int count, out string error)
        {
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.BeginDrag,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.None,
                inventoryIndex,
                0u,
                0u,
                count,
                out error);
        }

        public static bool TryDirectTransferInventoryItem(int inventoryIndex, int count, out string error)
        {
            uint targetPlacedRefId = ResolveOpenContainerPlacedRefId();
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.DirectTransfer,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.Container,
                inventoryIndex,
                0u,
                targetPlacedRefId,
                count,
                out error);
        }

        public static bool TryUnequipInventoryItem(int inventoryIndex, out string error)
        {
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.UnequipInventoryItem,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.None,
                inventoryIndex,
                0u,
                0u,
                0,
                out error);
        }

        public static bool TryBeginContainerItemDrag(int itemIndex, int count, out string error)
        {
            uint placedRefId = ResolveOpenContainerPlacedRefId();
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.BeginDrag,
                InventoryItemOwnerKind.Container,
                InventoryItemOwnerKind.None,
                itemIndex,
                placedRefId,
                0u,
                count,
                out error);
        }

        public static bool TryDirectTransferContainerItem(int itemIndex, int count, out string error)
        {
            uint placedRefId = ResolveOpenContainerPlacedRefId();
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.DirectTransfer,
                InventoryItemOwnerKind.Container,
                InventoryItemOwnerKind.PlayerInventory,
                itemIndex,
                placedRefId,
                0u,
                count,
                out error);
        }

        public static bool TryDropHeldItemToInventory(out string error)
        {
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.DropHeldToInventory,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.PlayerInventory,
                -1,
                0u,
                0u,
                0,
                out error);
        }

        public static bool TryDropHeldItemToContainer(out string error)
        {
            uint placedRefId = ResolveOpenContainerPlacedRefId();
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.DropHeldToContainer,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.Container,
                -1,
                0u,
                placedRefId,
                0,
                out error);
        }

        public static bool TryUseHeldInventoryItem(out string error)
        {
            return TryRequestInventoryItemAction(
                InventoryItemActionKind.UseHeld,
                InventoryItemOwnerKind.PlayerInventory,
                InventoryItemOwnerKind.PlayerInventory,
                -1,
                0u,
                0u,
                0,
                out error);
        }

        public static bool TrySelectContainerItem(int itemIndex, out string error)
        {
            return TryMutateRequest<ContainerWindowRequest>(
                "Container window request state",
                (ref ContainerWindowRequest request) =>
                {
                    request.PendingSelectionChange = 1;
                    request.SelectedItemIndex = itemIndex;
                },
                out error);
        }

        public static bool TryTakeSelectedContainerItem(out string error)
        {
            return TryMutateRequest<ContainerWindowRequest>(
                "Container window request state",
                static (ref ContainerWindowRequest request) =>
                {
                    request.PendingTakeSelected = 1;
                },
                out error);
        }

        public static bool TryTakeAllContainerItems(out string error)
        {
            return TryMutateRequest<ContainerWindowRequest>(
                "Container window request state",
                static (ref ContainerWindowRequest request) =>
                {
                    request.PendingTakeAll = 1;
                },
                out error);
        }

        public static bool TryCloseContainer(out string error)
        {
            return TryMutateRequest<ContainerWindowRequest>(
                "Container window request state",
                static (ref ContainerWindowRequest request) =>
                {
                    request.PendingClose = 1;
                },
                out error);
        }

        public static bool TrySetContainerWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<ContainerWindowRequest>(
                "Container window request state",
                (ref ContainerWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        static bool TryRequestInventoryItemAction(
            InventoryItemActionKind action,
            InventoryItemOwnerKind source,
            InventoryItemOwnerKind target,
            int sourceIndex,
            uint sourcePlacedRefId,
            uint targetPlacedRefId,
            int count,
            out string error)
        {
            if (TryAppendInventoryItemAction(
                    action,
                    source,
                    target,
                    sourceIndex,
                    sourcePlacedRefId,
                    targetPlacedRefId,
                    count,
                    out error))
            {
                return true;
            }

            return TryMutateRequest<InventoryItemActionRequest>(
                "Inventory item action request state",
                (ref InventoryItemActionRequest request) =>
                {
                    uint sequence = request.Sequence + 1u;
                    request = new InventoryItemActionRequest
                    {
                        Pending = 1,
                        Action = (byte)action,
                        SourceOwner = (byte)source,
                        TargetOwner = (byte)target,
                        SourceIndex = sourceIndex,
                        SourcePlacedRefId = sourcePlacedRefId,
                        TargetPlacedRefId = targetPlacedRefId,
                        Count = count,
                        Sequence = sequence,
                    };
                },
                out error);
        }

        static bool TryAppendInventoryItemAction(
            InventoryItemActionKind action,
            InventoryItemOwnerKind source,
            InventoryItemOwnerKind target,
            int sourceIndex,
            uint sourcePlacedRefId,
            uint targetPlacedRefId,
            int count,
            out string error)
        {
            if (!TryGetSingletonBufferOwner<InventoryItemActionRequestElement>(
                    "Inventory item action request queue",
                    out var entityManager,
                    out Entity entity,
                    out error))
            {
                return false;
            }

            var queue = entityManager.GetBuffer<InventoryItemActionRequestElement>(entity);
            uint sequence = queue.Length > 0 ? queue[queue.Length - 1].Sequence + 1u : 1u;
            queue.Add(new InventoryItemActionRequestElement
            {
                Action = (byte)action,
                SourceOwner = (byte)source,
                TargetOwner = (byte)target,
                SourceIndex = sourceIndex,
                SourcePlacedRefId = sourcePlacedRefId,
                TargetPlacedRefId = targetPlacedRefId,
                Count = count,
                Sequence = sequence,
            });
            error = null;
            return true;
        }

        static uint ResolveOpenContainerPlacedRefId()
        {
            if (!TryGetSingleton<ContainerWindowState>(
                    "Container window state",
                    out EntityManager entityManager,
                    out Entity entity,
                    out _))
            {
                return 0u;
            }

            var state = entityManager.GetComponentData<ContainerWindowState>(entity);
            return state.Visible != 0 ? state.OpenPlacedRefId : 0u;
        }

        public static bool TrySetStatsWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<StatsWindowRequest>(
                "Stats window request state",
                (ref StatsWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        public static bool TrySetSpellWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<SpellWindowRequest>(
                "Spell window request state",
                (ref SpellWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        public static bool TrySelectSpell(int spellIndex, out string error)
        {
            return TryMutateRequest<SpellWindowRequest>(
                "Spell window request state",
                (ref SpellWindowRequest request) =>
                {
                    request.PendingSelectionChange = 1;
                    request.SelectedSpellIndex = spellIndex;
                },
                out error);
        }

        public static bool TrySetSpellFilterText(string text, out string error)
        {
            if (text != null && text.Length > 63)
                text = text.Substring(0, 63);

            return TryMutateRequest<SpellWindowRequest>(
                "Spell window request state",
                (ref SpellWindowRequest request) =>
                {
                    request.PendingFilterTextChange = 1;
                    request.FilterText = string.IsNullOrEmpty(text) ? default : text;
                },
                out error);
        }

        public static bool TrySetMapWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<MapWindowRequest>(
                "Map window request state",
                (ref MapWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        public static bool TrySetMapWindowMode(MapWindowMode mode, out string error)
        {
            return TryMutateRequest<MapWindowRequest>(
                "Map window request state",
                (ref MapWindowRequest request) =>
                {
                    request.PendingModeChange = 1;
                    request.Mode = (byte)mode;
                },
                out error);
        }

        public static bool TrySetMapViewport(MapWindowMode mode, float panX, float panY, float zoom, out string error)
        {
            return TryMutateRequest<MapWindowRequest>(
                "Map window request state",
                (ref MapWindowRequest request) =>
                {
                    request.PendingViewportChange = 1;
                    request.ViewportMode = (byte)mode;
                    request.PanX = panX;
                    request.PanY = panY;
                    request.Zoom = zoom;
                },
                out error);
        }

        public static bool TryCenterMapOnPlayer(out string error)
        {
            return TryMutateRequest<MapWindowRequest>(
                "Map window request state",
                static (ref MapWindowRequest request) =>
                {
                    request.PendingCenterOnPlayer = 1;
                },
                out error);
        }

        public static bool TrySetJournalWindowRect(Rect normalizedRect, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    RuntimeWindowGeometryUtility.SetRectRequest(ref request.RectRequest, normalizedRect);
                },
                out error);
        }

        public static bool TrySetJournalShowAll(bool showAll, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingShowAllChange = 1;
                    request.ShowAll = showAll ? (byte)1 : (byte)0;
                },
                out error);
        }

        public static bool TrySelectJournalQuest(int dialogueIndex, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingSelectionChange = 1;
                    request.SelectedDialogueIndex = dialogueIndex;
                },
                out error);
        }

        public static bool TryOpenJournalQuest(int dialogueIndex, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingSelectionChange = 1;
                    request.SelectedDialogueIndex = dialogueIndex;
                    request.PendingModeChange = 1;
                    request.Mode = 1;
                    request.PendingOverlayChange = 1;
                    request.OverlayOpen = 0;
                    request.PendingPageChange = 1;
                    request.Page = 0;
                },
                out error);
        }

        public static bool TryOpenJournalMainBook(out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                static (ref JournalWindowRequest request) =>
                {
                    request.PendingModeChange = 1;
                    request.Mode = 0;
                    request.PendingOverlayChange = 1;
                    request.OverlayOpen = 0;
                    request.PendingPageChange = 1;
                    request.Page = -1;
                },
                out error);
        }

        public static bool TrySetJournalOverlay(bool open, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingOverlayChange = 1;
                    request.OverlayOpen = open ? (byte)1 : (byte)0;
                },
                out error);
        }

        public static bool TrySetJournalPage(int page, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingPageChange = 1;
                    request.Page = page;
                },
                out error);
        }

        public static bool TrySetJournalScroll(float questScrollY, float entryScrollY, out string error)
        {
            return TryMutateRequest<JournalWindowRequest>(
                "Journal window request state",
                (ref JournalWindowRequest request) =>
                {
                    request.PendingScrollChange = 1;
                    request.QuestScrollY = Mathf.Clamp01(questScrollY);
                    request.EntryScrollY = Mathf.Clamp01(entryScrollY);
                },
                out error);
        }

        static bool TryMutateRequest<T>(
            string label,
            RequestMutation<T> mutate,
            out string error)
            where T : unmanaged, IComponentData
        {
            if (!TryGetSingleton<T>(label, out var entityManager, out Entity entity, out error))
                return false;

            var request = entityManager.GetComponentData<T>(entity);
            mutate(ref request);
            entityManager.SetComponentData(entity, request);
            error = null;
            return true;
        }

        static bool TryMutateSaveLoadRequest(
            SaveLoadRequestMutation mutate,
            out string error)
        {
            if (!TryGetSaveLoadRequestSingleton(out var entityManager, out Entity requestEntity, out Entity stateEntity, out error))
                return false;

            var state = entityManager.GetComponentData<SaveLoadBrowserState>(stateEntity);
            var request = entityManager.GetComponentData<SaveLoadBrowserRequest>(requestEntity);
            mutate(ref request, state);
            entityManager.SetComponentData(requestEntity, request);
            error = null;
            return true;
        }

        static bool TryGetSaveLoadRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out Entity stateEntity, out string error)
        {
            requestEntity = Entity.Null;
            stateEntity = Entity.Null;
            entityManager = default;

            if (!TryGetSingleton<SaveLoadBrowserRequest>("Save/load browser request state", out entityManager, out requestEntity, out error))
                return false;
            if (!TryGetSingleton<SaveLoadBrowserState>("Save/load browser state", out _, out stateEntity, out error))
                return false;

            error = null;
            return true;
        }

        static bool TryGetSingleton<T>(
            string label,
            out EntityManager entityManager,
            out Entity entity,
            out string error)
            where T : unmanaged, IComponentData
        {
            entity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            EntityQuery query = SingletonQueryCache<T>.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                error = $"{label} is not ready.";
                return false;
            }

            entity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetSingletonBufferOwner<T>(
            string label,
            out EntityManager entityManager,
            out Entity entity,
            out string error)
            where T : unmanaged, IBufferElementData
        {
            entity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            EntityQuery query = SingletonBufferOwnerQueryCache<T>.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                error = $"{label} is not ready.";
                return false;
            }

            entity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static class SingletonQueryCache<T>
            where T : unmanaged, IComponentData
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class SingletonBufferOwnerQueryCache<T>
            where T : unmanaged, IBufferElementData
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
