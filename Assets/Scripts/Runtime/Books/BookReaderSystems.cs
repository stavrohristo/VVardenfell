using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct BookReaderRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<BookReaderState>();
            systemState.RequireForUpdate<BookReaderRequest>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
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
                state.StatusText = BuildWorldBookTakeQueuedStatus();
            }

            request = default;
        }

        static Unity.Collections.FixedString128Bytes BuildWorldBookTakeQueuedStatus()
        {
            var result = default(Unity.Collections.FixedString128Bytes);
            result.Append('T'); result.Append('a'); result.Append('k'); result.Append('i'); result.Append('n'); result.Append('g'); result.Append(' ');
            result.Append('w'); result.Append('o'); result.Append('r'); result.Append('l'); result.Append('d'); result.Append(' ');
            result.Append('b'); result.Append('o'); result.Append('o'); result.Append('k'); result.Append('s'); result.Append(' ');
            result.Append('i'); result.Append('s'); result.Append(' ');
            result.Append('q'); result.Append('u'); result.Append('e'); result.Append('u'); result.Append('e'); result.Append('d'); result.Append(' ');
            result.Append('f'); result.Append('o'); result.Append('r'); result.Append(' ');
            result.Append('t'); result.Append('h'); result.Append('e'); result.Append(' ');
            result.Append('p'); result.Append('i'); result.Append('c'); result.Append('k'); result.Append('u'); result.Append('p'); result.Append(' ');
            result.Append('i'); result.Append('n'); result.Append('t'); result.Append('e'); result.Append('g'); result.Append('r'); result.Append('a'); result.Append('t'); result.Append('i'); result.Append('o'); result.Append('n'); result.Append(' ');
            result.Append('p'); result.Append('a'); result.Append('s'); result.Append('s'); result.Append('.');
            return result;
        }
    }

    [BurstCompile]
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

        [BurstCompile]
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
            reader.Title = metadata.Title;
            reader.StatusText = default;

            TryQueueSkillGrant(ref systemState, request.Sequence, metadata);

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            RuntimeShellStateUtility.ShowDialog(ref shell, metadata.Title, metadata.IsScroll ? BuildScrollTextPendingBody() : BuildBookTextPendingBody());

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

        static Unity.Collections.FixedString512Bytes BuildBookTextPendingBody()
        {
            var result = default(Unity.Collections.FixedString512Bytes);
            result.Append('B'); result.Append('o'); result.Append('o'); result.Append('k'); result.Append(' ');
            result.Append('t'); result.Append('e'); result.Append('x'); result.Append('t'); result.Append(' ');
            result.Append('i'); result.Append('m'); result.Append('p'); result.Append('o'); result.Append('r'); result.Append('t'); result.Append(' ');
            result.Append('i'); result.Append('s'); result.Append(' ');
            result.Append('n'); result.Append('o'); result.Append('t'); result.Append(' ');
            result.Append('w'); result.Append('i'); result.Append('r'); result.Append('e'); result.Append('d'); result.Append(' ');
            result.Append('y'); result.Append('e'); result.Append('t'); result.Append('.');
            return result;
        }

        static Unity.Collections.FixedString512Bytes BuildScrollTextPendingBody()
        {
            var result = default(Unity.Collections.FixedString512Bytes);
            result.Append('S'); result.Append('c'); result.Append('r'); result.Append('o'); result.Append('l'); result.Append('l'); result.Append(' ');
            result.Append('t'); result.Append('e'); result.Append('x'); result.Append('t'); result.Append(' ');
            result.Append('i'); result.Append('m'); result.Append('p'); result.Append('o'); result.Append('r'); result.Append('t'); result.Append(' ');
            result.Append('i'); result.Append('s'); result.Append(' ');
            result.Append('n'); result.Append('o'); result.Append('t'); result.Append(' ');
            result.Append('w'); result.Append('i'); result.Append('r'); result.Append('e'); result.Append('d'); result.Append(' ');
            result.Append('y'); result.Append('e'); result.Append('t'); result.Append('.');
            return result;
        }
    }
}
