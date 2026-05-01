using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{

    internal static partial class GameplayContentBakery
    {
        static T[] OrderByNormalizedId<T>(Dictionary<string, T> map)
        {
            return map
                .OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToArray();
        }


        static void BuildPathGridArrays(
            Dictionary<string, PathGridAccumulator> map,
            out PathGridDef[] pathGrids,
            out PathGridPointDef[] points,
            out PathGridConnectionDef[] connections)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            pathGrids = new PathGridDef[ordered.Length];
            var flatPoints = new List<PathGridPointDef>(ordered.Sum(pair => pair.Value.Points.Count));
            var flatConnections = new List<PathGridConnectionDef>(ordered.Sum(pair => pair.Value.RawConnectionTargets.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var accumulator = ordered[i].Value;
                var def = accumulator.Def;
                def.FirstPointIndex = accumulator.Points.Count > 0 ? flatPoints.Count : -1;
                def.PointCount = accumulator.Points.Count;
                def.FirstConnectionIndex = accumulator.RawConnectionTargets.Count > 0 ? flatConnections.Count : -1;

                int rawConnectionIndex = 0;
                for (int pointIndex = 0; pointIndex < accumulator.Points.Count; pointIndex++)
                {
                    var point = accumulator.Points[pointIndex];
                    int available = Math.Max(0, accumulator.RawConnectionTargets.Count - rawConnectionIndex);
                    int connectionCount = Math.Min(point.SourceConnectionCount, available);
                    point.FirstConnectionIndex = connectionCount > 0 ? flatConnections.Count : -1;
                    point.ConnectionCount = connectionCount;

                    for (int j = 0; j < connectionCount; j++)
                    {
                        flatConnections.Add(new PathGridConnectionDef
                        {
                            FromPointIndex = pointIndex,
                            ToPointIndex = accumulator.RawConnectionTargets[rawConnectionIndex],
                        });
                        rawConnectionIndex++;
                    }

                    flatPoints.Add(point);
                }

                int unusedConnections = Math.Max(0, accumulator.RawConnectionTargets.Count - rawConnectionIndex);
                for (int j = 0; j < unusedConnections; j++)
                {
                    flatConnections.Add(new PathGridConnectionDef
                    {
                        FromPointIndex = -1,
                        ToPointIndex = accumulator.RawConnectionTargets[rawConnectionIndex + j],
                    });
                }

                def.ConnectionCount = accumulator.RawConnectionTargets.Count;
                pathGrids[i] = def;
            }

            points = flatPoints.ToArray();
            connections = flatConnections.ToArray();
        }


        static void BuildPathGridNavigationArrays(
            ref PathGridDef[] pathGrids,
            PathGridPointDef[] points,
            PathGridConnectionDef[] connections,
            out PathGridNavigationNodeDef[] navigationNodes,
            out PathGridNavigationEdgeDef[] navigationEdges,
            out PathGridNavigationPortalDef[] navigationPortals,
            out PathGridNavigationAbstractEdgeDef[] navigationAbstractEdges,
            out PathGridNavigationNeighborDef[] navigationNeighbors)
        {
            pathGrids ??= Array.Empty<PathGridDef>();
            points ??= Array.Empty<PathGridPointDef>();
            connections ??= Array.Empty<PathGridConnectionDef>();

            var nodeList = new List<PathGridNavigationNodeDef>(points.Length);
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                pathGrid.FirstNavigationNodeIndex = pathGrid.PointCount > 0 ? nodeList.Count : -1;
                pathGrid.NavigationNodeCount = pathGrid.PointCount;
                pathGrid.FirstNavigationEdgeIndex = -1;
                pathGrid.NavigationEdgeCount = 0;
                pathGrid.FirstNavigationPortalIndex = -1;
                pathGrid.NavigationPortalCount = 0;
                pathGrid.FirstNavigationAbstractEdgeIndex = -1;
                pathGrid.NavigationAbstractEdgeCount = 0;
                pathGrid.FirstNavigationNeighborIndex = -1;
                pathGrid.NavigationNeighborCount = 0;
                pathGrid.NavigationComponentId = -1;

                for (int pointOffset = 0; pointOffset < pathGrid.PointCount; pointOffset++)
                {
                    int pointIndex = pathGrid.FirstPointIndex + pointOffset;
                    if ((uint)pointIndex >= (uint)points.Length)
                        continue;

                    var point = points[pointIndex];
                    nodeList.Add(new PathGridNavigationNodeDef
                    {
                        PathGridIndex = pathGridIndex,
                        PointIndex = pointOffset,
                        SourceX = point.SourceX,
                        SourceY = point.SourceY,
                        SourceZ = point.SourceZ,
                        UnityX = point.UnityX,
                        UnityY = point.UnityY,
                        UnityZ = point.UnityZ,
                        FirstEdgeIndex = -1,
                        ComponentId = -1,
                    });
                }

                pathGrids[pathGridIndex] = pathGrid;
            }

            var outgoing = new List<NavigationEdgeDraft>[nodeList.Count];
            for (int i = 0; i < outgoing.Length; i++)
                outgoing[i] = new List<NavigationEdgeDraft>();

            var edgeKeys = new HashSet<long>();
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.FirstNavigationNodeIndex < 0)
                    continue;

                for (int connectionOffset = 0; connectionOffset < pathGrid.ConnectionCount; connectionOffset++)
                {
                    int connectionIndex = pathGrid.FirstConnectionIndex + connectionOffset;
                    if ((uint)connectionIndex >= (uint)connections.Length)
                        continue;

                    var connection = connections[connectionIndex];
                    if ((uint)connection.FromPointIndex >= (uint)pathGrid.NavigationNodeCount ||
                        (uint)connection.ToPointIndex >= (uint)pathGrid.NavigationNodeCount)
                    {
                        continue;
                    }

                    int fromNode = pathGrid.FirstNavigationNodeIndex + connection.FromPointIndex;
                    int toNode = pathGrid.FirstNavigationNodeIndex + connection.ToPointIndex;
                    AddNavigationEdge(outgoing, edgeKeys, nodeList, fromNode, toNode, PathGridNavigationEdgeKind.Authored);
                }
            }

            InferExteriorBorderNavigationEdges(pathGrids, nodeList, outgoing, edgeKeys);

            var union = new NavigationUnionFind(nodeList.Count);
            for (int fromNode = 0; fromNode < outgoing.Length; fromNode++)
            {
                for (int edgeIndex = 0; edgeIndex < outgoing[fromNode].Count; edgeIndex++)
                    union.Union(fromNode, outgoing[fromNode][edgeIndex].ToNodeIndex);
            }

            AssignNavigationComponents(ref pathGrids, nodeList, union);
            navigationEdges = FlattenNavigationEdges(ref pathGrids, nodeList, outgoing);
            BuildNavigationPortalsAndAbstractEdges(
                ref pathGrids,
                nodeList,
                outgoing,
                out navigationPortals,
                out navigationAbstractEdges,
                out navigationNeighbors);
            navigationNodes = nodeList.ToArray();
        }


        static void InferExteriorBorderNavigationEdges(
            PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys)
        {
            var exteriorByCoord = new Dictionary<long, int>();
            for (int i = 0; i < pathGrids.Length; i++)
            {
                if (pathGrids[i].IsExterior != 0)
                    exteriorByCoord[PackExteriorPathGridKey(pathGrids[i].GridX, pathGrids[i].GridY)] = i;
            }

            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.IsExterior == 0 || pathGrid.NavigationNodeCount <= 0)
                    continue;

                TryInferExteriorBorderNavigationEdges(pathGrids, nodes, outgoing, edgeKeys, exteriorByCoord, pathGridIndex, 1, 0);
                TryInferExteriorBorderNavigationEdges(pathGrids, nodes, outgoing, edgeKeys, exteriorByCoord, pathGridIndex, 0, 1);
            }
        }


        static void TryInferExteriorBorderNavigationEdges(
            PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys,
            Dictionary<long, int> exteriorByCoord,
            int pathGridIndex,
            int deltaX,
            int deltaY)
        {
            var a = pathGrids[pathGridIndex];
            if (!exteriorByCoord.TryGetValue(PackExteriorPathGridKey(a.GridX + deltaX, a.GridY + deltaY), out int neighborIndex))
                return;

            var b = pathGrids[neighborIndex];
            if (b.NavigationNodeCount <= 0)
                return;

            int borderX = deltaX != 0 ? Math.Max(a.GridX, b.GridX) * LandRecordSize.CellUnitsMw : 0;
            int borderY = deltaY != 0 ? Math.Max(a.GridY, b.GridY) * LandRecordSize.CellUnitsMw : 0;

            for (int aOffset = 0; aOffset < a.NavigationNodeCount; aOffset++)
            {
                int aNodeIndex = a.FirstNavigationNodeIndex + aOffset;
                var aNode = nodes[aNodeIndex];
                if (!IsNearExteriorBorder(aNode, borderX, borderY, deltaX, deltaY))
                    continue;

                for (int bOffset = 0; bOffset < b.NavigationNodeCount; bOffset++)
                {
                    int bNodeIndex = b.FirstNavigationNodeIndex + bOffset;
                    var bNode = nodes[bNodeIndex];
                    if (!IsNearExteriorBorder(bNode, borderX, borderY, deltaX, deltaY))
                        continue;

                    int dx = aNode.SourceX - bNode.SourceX;
                    int dy = aNode.SourceY - bNode.SourceY;
                    int dz = aNode.SourceZ - bNode.SourceZ;
                    if (Math.Abs(dz) > ExteriorBorderVerticalDistanceMw)
                        continue;

                    long planarDistanceSq = (long)dx * dx + (long)dy * dy;
                    if (planarDistanceSq > (long)ExteriorBorderCandidateDistanceMw * ExteriorBorderCandidateDistanceMw)
                        continue;

                    AddNavigationEdge(outgoing, edgeKeys, nodes, aNodeIndex, bNodeIndex, PathGridNavigationEdgeKind.ExteriorBorder);
                    AddNavigationEdge(outgoing, edgeKeys, nodes, bNodeIndex, aNodeIndex, PathGridNavigationEdgeKind.ExteriorBorder);

                    aNode.IsPortal = 1;
                    bNode.IsPortal = 1;
                    nodes[aNodeIndex] = aNode;
                    nodes[bNodeIndex] = bNode;
                }
            }
        }


        static bool IsNearExteriorBorder(PathGridNavigationNodeDef node, int borderX, int borderY, int deltaX, int deltaY)
        {
            if (deltaX != 0)
                return Math.Abs(node.SourceX - borderX) <= ExteriorBorderCandidateDistanceMw;
            if (deltaY != 0)
                return Math.Abs(node.SourceY - borderY) <= ExteriorBorderCandidateDistanceMw;
            return false;
        }


        static void AddNavigationEdge(
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys,
            List<PathGridNavigationNodeDef> nodes,
            int fromNode,
            int toNode,
            PathGridNavigationEdgeKind kind)
        {
            if ((uint)fromNode >= (uint)outgoing.Length || (uint)toNode >= (uint)outgoing.Length)
                return;

            long key = PackNavigationEdgeKey(fromNode, toNode);
            if (!edgeKeys.Add(key))
                return;

            outgoing[fromNode].Add(new NavigationEdgeDraft(fromNode, toNode, NavigationDistance(nodes[fromNode], nodes[toNode]), kind));
        }


        static float NavigationDistance(PathGridNavigationNodeDef a, PathGridNavigationNodeDef b)
        {
            float dx = a.UnityX - b.UnityX;
            float dy = a.UnityY - b.UnityY;
            float dz = a.UnityZ - b.UnityZ;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }


        static long PackNavigationEdgeKey(int fromNode, int toNode)
            => ((long)fromNode << 32) ^ (uint)toNode;


        static long PackExteriorPathGridKey(int gridX, int gridY)
            => ((long)gridX << 32) ^ (uint)gridY;


        static void AssignNavigationComponents(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            NavigationUnionFind union)
        {
            var componentByRoot = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int root = union.Find(i);
                if (!componentByRoot.TryGetValue(root, out int componentId))
                {
                    componentId = componentByRoot.Count;
                    componentByRoot[root] = componentId;
                }

                var node = nodes[i];
                node.ComponentId = componentId;
                nodes[i] = node;
            }

            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                if (pathGrid.FirstNavigationNodeIndex >= 0 && pathGrid.NavigationNodeCount > 0)
                    pathGrid.NavigationComponentId = nodes[pathGrid.FirstNavigationNodeIndex].ComponentId;
                pathGrids[i] = pathGrid;
            }
        }


        static PathGridNavigationEdgeDef[] FlattenNavigationEdges(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing)
        {
            var flat = new List<PathGridNavigationEdgeDef>(outgoing.Sum(list => list.Count));
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                node.FirstEdgeIndex = outgoing[nodeIndex].Count > 0 ? flat.Count : -1;
                node.EdgeCount = outgoing[nodeIndex].Count;
                nodes[nodeIndex] = node;

                var pathGrid = pathGrids[node.PathGridIndex];
                if (outgoing[nodeIndex].Count > 0)
                {
                    if (pathGrid.FirstNavigationEdgeIndex < 0)
                        pathGrid.FirstNavigationEdgeIndex = flat.Count;
                    pathGrid.NavigationEdgeCount += outgoing[nodeIndex].Count;
                }

                for (int i = 0; i < outgoing[nodeIndex].Count; i++)
                {
                    var edge = outgoing[nodeIndex][i];
                    flat.Add(new PathGridNavigationEdgeDef
                    {
                        FromNodeIndex = edge.FromNodeIndex,
                        ToNodeIndex = edge.ToNodeIndex,
                        Cost = edge.Cost,
                        Kind = edge.Kind,
                    });
                }

                pathGrids[node.PathGridIndex] = pathGrid;
            }

            return flat.ToArray();
        }


        static void BuildNavigationPortalsAndAbstractEdges(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            out PathGridNavigationPortalDef[] portals,
            out PathGridNavigationAbstractEdgeDef[] abstractEdges,
            out PathGridNavigationNeighborDef[] neighbors)
        {
            var portalList = new List<PathGridNavigationPortalDef>();
            var portalByNode = new Dictionary<int, int>();
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                pathGrid.FirstNavigationPortalIndex = -1;
                pathGrid.NavigationPortalCount = 0;

                for (int localNode = 0; localNode < pathGrid.NavigationNodeCount; localNode++)
                {
                    int nodeIndex = pathGrid.FirstNavigationNodeIndex + localNode;
                    if ((uint)nodeIndex >= (uint)nodes.Count || nodes[nodeIndex].IsPortal == 0)
                        continue;

                    if (pathGrid.FirstNavigationPortalIndex < 0)
                        pathGrid.FirstNavigationPortalIndex = portalList.Count;

                    int portalIndex = portalList.Count;
                    portalByNode[nodeIndex] = portalIndex;
                    portalList.Add(new PathGridNavigationPortalDef
                    {
                        PathGridIndex = pathGridIndex,
                        NodeIndex = nodeIndex,
                        PointIndex = nodes[nodeIndex].PointIndex,
                        FirstAbstractEdgeIndex = -1,
                        ComponentId = nodes[nodeIndex].ComponentId,
                    });
                    pathGrid.NavigationPortalCount++;
                }

                pathGrids[pathGridIndex] = pathGrid;
            }

            var outgoingAbstract = new List<PathGridNavigationAbstractEdgeDef>[portalList.Count];
            for (int i = 0; i < outgoingAbstract.Length; i++)
                outgoingAbstract[i] = new List<PathGridNavigationAbstractEdgeDef>();
            var abstractKeys = new HashSet<long>();

            for (int nodeIndex = 0; nodeIndex < outgoing.Length; nodeIndex++)
            {
                for (int edgeIndex = 0; edgeIndex < outgoing[nodeIndex].Count; edgeIndex++)
                {
                    var edge = outgoing[nodeIndex][edgeIndex];
                    if (edge.Kind != PathGridNavigationEdgeKind.ExteriorBorder)
                        continue;
                    if (!portalByNode.TryGetValue(edge.FromNodeIndex, out int fromPortal) ||
                        !portalByNode.TryGetValue(edge.ToNodeIndex, out int toPortal))
                    {
                        continue;
                    }

                    AddAbstractEdge(outgoingAbstract, abstractKeys, fromPortal, toPortal, edge.Cost, PathGridNavigationEdgeKind.ExteriorBorder);
                }
            }

            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.NavigationPortalCount <= 1)
                    continue;

                int firstPortal = pathGrid.FirstNavigationPortalIndex;
                int endPortal = firstPortal + pathGrid.NavigationPortalCount;
                for (int fromPortal = firstPortal; fromPortal < endPortal; fromPortal++)
                {
                    for (int toPortal = firstPortal; toPortal < endPortal; toPortal++)
                    {
                        if (fromPortal == toPortal)
                            continue;

                        float cost = FindAuthoredPathCost(
                            pathGridIndex,
                            portalList[fromPortal].NodeIndex,
                            portalList[toPortal].NodeIndex,
                            nodes,
                            outgoing);
                        if (!float.IsPositiveInfinity(cost))
                            AddAbstractEdge(outgoingAbstract, abstractKeys, fromPortal, toPortal, cost, PathGridNavigationEdgeKind.IntraPathGrid);
                    }
                }
            }

            var flatAbstract = new List<PathGridNavigationAbstractEdgeDef>(outgoingAbstract.Sum(list => list.Count));
            for (int portalIndex = 0; portalIndex < portalList.Count; portalIndex++)
            {
                var portal = portalList[portalIndex];
                portal.FirstAbstractEdgeIndex = outgoingAbstract[portalIndex].Count > 0 ? flatAbstract.Count : -1;
                portal.AbstractEdgeCount = outgoingAbstract[portalIndex].Count;
                portalList[portalIndex] = portal;

                var pathGrid = pathGrids[portal.PathGridIndex];
                if (outgoingAbstract[portalIndex].Count > 0)
                {
                    if (pathGrid.FirstNavigationAbstractEdgeIndex < 0)
                        pathGrid.FirstNavigationAbstractEdgeIndex = flatAbstract.Count;
                    pathGrid.NavigationAbstractEdgeCount += outgoingAbstract[portalIndex].Count;
                }

                flatAbstract.AddRange(outgoingAbstract[portalIndex]);
                pathGrids[portal.PathGridIndex] = pathGrid;
            }

            abstractEdges = flatAbstract.ToArray();
            portals = portalList.ToArray();
            neighbors = BuildNavigationNeighbors(ref pathGrids, portals, abstractEdges);
        }


        static void AddAbstractEdge(
            List<PathGridNavigationAbstractEdgeDef>[] outgoing,
            HashSet<long> keys,
            int fromPortal,
            int toPortal,
            float cost,
            PathGridNavigationEdgeKind kind)
        {
            if ((uint)fromPortal >= (uint)outgoing.Length || (uint)toPortal >= (uint)outgoing.Length)
                return;

            long key = PackNavigationEdgeKey(fromPortal, toPortal);
            if (!keys.Add(key))
                return;

            outgoing[fromPortal].Add(new PathGridNavigationAbstractEdgeDef
            {
                FromPortalIndex = fromPortal,
                ToPortalIndex = toPortal,
                Cost = cost,
                Kind = kind,
            });
        }


        static float FindAuthoredPathCost(
            int pathGridIndex,
            int startNode,
            int goalNode,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing)
        {
            if (startNode == goalNode)
                return 0f;

            var dist = new float[nodes.Count];
            var closed = new bool[nodes.Count];
            var open = new List<int>();
            for (int i = 0; i < dist.Length; i++)
                dist[i] = float.PositiveInfinity;

            dist[startNode] = 0f;
            open.Add(startNode);
            while (open.Count > 0)
            {
                int bestOpenIndex = 0;
                float bestCost = dist[open[0]];
                for (int i = 1; i < open.Count; i++)
                {
                    float cost = dist[open[i]];
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestOpenIndex = i;
                    }
                }

                int current = open[bestOpenIndex];
                open.RemoveAt(bestOpenIndex);
                if (closed[current])
                    continue;

                if (current == goalNode)
                    return dist[current];

                closed[current] = true;
                for (int edgeIndex = 0; edgeIndex < outgoing[current].Count; edgeIndex++)
                {
                    var edge = outgoing[current][edgeIndex];
                    if (edge.Kind != PathGridNavigationEdgeKind.Authored)
                        continue;

                    int toNode = edge.ToNodeIndex;
                    if ((uint)toNode >= (uint)nodes.Count ||
                        nodes[toNode].PathGridIndex != pathGridIndex ||
                        closed[toNode])
                    {
                        continue;
                    }

                    float tentative = dist[current] + edge.Cost;
                    if (tentative >= dist[toNode])
                        continue;

                    dist[toNode] = tentative;
                    open.Add(toNode);
                }
            }

            return float.PositiveInfinity;
        }


        static PathGridNavigationNeighborDef[] BuildNavigationNeighbors(
            ref PathGridDef[] pathGrids,
            PathGridNavigationPortalDef[] portals,
            PathGridNavigationAbstractEdgeDef[] abstractEdges)
        {
            var map = new Dictionary<long, PathGridNavigationNeighborDef>();
            for (int i = 0; i < abstractEdges.Length; i++)
            {
                var edge = abstractEdges[i];
                if (edge.Kind != PathGridNavigationEdgeKind.ExteriorBorder)
                    continue;
                if ((uint)edge.FromPortalIndex >= (uint)portals.Length ||
                    (uint)edge.ToPortalIndex >= (uint)portals.Length)
                {
                    continue;
                }

                int fromPathGrid = portals[edge.FromPortalIndex].PathGridIndex;
                int toPathGrid = portals[edge.ToPortalIndex].PathGridIndex;
                long key = PackNavigationEdgeKey(fromPathGrid, toPathGrid);
                if (map.TryGetValue(key, out var neighbor))
                {
                    neighbor.BorderEdgeCount++;
                    neighbor.MinCost = Math.Min(neighbor.MinCost, edge.Cost);
                    map[key] = neighbor;
                }
                else
                {
                    map[key] = new PathGridNavigationNeighborDef
                    {
                        PathGridIndex = fromPathGrid,
                        NeighborPathGridIndex = toPathGrid,
                        BorderEdgeCount = 1,
                        MinCost = edge.Cost,
                    };
                }
            }

            var result = map.Values
                .OrderBy(value => value.PathGridIndex)
                .ThenBy(value => value.NeighborPathGridIndex)
                .ToArray();

            int cursor = 0;
            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                pathGrid.FirstNavigationNeighborIndex = -1;
                pathGrid.NavigationNeighborCount = 0;
                while (cursor < result.Length && result[cursor].PathGridIndex < i)
                    cursor++;
                int start = cursor;
                while (cursor < result.Length && result[cursor].PathGridIndex == i)
                    cursor++;
                int count = cursor - start;
                if (count > 0)
                {
                    pathGrid.FirstNavigationNeighborIndex = start;
                    pathGrid.NavigationNeighborCount = count;
                }

                pathGrids[i] = pathGrid;
            }

            return result;
        }


        static void BuildActorArrays(
            Dictionary<string, ActorAccumulator> map,
            out ActorDef[] actors,
            out ActorSpellDef[] spells,
            out ContainerItemDef[] inventoryItems,
            out ActorAiPackageDef[] aiPackages,
            out ActorTravelDestinationDef[] travelDestinations)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            actors = new ActorDef[ordered.Length];
            var flatSpells = new List<ActorSpellDef>(ordered.Sum(pair => pair.Value.Spells.Count));
            var flatItems = new List<ContainerItemDef>(ordered.Sum(pair => pair.Value.InventoryItems.Count));
            var flatAiPackages = new List<ActorAiPackageDef>(ordered.Sum(pair => pair.Value.AiPackages.Count));
            var flatTravelDestinations = new List<ActorTravelDestinationDef>(ordered.Sum(pair => pair.Value.TravelDestinations.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var accumulator = ordered[i].Value;
                var def = accumulator.Def;
                def.FirstSpellIndex = accumulator.Spells.Count > 0 ? flatSpells.Count : -1;
                def.SpellCount = accumulator.Spells.Count;
                def.FirstInventoryIndex = accumulator.InventoryItems.Count > 0 ? flatItems.Count : -1;
                def.InventoryCount = accumulator.InventoryItems.Count;
                def.FirstAiPackageIndex = accumulator.AiPackages.Count > 0 ? flatAiPackages.Count : -1;
                def.AiPackageCount = accumulator.AiPackages.Count;
                def.FirstTravelDestinationIndex = accumulator.TravelDestinations.Count > 0 ? flatTravelDestinations.Count : -1;
                def.TravelDestinationCount = accumulator.TravelDestinations.Count;
                actors[i] = def;
                flatSpells.AddRange(accumulator.Spells);
                flatItems.AddRange(accumulator.InventoryItems);
                flatAiPackages.AddRange(accumulator.AiPackages);
                flatTravelDestinations.AddRange(accumulator.TravelDestinations);
            }

            spells = flatSpells.ToArray();
            inventoryItems = flatItems.ToArray();
            aiPackages = flatAiPackages.ToArray();
            travelDestinations = flatTravelDestinations.ToArray();
        }


        static void BuildDialogueArrays(
            Dictionary<string, DialogueAccumulator> map,
            out DialogueDef[] dialogues,
            out DialogueInfoDef[] infos,
            out DialogueConditionDef[] conditions)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var dialogueList = new List<DialogueDef>(ordered.Length);
            var infoList = new List<DialogueInfoDef>(ordered.Sum(pair => pair.Value.Infos.Count));
            var conditionList = new List<DialogueConditionDef>();

            foreach (var pair in ordered)
            {
                var def = pair.Value.Def;
                def.FirstInfoIndex = infoList.Count;
                def.InfoCount = pair.Value.Infos.Count;
                dialogueList.Add(def);
                for (int i = 0; i < pair.Value.Infos.Count; i++)
                {
                    var info = pair.Value.Infos[i];
                    var selectRules = i < pair.Value.SelectRules.Count ? pair.Value.SelectRules[i] : null;
                    if (selectRules != null && selectRules.Count > 0)
                    {
                        info.FirstSelectRuleIndex = conditionList.Count;
                        info.SelectRuleCount = selectRules.Count;
                        conditionList.AddRange(selectRules);
                    }
                    else
                    {
                        info.FirstSelectRuleIndex = -1;
                        info.SelectRuleCount = 0;
                    }

                    infoList.Add(info);
                }
            }

            dialogues = dialogueList.ToArray();
            infos = infoList.ToArray();
            conditions = conditionList.ToArray();
        }


        static void BuildContainerContentArrays(
            BaseDef[] containers,
            Dictionary<string, List<ContainerItemDef>> itemMap,
            out ContainerContentRangeDef[] ranges,
            out ContainerItemDef[] items)
        {
            ranges = new ContainerContentRangeDef[containers?.Length ?? 0];
            var flatItems = new List<ContainerItemDef>();

            for (int i = 0; i < ranges.Length; i++)
            {
                string id = containers[i].Id ?? string.Empty;
                if (!itemMap.TryGetValue(id, out var containerItems) || containerItems == null || containerItems.Count == 0)
                {
                    ranges[i] = new ContainerContentRangeDef
                    {
                        FirstItemIndex = -1,
                        ItemCount = 0,
                    };
                    continue;
                }

                ranges[i] = new ContainerContentRangeDef
                {
                    FirstItemIndex = flatItems.Count,
                    ItemCount = containerItems.Count,
                };
                flatItems.AddRange(containerItems);
            }

            items = flatItems.ToArray();
        }


        static void BuildItemEquipmentArrays(
            BaseDef[] items,
            Dictionary<string, ItemEquipmentAccumulator> equipmentMap,
            out ItemEquipmentDef[] equipmentDefs,
            out ItemEquipmentBodyPartDef[] bodyPartDefs)
        {
            var defs = new List<ItemEquipmentDef>();
            var flatBodyParts = new List<ItemEquipmentBodyPartDef>();
            if (items == null || equipmentMap == null || equipmentMap.Count == 0)
            {
                equipmentDefs = Array.Empty<ItemEquipmentDef>();
                bodyPartDefs = Array.Empty<ItemEquipmentBodyPartDef>();
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                string id = items[i].Id ?? string.Empty;
                if (!equipmentMap.TryGetValue(id, out var equipment) || equipment == null)
                    continue;

                var def = equipment.Def;
                def.Item = ItemDefHandle.FromIndex(i);
                def.FirstBodyPartIndex = equipment.BodyParts.Count > 0 ? flatBodyParts.Count : -1;
                def.BodyPartCount = equipment.BodyParts.Count;
                defs.Add(def);

                for (int partIndex = 0; partIndex < equipment.BodyParts.Count; partIndex++)
                {
                    var bodyPart = equipment.BodyParts[partIndex];
                    bodyPart.Item = def.Item;
                    flatBodyParts.Add(bodyPart);
                }
            }

            equipmentDefs = defs.ToArray();
            bodyPartDefs = flatBodyParts.ToArray();
        }


        static void BuildItemLeveledListArrays(
            Dictionary<string, ItemLeveledListAccumulator> map,
            out ItemLeveledListDef[] defs,
            out ItemLeveledListEntryDef[] entries)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            defs = new ItemLeveledListDef[ordered.Length];
            var flatEntries = new List<ItemLeveledListEntryDef>(ordered.Sum(pair => pair.Value.Entries.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value.Def;
                def.FirstEntryIndex = flatEntries.Count;
                def.EntryCount = ordered[i].Value.Entries.Count;
                defs[i] = def;
                flatEntries.AddRange(ordered[i].Value.Entries);
            }

            entries = flatEntries.ToArray();
        }


        static void BuildSpellArrays(
            Dictionary<string, SpellDef> map,
            Dictionary<string, List<MagicEffectInstanceDef>> effectMap,
            out SpellDef[] defs,
            ref MagicEffectInstanceDef[] effectInstances)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var output = new SpellDef[ordered.Length];
            var effects = effectInstances != null ? new List<MagicEffectInstanceDef>(effectInstances) : new List<MagicEffectInstanceDef>();

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value;
                if (effectMap.TryGetValue(ContentId.NormalizeId(def.Id), out var spellEffects) && spellEffects.Count > 0)
                {
                    def.EffectStartIndex = effects.Count;
                    def.EffectCount = spellEffects.Count;
                    effects.AddRange(spellEffects);
                }
                else
                {
                    def.EffectStartIndex = -1;
                    def.EffectCount = 0;
                }

                output[i] = def;
            }

            defs = output;
            effectInstances = effects.ToArray();
        }


        }
    }
