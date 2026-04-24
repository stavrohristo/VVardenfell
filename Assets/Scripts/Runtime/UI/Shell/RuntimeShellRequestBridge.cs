using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI.Shell
{
    public static class RuntimeShellRequestBridge
    {
        public static bool TryRequestAction(RuntimeShellMenuActionId action, out string error)
        {
            if (!TryGetRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<RuntimeShellActionRequest>(entity);
            request.Pending = 1;
            request.DismissModal = 0;
            request.Action = (byte)action;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TryDismissModal(out string error)
        {
            if (!TryGetRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<RuntimeShellActionRequest>(entity);
            request.DismissModal = 1;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TryCloseOptions(out string error)
        {
            if (!TryGetRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<RuntimeShellActionRequest>(entity);
            request.CloseOptions = 1;
            em.SetComponentData(entity, request);
            error = null;
            return true;
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

            if (!TryGetRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<RuntimeShellActionRequest>(entity);
            request.PendingPinToggle = 1;
            request.PinWindow = (byte)window;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySaveLoadSelectSlot(string slotId, out string error)
        {
            if (!TryGetSaveLoadRequestSingleton(out var em, out Entity requestEntity, out Entity stateEntity, out error))
                return false;

            var request = em.GetComponentData<SaveLoadBrowserRequest>(requestEntity);
            request.Pending = 1;
            request.Action = (byte)SaveLoadBrowserPendingAction.SelectSlot;
            request.SlotId = ToFixed128(slotId);
            em.SetComponentData(requestEntity, request);
            error = null;
            return true;
        }

        public static bool TrySaveLoadSetName(string value, out string error)
        {
            if (!TryGetSaveLoadRequestSingleton(out var em, out Entity requestEntity, out Entity stateEntity, out error))
                return false;

            var request = em.GetComponentData<SaveLoadBrowserRequest>(requestEntity);
            request.Pending = 1;
            request.Action = (byte)SaveLoadBrowserPendingAction.SetName;
            request.SaveName = ToFixed64(value);
            em.SetComponentData(requestEntity, request);
            error = null;
            return true;
        }

        public static bool TrySaveLoadAction(SaveLoadBrowserPendingAction action, out string error)
        {
            if (!TryGetSaveLoadRequestSingleton(out var em, out Entity requestEntity, out Entity stateEntity, out error))
                return false;

            var state = em.GetComponentData<SaveLoadBrowserState>(stateEntity);
            var request = em.GetComponentData<SaveLoadBrowserRequest>(requestEntity);
            request.Pending = 1;
            request.Action = (byte)action;
            request.SlotId = state.SelectedSlotId;
            request.SaveName = state.DraftSaveName;
            em.SetComponentData(requestEntity, request);
            error = null;
            return true;
        }

        public static bool TrySelectInventoryItem(int inventoryIndex, out string error)
        {
            if (!TryGetInventoryRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<InventoryWindowRequest>(entity);
            request.PendingSelectionChange = 1;
            request.SelectedInventoryIndex = inventoryIndex;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetInventoryCategory(InventoryWindowCategory category, out string error)
        {
            if (!TryGetInventoryRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<InventoryWindowRequest>(entity);
            request.PendingCategoryChange = 1;
            request.ActiveCategory = (byte)category;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetInventoryFilterText(string text, out string error)
        {
            if (!TryGetInventoryRequestSingleton(out var em, out Entity entity, out error))
                return false;

            if (text != null && text.Length > 63)
                text = text.Substring(0, 63);

            var request = em.GetComponentData<InventoryWindowRequest>(entity);
            request.PendingFilterTextChange = 1;
            request.FilterText = string.IsNullOrEmpty(text) ? default : text;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetInventoryWindowRect(Rect normalizedRect, out string error)
        {
            if (!TryGetInventoryRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<InventoryWindowRequest>(entity);
            request.PendingRectUpdate = 1;
            request.NormalizedX = normalizedRect.x;
            request.NormalizedY = normalizedRect.y;
            request.NormalizedWidth = normalizedRect.width;
            request.NormalizedHeight = normalizedRect.height;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySelectContainerItem(int itemIndex, out string error)
        {
            if (!TryGetContainerRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<ContainerWindowRequest>(entity);
            request.PendingSelectionChange = 1;
            request.SelectedItemIndex = itemIndex;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TryTakeSelectedContainerItem(out string error)
        {
            if (!TryGetContainerRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<ContainerWindowRequest>(entity);
            request.PendingTakeSelected = 1;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TryTakeAllContainerItems(out string error)
        {
            if (!TryGetContainerRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<ContainerWindowRequest>(entity);
            request.PendingTakeAll = 1;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TryCloseContainer(out string error)
        {
            if (!TryGetContainerRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<ContainerWindowRequest>(entity);
            request.PendingClose = 1;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetContainerWindowRect(Rect normalizedRect, out string error)
        {
            if (!TryGetContainerRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<ContainerWindowRequest>(entity);
            request.PendingRectUpdate = 1;
            request.NormalizedX = normalizedRect.x;
            request.NormalizedY = normalizedRect.y;
            request.NormalizedWidth = normalizedRect.width;
            request.NormalizedHeight = normalizedRect.height;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetStatsWindowRect(Rect normalizedRect, out string error)
        {
            if (!TryGetStatsRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<StatsWindowRequest>(entity);
            request.PendingRectUpdate = 1;
            request.NormalizedX = normalizedRect.x;
            request.NormalizedY = normalizedRect.y;
            request.NormalizedWidth = normalizedRect.width;
            request.NormalizedHeight = normalizedRect.height;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetSpellWindowRect(Rect normalizedRect, out string error)
        {
            if (!TryGetSpellRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<SpellWindowRequest>(entity);
            request.PendingRectUpdate = 1;
            request.NormalizedX = normalizedRect.x;
            request.NormalizedY = normalizedRect.y;
            request.NormalizedWidth = normalizedRect.width;
            request.NormalizedHeight = normalizedRect.height;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySelectSpell(int spellIndex, out string error)
        {
            if (!TryGetSpellRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<SpellWindowRequest>(entity);
            request.PendingSelectionChange = 1;
            request.SelectedSpellIndex = spellIndex;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        public static bool TrySetMapWindowRect(Rect normalizedRect, out string error)
        {
            if (!TryGetMapRequestSingleton(out var em, out Entity entity, out error))
                return false;

            var request = em.GetComponentData<MapWindowRequest>(entity);
            request.PendingRectUpdate = 1;
            request.NormalizedX = normalizedRect.x;
            request.NormalizedY = normalizedRect.y;
            request.NormalizedWidth = normalizedRect.width;
            request.NormalizedHeight = normalizedRect.height;
            em.SetComponentData(entity, request);
            error = null;
            return true;
        }

        static bool TryGetRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeShellActionRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Runtime shell request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetSaveLoadRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out Entity stateEntity, out string error)
        {
            requestEntity = Entity.Null;
            stateEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var requestQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SaveLoadBrowserRequest>());
            if (requestQuery.IsEmptyIgnoreFilter)
            {
                error = "Save/load browser request state is not ready.";
                return false;
            }

            using var stateQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SaveLoadBrowserState>());
            if (stateQuery.IsEmptyIgnoreFilter)
            {
                error = "Save/load browser state is not ready.";
                return false;
            }

            requestEntity = requestQuery.GetSingletonEntity();
            stateEntity = stateQuery.GetSingletonEntity();
            error = null;
            return true;
        }

        static FixedString128Bytes ToFixed128(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        static FixedString64Bytes ToFixed64(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }

        static bool TryGetInventoryRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<InventoryWindowRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Inventory window request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetContainerRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ContainerWindowRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Container window request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetStatsRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<StatsWindowRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Stats window request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetSpellRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SpellWindowRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Spell window request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static bool TryGetMapRequestSingleton(out EntityManager entityManager, out Entity requestEntity, out string error)
        {
            requestEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MapWindowRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Map window request state is not ready.";
                return false;
            }

            requestEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }
    }
}
