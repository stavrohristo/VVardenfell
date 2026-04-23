using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI
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
    }
}
