using Unity.Entities;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    public partial struct RuntimeShellInputSystem : ISystem
    {
        EntityQuery _dialogueSessionQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dialogueSessionQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindDialogueSession>());
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<ContainerWindowState>();
            systemState.RequireForUpdate<BookReaderState>();
            systemState.RequireForUpdate<BookReaderRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (BootstrapPresentationGate.BlocksGameplayInput)
                return;

            ref var state = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var containerState = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ref var browserState = ref SystemAPI.GetSingletonRW<SaveLoadBrowserState>().ValueRW;
            ref var bookRequest = ref SystemAPI.GetSingletonRW<BookReaderRequest>().ValueRW;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            if (SystemAPI.HasSingleton<CharGenStage>())
                return;

            bool togglePausePressed = (keyboard?.escapeKey.wasPressedThisFrame ?? false)
                || (gamepad?.startButton.wasPressedThisFrame ?? false);
            bool backPressed = gamepad?.buttonEast.wasPressedThisFrame ?? false;
            bool toggleInventoryPressed = (keyboard?.tabKey.wasPressedThisFrame ?? false)
                || (keyboard?.iKey.wasPressedThisFrame ?? false)
                || (gamepad?.buttonWest.wasPressedThisFrame ?? false);
            bool toggleJournalPressed = keyboard?.jKey.wasPressedThisFrame ?? false;
            bool openWaitPressed = keyboard?.tKey.wasPressedThisFrame ?? false;

            if (SystemAPI.TryGetSingleton<BookReaderState>(out var bookReader) && bookReader.Visible != 0)
            {
                if (togglePausePressed || backPressed)
                    bookRequest.PendingClose = 1;
                else if ((keyboard?.leftArrowKey.wasPressedThisFrame ?? false) || (keyboard?.pageUpKey.wasPressedThisFrame ?? false))
                    bookRequest.PendingPreviousPage = 1;
                else if ((keyboard?.rightArrowKey.wasPressedThisFrame ?? false) || (keyboard?.pageDownKey.wasPressedThisFrame ?? false))
                    bookRequest.PendingNextPage = 1;
                else if ((BookReaderKind)bookReader.Kind == BookReaderKind.Scroll && (keyboard?.upArrowKey.wasPressedThisFrame ?? false))
                {
                    bookRequest.PendingScroll = 1;
                    bookRequest.ScrollOffset = bookReader.ScrollOffset - 40f;
                }
                else if ((BookReaderKind)bookReader.Kind == BookReaderKind.Scroll && (keyboard?.downArrowKey.wasPressedThisFrame ?? false))
                {
                    bookRequest.PendingScroll = 1;
                    bookRequest.ScrollOffset = bookReader.ScrollOffset + 40f;
                }

                return;
            }
            else if (state.ModalOpen != 0)
            {
                if (togglePausePressed || backPressed)
                {
                    bool scriptMessageBox = state.ModalTitle.IsEmpty;
                    RuntimeShellStateUtility.CloseModal(ref state);
                    if (scriptMessageBox)
                        state.PauseMenuOpen = 0;
                }
            }
            else if (state.SaveLoadBrowserOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    RuntimeShellStateUtility.CloseSaveLoadBrowser(ref state, ref browserState);
            }
            else if (state.ContainerOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    ContainerWindowRuntimeUtility.CloseContainer(ref state, ref containerState);
                else if (toggleJournalPressed)
                    RuntimeShellStateUtility.OpenJournal(ref state);
            }
            else if (state.InventoryOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleInventoryPressed)
                    RuntimeShellStateUtility.CloseInventory(ref state);
                else if (toggleJournalPressed)
                    RuntimeShellStateUtility.OpenJournal(ref state);
            }
            else if (state.PauseMenuOpen != 0)
            {
                if (togglePausePressed || backPressed)
                    RuntimeShellStateUtility.ClosePause(ref state);
                else if (toggleJournalPressed)
                    RuntimeShellStateUtility.OpenJournal(ref state);
            }
            else if (state.JournalOpen != 0)
            {
                if (togglePausePressed || backPressed || toggleJournalPressed)
                    RuntimeShellStateUtility.CloseJournal(ref state);
            }
            else if (state.DialogueOpen != 0)
            {
                if ((togglePausePressed || backPressed) && CanCloseDialogue(ref systemState, _dialogueSessionQuery))
                {
                    RuntimeShellStateUtility.CloseDialogue(ref state);
                    if (SystemAPI.TryGetSingletonRW<MorrowindDialogueSession>(out var sessionRef))
                    {
                        sessionRef.ValueRW = new MorrowindDialogueSession
                        {
                            SelectedTopicDialogueIndex = -1,
                            LastInfoIndex = -1,
                        };
                    }
                }
            }
            else if (state.RestMenuOpen != 0)
            {
                if (state.RestMenuAdvancing == 0 && (togglePausePressed || backPressed))
                    RuntimeShellStateUtility.CloseRestMenu(ref state);
            }
            else if (toggleInventoryPressed)
            {
                RuntimeShellStateUtility.OpenInventory(ref state);
            }
            else if (toggleJournalPressed)
            {
                RuntimeShellStateUtility.OpenJournal(ref state);
            }
            else if (openWaitPressed)
            {
                RuntimeShellStateUtility.OpenRestMenu(ref state, Entity.Null, 0u, canSleep: false);
            }
            else if (togglePausePressed)
            {
                RuntimeShellStateUtility.OpenPause(ref state);
            }

        }

        static bool CanCloseDialogue(ref SystemState systemState, EntityQuery dialogueSessionQuery)
        {
            if (dialogueSessionQuery.IsEmptyIgnoreFilter)
                return true;

            var session = systemState.EntityManager.GetComponentData<MorrowindDialogueSession>(dialogueSessionQuery.GetSingletonEntity());
            return session.ChoiceActive == 0;
        }
    }
}

