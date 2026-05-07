using Unity.Entities;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct BookReaderRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<BookReaderState>();
            systemState.RequireForUpdate<BookReaderRequest>();
            systemState.RequireForUpdate<BookTakeRequest>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var state = ref SystemAPI.GetSingletonRW<BookReaderState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<BookReaderRequest>().ValueRW;
            if (request.PendingClose == 0
                && request.PendingNextPage == 0
                && request.PendingPreviousPage == 0
                && request.PendingTake == 0
                && request.PendingScroll == 0)
            {
                return;
            }

            if (request.PendingClose != 0)
            {
                CloseReader(ref state);
                ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
                RuntimeShellStateUtility.ClearModal(ref shell);
            }
            else if (request.PendingNextPage != 0 && state.Visible != 0 && (BookReaderKind)state.Kind == BookReaderKind.Book)
            {
                state.CurrentPage += 2;
            }
            else if (request.PendingPreviousPage != 0 && state.Visible != 0 && (BookReaderKind)state.Kind == BookReaderKind.Book && state.CurrentPage > 0)
            {
                state.CurrentPage -= state.CurrentPage >= 2 ? 2 : state.CurrentPage;
            }
            else if (request.PendingScroll != 0 && state.Visible != 0 && (BookReaderKind)state.Kind == BookReaderKind.Scroll)
            {
                state.ScrollOffset = request.ScrollOffset;
            }
            else if (request.PendingTake != 0 && state.Visible != 0 && state.AllowTake != 0)
            {
                ref var take = ref SystemAPI.GetSingletonRW<BookTakeRequest>().ValueRW;
                take.Pending = 1;
                take.SourceEntity = state.SourceEntity;
                take.SourcePlacedRefId = state.SourcePlacedRefId;
                take.Content = state.Content;
                CloseReader(ref state);
                ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
                RuntimeShellStateUtility.ClearModal(ref shell);
            }

            request = default;
        }

        static void CloseReader(ref BookReaderState state)
        {
            state.Visible = 0;
            state.Kind = 0;
            state.AllowTake = 0;
            state.SourceEntity = Entity.Null;
            state.SourcePlacedRefId = 0u;
            state.Content = default;
            state.InventoryIndex = -1;
            state.CurrentPage = 0;
            state.ScrollOffset = 0f;
            state.Title = default;
            state.StatusText = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(BookReaderRequestSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct BookReadRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<BookReadRequest>();
            systemState.RequireForUpdate<BookReaderState>();
            systemState.RequireForUpdate<BookSkillGrantRequest>();
            systemState.RequireForUpdate<BookReadHistoryEntry>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var request = ref SystemAPI.GetSingletonRW<BookReadRequest>().ValueRW;
            if (request.Pending == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentMetadataResolver.TryResolveBookFixed(ref contentBlob, request.Content, out var metadata))
            {
                request = default;
                request.InventoryIndex = -1;
                return;
            }

            ref var reader = ref SystemAPI.GetSingletonRW<BookReaderState>().ValueRW;
            reader.Visible = 1;
            reader.Kind = (byte)BookRuntimeUtility.ResolveKind(metadata);
            reader.AllowTake = request.AllowTake;
            reader.SourceEntity = request.SourceEntity;
            reader.SourcePlacedRefId = request.SourcePlacedRefId;
            reader.Content = metadata.Content;
            reader.InventoryIndex = request.InventoryIndex;
            reader.CurrentPage = 0;
            reader.ScrollOffset = 0f;
            reader.Title = metadata.Title;
            reader.StatusText = default;

            TryQueueSkillGrant(ref systemState, request.Sequence, metadata);

            request = default;
            request.InventoryIndex = -1;
        }

        void TryQueueSkillGrant(ref SystemState systemState, uint sequence, in FixedBookContentMetadata metadata)
        {
            if (metadata.SkillId < 0 || HasRead(ref systemState, metadata.Content))
                return;

            ref var skillGrant = ref SystemAPI.GetSingletonRW<BookSkillGrantRequest>().ValueRW;
            skillGrant.Pending = 1;
            skillGrant.Content = metadata.Content;
            skillGrant.SkillId = metadata.SkillId;
            skillGrant.Sequence = sequence;

            var history = SystemAPI.GetSingletonBuffer<BookReadHistoryEntry>();
            history.Add(new BookReadHistoryEntry
            {
                Content = metadata.Content,
            });
        }

        bool HasRead(ref SystemState systemState, ContentReference content)
        {
            var history = SystemAPI.GetSingletonBuffer<BookReadHistoryEntry>();
            for (int i = 0; i < history.Length; i++)
            {
                if (history[i].Content.Kind == content.Kind
                    && history[i].Content.HandleValue == content.HandleValue)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
