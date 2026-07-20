using System;
using System.Collections.Generic;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;
using KeyboardWanderer.Networking;
using UnityEngine;

namespace KeyboardWanderer.World
{
    /// <summary>
    /// 로컬 Region과 서버 월드 DTO에서 이동 가능 여부와 화면용 경로를 계산한다.
    /// 컨트롤러는 입력 좌표와 결과 경로만 전달하고 BFS·점유 판정을 직접 구현하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererPathPlanner : MonoBehaviour
    {
        public readonly struct RoutePreview
        {
            public int Distance { get; }
            public int HazardTiles { get; }
            public int HostileInfluenceTiles { get; }
            public int UnknownAreaTiles { get; }
            public bool HasPath { get; }
            public bool IsSafe => HasPath && HazardTiles == 0 && HostileInfluenceTiles == 0 && UnknownAreaTiles == 0;

            public RoutePreview(int distance, int hazardTiles, int hostileInfluenceTiles,
                int unknownAreaTiles, bool hasPath)
            {
                Distance = distance;
                HazardTiles = hazardTiles;
                HostileInfluenceTiles = hostileInfluenceTiles;
                UnknownAreaTiles = unknownAreaTiles;
                HasPath = hasPath;
            }

            public string PlayerText()
            {
                if (!HasPath) return "경로 정보 · 도달 가능한 경로가 없습니다.";
                var risks = new List<string>();
                if (HazardTiles > 0) risks.Add("위험 지형 " + HazardTiles + "칸");
                if (HostileInfluenceTiles > 0) risks.Add("적 영향권 " + HostileInfluenceTiles + "칸");
                if (UnknownAreaTiles > 0) risks.Add("미탐험 구역 " + UnknownAreaTiles + "칸");
                return "경로 정보 · " + Distance + "칸 · " +
                       (risks.Count == 0 ? "안전 경로 · 사건 예상 없음" :
                           string.Join(" · ", risks) + " · 이동 사건 가능");
            }
        }

        public bool CanSelectDestination(RunView view, GameApiClient.RunSnapshot serverRun,
            GridCoord start, GridCoord goal)
        {
            if (view == null || start == goal)
                return false;
            if (serverRun?.world != null)
                return !IsServerOccupied(serverRun, goal) && FindServerPath(serverRun, start, goal).Count > 1;
            if (!view.Region.IsWalkable(goal) || IsLocalOccupied(view, goal))
                return false;
            return GridPathfinder.FindPath(view.Region, start, goal,
                coord => IsLocalOccupied(view, coord)).Count > 1;
        }

        /// <summary>로컬 이동 결과를 엔티티 점유를 피해 재생할 연속 경로로 만든다.</summary>
        public List<GridCoord> FindLocalVisualPath(RunView view, GridCoord start, GridCoord goal)
        {
            if (view == null)
                return new List<GridCoord>();
            if (start == goal)
                return new List<GridCoord> { start };
            return GridPathfinder.FindPath(view.Region, start, goal,
                coord => coord != goal && IsLocalOccupied(view, coord));
        }

        /// <summary>현재 선택한 이동 경로에서 플레이어가 만나게 될 위험을 실행 전에 요약한다.</summary>
        public RoutePreview Preview(RunView view, GameApiClient.RunSnapshot serverRun,
            GridCoord start, GridCoord goal)
        {
            if (serverRun?.world != null)
                return PreviewServer(serverRun, FindServerPath(serverRun, start, goal));
            if (view == null)
                return new RoutePreview(0, 0, 0, 0, false);
            List<GridCoord> path = FindLocalVisualPath(view, start, goal);
            if (path.Count == 0)
                return new RoutePreview(0, 0, 0, 0, false);

            int hazards = 0;
            int hostile = 0;
            int unknown = 0;
            for (int i = 1; i < path.Count; i++)
            {
                GridCoord coord = path[i];
                TileKind kind = view.Region.GetTile(coord).Kind;
                if (kind == TileKind.Hazard || kind == TileKind.Ruin) hazards++;
                if (IsLocallyHostileInfluence(view, coord)) hostile++;
                WorldArea area = view.Region.AreaAt(coord);
                if (area != null && !Contains(view.VisitedAreaIds, area.Id)) unknown++;
            }
            return new RoutePreview(path.Count - 1, hazards, hostile, unknown, true);
        }

        /// <summary>
        /// 서버가 준 navigation.path를 우선 사용하고, 끊긴 경로라면 동일 월드에서 BFS로 복구한다.
        /// </summary>
        public List<GridCoord> FindServerVisualPath(GameApiClient.NavigationSnapshot navigation,
            GameApiClient.RunSnapshot serverRun, GridCoord start, GridCoord goal)
        {
            var path = new List<GridCoord>();
            if (navigation?.path != null)
            {
                for (int i = 0; i < navigation.path.Length; i++)
                {
                    GameApiClient.PositionSnapshot step = navigation.path[i];
                    if (step == null)
                        continue;
                    var coord = new GridCoord(step.x, step.y);
                    if (path.Count == 0 || path[path.Count - 1] != coord)
                        path.Add(coord);
                }
            }
            if (path.Count == 0 || path[0] != start)
                path.Insert(0, start);
            if (path[path.Count - 1] != goal)
                path.Add(goal);
            return IsContiguous(path) ? path : FindServerPath(serverRun, start, goal);
        }

        private static List<GridCoord> FindServerPath(GameApiClient.RunSnapshot run,
            GridCoord start, GridCoord goal)
        {
            var empty = new List<GridCoord>();
            if (!IsServerWalkable(run, start) || !IsServerWalkable(run, goal))
                return empty;
            var queue = new Queue<GridCoord>();
            var visited = new HashSet<GridCoord> { start };
            var cameFrom = new Dictionary<GridCoord, GridCoord>();
            queue.Enqueue(start);
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(-1, 0),
                new GridCoord(0, 1), new GridCoord(0, -1)
            };
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                if (current == goal)
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
                for (int i = 0; i < directions.Length; i++)
                {
                    var next = new GridCoord(current.X + directions[i].X, current.Y + directions[i].Y);
                    if (visited.Contains(next) || !IsServerWalkable(run, next) ||
                        (next != goal && IsServerOccupied(run, next)))
                        continue;
                    visited.Add(next);
                    cameFrom[next] = current;
                    queue.Enqueue(next);
                }
            }
            return empty;
        }

        private static bool IsLocalOccupied(RunView view, GridCoord coord)
        {
            for (int i = 0; i < view.Entities.Count; i++)
            {
                EntityView entity = view.Entities[i];
                if (entity.EntityId != view.PlayerEntityId && entity.Position == coord)
                    return true;
            }
            return false;
        }

        private static RoutePreview PreviewServer(GameApiClient.RunSnapshot run, IReadOnlyList<GridCoord> path)
        {
            if (path == null || path.Count == 0)
                return new RoutePreview(0, 0, 0, 0, false);
            int hazards = 0;
            int hostile = 0;
            int unknown = 0;
            for (int i = 1; i < path.Count; i++)
            {
                GridCoord coord = path[i];
                string tile = ServerTileName(run.world, coord);
                if (tile.Contains("hazard") || tile.Contains("ruin")) hazards++;
                if (IsServerHostileInfluence(run, coord)) hostile++;
                string areaId = run.world.AreaIdAt(coord.X, coord.Y);
                if (!string.IsNullOrWhiteSpace(areaId) && !Contains(run.discoveredAreaIds, areaId)) unknown++;
            }
            return new RoutePreview(path.Count - 1, hazards, hostile, unknown, true);
        }

        private static bool IsLocallyHostileInfluence(RunView view, GridCoord coord)
        {
            for (int i = 0; i < view.Entities.Count; i++)
                if (view.Entities[i].IsHostile && view.Entities[i].Health > 0 &&
                    view.Entities[i].Position.ManhattanDistance(coord) <= 2)
                    return true;
            return false;
        }

        private static bool IsServerHostileInfluence(GameApiClient.RunSnapshot run, GridCoord coord)
        {
            if (run?.entities == null) return false;
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = run.entities[i];
                if (entity?.position == null || !string.Equals(entity.kind, "enemy", StringComparison.OrdinalIgnoreCase) ||
                    entity.state?.defeated == true || entity.state?.disabled == true || entity.state?.fled == true)
                    continue;
                if (new GridCoord(entity.position.x, entity.position.y).ManhattanDistance(coord) <= 2)
                    return true;
            }
            return false;
        }

        private static string ServerTileName(GameApiClient.WorldSnapshot world, GridCoord coord)
        {
            if (world == null || !world.HasCompleteLayout) return string.Empty;
            int code = world.tileCodes[coord.Y * world.width + coord.X];
            return world.tileLegend != null && code >= 0 && code < world.tileLegend.Length
                ? (world.tileLegend[code] ?? string.Empty).ToLowerInvariant()
                : string.Empty;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsServerWalkable(GameApiClient.RunSnapshot run, GridCoord coord)
        {
            GameApiClient.WorldSnapshot world = run?.world;
            if (world == null || !world.HasCompleteLayout || coord.X < 0 || coord.Y < 0 ||
                coord.X >= world.width || coord.Y >= world.height)
                return false;
            string name = ServerTileName(world, coord);
            return !name.Contains("wall") && !name.Contains("water");
        }

        private static bool IsServerOccupied(GameApiClient.RunSnapshot run, GridCoord coord)
        {
            if (run?.entities == null)
                return false;
            for (int i = 0; i < run.entities.Length; i++)
            {
                GameApiClient.EntitySnapshot entity = run.entities[i];
                if (entity?.position != null &&
                    !string.Equals(entity.id, run.playerEntityId, StringComparison.OrdinalIgnoreCase) &&
                    entity.position.x == coord.X && entity.position.y == coord.Y)
                    return true;
            }
            return false;
        }

        private static bool IsContiguous(IReadOnlyList<GridCoord> path)
        {
            if (path == null || path.Count == 0)
                return false;
            for (int i = 1; i < path.Count; i++)
                if (path[i - 1].ManhattanDistance(path[i]) != 1)
                    return false;
            return true;
        }
    }
}
