using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class BookReaderRequestSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BookReaderState>();
            RequireForUpdate<BookReaderRequest>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            ref var state = ref SystemAPI.GetSingletonRW<BookReaderState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<BookReaderRequest>().ValueRW;
            if (request.PendingClose == 0
                && request.PendingNextPage == 0
                && request.PendingPreviousPage == 0
                && request.PendingTake == 0)
            {
                return;
            }

            if (request.PendingClose != 0)
            {
                state.Visible = 0;
                state.SourceEntity = Entity.Null;
                state.Content = default;
                state.InventoryIndex = -1;
                ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
                RuntimeShellStateUtility.ClearModal(ref shell);
            }
            else if (request.PendingNextPage != 0 && state.Visible != 0)
            {
                state.CurrentPage++;
            }
            else if (request.PendingPreviousPage != 0 && state.Visible != 0 && state.CurrentPage > 0)
            {
                state.CurrentPage--;
            }
            else if (request.PendingTake != 0 && state.Visible != 0 && state.AllowTake != 0)
            {
                state.StatusText = RuntimeFixedStringUtility.ToFixed128OrDefaultWhiteSpace("Taking world books is queued for the pickup integration pass.");
            }

            request = default;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(BookReaderRequestSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class BookReadRequestSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BookReadRequest>();
            RequireForUpdate<BookReaderState>();
            RequireForUpdate<BookSkillGrantRequest>();
            RequireForUpdate<BookReadHistoryEntry>();
            RequireForUpdate<RuntimeShellState>();
        }

        protected override void OnUpdate()
        {
            ref var request = ref SystemAPI.GetSingletonRW<BookReadRequest>().ValueRW;
            if (request.Pending == 0)
                return;

            var contentDb = RuntimeContentDatabase.Active;
            if (!RuntimeContentMetadataResolver.TryResolveBook(contentDb, request.Content, out var metadata))
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
            reader.Title = RuntimeFixedStringUtility.ToFixed128OrDefaultWhiteSpace(metadata.Title);
            reader.StatusText = default;

            TryQueueSkillGrant(request.Sequence, metadata);

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            string body = metadata.IsScroll
                ? "Scroll text import is not wired yet."
                : "Book text import is not wired yet.";
            RuntimeShellStateUtility.ShowDialog(ref shell, metadata.Title, body);

            request = default;
            request.InventoryIndex = -1;
        }

        void TryQueueSkillGrant(uint sequence, in BookContentMetadata metadata)
        {
            if (metadata.SkillId < 0 || HasRead(metadata.Content))
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

        bool HasRead(ContentReference content)
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
