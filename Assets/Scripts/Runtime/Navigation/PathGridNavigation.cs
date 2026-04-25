using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Pathfinding
{
    public enum PathGridPathStatus : byte
    {
        Failed = 0,
        Success = 1,
        Partial = 2,
    }

    public struct PathGridPathRequest
    {
        public int StartNodeIndex;
        public int GoalNodeIndex;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
    }

    public struct PathGridPathResult
    {
        public PathGridPathStatus Status;
        public float Cost;
        public int OutputStartIndex;
        public int OutputCount;
        public byte UsedAbstractRoute;
        public byte ReachedGoal;
    }

    public struct PathGridNavigationPathGridInfo
    {
        public int GridX;
        public int GridY;
        public int FirstNodeIndex;
        public int NodeCount;
        public int FirstPortalIndex;
        public int PortalCount;
        public int ComponentId;
        public byte IsExterior;
    }

    public struct PathGridNavigationNativeMinHeap : IDisposable
    {
        struct Entry
        {
            public int Key;
            public float Priority;
        }

        NativeList<Entry> _heap;

        public PathGridNavigationNativeMinHeap(int capacity, Allocator allocator)
        {
            _heap = new NativeList<Entry>(math.max(1, capacity), allocator);
        }

        public bool IsCreated => _heap.IsCreated;
        public bool IsEmpty => !_heap.IsCreated || _heap.Length == 0;
        public void Clear() => _heap.Clear();

        public void Insert(int key, float priority)
        {
            _heap.Add(new Entry { Key = key, Priority = priority });
            HeapifyUp(_heap.Length - 1);
        }

        public int ExtractMin()
        {
            if (_heap.Length == 0)
                return -1;

            var min = _heap[0];
            _heap[0] = _heap[_heap.Length - 1];
            _heap.RemoveAt(_heap.Length - 1);
            if (_heap.Length > 0)
                HeapifyDown(0);
            return min.Key;
        }

        void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_heap[index].Priority >= _heap[parent].Priority)
                    break;
                Swap(index, parent);
                index = parent;
            }
        }

        void HeapifyDown(int index)
        {
            int last = _heap.Length - 1;
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;
                if (left <= last && _heap[left].Priority < _heap[smallest].Priority)
                    smallest = left;
                if (right <= last && _heap[right].Priority < _heap[smallest].Priority)
                    smallest = right;
                if (smallest == index)
                    break;
                Swap(index, smallest);
                index = smallest;
            }
        }

        void Swap(int a, int b)
        {
            var temp = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = temp;
        }

        public void Dispose()
        {
            if (_heap.IsCreated)
                _heap.Dispose();
        }
    }

    public struct PathGridPathWorkingSet : IDisposable
    {
        public NativeArray<float> NodeG;
        public NativeArray<int> NodeParent;
        public NativeArray<byte> NodeState;
        public NativeList<int> TouchedNodes;
        public PathGridNavigationNativeMinHeap NodeOpen;

        public NativeArray<float> PortalG;
        public NativeArray<int> PortalParent;
        public NativeArray<byte> PortalState;
        public NativeList<int> TouchedPortals;
        public PathGridNavigationNativeMinHeap PortalOpen;

        public NativeList<int> Reverse;
        public NativeList<int> AbstractReverse;
        public NativeList<int> AbstractPath;
        public NativeList<int> StartPortals;
        public NativeList<float> StartPortalCosts;
        public NativeList<int> GoalPortals;
        public NativeList<float> GoalPortalCosts;

        public PathGridPathWorkingSet(int nodeCount, int portalCount, Allocator allocator)
        {
            NodeG = new NativeArray<float>(math.max(1, nodeCount), allocator);
            NodeParent = new NativeArray<int>(math.max(1, nodeCount), allocator);
            NodeState = new NativeArray<byte>(math.max(1, nodeCount), allocator);
            TouchedNodes = new NativeList<int>(math.max(16, nodeCount / 8), allocator);
            NodeOpen = new PathGridNavigationNativeMinHeap(math.max(16, nodeCount / 8), allocator);

            PortalG = new NativeArray<float>(math.max(1, portalCount), allocator);
            PortalParent = new NativeArray<int>(math.max(1, portalCount), allocator);
            PortalState = new NativeArray<byte>(math.max(1, portalCount), allocator);
            TouchedPortals = new NativeList<int>(math.max(16, portalCount / 8), allocator);
            PortalOpen = new PathGridNavigationNativeMinHeap(math.max(16, portalCount / 8), allocator);

            Reverse = new NativeList<int>(256, allocator);
            AbstractReverse = new NativeList<int>(128, allocator);
            AbstractPath = new NativeList<int>(128, allocator);
            StartPortals = new NativeList<int>(64, allocator);
            StartPortalCosts = new NativeList<float>(64, allocator);
            GoalPortals = new NativeList<int>(64, allocator);
            GoalPortalCosts = new NativeList<float>(64, allocator);
        }

        public void Clear()
        {
            for (int i = 0; i < TouchedNodes.Length; i++)
            {
                int node = TouchedNodes[i];
                NodeG[node] = 0f;
                NodeParent[node] = -1;
                NodeState[node] = 0;
            }

            for (int i = 0; i < TouchedPortals.Length; i++)
            {
                int portal = TouchedPortals[i];
                PortalG[portal] = 0f;
                PortalParent[portal] = -1;
                PortalState[portal] = 0;
            }

            TouchedNodes.Clear();
            TouchedPortals.Clear();
            NodeOpen.Clear();
            PortalOpen.Clear();
            Reverse.Clear();
            AbstractReverse.Clear();
            AbstractPath.Clear();
            StartPortals.Clear();
            StartPortalCosts.Clear();
            GoalPortals.Clear();
            GoalPortalCosts.Clear();
        }

        public void Dispose()
        {
            if (NodeG.IsCreated) NodeG.Dispose();
            if (NodeParent.IsCreated) NodeParent.Dispose();
            if (NodeState.IsCreated) NodeState.Dispose();
            if (TouchedNodes.IsCreated) TouchedNodes.Dispose();
            NodeOpen.Dispose();
            if (PortalG.IsCreated) PortalG.Dispose();
            if (PortalParent.IsCreated) PortalParent.Dispose();
            if (PortalState.IsCreated) PortalState.Dispose();
            if (TouchedPortals.IsCreated) TouchedPortals.Dispose();
            PortalOpen.Dispose();
            if (Reverse.IsCreated) Reverse.Dispose();
            if (AbstractReverse.IsCreated) AbstractReverse.Dispose();
            if (AbstractPath.IsCreated) AbstractPath.Dispose();
            if (StartPortals.IsCreated) StartPortals.Dispose();
            if (StartPortalCosts.IsCreated) StartPortalCosts.Dispose();
            if (GoalPortals.IsCreated) GoalPortals.Dispose();
            if (GoalPortalCosts.IsCreated) GoalPortalCosts.Dispose();
        }
    }

    public struct PathGridNavigationWorld : IDisposable
    {
        [ReadOnly] public NativeArray<PathGridNavigationPathGridInfo> PathGrids;
        [ReadOnly] public NativeArray<PathGridNavigationNodeDef> Nodes;
        [ReadOnly] public NativeArray<PathGridNavigationEdgeDef> Edges;
        [ReadOnly] public NativeArray<PathGridNavigationPortalDef> Portals;
        [ReadOnly] public NativeArray<PathGridNavigationAbstractEdgeDef> AbstractEdges;
        [ReadOnly] public NativeArray<PathGridNavigationNeighborDef> Neighbors;

        public bool IsCreated => Nodes.IsCreated;
        public int NodeCount => Nodes.IsCreated ? Nodes.Length : 0;
        public int PortalCount => Portals.IsCreated ? Portals.Length : 0;

        public static PathGridNavigationWorld Create(RuntimeContentDatabase database, Allocator allocator = Allocator.Persistent)
        {
            var data = database?.Data ?? new GameplayContentData();
            var pathGrids = data.PathGrids ?? Array.Empty<PathGridDef>();
            var world = new PathGridNavigationWorld
            {
                PathGrids = new NativeArray<PathGridNavigationPathGridInfo>(pathGrids.Length, allocator),
                Nodes = new NativeArray<PathGridNavigationNodeDef>(data.PathGridNavigationNodes ?? Array.Empty<PathGridNavigationNodeDef>(), allocator),
                Edges = new NativeArray<PathGridNavigationEdgeDef>(data.PathGridNavigationEdges ?? Array.Empty<PathGridNavigationEdgeDef>(), allocator),
                Portals = new NativeArray<PathGridNavigationPortalDef>(data.PathGridNavigationPortals ?? Array.Empty<PathGridNavigationPortalDef>(), allocator),
                AbstractEdges = new NativeArray<PathGridNavigationAbstractEdgeDef>(data.PathGridNavigationAbstractEdges ?? Array.Empty<PathGridNavigationAbstractEdgeDef>(), allocator),
                Neighbors = new NativeArray<PathGridNavigationNeighborDef>(data.PathGridNavigationNeighbors ?? Array.Empty<PathGridNavigationNeighborDef>(), allocator),
            };

            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                world.PathGrids[i] = new PathGridNavigationPathGridInfo
                {
                    GridX = pathGrid.GridX,
                    GridY = pathGrid.GridY,
                    FirstNodeIndex = pathGrid.FirstNavigationNodeIndex,
                    NodeCount = pathGrid.NavigationNodeCount,
                    FirstPortalIndex = pathGrid.FirstNavigationPortalIndex,
                    PortalCount = pathGrid.NavigationPortalCount,
                    ComponentId = pathGrid.NavigationComponentId,
                    IsExterior = pathGrid.IsExterior,
                };
            }

            return world;
        }

        public PathGridPathWorkingSet CreateWorkingSet(Allocator allocator = Allocator.Persistent)
            => new PathGridPathWorkingSet(NodeCount, PortalCount, allocator);

        public bool TryResolveNearestNodeInPathGrid(int pathGridIndex, float3 worldPosition, out int nodeIndex)
        {
            nodeIndex = -1;
            if (!PathGrids.IsCreated || (uint)pathGridIndex >= (uint)PathGrids.Length)
                return false;

            var pathGrid = PathGrids[pathGridIndex];
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < pathGrid.NodeCount; i++)
            {
                int candidate = pathGrid.FirstNodeIndex + i;
                if ((uint)candidate >= (uint)Nodes.Length)
                    continue;

                float distance = math.lengthsq(GetNodePosition(Nodes[candidate]) - worldPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nodeIndex = candidate;
                }
            }

            return nodeIndex >= 0;
        }

        public bool TryResolveNearestExteriorNode(int gridX, int gridY, float3 worldPosition, out int nodeIndex)
        {
            nodeIndex = -1;
            if (!PathGrids.IsCreated)
                return false;

            for (int i = 0; i < PathGrids.Length; i++)
            {
                var pathGrid = PathGrids[i];
                if (pathGrid.IsExterior != 0 && pathGrid.GridX == gridX && pathGrid.GridY == gridY)
                    return TryResolveNearestNodeInPathGrid(i, worldPosition, out nodeIndex);
            }

            return false;
        }

        public static float3 GetNodePosition(PathGridNavigationNodeDef node)
            => new float3(node.UnityX, node.UnityY, node.UnityZ);

        public void Dispose()
        {
            if (PathGrids.IsCreated) PathGrids.Dispose();
            if (Nodes.IsCreated) Nodes.Dispose();
            if (Edges.IsCreated) Edges.Dispose();
            if (Portals.IsCreated) Portals.Dispose();
            if (AbstractEdges.IsCreated) AbstractEdges.Dispose();
            if (Neighbors.IsCreated) Neighbors.Dispose();
        }
    }

    [BurstCompile]
    public struct PathGridPathfindingJob : IJob
    {
        [ReadOnly] public PathGridNavigationWorld World;
        [ReadOnly] public PathGridPathRequest Request;
        public PathGridPathWorkingSet WorkingSet;
        public NativeList<int> OutputPath;
        public NativeArray<PathGridPathResult> Result;
        public int ResultIndex;

        public void Execute()
        {
            WorkingSet.Clear();
            OutputPath.Clear();

            var result = new PathGridPathResult
            {
                Status = PathGridPathStatus.Failed,
                Cost = float.PositiveInfinity,
                OutputStartIndex = 0,
                OutputCount = 0,
            };

            if (!World.IsCreated ||
                (uint)Request.StartNodeIndex >= (uint)World.Nodes.Length ||
                (uint)Request.GoalNodeIndex >= (uint)World.Nodes.Length)
            {
                WriteResult(result);
                return;
            }

            var start = World.Nodes[Request.StartNodeIndex];
            var goal = World.Nodes[Request.GoalNodeIndex];
            if (start.ComponentId != goal.ComponentId)
            {
                WriteResult(result);
                return;
            }

            if (Request.StartNodeIndex == Request.GoalNodeIndex)
            {
                OutputPath.Add(Request.StartNodeIndex);
                result.Status = PathGridPathStatus.Success;
                result.Cost = 0f;
                result.OutputCount = 1;
                result.ReachedGoal = 1;
                WriteResult(result);
                return;
            }

            int maxFineIterations = math.max(1, Request.MaxFineIterations);
            int maxAbstractIterations = math.max(1, Request.MaxAbstractIterations);
            if (start.PathGridIndex == goal.PathGridIndex)
            {
                if (RunFineAStar(Request.StartNodeIndex, Request.GoalNodeIndex, start.PathGridIndex, maxFineIterations, Request.AllowPartial != 0, false, out float cost, out bool reached))
                {
                    result.Status = reached ? PathGridPathStatus.Success : PathGridPathStatus.Partial;
                    result.Cost = cost;
                    result.OutputCount = OutputPath.Length;
                    result.ReachedGoal = ToByte(reached);
                }

                WriteResult(result);
                return;
            }

            if (CollectReachablePortals(Request.StartNodeIndex, start.PathGridIndex, WorkingSet.StartPortals, WorkingSet.StartPortalCosts, maxFineIterations) == 0 ||
                CollectGoalPortals(Request.GoalNodeIndex, goal.PathGridIndex, WorkingSet.GoalPortals, WorkingSet.GoalPortalCosts, maxFineIterations) == 0)
            {
                WriteResult(result);
                return;
            }

            if (!RunAbstractAStar(maxAbstractIterations, out int terminalPortal, out float abstractCost, out bool reachedGoalPortal))
            {
                WriteResult(result);
                return;
            }

            ReconstructAbstractPath(terminalPortal);
            if (RefineAbstractPath(Request.StartNodeIndex, Request.GoalNodeIndex, start.PathGridIndex, goal.PathGridIndex, maxFineIterations, out float refinedCost))
            {
                result.Status = reachedGoalPortal ? PathGridPathStatus.Success : PathGridPathStatus.Partial;
                result.Cost = refinedCost > 0f ? refinedCost : abstractCost;
                result.OutputCount = OutputPath.Length;
                result.UsedAbstractRoute = 1;
                result.ReachedGoal = ToByte(reachedGoalPortal);
            }

            WriteResult(result);
        }

        int CollectReachablePortals(int originNode, int pathGridIndex, NativeList<int> portals, NativeList<float> costs, int maxIterations)
        {
            portals.Clear();
            costs.Clear();
            if ((uint)pathGridIndex >= (uint)World.PathGrids.Length)
                return 0;

            var pathGrid = World.PathGrids[pathGridIndex];
            for (int i = 0; i < pathGrid.PortalCount; i++)
            {
                int portalIndex = pathGrid.FirstPortalIndex + i;
                if ((uint)portalIndex >= (uint)World.Portals.Length)
                    continue;

                int portalNode = World.Portals[portalIndex].NodeIndex;
                if (RunFineAStar(originNode, portalNode, pathGridIndex, maxIterations, false, true, out float cost, out bool reached) && reached)
                {
                    portals.Add(portalIndex);
                    costs.Add(cost);
                }
            }

            OutputPath.Clear();
            return portals.Length;
        }

        int CollectGoalPortals(int goalNode, int pathGridIndex, NativeList<int> portals, NativeList<float> costs, int maxIterations)
        {
            portals.Clear();
            costs.Clear();
            if ((uint)pathGridIndex >= (uint)World.PathGrids.Length)
                return 0;

            var pathGrid = World.PathGrids[pathGridIndex];
            for (int i = 0; i < pathGrid.PortalCount; i++)
            {
                int portalIndex = pathGrid.FirstPortalIndex + i;
                if ((uint)portalIndex >= (uint)World.Portals.Length)
                    continue;

                int portalNode = World.Portals[portalIndex].NodeIndex;
                if (RunFineAStar(portalNode, goalNode, pathGridIndex, maxIterations, false, true, out float cost, out bool reached) && reached)
                {
                    portals.Add(portalIndex);
                    costs.Add(cost);
                }
            }

            OutputPath.Clear();
            return portals.Length;
        }

        bool RunAbstractAStar(int maxIterations, out int terminalPortal, out float terminalCost, out bool reachedGoal)
        {
            terminalPortal = -1;
            terminalCost = float.PositiveInfinity;
            reachedGoal = false;

            for (int i = 0; i < WorkingSet.StartPortals.Length; i++)
            {
                int portal = WorkingSet.StartPortals[i];
                float g = WorkingSet.StartPortalCosts[i];
                TouchPortal(portal);
                WorkingSet.PortalG[portal] = g;
                WorkingSet.PortalParent[portal] = -1;
                WorkingSet.PortalState[portal] = 1;
                WorkingSet.PortalOpen.Insert(portal, g + AbstractHeuristic(portal));
            }

            int bestPartialPortal = -1;
            float bestPartialHeuristic = float.PositiveInfinity;
            int iterations = 0;
            while (!WorkingSet.PortalOpen.IsEmpty && iterations++ < maxIterations)
            {
                int current = WorkingSet.PortalOpen.ExtractMin();
                if ((uint)current >= (uint)World.Portals.Length || WorkingSet.PortalState[current] == 2)
                    continue;

                WorkingSet.PortalState[current] = 2;
                float currentG = WorkingSet.PortalG[current];
                float currentH = AbstractHeuristic(current);
                if (currentH < bestPartialHeuristic)
                {
                    bestPartialHeuristic = currentH;
                    bestPartialPortal = current;
                }

                if (TryGetGoalPortalCost(current, out float goalCost))
                {
                    terminalPortal = current;
                    terminalCost = currentG + goalCost;
                    reachedGoal = true;
                    return true;
                }

                var portal = World.Portals[current];
                for (int i = 0; i < portal.AbstractEdgeCount; i++)
                {
                    int edgeIndex = portal.FirstAbstractEdgeIndex + i;
                    if ((uint)edgeIndex >= (uint)World.AbstractEdges.Length)
                        continue;

                    var edge = World.AbstractEdges[edgeIndex];
                    int next = edge.ToPortalIndex;
                    if ((uint)next >= (uint)World.Portals.Length || WorkingSet.PortalState[next] == 2)
                        continue;

                    float tentative = currentG + edge.Cost;
                    if (WorkingSet.PortalState[next] != 0 && tentative >= WorkingSet.PortalG[next])
                        continue;

                    TouchPortal(next);
                    WorkingSet.PortalG[next] = tentative;
                    WorkingSet.PortalParent[next] = current;
                    WorkingSet.PortalState[next] = 1;
                    WorkingSet.PortalOpen.Insert(next, tentative + AbstractHeuristic(next));
                }
            }

            if (Request.AllowPartial != 0 && bestPartialPortal >= 0)
            {
                terminalPortal = bestPartialPortal;
                terminalCost = WorkingSet.PortalG[bestPartialPortal];
                reachedGoal = false;
                return true;
            }

            return false;
        }

        bool RefineAbstractPath(int startNode, int goalNode, int startPathGrid, int goalPathGrid, int maxIterations, out float cost)
        {
            cost = 0f;
            if (WorkingSet.AbstractPath.Length == 0)
                return false;

            int currentNode = startNode;
            for (int i = 0; i < WorkingSet.AbstractPath.Length; i++)
            {
                int portalIndex = WorkingSet.AbstractPath[i];
                int portalNode = World.Portals[portalIndex].NodeIndex;
                int pathGridIndex = World.Nodes[currentNode].PathGridIndex;
                if (World.Nodes[currentNode].PathGridIndex == World.Nodes[portalNode].PathGridIndex)
                {
                    if (!RunFineAStar(currentNode, portalNode, pathGridIndex, maxIterations, false, OutputPath.Length > 0, out float segmentCost, out bool reached) || !reached)
                        return false;
                    cost += segmentCost;
                }
                else
                {
                    AppendNode(portalNode);
                    cost += Distance(currentNode, portalNode);
                }

                currentNode = portalNode;
            }

            int lastPathGrid = World.Nodes[currentNode].PathGridIndex;
            if (lastPathGrid == goalPathGrid)
            {
                if (!RunFineAStar(currentNode, goalNode, goalPathGrid, maxIterations, Request.AllowPartial != 0, OutputPath.Length > 0, out float finalCost, out bool reachedGoal) || !reachedGoal)
                    return Request.AllowPartial != 0 && OutputPath.Length > 0;
                cost += finalCost;
            }

            return true;
        }

        bool RunFineAStar(int startNode, int goalNode, int pathGridIndex, int maxIterations, bool allowPartial, bool append, out float cost, out bool reachedGoal)
        {
            ClearNodeSearch();
            cost = float.PositiveInfinity;
            reachedGoal = false;

            TouchNode(startNode);
            WorkingSet.NodeG[startNode] = 0f;
            WorkingSet.NodeParent[startNode] = -1;
            WorkingSet.NodeState[startNode] = 1;
            WorkingSet.NodeOpen.Insert(startNode, Heuristic(startNode, goalNode));

            int bestPartial = startNode;
            float bestPartialHeuristic = Heuristic(startNode, goalNode);
            int iterations = 0;
            while (!WorkingSet.NodeOpen.IsEmpty && iterations++ < maxIterations)
            {
                int current = WorkingSet.NodeOpen.ExtractMin();
                if ((uint)current >= (uint)World.Nodes.Length || WorkingSet.NodeState[current] == 2)
                    continue;

                WorkingSet.NodeState[current] = 2;
                float h = Heuristic(current, goalNode);
                if (h < bestPartialHeuristic)
                {
                    bestPartialHeuristic = h;
                    bestPartial = current;
                }

                if (current == goalNode)
                {
                    cost = WorkingSet.NodeG[current];
                    reachedGoal = true;
                    ReconstructFinePath(goalNode, append);
                    return true;
                }

                var node = World.Nodes[current];
                for (int i = 0; i < node.EdgeCount; i++)
                {
                    int edgeIndex = node.FirstEdgeIndex + i;
                    if ((uint)edgeIndex >= (uint)World.Edges.Length)
                        continue;

                    var edge = World.Edges[edgeIndex];
                    if (edge.Kind != PathGridNavigationEdgeKind.Authored)
                        continue;

                    int next = edge.ToNodeIndex;
                    if ((uint)next >= (uint)World.Nodes.Length ||
                        World.Nodes[next].PathGridIndex != pathGridIndex ||
                        WorkingSet.NodeState[next] == 2)
                    {
                        continue;
                    }

                    float tentative = WorkingSet.NodeG[current] + edge.Cost;
                    if (WorkingSet.NodeState[next] != 0 && tentative >= WorkingSet.NodeG[next])
                        continue;

                    TouchNode(next);
                    WorkingSet.NodeG[next] = tentative;
                    WorkingSet.NodeParent[next] = current;
                    WorkingSet.NodeState[next] = 1;
                    WorkingSet.NodeOpen.Insert(next, tentative + Heuristic(next, goalNode));
                }
            }

            if (allowPartial && bestPartial != startNode)
            {
                cost = WorkingSet.NodeG[bestPartial];
                ReconstructFinePath(bestPartial, append);
                return true;
            }

            return false;
        }

        void ReconstructFinePath(int terminalNode, bool append)
        {
            WorkingSet.Reverse.Clear();
            int current = terminalNode;
            int guard = World.Nodes.Length + 1;
            while ((uint)current < (uint)World.Nodes.Length && guard-- > 0)
            {
                WorkingSet.Reverse.Add(current);
                current = WorkingSet.NodeParent[current];
                if (current < 0)
                    break;
            }

            if (!append)
                OutputPath.Clear();
            for (int i = WorkingSet.Reverse.Length - 1; i >= 0; i--)
                AppendNode(WorkingSet.Reverse[i]);
        }

        void ReconstructAbstractPath(int terminalPortal)
        {
            WorkingSet.AbstractReverse.Clear();
            WorkingSet.AbstractPath.Clear();
            int current = terminalPortal;
            int guard = World.Portals.Length + 1;
            while ((uint)current < (uint)World.Portals.Length && guard-- > 0)
            {
                WorkingSet.AbstractReverse.Add(current);
                current = WorkingSet.PortalParent[current];
                if (current < 0)
                    break;
            }

            for (int i = WorkingSet.AbstractReverse.Length - 1; i >= 0; i--)
                WorkingSet.AbstractPath.Add(WorkingSet.AbstractReverse[i]);
        }

        bool TryGetGoalPortalCost(int portal, out float cost)
        {
            for (int i = 0; i < WorkingSet.GoalPortals.Length; i++)
            {
                if (WorkingSet.GoalPortals[i] == portal)
                {
                    cost = WorkingSet.GoalPortalCosts[i];
                    return true;
                }
            }

            cost = 0f;
            return false;
        }

        float AbstractHeuristic(int portal)
        {
            if ((uint)portal >= (uint)World.Portals.Length || WorkingSet.GoalPortals.Length == 0)
                return 0f;

            int node = World.Portals[portal].NodeIndex;
            float best = float.PositiveInfinity;
            for (int i = 0; i < WorkingSet.GoalPortals.Length; i++)
            {
                int goalNode = World.Portals[WorkingSet.GoalPortals[i]].NodeIndex;
                best = math.min(best, Heuristic(node, goalNode));
            }

            return best;
        }

        float Heuristic(int fromNode, int toNode) => Distance(fromNode, toNode);

        float Distance(int fromNode, int toNode)
        {
            float3 a = PathGridNavigationWorld.GetNodePosition(World.Nodes[fromNode]);
            float3 b = PathGridNavigationWorld.GetNodePosition(World.Nodes[toNode]);
            return math.distance(a, b);
        }

        void TouchNode(int node)
        {
            if (WorkingSet.NodeState[node] == 0)
                WorkingSet.TouchedNodes.Add(node);
        }

        void TouchPortal(int portal)
        {
            if (WorkingSet.PortalState[portal] == 0)
                WorkingSet.TouchedPortals.Add(portal);
        }

        void ClearNodeSearch()
        {
            for (int i = 0; i < WorkingSet.TouchedNodes.Length; i++)
            {
                int node = WorkingSet.TouchedNodes[i];
                WorkingSet.NodeG[node] = 0f;
                WorkingSet.NodeParent[node] = -1;
                WorkingSet.NodeState[node] = 0;
            }

            WorkingSet.TouchedNodes.Clear();
            WorkingSet.NodeOpen.Clear();
            WorkingSet.Reverse.Clear();
        }

        void AppendNode(int node)
        {
            if (OutputPath.Length == 0 || OutputPath[OutputPath.Length - 1] != node)
                OutputPath.Add(node);
        }

        void WriteResult(PathGridPathResult result)
        {
            result.OutputStartIndex = 0;
            result.OutputCount = OutputPath.Length;
            if (Result.IsCreated && (uint)ResultIndex < (uint)Result.Length)
                Result[ResultIndex] = result;
        }

        static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
    }
}
