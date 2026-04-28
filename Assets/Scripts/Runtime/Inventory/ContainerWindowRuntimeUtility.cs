using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Inventory
{
    static class ContainerWindowRuntimeUtility
    {
        public static void OpenContainer(
            ref RuntimeShellState shell,
            ref ContainerWindowState state,
            Entity target,
            uint placedRefId,
            ContainerDefHandle definition,
            string title)
        {
            bool inventoryWasAlreadyOpen = shell.InventoryOpen != 0;

            shell.InventoryOpen = 1;
            shell.ContainerOpen = 1;
            shell.PauseMenuOpen = 0;
            shell.ModalOpen = 0;
            shell.ModalTitle = default;
            shell.ModalBody = default;

            state.Visible = 1;
            state.OpenTargetEntity = target;
            state.OpenPlacedRefId = placedRefId;
            state.Definition = definition;
            state.PreserveInventoryOnClose = (byte)(inventoryWasAlreadyOpen ? 1 : 0);
            state.Title = RuntimeFixedStringUtility.ToFixed128OrDefaultWhiteSpace(title);
        }

        public static void CloseContainer(ref RuntimeShellState shell, ref ContainerWindowState state)
        {
            shell.ContainerOpen = 0;
            if (state.PreserveInventoryOnClose == 0)
                shell.InventoryOpen = 0;

            state.Visible = 0;
            state.OpenPlacedRefId = 0u;
            state.OpenTargetEntity = Entity.Null;
            state.Definition = default;
            state.SelectedItemIndex = -1;
            state.PreserveInventoryOnClose = 0;
            state.Title = default;
            state.SelectedItemDetailsText = default;
        }

    }
}
