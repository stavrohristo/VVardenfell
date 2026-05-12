using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Pathfinding
{
    public enum PathGridTraversalStatus : byte
    {
        Idle = 0,
        RequestingPath = 1,
        Traversing = 2,
        Reached = 3,
        Failed = 4,
        Canceled = 5,
    }

    public struct PathGridTraversalSettings : IComponentData
    {
        public float NodeArrivalDistance;
        public float FinalArrivalDistance;
        public float FacingSharpness;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Run;
        public byte FacePath;

        public static PathGridTraversalSettings Defaults => new PathGridTraversalSettings
        {
            NodeArrivalDistance = 0.35f,
            FinalArrivalDistance = 0.45f,
            FacingSharpness = 12f,
            MaxFineIterations = 4096,
            MaxAbstractIterations = 4096,
            AllowPartial = 0,
            Run = 0,
            FacePath = 1,
        };
    }

    public struct PathGridTraversalPendingRequest : IComponentData, IEnableableComponent
    {
        public int StartNodeIndex;
        public int GoalNodeIndex;
        public float3 FinalTargetPosition;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Run;
        public byte UseFinalTargetPosition;
    }

    public struct PathGridTraversalAwaitingResult : IComponentData, IEnableableComponent
    {
    }

    public struct PathGridTraversalState : IComponentData
    {
        public int ActivePathRequestId;
        public int CurrentNodeOffset;
        public int PathNodeCount;
        public int LastStartNodeIndex;
        public int LastGoalNodeIndex;
        public float PathCost;
        public float3 FinalTargetPosition;
        public byte Status;
        public byte UsedAbstractRoute;
        public byte ReachedGoal;
        public byte Run;
        public byte UseFinalTargetPosition;
    }

    public struct PathGridTraversalNode : IBufferElementData
    {
        public int NodeIndex;
    }
}
