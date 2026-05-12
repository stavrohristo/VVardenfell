using Unity.Entities;

namespace VVardenfell.Runtime.Pathfinding
{
    public struct RuntimePathGridNavigationResource : IComponentData
    {
        public PathGridNavigationWorld Navigation;
    }
}
