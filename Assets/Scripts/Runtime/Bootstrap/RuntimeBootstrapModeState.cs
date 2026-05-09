using Unity.Entities;

namespace VVardenfell.Runtime.Bootstrap
{
    public struct RuntimeBootstrapModeState : IComponentData
    {
        public byte Mode;
    }
}
