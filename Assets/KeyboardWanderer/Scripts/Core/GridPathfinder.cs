using System;
using System.Collections.Generic;

namespace KeyboardWanderer.Core
{
    public static class GridPathfinder
    {
        private static readonly GridCoord[] Directions =
        {
            new GridCoord(1, 0),
            new GridCoord(-1, 0),
            new GridCoord(0, 1),
            new GridCoord(0, -1)
        };

        public static List<GridCoord> FindPath(
            RegionMap map,
            GridCoord start,
            GridCoord goal,
            Func<GridCoord, bool> isBlocked = null,
            int maxExpandedNodes = 0)
        {
            var empty = new List<GridCoord>();
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (!map.IsWalkable(start) || !map.IsWalkable(goal) || (isBlocked != null && isBlocked(goal)))
                return empty;
            if (start == goal)
                return new List<GridCoord> { start };

            int nodeBudget = maxExpandedNodes > 0
                ? Math.Min(maxExpandedNodes, map.Width * map.Height)
                : map.Width * map.Height;
            var open = new MinHeap();
            var closed = new HashSet<GridCoord>();
            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            var gScore = new Dictionary<GridCoord, int> { [start] = 0 };
            int sequence = 0;
            open.Push(new OpenNode(start, start.ManhattanDistance(goal), 0, sequence++));
            int expanded = 0;

            while (open.Count > 0)
            {
                OpenNode entry = open.Pop();
                GridCoord current = entry.Coord;
                if (closed.Contains(current) || !gScore.TryGetValue(current, out int currentCost) ||
                    entry.GScore != currentCost)
                    continue;
                if (current == goal)
                    return Reconstruct(cameFrom, current);
                if (expanded++ >= nodeBudget)
                    return empty;

                closed.Add(current);
                for (int i = 0; i < Directions.Length; i++)
                {
                    var neighbor = new GridCoord(current.X + Directions[i].X, current.Y + Directions[i].Y);
                    if (closed.Contains(neighbor) || !map.IsWalkable(neighbor) ||
                        (isBlocked != null && neighbor != goal && isBlocked(neighbor)))
                        continue;

                    int tentative = currentCost + map.MovementCost(neighbor);
                    if (!gScore.TryGetValue(neighbor, out int known) || tentative < known)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative;
                        open.Push(new OpenNode(neighbor, tentative + neighbor.ManhattanDistance(goal),
                            tentative, sequence++));
                    }
                }
            }

            return empty;
        }

        /// <summary>
        /// Finds the shortest four-directional path to any goal with one breadth-first traversal.
        /// This is intended for actors that only move one tile and therefore do not need weighted terrain costs.
        /// </summary>
        public static List<GridCoord> FindShortestPathToAny(
            RegionMap map,
            GridCoord start,
            IEnumerable<GridCoord> goals,
            Func<GridCoord, bool> isBlocked = null,
            int maxExpandedNodes = 0)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            var empty = new List<GridCoord>();
            if (!map.IsWalkable(start) || goals == null)
                return empty;

            var goalSet = new HashSet<GridCoord>();
            foreach (GridCoord goal in goals)
                if (map.IsWalkable(goal) && (isBlocked == null || !isBlocked(goal)))
                    goalSet.Add(goal);
            if (goalSet.Count == 0)
                return empty;
            if (goalSet.Contains(start))
                return new List<GridCoord> { start };

            int nodeBudget = maxExpandedNodes > 0
                ? Math.Min(maxExpandedNodes, map.Width * map.Height)
                : map.Width * map.Height;
            var queue = new Queue<GridCoord>();
            var visited = new HashSet<GridCoord> { start };
            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            queue.Enqueue(start);
            int expanded = 0;

            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                if (expanded++ >= nodeBudget)
                    return empty;
                for (int i = 0; i < Directions.Length; i++)
                {
                    var neighbor = new GridCoord(current.X + Directions[i].X, current.Y + Directions[i].Y);
                    if (!visited.Add(neighbor) || !map.IsWalkable(neighbor) ||
                        (isBlocked != null && !goalSet.Contains(neighbor) && isBlocked(neighbor)))
                        continue;
                    cameFrom[neighbor] = current;
                    if (goalSet.Contains(neighbor))
                        return Reconstruct(cameFrom, neighbor);
                    queue.Enqueue(neighbor);
                }
            }
            return empty;
        }

        private readonly struct OpenNode
        {
            public GridCoord Coord { get; }
            public int Score { get; }
            public int GScore { get; }
            public int Sequence { get; }

            public OpenNode(GridCoord coord, int score, int gScore, int sequence)
            {
                Coord = coord;
                Score = score;
                GScore = gScore;
                Sequence = sequence;
            }
        }

        private sealed class MinHeap
        {
            private readonly List<OpenNode> _items = new List<OpenNode>();
            public int Count => _items.Count;

            public void Push(OpenNode value)
            {
                _items.Add(value);
                int index = _items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(value, _items[parent])) break;
                    _items[index] = _items[parent];
                    index = parent;
                }
                _items[index] = value;
            }

            public OpenNode Pop()
            {
                OpenNode root = _items[0];
                int lastIndex = _items.Count - 1;
                OpenNode last = _items[lastIndex];
                _items.RemoveAt(lastIndex);
                if (_items.Count == 0) return root;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= _items.Count) break;
                    int right = left + 1;
                    int child = right < _items.Count && Less(_items[right], _items[left]) ? right : left;
                    if (!Less(_items[child], last)) break;
                    _items[index] = _items[child];
                    index = child;
                }
                _items[index] = last;
                return root;
            }

            private static bool Less(OpenNode left, OpenNode right)
            {
                if (left.Score != right.Score) return left.Score < right.Score;
                if (left.GScore != right.GScore) return left.GScore < right.GScore;
                return left.Sequence < right.Sequence;
            }
        }

        private static List<GridCoord> Reconstruct(Dictionary<GridCoord, GridCoord> cameFrom, GridCoord current)
        {
            var path = new List<GridCoord> { current };
            while (cameFrom.TryGetValue(current, out GridCoord previous))
            {
                current = previous;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }
    }
}
