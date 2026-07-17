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
            Func<GridCoord, bool> isBlocked = null)
        {
            var empty = new List<GridCoord>();
            if (!map.IsWalkable(start) || !map.IsWalkable(goal) || (isBlocked != null && isBlocked(goal)))
                return empty;

            var open = new List<GridCoord> { start };
            var closed = new HashSet<GridCoord>();
            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            var gScore = new Dictionary<GridCoord, int> { [start] = 0 };

            while (open.Count > 0)
            {
                int bestIndex = 0;
                int bestScore = Score(open[0], goal, gScore);
                for (int i = 1; i < open.Count; i++)
                {
                    int score = Score(open[i], goal, gScore);
                    if (score < bestScore)
                    {
                        bestIndex = i;
                        bestScore = score;
                    }
                }

                GridCoord current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (current == goal)
                    return Reconstruct(cameFrom, current);

                closed.Add(current);
                for (int i = 0; i < Directions.Length; i++)
                {
                    var neighbor = new GridCoord(current.X + Directions[i].X, current.Y + Directions[i].Y);
                    if (closed.Contains(neighbor) || !map.IsWalkable(neighbor) ||
                        (isBlocked != null && neighbor != goal && isBlocked(neighbor)))
                        continue;

                    int tentative = gScore[current] + map.MovementCost(neighbor);
                    if (!gScore.TryGetValue(neighbor, out int known) || tentative < known)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative;
                        if (!open.Contains(neighbor))
                            open.Add(neighbor);
                    }
                }
            }

            return empty;
        }

        private static int Score(GridCoord coord, GridCoord goal, Dictionary<GridCoord, int> gScore)
        {
            return gScore[coord] + coord.ManhattanDistance(goal);
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
