using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Components
{
    public struct LocalMapDiscoveryState : IComponentData
    {
        public int MaskResolution;
        public int RenderResolution;
        public float RevealRadiusFraction;
        public uint Revision;
    }

    public struct ExteriorMapDiscoveryTile : IComponentData
    {
        public int2 Cell;
        public uint Revision;
        public byte Dirty;
    }

    public struct ExteriorMapDiscoverySample : IBufferElementData
    {
        public byte Alpha;
    }
}
