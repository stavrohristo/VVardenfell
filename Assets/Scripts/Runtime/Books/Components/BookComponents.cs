using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public enum BookReadSource : byte
    {
        None = 0,
        World = 1,
        Inventory = 2,
        Container = 3,
    }

    public enum BookReaderKind : byte
    {
        None = 0,
        Book = 1,
        Scroll = 2,
    }

    public struct BookReadRequest : IComponentData
    {
        public byte Pending;
        public byte Source;
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public ContentReference Content;
        public int InventoryIndex;
        public byte AllowTake;
        public uint Sequence;
    }

    public struct BookReaderState : IComponentData
    {
        public byte Visible;
        public byte Kind;
        public byte AllowTake;
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public ContentReference Content;
        public int InventoryIndex;
        public int CurrentPage;
        public float ScrollOffset;
        public FixedString128Bytes Title;
        public FixedString128Bytes StatusText;
    }

    public struct BookReaderRequest : IComponentData
    {
        public byte PendingClose;
        public byte PendingNextPage;
        public byte PendingPreviousPage;
        public byte PendingTake;
        public byte PendingScroll;
        public float ScrollOffset;
    }

    public struct BookInventoryReadRequest : IComponentData
    {
        public byte Pending;
        public int InventoryIndex;
        public uint Sequence;
    }

    public struct BookSkillGrantRequest : IComponentData
    {
        public byte Pending;
        public ContentReference Content;
        public int SkillId;
        public uint Sequence;
    }

    public struct BookTakeRequest : IComponentData
    {
        public byte Pending;
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public ContentReference Content;
    }

    public struct BookReadHistoryEntry : IBufferElementData
    {
        public ContentReference Content;
        public uint PlacedRefId;
    }
}
