using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Core
{
    /// <summary>
    /// Deterministic offline fallback world. The server remains the canonical online world
    /// authority; this local version intentionally has its own version/hash namespace while sharing
    /// the same logical-first constraints.
    /// </summary>
    public static class DeterministicRegionGenerator
    {
        public const string CurrentVersion = "codria-local-world.v10";
        public const string ProgressionVersion = "codria-access-progression.v4";
        public const int DefaultWidth = 160;
        public const int DefaultHeight = 160;
        public const int AreaColumns = 4;
        public const int AreaRows = 3;
        public const int RequiredAreaCount = AreaColumns * AreaRows;
        public const int RequiredBiomeCount = 6;

        private static readonly string[] CampaignRoles = CampaignCatalog.RoleIds;

        private static readonly string[] CampaignBeatIds =
        {
            "arrival", "admin-access-1", "admin-access-2", "truth", "admin-access-3", "root-entry"
        };

        private static readonly string[] ProgressionNodeIds =
        {
            "keyboard-awakening", "admin-access-1", "admin-access-2", "internal-failure",
            "admin-access-3", "root-system"
        };

        private static readonly string[] AccessTokens = CampaignCatalog.AdminAccessLevelIds;

        private static readonly GridCoord[] CardinalDirections =
        {
            new GridCoord(1, 0), new GridCoord(-1, 0),
            new GridCoord(0, 1), new GridCoord(0, -1)
        };

        private sealed class RoutePlan
        {
            public int Left;
            public int Right;
            public bool IsLoop;
            public string Kind;
        }

        private sealed class EdgeCandidate
        {
            public int Left;
            public int Right;
            public int DistanceSquared;
            public int TieBreaker;
        }

        private sealed class ValidationSummary
        {
            public int ConnectedTileCount;
            public int LoopRouteCount;
            public readonly List<string> Checks = new List<string>();
        }

        public static RegionMap Generate(long worldSeed, string regionKey, int width = DefaultWidth,
            int height = DefaultHeight)
        {
            if (width < 64 || height < 48)
                throw new ArgumentOutOfRangeException(nameof(width),
                    "Seeded worlds require at least 64x48 tiles for constrained areas and slots.");

            regionKey = string.IsNullOrWhiteSpace(regionKey) ? "seeded-world" : regionKey.Trim();
            long regionSeed = DeriveSeed(worldSeed, regionKey, CurrentVersion);
            Guid regionId = DeterministicGuid.Create(
                "local-world:" + worldSeed + ":" + regionKey + ":" + CurrentVersion);
            var random = new StableRandom(unchecked((uint)regionSeed));

            // 1. Fix the campaign spine and its executable recovery modes before coordinates.
            string[][] progressionModes = CreateProgressionModes();

            // 2. Bind the six fixed Codria region axes to distinct areas, independently of biome.
            int[] roleArea = SelectRoleAreas(random);
            var roleByArea = new Dictionary<int, string>();
            for (int i = 0; i < CampaignRoles.Length; i++) roleByArea[roleArea[i]] = CampaignRoles[i];

            // 3. Assign every required terrain family exactly twice, then construct an MST and loops.
            List<BiomeDescriptor> biomes = CreateBiomes();
            int[] biomeOrder = { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 };
            Shuffle(biomeOrder, random);
            List<RoutePlan> routePlans = CreateRoutePlans(roleArea[5], width, height, random);
            List<WorldArea> areas = CreateAreas(width, height, biomes, biomeOrder, roleByArea,
                routePlans, random, roleArea[5]);
            List<WorldRoute> routes = CreateRoutes(worldSeed, width, height, areas, routePlans,
                roleArea[5], random);
            ProgressionGraph progression = CreateProgressionGraph(areas, roleArea, progressionModes);

            // 4. Rasterize broad clustered terrain, then carve the validated logical routes over it.
            BaseTile[] tiles = CreateClusteredTerrain(width, height, biomes, biomeOrder,
                unchecked((uint)regionSeed));
            // 원반(disc) 월드: 원 밖을 void 처리해 원형 실루엣을 만든다. 아래 CarveRoute/CarveClearing이
            // 마스크 이후 실행되어 길·구역 클리어링이 걷는 길을 다시 뚫으므로 연결성·도달성 검증은 유지된다.
            ApplyDiscMask(tiles, width, height, areas);
            for (int i = 0; i < routes.Count; i++) CarveRoute(tiles, width, height, routes[i]);
            for (int i = 0; i < areas.Count; i++)
            {
                if (i == roleArea[5]) CarveCircle(tiles, width, height, areas[i].Center, 14); // 중심 최종 스테이지: 원형
                else CarveCircle(tiles, width, height, areas[i].Center, 3);
            }

            WorldArea startArea = areas[roleArea[0]];
            WorldArea rootArea = areas[roleArea[5]];
            GridCoord start = startArea.Center;
            GridCoord exit = rootArea.Center;
            HashSet<GridCoord> reachable = FloodFill(tiles, width, height, start);
            HashSet<GridCoord> reachableBeforeRoot = FloodFill(tiles, width, height, start, rootArea);

            // 5. Select only unique, walkable, entry-connected coordinates inside each declared area.
            List<PlacementSlot> slots = CreateSlots(areas, roleArea, tiles, width, height,
                start, exit, reachable, reachableBeforeRoot, random);

            // 6. Validate geometry and progression gates as separate layers and seal a report/hash.
            ValidationSummary validation = ValidateGeneratedWorld(tiles, width, height, start, exit,
                areas, biomes, routes, progression, slots, roleArea);
            var report = new GenerationReport(
                "logical_first_constraint_pipeline",
                CurrentVersion,
                true,
                1,
                validation.ConnectedTileCount,
                routes.Count,
                validation.LoopRouteCount,
                0,
                0,
                validation.Checks,
                Array.Empty<GenerationRepair>());

            string layoutHash = ComputeLayoutHashCore(regionId, CurrentVersion, regionSeed, regionKey,
                width, height, start, exit, startArea.Id, rootArea.Id, tiles, slots, areas, biomes,
                routes, progression, report);
            return new RegionMap(regionId, regionKey, regionSeed, CurrentVersion, width, height, start,
                exit, tiles, slots, areas, biomes, startArea.Id, rootArea.Id, layoutHash, routes,
                progression, report);
        }

        public static long DeriveSeed(long worldSeed, string regionKey, string generatorVersion)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(worldSeed + "|" + regionKey + "|" + generatorVersion);
                return BitConverter.ToInt64(sha.ComputeHash(bytes), 0);
            }
        }

        private static List<BiomeDescriptor> CreateBiomes()
        {
            return new List<BiomeDescriptor>
            {
                new BiomeDescriptor("temperate_forest_field", "온대 수림·들판", TileKind.Grass,
                    "forest", "field", "growth"),
                new BiomeDescriptor("river_wetland", "강·습지", TileKind.DarkGrass,
                    "wetland", "river", "water"),
                new BiomeDescriptor("arid_desert", "건조 사막", TileKind.Dirt,
                    "desert", "exposure", "ruin"),
                new BiomeDescriptor("frost_highland", "서리 고원", TileKind.Floor,
                    "frost", "highland", "isolation"),
                new BiomeDescriptor("subterranean_cavern", "지하 동굴", TileKind.DarkGrass,
                    "cavern", "dungeon", "enclosed"),
                new BiomeDescriptor("ancient_ruins", "고대 폐허", TileKind.Ruin,
                    "ruin", "temple", "legacy")
            };
        }

        private static string[][] CreateProgressionModes()
        {
            var modes = new string[CampaignBeatIds.Length][];
            for (int beatIndex = 0; beatIndex < CampaignBeatIds.Length; beatIndex++)
                modes[beatIndex] = AllowedModeNamesForBeat(CampaignBeatIds[beatIndex]);
            return modes;
        }

        private static string[] AllowedModeNamesForBeat(string beatId)
        {
            IReadOnlyList<AbilityKind> allowed = CampaignDirector.AllowedAbilitiesForBeat(beatId);
            var modes = new string[allowed.Count];
            for (int abilityIndex = 0; abilityIndex < allowed.Count; abilityIndex++)
                modes[abilityIndex] = allowed[abilityIndex].ToString().ToLowerInvariant();
            return modes;
        }

        private static int[] SelectRoleAreas(StableRandom random)
        {
            int start = random.Next(RequiredAreaCount);
            var used = new HashSet<int> { start };
            int root = PickFarthest(start, used, random);
            used.Add(root);
            int admin1 = PickSpaced(new[] { start, root }, used, random);
            used.Add(admin1);
            int admin2 = PickSpaced(new[] { start, admin1, root }, used, random);
            used.Add(admin2);
            int truth = PickSpaced(new[] { start, admin1, admin2, root }, used, random);
            used.Add(truth);
            int admin3 = PickSpaced(new[] { admin1, admin2, truth, root }, used, random);
            return new[] { start, admin1, admin2, truth, admin3, root };
        }

        private static int PickFarthest(int origin, HashSet<int> used, StableRandom random)
        {
            int best = -1;
            int bestScore = int.MinValue;
            for (int candidate = 0; candidate < RequiredAreaCount; candidate++)
            {
                if (used.Contains(candidate)) continue;
                int score = GridDistance(origin, candidate) * 100 + random.Next(97);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            return best;
        }

        private static int PickSpaced(int[] anchors, HashSet<int> used, StableRandom random)
        {
            int best = -1;
            int bestScore = int.MinValue;
            for (int candidate = 0; candidate < RequiredAreaCount; candidate++)
            {
                if (used.Contains(candidate)) continue;
                int minimum = int.MaxValue;
                for (int i = 0; i < anchors.Length; i++)
                    minimum = Math.Min(minimum, GridDistance(candidate, anchors[i]));
                int score = minimum * 100 + random.Next(97);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            return best;
        }

        private static int GridDistance(int left, int right)
        {
            return Math.Abs(left % AreaColumns - right % AreaColumns) +
                   Math.Abs(left / AreaColumns - right / AreaColumns);
        }

        // 방사형(wheel) 배치: 루트 시스템(최종 스테이지)을 원 중심에, 나머지 11개 구역을 고리에 둔다.
        // 이렇게 하면 route 거리(엣지 후보)가 방사형이 되어 바큇살·고리 도로가 자연스럽게 생긴다.
        private const double RingRadiusFactor = 0.34; // 맵 최소변 대비 고리 반지름 비율(≈54/160)

        private static int RingSlotFor(int index, int rootIndex)
        {
            int slot = 0;
            for (int i = 0; i < index; i++) if (i != rootIndex) slot++;
            return slot;
        }

        private static GridCoord AreaCenter(int index, int rootIndex, int width, int height)
        {
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            if (index == rootIndex)
                return new GridCoord((int)Math.Round(cx), (int)Math.Round(cy));
            int ringCount = RequiredAreaCount - 1;
            double ringRadius = Math.Min(width, height) * RingRadiusFactor;
            double angle = RingSlotFor(index, rootIndex) * (2.0 * Math.PI / ringCount);
            int x = (int)Math.Round(cx + ringRadius * Math.Cos(angle));
            int y = (int)Math.Round(cy + ringRadius * Math.Sin(angle));
            return new GridCoord(x, y);
        }

        private static void AreaBounds(int index, int rootIndex, int width, int height,
            out GridCoord min, out GridCoord max)
        {
            GridCoord center = AreaCenter(index, rootIndex, width, height);
            int half = index == rootIndex ? 15 : 12;
            min = new GridCoord(Math.Max(1, center.X - half), Math.Max(1, center.Y - half));
            max = new GridCoord(Math.Min(width - 2, center.X + half), Math.Min(height - 2, center.Y + half));
        }

        private static List<EdgeCandidate> CreateEdgeCandidates(int rootIndex, int width, int height, StableRandom random)
        {
            var values = new List<EdgeCandidate>();
            for (int left = 0; left < RequiredAreaCount; left++)
            {
                GridCoord leftCenter = AreaCenter(left, rootIndex, width, height);
                for (int right = left + 1; right < RequiredAreaCount; right++)
                {
                    GridCoord rightCenter = AreaCenter(right, rootIndex, width, height);
                    int dx = leftCenter.X - rightCenter.X;
                    int dy = leftCenter.Y - rightCenter.Y;
                    values.Add(new EdgeCandidate
                    {
                        Left = left,
                        Right = right,
                        DistanceSquared = dx * dx + dy * dy,
                        TieBreaker = random.Next(1_000_000)
                    });
                }
            }
            values.Sort((left, right) =>
            {
                int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
                if (distance != 0) return distance;
                int tie = left.TieBreaker.CompareTo(right.TieBreaker);
                if (tie != 0) return tie;
                int first = left.Left.CompareTo(right.Left);
                return first != 0 ? first : left.Right.CompareTo(right.Right);
            });
            return values;
        }

        private static List<RoutePlan> CreateRoutePlans(int rootAreaIndex, int width, int height,
            StableRandom random)
        {
            List<EdgeCandidate> candidates = CreateEdgeCandidates(rootAreaIndex, width, height, random);
            var selected = new List<RoutePlan>();
            var selectedKeys = new HashSet<string>();
            var disjoint = new DisjointSet(RequiredAreaCount);

            // Connect every pre-root area first so ROOT_SYSTEM can never become an early bridge.
            for (int i = 0; i < candidates.Count && selected.Count < RequiredAreaCount - 2; i++)
            {
                EdgeCandidate edge = candidates[i];
                if (edge.Left == rootAreaIndex || edge.Right == rootAreaIndex) continue;
                if (!disjoint.Union(edge.Left, edge.Right)) continue;
                AddRoutePlan(selected, selectedKeys, edge, false, "major");
            }

            var rootCandidates = new List<EdgeCandidate>();
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Left == rootAreaIndex || candidates[i].Right == rootAreaIndex)
                    rootCandidates.Add(candidates[i]);
            if (rootCandidates.Count < 2)
                throw new InvalidOperationException("ROOT_SYSTEM requires two deterministic route candidates.");
            AddRoutePlan(selected, selectedKeys, rootCandidates[0], false, "major");
            AddRoutePlan(selected, selectedKeys, rootCandidates[1], true, "minor");

            // 고리(circumferential) 오솔길을 넉넉히 추가해 바이옴 조각들을 서로 잇는다.
            int loopsAdded = 0;
            for (int i = 0; i < candidates.Count && loopsAdded < 9; i++)
            {
                EdgeCandidate edge = candidates[i];
                if (edge.Left == rootAreaIndex || edge.Right == rootAreaIndex) continue;
                if (!AddRoutePlan(selected, selectedKeys, edge, true,
                        loopsAdded < 4 ? "minor" : "secret")) continue;
                loopsAdded++;
            }
            if (loopsAdded < 2)
                throw new InvalidOperationException("Area graph could not create two loop routes.");
            return selected;
        }

        private static bool AddRoutePlan(List<RoutePlan> plans, HashSet<string> keys,
            EdgeCandidate edge, bool isLoop, string kind)
        {
            string key = edge.Left + ":" + edge.Right;
            if (!keys.Add(key)) return false;
            plans.Add(new RoutePlan
            {
                Left = edge.Left,
                Right = edge.Right,
                IsLoop = isLoop,
                Kind = kind
            });
            return true;
        }

        private static List<WorldArea> CreateAreas(int width, int height,
            List<BiomeDescriptor> biomes, int[] biomeOrder, Dictionary<int, string> roleByArea,
            List<RoutePlan> routePlans, StableRandom random, int rootIndex)
        {
            var neighbors = new List<int>[RequiredAreaCount];
            for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new List<int>();
            for (int i = 0; i < routePlans.Count; i++)
            {
                neighbors[routePlans[i].Left].Add(routePlans[i].Right);
                neighbors[routePlans[i].Right].Add(routePlans[i].Left);
            }
            for (int i = 0; i < neighbors.Length; i++) neighbors[i].Sort();

            var areas = new List<WorldArea>(RequiredAreaCount);
            for (int index = 0; index < RequiredAreaCount; index++)
            {
                AreaBounds(index, rootIndex, width, height, out GridCoord areaMin, out GridCoord areaMax);
                string role = roleByArea.TryGetValue(index, out string selectedRole)
                    ? selectedRole
                    : string.Empty;
                BiomeDescriptor biome = biomes[biomeOrder[index]];
                string areaId = AreaId(index);
                var neighborIds = new List<string>();
                for (int i = 0; i < neighbors[index].Count; i++)
                    neighborIds.Add(AreaId(neighbors[index][i]));
                string landmark = LandmarkFor(role, biome.Id, index);
                areas.Add(new WorldArea(areaId, DisplayNameFor(role, biome.DisplayName, index), biome.Id,
                    DescriptionFor(role, biome.DisplayName), areaMin,
                    areaMax, landmark, role, neighborIds, 1 + random.Next(3),
                    TagsFor(role, landmark), role == CampaignCatalog.FinalConvergenceRole ? 3 : 0));
            }
            return areas;
        }

        private static string AreaId(int index) { return "area-" + (index + 1).ToString("00"); }

        private static List<WorldRoute> CreateRoutes(long worldSeed, int width, int height,
            List<WorldArea> areas, List<RoutePlan> plans, int rootAreaIndex, StableRandom random)
        {
            var routes = new List<WorldRoute>(plans.Count);
            WorldArea rootArea = areas[rootAreaIndex];
            for (int i = 0; i < plans.Count; i++)
            {
                RoutePlan plan = plans[i];
                WorldArea from = areas[plan.Left];
                WorldArea to = areas[plan.Right];
                bool gated = from.RequiredAdminAccess == 3 || to.RequiredAdminAccess == 3;
                // 지도 전체의 길을 한 타일 오솔길로 통일한다. 중요도는 Kind로만 표현하고,
                // 폭으로 넓은 덩어리를 만들지 않아 여러 갈래가 지형 사이로 스며들게 한다.
                const int routeWidth = 1;
                List<GridCoord> path = gated
                    ? CreateBentPath(from.Center, to.Center, width, height, random)
                    : CreateOrganicPath(from.Center, to.Center, width, height, rootArea,
                        routeWidth / 2, random);
                routes.Add(new WorldRoute(
                    "route-" + (i + 1).ToString("00") + "-" + ShortStableId(worldSeed,
                        from.Id + ":" + to.Id),
                    from.Id,
                    to.Id,
                    plan.Kind,
                    routeWidth,
                    plan.IsLoop,
                    gated,
                    gated ? 3 : 0,
                    gated ? AccessTokens : Array.Empty<string>(),
                    path));
            }
            return routes;
        }

        private static List<GridCoord> CreatePathAvoidingArea(GridCoord from, GridCoord to, int width,
            int height, WorldArea blockedArea, int footprintRadius, StableRandom random)
        {
            int tileCount = checked(width * height);
            var parent = new int[tileCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = -2;
            int startIndex = from.Y * width + from.X;
            int goalIndex = to.Y * width + to.X;
            parent[startIndex] = -1;
            var queue = new Queue<GridCoord>();
            queue.Enqueue(from);
            int directionOffset = random.Next(CardinalDirections.Length);

            while (queue.Count > 0 && parent[goalIndex] == -2)
            {
                GridCoord current = queue.Dequeue();
                for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    GridCoord direction = CardinalDirections[
                        (directionIndex + directionOffset) % CardinalDirections.Length];
                    var next = new GridCoord(current.X + direction.X, current.Y + direction.Y);
                    if (!RouteCenterIsAllowed(next, width, height, blockedArea, footprintRadius)) continue;
                    int nextIndex = next.Y * width + next.X;
                    if (parent[nextIndex] != -2) continue;
                    parent[nextIndex] = current.Y * width + current.X;
                    queue.Enqueue(next);
                }
            }
            if (parent[goalIndex] == -2)
                throw new InvalidOperationException("A pre-root route could not avoid the gated ROOT_SYSTEM footprint.");

            var path = new List<GridCoord>();
            for (int index = goalIndex; index >= 0; index = parent[index])
                path.Add(new GridCoord(index % width, index / width));
            path.Reverse();
            return path;
        }

        // 위에서 본 지도처럼 자연스러운 곡선 길: from→to 를 여러 웨이포인트로 나누고
        // 각 웨이포인트를 진행 방향의 수직으로 흔들어(중간에서 가장 크게) 유기적으로 굽힌 뒤,
        // 웨이포인트 사이를 짧은 BFS 구간으로 이어 붙인다(4방향 유지 → 도달성 보장).
        private static List<GridCoord> CreateOrganicPath(GridCoord from, GridCoord to, int width,
            int height, WorldArea blockedArea, int footprintRadius, StableRandom random)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int segments = Math.Max(2, (int)(len / 12.0));
            double px = len > 0.001 ? -dy / len : 0.0;
            double py = len > 0.001 ? dx / len : 0.0;

            var waypoints = new List<GridCoord> { from };
            for (int s = 1; s < segments; s++)
            {
                double t = s / (double)segments;
                double baseX = from.X + dx * t;
                double baseY = from.Y + dy * t;
                double wobble = Math.Sin(t * Math.PI); // 양끝 0, 중앙 1
                double amp = ((random.Next(201) - 100) / 100.0) * len * 0.22 * wobble;
                int wx = Clamp((int)Math.Round(baseX + px * amp), 2, width - 3);
                int wy = Clamp((int)Math.Round(baseY + py * amp), 2, height - 3);
                var wp = new GridCoord(wx, wy);
                if (RouteCenterIsAllowed(wp, width, height, blockedArea, footprintRadius))
                    waypoints.Add(wp);
            }
            waypoints.Add(to);

            var path = new List<GridCoord>();
            for (int i = 0; i + 1 < waypoints.Count; i++)
            {
                List<GridCoord> segment = CreatePathAvoidingArea(waypoints[i], waypoints[i + 1],
                    width, height, blockedArea, footprintRadius, random);
                for (int k = i == 0 ? 0 : 1; k < segment.Count; k++) path.Add(segment[k]);
            }
            return path;
        }

        private static bool RouteCenterIsAllowed(GridCoord coord, int width, int height,
            WorldArea blockedArea, int footprintRadius)
        {
            if (coord.X - footprintRadius <= 0 || coord.Y - footprintRadius <= 0 ||
                coord.X + footprintRadius >= width - 1 || coord.Y + footprintRadius >= height - 1)
                return false;
            for (int y = coord.Y - footprintRadius; y <= coord.Y + footprintRadius; y++)
                for (int x = coord.X - footprintRadius; x <= coord.X + footprintRadius; x++)
                    if (blockedArea.Contains(new GridCoord(x, y))) return false;
            return true;
        }

        private static List<GridCoord> CreateBentPath(GridCoord from, GridCoord to, int width, int height,
            StableRandom random)
        {
            int midX = Clamp((from.X + to.X) / 2 + random.Next(9) - 4, 2, width - 3);
            int midY = Clamp((from.Y + to.Y) / 2 + random.Next(9) - 4, 2, height - 3);
            bool horizontalFirst = random.Next(2) == 0;
            var points = new List<GridCoord> { from };
            if (horizontalFirst)
            {
                points.Add(new GridCoord(midX, from.Y));
                points.Add(new GridCoord(midX, to.Y));
            }
            else
            {
                points.Add(new GridCoord(from.X, midY));
                points.Add(new GridCoord(to.X, midY));
            }
            points.Add(to);

            var path = new List<GridCoord>();
            for (int i = 0; i + 1 < points.Count; i++)
                AppendOrthogonalSegment(path, points[i], points[i + 1], (i + (horizontalFirst ? 0 : 1)) % 2 == 0);
            return path;
        }

        private static void AppendOrthogonalSegment(List<GridCoord> path, GridCoord from, GridCoord to,
            bool horizontalFirst)
        {
            int x = from.X;
            int y = from.Y;
            AddPathPoint(path, new GridCoord(x, y));
            if (horizontalFirst)
            {
                while (x != to.X) { x += x < to.X ? 1 : -1; AddPathPoint(path, new GridCoord(x, y)); }
                while (y != to.Y) { y += y < to.Y ? 1 : -1; AddPathPoint(path, new GridCoord(x, y)); }
            }
            else
            {
                while (y != to.Y) { y += y < to.Y ? 1 : -1; AddPathPoint(path, new GridCoord(x, y)); }
                while (x != to.X) { x += x < to.X ? 1 : -1; AddPathPoint(path, new GridCoord(x, y)); }
            }
        }

        private static void AddPathPoint(List<GridCoord> path, GridCoord coord)
        {
            if (path.Count == 0 || path[path.Count - 1] != coord) path.Add(coord);
        }

        private static ProgressionGraph CreateProgressionGraph(List<WorldArea> areas, int[] roleArea,
            string[][] modes)
        {
            var nodes = new List<ProgressionNode>
            {
                new ProgressionNode("keyboard-awakening", CampaignRoles[0], areas[roleArea[0]].Id,
                    "slot-catalyst", 0, 0, 0, string.Empty, Array.Empty<string>(), modes[0],
                    new[]
                    {
                        CandidatePath("slot-catalyst", "copy")
                    }),
                new ProgressionNode("admin-access-1", CampaignRoles[1], areas[roleArea[1]].Id,
                    "slot-admin-access-1", 1, 0, 1, AccessTokens[0], new[] { "keyboard-awakening" }, modes[1],
                    new[]
                    {
                        CandidatePath("slot-admin-access-1", "connect"),
                        CandidatePath("slot-admin-access-1-alt", "copy")
                    }),
                new ProgressionNode("admin-access-2", CampaignRoles[2], areas[roleArea[2]].Id,
                    "slot-admin-access-2", 2, 1, 2, AccessTokens[1], new[] { "admin-access-1" }, modes[2],
                    new[]
                    {
                        CandidatePath("slot-admin-access-2", "delete"),
                        CandidatePath("slot-admin-access-2-alt", "connect")
                    }),
                new ProgressionNode("internal-failure", CampaignRoles[3], areas[roleArea[3]].Id,
                    "slot-internal-failure-primary", 3, 2, 0, string.Empty, new[] { "admin-access-2" }, modes[3],
                    new[]
                    {
                        CandidatePath("slot-internal-failure-primary", "copy"),
                        CandidatePath("slot-internal-failure-backup", "copy")
                    }),
                new ProgressionNode("admin-access-3", CampaignRoles[4], areas[roleArea[4]].Id,
                    "slot-admin-access-3", 4, 2, 3, AccessTokens[2], new[] { "internal-failure" }, modes[4],
                    new[]
                    {
                        CandidatePath("slot-admin-access-3", "restore"),
                        CandidatePath("slot-admin-access-3-alt", "copy")
                    }),
                new ProgressionNode("root-system", CampaignRoles[5], areas[roleArea[5]].Id,
                    "slot-finale-anchor", 5, 3, 0, string.Empty, new[] { "admin-access-3" }, modes[5],
                    new[]
                    {
                        CandidatePath("slot-finale-anchor", "connect"),
                        CandidatePath("slot-finale-safeguard", "connect"),
                        CandidatePath("slot-finale-passage", "connect"),
                        CandidatePath("slot-finale-witness", "connect"),
                        CandidatePath("slot-finale-freedom", "connect", "delete"),
                        CandidatePath("slot-finale-threat", "connect", "delete"),
                        CandidatePath("slot-finale-memory", "connect")
                    })
            };
            var edges = new List<ProgressionEdge>
            {
                new ProgressionEdge("keyboard-awakening", "admin-access-1"),
                new ProgressionEdge("admin-access-1", "admin-access-2"),
                new ProgressionEdge("admin-access-2", "internal-failure"),
                new ProgressionEdge("internal-failure", "admin-access-3"),
                new ProgressionEdge("admin-access-3", "root-system")
            };
            return new ProgressionGraph(ProgressionVersion, nodes, edges, 3, AccessTokens);
        }

        private static ProgressionCandidatePath CandidatePath(string slotId, params string[] modes)
        {
            return new ProgressionCandidatePath(slotId, modes);
        }

        private static string NpcSlotId(int areaIndex)
        {
            return "slot-npc-" + (areaIndex + 1).ToString("00");
        }

        private static BaseTile[] CreateClusteredTerrain(int width, int height,
            List<BiomeDescriptor> biomes, int[] biomeOrder, uint seed)
        {
            var tiles = new BaseTile[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    // 원반을 6개 각도 섹터로 나눠 파이 슬라이스 바이옴으로 지형을 칠한다.
                    TileKind kind = border
                        ? TileKind.Wall
                        : TerrainKindFor(biomes[SectorBiomeIndex(x, y, width, height)],
                            seed, x, y);
                    tiles[y * width + x] = new BaseTile(kind, MovementCostFor(kind));
                }
            }
            return tiles;
        }

        // 원반 중심 기준 각도를 6등분해 파이 슬라이스 바이옴 인덱스를 돌려준다.
        // 컨트롤러 렌더(BiomeIdAt)도 동일한 공식을 써야 지형과 장식이 일치한다.
        private static int SectorBiomeIndex(int x, int y, int width, int height)
        {
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            double dx = x - cx;
            double dy = y - cy;
            double centralRadius = Math.Min(width, height) * 0.12; // 중심 최종 스테이지 존
            if (dx * dx + dy * dy <= centralRadius * centralRadius)
                return RequiredBiomeCount - 1; // 고대 폐허 = 루트 시스템 코어 룩
            double angle = Math.Atan2(dy, dx) + Math.PI; // 0..2π
            int sector = (int)(angle / (2.0 * Math.PI) * RequiredBiomeCount);
            if (sector < 0) sector = 0;
            if (sector >= RequiredBiomeCount) sector = RequiredBiomeCount - 1;
            return sector;
        }

        private static TileKind TerrainKindFor(BiomeDescriptor biome, uint seed, int x, int y)
        {
            double structure = ValueNoise(seed, x, y, 11, 0x1b873593u);
            double moisture = ValueNoise(seed, x, y, 17, 0x85ebca6bu);
            double danger = ValueNoise(seed, x, y, 13, 0xc2b2ae35u);
            switch (biome.Id)
            {
                case "river_wetland":
                    if (moisture > 0.48) return TileKind.Water;
                    if (structure > 0.82) return TileKind.Wall;
                    if (danger > 0.72) return TileKind.Hazard;
                    return TileKind.DarkGrass;
                case "arid_desert":
                    if (structure > 0.84) return TileKind.Ruin;
                    if (danger > 0.70) return TileKind.Hazard;
                    return TileKind.Dirt;
                case "frost_highland":
                    if (structure > 0.72) return TileKind.Wall;
                    if (moisture > 0.84) return TileKind.Water;
                    if (danger > 0.77) return TileKind.Hazard;
                    return TileKind.Floor;
                case "subterranean_cavern":
                    if (structure > 0.52) return TileKind.Wall;
                    if (moisture > 0.86) return TileKind.Water;
                    if (danger > 0.74) return TileKind.Hazard;
                    return TileKind.DarkGrass;
                case "ancient_ruins":
                    if (structure > 0.79) return TileKind.Wall;
                    if (moisture > 0.90) return TileKind.Water;
                    if (danger > 0.63) return TileKind.Hazard;
                    return TileKind.Ruin;
                default:
                    if (structure > 0.76) return TileKind.Wall;
                    if (moisture > 0.88) return TileKind.Water;
                    if (danger > 0.80) return TileKind.Hazard;
                    return TileKind.Grass;
            }
        }

        private static double ValueNoise(uint seed, int x, int y, int scale, uint salt)
        {
            int gridX = x / scale;
            int gridY = y / scale;
            double localX = SmoothStep((x - gridX * scale) / (double)scale);
            double localY = SmoothStep((y - gridY * scale) / (double)scale);
            double top = Lerp(LatticeValue(seed, gridX, gridY, salt),
                LatticeValue(seed, gridX + 1, gridY, salt), localX);
            double bottom = Lerp(LatticeValue(seed, gridX, gridY + 1, salt),
                LatticeValue(seed, gridX + 1, gridY + 1, salt), localX);
            return Lerp(top, bottom, localY);
        }

        private static double LatticeValue(uint seed, int x, int y, uint salt)
        {
            unchecked
            {
                uint value = seed ^ ((uint)x * 0x1f123bb5u) ^ ((uint)y * 0x5f356495u) ^ salt;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value / (double)uint.MaxValue * 2d - 1d;
            }
        }

        private static double SmoothStep(double value) { return value * value * (3d - 2d * value); }
        private static double Lerp(double left, double right, double amount) { return left + (right - left) * amount; }

        private static byte MovementCostFor(TileKind kind)
        {
            if (kind == TileKind.Wall || kind == TileKind.Water) return 255;
            if (kind == TileKind.Hazard) return 3;
            if (kind == TileKind.Ruin) return 2;
            return 1;
        }

        private static void CarveRoute(BaseTile[] tiles, int width, int height, WorldRoute route)
        {
            int radius = route.Width / 2;
            for (int i = 0; i < route.Path.Count; i++)
            {
                GridCoord point = route.Path[i];
                for (int y = point.Y - radius; y <= point.Y + radius; y++)
                    for (int x = point.X - radius; x <= point.X + radius; x++)
                        if (x > 0 && y > 0 && x < width - 1 && y < height - 1)
                            Carve(tiles, width, x, y);
            }
        }

        private static void CarveClearing(BaseTile[] tiles, int width, int height, GridCoord center, int radius)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                    if (x > 0 && y > 0 && x < width - 1 && y < height - 1)
                        Carve(tiles, width, x, y);
        }

        // 원형 클리어링: 중심 최종 스테이지(루트 시스템)를 원 모양으로 판다.
        private static void CarveCircle(BaseTile[] tiles, int width, int height, GridCoord center, int radius)
        {
            int radiusSq = radius * radius;
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    int dx = x - center.X;
                    int dy = y - center.Y;
                    if (dx * dx + dy * dy > radiusSq) continue;
                    if (x > 0 && y > 0 && x < width - 1 && y < height - 1)
                        Carve(tiles, width, x, y);
                }
        }

        private static void Carve(BaseTile[] tiles, int width, int x, int y)
        {
            int index = y * width + x;
            tiles[index] = new BaseTile(tiles[index].Kind == TileKind.Water ? TileKind.Bridge : TileKind.Dirt, 1);
        }

        // 원반(disc) 월드: 세계 중심에서 반지름 밖 타일을 void(Wall)로 만들어 원형 실루엣을 만든다.
        // 구역 중심 주변 코어는 보호해 슬롯 배치 후보를 남기고, 이 마스크는 CarveRoute/CarveClearing보다
        // 먼저 적용되므로 이후 길·클리어링이 원 밖까지 걷는 길을 다시 뚫어 연결성·도달성 검증을 유지한다.
        private static void ApplyDiscMask(BaseTile[] tiles, int width, int height, List<WorldArea> areas)
        {
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            double radius = Math.Min(width, height) / 2.0 - 1.0;
            double radiusSq = radius * radius;
            const int coreProtect = 6;
            int coreProtectSq = coreProtect * coreProtect;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    if (dx * dx + dy * dy <= radiusSq) continue;
                    bool nearCore = false;
                    for (int i = 0; i < areas.Count; i++)
                    {
                        GridCoord center = areas[i].Center;
                        int ax = x - center.X;
                        int ay = y - center.Y;
                        if (ax * ax + ay * ay <= coreProtectSq) { nearCore = true; break; }
                    }
                    if (nearCore) continue;
                    int index = y * width + x;
                    tiles[index] = new BaseTile(TileKind.Wall, MovementCostFor(TileKind.Wall));
                }
            }
        }

        private static HashSet<GridCoord> FloodFill(BaseTile[] tiles, int width, int height, GridCoord start,
            WorldArea blockedArea = null)
        {
            var visited = new HashSet<GridCoord>();
            if (!Contains(width, height, start) || !tiles[start.Y * width + start.X].IsWalkable ||
                (blockedArea != null && blockedArea.Contains(start))) return visited;
            var queue = new Queue<GridCoord>();
            visited.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                GridCoord current = queue.Dequeue();
                for (int i = 0; i < CardinalDirections.Length; i++)
                {
                    var next = new GridCoord(current.X + CardinalDirections[i].X,
                        current.Y + CardinalDirections[i].Y);
                    if (!Contains(width, height, next) || visited.Contains(next) ||
                        !tiles[next.Y * width + next.X].IsWalkable ||
                        (blockedArea != null && blockedArea.Contains(next))) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            return visited;
        }

        private static bool Contains(int width, int height, GridCoord coord)
        {
            return coord.X >= 0 && coord.X < width && coord.Y >= 0 && coord.Y < height;
        }

        private static List<PlacementSlot> CreateSlots(List<WorldArea> areas, int[] roleArea,
            BaseTile[] tiles, int width, int height, GridCoord start, GridCoord exit,
            HashSet<GridCoord> reachable, HashSet<GridCoord> reachableBeforeRoot, StableRandom random)
        {
            var used = new HashSet<GridCoord>();
            var slots = new List<PlacementSlot>();
            WorldArea arrival = areas[roleArea[0]];
            WorldArea access1 = areas[roleArea[1]];
            WorldArea access2 = areas[roleArea[2]];
            WorldArea truth = areas[roleArea[3]];
            WorldArea access3 = areas[roleArea[4]];
            WorldArea rootSystem = areas[roleArea[5]];

            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, arrival, "slot-player-entry", "entry",
                start, "player", "safe_travel");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, arrival, "slot-catalyst", "artifact",
                Offset(start, 2, 0), "administrator_keyboard", "protected", "keyboard_awakening");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, access1, "slot-admin-access-1", "admin_access",
                SeededPreferred(access1.Center, random, 2, 5), "reserved", "admin_access_candidate", "admin_level_1",
                "context_negotiation", "skill_connect");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, arrival, "slot-admin-access-1-alt", "admin_access",
                SeededPreferred(arrival.Center, random, 3, 6), "reserved", "admin_access_candidate", "admin_level_1",
                "context_investigation", "skill_copy");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, access2, "slot-admin-access-2", "admin_access",
                SeededPreferred(access2.Center, random, 2, 5), "reserved", "admin_access_candidate", "admin_level_2",
                "context_combat", "skill_delete");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, access1, "slot-admin-access-2-alt", "admin_access",
                SeededPreferred(access1.Center, random, 3, 6), "reserved", "admin_access_candidate", "admin_level_2",
                "context_negotiation", "skill_connect");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, access3, "slot-admin-access-3", "admin_access",
                SeededPreferred(access3.Center, random, 2, 5), "reserved", "admin_access_candidate", "admin_level_3",
                "context_deployment", "skill_restore");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, truth, "slot-admin-access-3-alt", "admin_access",
                SeededPreferred(truth.Center, random, 4, 7), "reserved", "admin_access_candidate", "admin_level_3",
                "context_investigation", "skill_copy");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, truth, "slot-internal-failure-primary", "truth",
                SeededPreferred(truth.Center, random, 1, 4), "reserved", "internal_failure", "record");
            AddSlot(slots, used, tiles, width, height, reachableBeforeRoot, truth, "slot-internal-failure-backup", "truth",
                SeededPreferred(truth.Center, random, 3, 6), "reserved", "internal_failure", "redundant_clue");

            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-approach", "finale",
                Offset(exit, -5, 0), "reserved", "root_approach", "requires_admin_access_3");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-anchor", "finale",
                exit, "reserved", "root_anchor", "finale_puzzle", "protected", "requires_admin_access_3");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-safeguard", "finale_puzzle",
                Offset(exit, 3, 0), "finale_puzzle", "safeguard");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-passage", "finale_puzzle",
                Offset(exit, 0, 3), "finale_puzzle", "passage");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-freedom", "finale_puzzle",
                Offset(exit, -3, 0), "finale_puzzle", "freedom");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-threat", "finale_puzzle",
                Offset(exit, 3, 3), "finale_puzzle", "threat");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-memory", "finale_puzzle",
                Offset(exit, 0, -3), "finale_puzzle", "memory");
            AddSlot(slots, used, tiles, width, height, reachable, rootSystem, "slot-finale-witness", "finale_puzzle",
                Offset(exit, -3, -3), "finale_puzzle", "witness");

            for (int i = 0; i < areas.Count; i++)
            {
                WorldArea area = areas[i];
                HashSet<GridCoord> areaReachable = area.Id == rootSystem.Id ? reachable : reachableBeforeRoot;
                AddSlot(slots, used, tiles, width, height, areaReachable, area,
                    NpcSlotId(i), "npc",
                    SeededPreferred(area.Center, random, 2, 7), "role_slot", "asset_catalog_required");
                AddSlot(slots, used, tiles, width, height, areaReachable, area,
                    "slot-prop-" + (i + 1).ToString("00"), "prop",
                    SeededPreferred(area.Center, random, 2, 7), "cloneable", "fantasy_system_metaphor");
                AddSlot(slots, used, tiles, width, height, areaReachable, area,
                    "slot-encounter-" + (i + 1).ToString("00") + "-a", "encounter",
                    SeededPreferred(area.Center, random, 3, 8), "combat", "investigation", "negotiation");
                AddSlot(slots, used, tiles, width, height, areaReachable, area,
                    "slot-encounter-" + (i + 1).ToString("00") + "-b", "encounter",
                    SeededPreferred(area.Center, random, 3, 8), "defense", "repair", "escort");
                if (i % 2 == 0)
                    AddSlot(slots, used, tiles, width, height, areaReachable, area,
                        "slot-anomaly-" + (i + 1).ToString("00"), "enemy",
                        SeededPreferred(area.Center, random, 3, 8), "corrupted_creature", "asset_catalog_required");
            }
            return slots;
        }

        private static GridCoord SeededPreferred(GridCoord center, StableRandom random, int minimum, int maximum)
        {
            GridCoord[] directions =
            {
                new GridCoord(1, 0), new GridCoord(1, 1), new GridCoord(0, 1), new GridCoord(-1, 1),
                new GridCoord(-1, 0), new GridCoord(-1, -1), new GridCoord(0, -1), new GridCoord(1, -1)
            };
            GridCoord direction = directions[random.Next(directions.Length)];
            int distance = minimum + random.Next(Math.Max(1, maximum - minimum + 1));
            return new GridCoord(center.X + direction.X * distance, center.Y + direction.Y * distance);
        }

        private static void AddSlot(List<PlacementSlot> slots, HashSet<GridCoord> used,
            BaseTile[] tiles, int width, int height, HashSet<GridCoord> reachable, WorldArea area,
            string id, string type, GridCoord preferred, params string[] tags)
        {
            GridCoord coord = FindSlotCoordinate(tiles, width, height, preferred, area, used, reachable);
            used.Add(coord);
            slots.Add(new PlacementSlot(id, type, coord, area.Id, tags));
        }

        private static GridCoord FindSlotCoordinate(BaseTile[] tiles, int width, int height,
            GridCoord preferred, WorldArea area, HashSet<GridCoord> used, HashSet<GridCoord> reachable)
        {
            int minX = Math.Max(1, area.Min.X + 1);
            int maxX = Math.Min(width - 2, area.Max.X - 1);
            int minY = Math.Max(1, area.Min.Y + 1);
            int maxY = Math.Min(height - 2, area.Max.Y - 1);
            preferred = new GridCoord(Clamp(preferred.X, minX, maxX), Clamp(preferred.Y, minY, maxY));
            int maximumRadius = Math.Max(area.Max.X - area.Min.X, area.Max.Y - area.Min.Y) * 2;
            for (int radius = 0; radius <= maximumRadius; radius++)
            {
                for (int y = Math.Max(minY, preferred.Y - radius); y <= Math.Min(maxY, preferred.Y + radius); y++)
                {
                    for (int x = Math.Max(minX, preferred.X - radius); x <= Math.Min(maxX, preferred.X + radius); x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(preferred) != radius || used.Contains(candidate) ||
                            !reachable.Contains(candidate) || !tiles[y * width + x].IsWalkable) continue;
                        return candidate;
                    }
                }
            }
            throw new InvalidOperationException("No connected placement slot is available inside " + area.Id + ".");
        }

        private static ValidationSummary ValidateGeneratedWorld(BaseTile[] tiles, int width, int height,
            GridCoord start, GridCoord exit, List<WorldArea> areas, List<BiomeDescriptor> biomes,
            List<WorldRoute> routes, ProgressionGraph progression, List<PlacementSlot> slots,
            int[] roleArea)
        {
            if (biomes.Count != RequiredBiomeCount || areas.Count != RequiredAreaCount)
                throw new InvalidOperationException("World contract requires six biomes and twelve areas.");
            var representedBiomes = new HashSet<string>();
            var representedRoles = new HashSet<string>();
            for (int i = 0; i < areas.Count; i++)
            {
                representedBiomes.Add(areas[i].Biome);
                if (!string.IsNullOrEmpty(areas[i].CampaignRole) && !representedRoles.Add(areas[i].CampaignRole))
                    throw new InvalidOperationException("Codria region axes must map to distinct areas.");
            }
            if (representedBiomes.Count != RequiredBiomeCount)
                throw new InvalidOperationException("Every biome family must be represented.");
            for (int i = 0; i < CampaignRoles.Length; i++)
                if (!representedRoles.Contains(CampaignRoles[i]))
                    throw new InvalidOperationException("Missing Codria region axis " + CampaignRoles[i]);
            WorldArea rootArea = areas[roleArea[5]];

            int loopCount = 0;
            for (int i = 0; i < routes.Count; i++)
            {
                WorldRoute route = routes[i];
                if (route.IsLoop) loopCount++;
                if (route.Width != 1)
                    throw new InvalidOperationException("Disc-world routes must remain one-tile paths.");
                if (route.Path.Count == 0)
                    throw new InvalidOperationException("Routes must include a raster path.");
                bool incidentToRoot = route.Connects(rootArea.Id);
                if (incidentToRoot != route.IsGated || (incidentToRoot &&
                    (route.RequiredAdminAccess != 3 ||
                     !SameStringSet(route.RequiredAccessTokens, AccessTokens))))
                    throw new InvalidOperationException("ROOT_SYSTEM routes must carry all three administrator access IDs.");
                if (!incidentToRoot && RouteFootprintTouchesArea(route, rootArea))
                    throw new InvalidOperationException("A pre-root route crosses the gated ROOT_SYSTEM footprint.");
            }
            if (routes.Count < areas.Count || loopCount < 2)
                throw new InvalidOperationException("The route graph requires an MST and at least two loops.");

            HashSet<GridCoord> reachable = FloodFill(tiles, width, height, start);
            HashSet<GridCoord> reachableBeforeRoot = FloodFill(tiles, width, height, start, rootArea);
            for (int i = 0; i < areas.Count; i++)
            {
                if (!reachable.Contains(areas[i].Center))
                    throw new InvalidOperationException("Generated area is unreachable: " + areas[i].Id);
                if (areas[i].Id != rootArea.Id && !reachableBeforeRoot.Contains(areas[i].Center))
                    throw new InvalidOperationException("Pre-finale area requires traversal through the finale: " + areas[i].Id);
            }
            if (start.ManhattanDistance(exit) < Math.Min(width, height) / 4)
                throw new InvalidOperationException("ROOT_SYSTEM is too close to Nupjukyi's arrival area.");

            var slotCoordinates = new HashSet<GridCoord>();
            for (int i = 0; i < slots.Count; i++)
            {
                PlacementSlot slot = slots[i];
                WorldArea area = FindArea(areas, slot.AreaId);
                if (area == null || !area.Contains(slot.Coord))
                    throw new InvalidOperationException("Slot escaped its declared area: " + slot.Id);
                if (!slotCoordinates.Add(slot.Coord))
                    throw new InvalidOperationException("Placement slots overlap at " + slot.Coord);
                if (!reachable.Contains(slot.Coord))
                    throw new InvalidOperationException("Placement slot is unreachable: " + slot.Id);
                if (slot.AreaId != rootArea.Id && !reachableBeforeRoot.Contains(slot.Coord))
                    throw new InvalidOperationException("Pre-finale slot requires traversal through the finale: " + slot.Id);
            }

            var accessCandidates = slots.FindAll(slot => HasTag(slot, "admin_access_candidate"));
            if (accessCandidates.Count != 6)
                throw new InvalidOperationException("Each of three administrator levels requires two candidate slots.");
            for (int level = 1; level <= 3; level++)
            {
                string levelTag = "admin_level_" + level;
                List<PlacementSlot> levelCandidates = accessCandidates.FindAll(slot => HasTag(slot, levelTag));
                if (levelCandidates.Count != 2 || levelCandidates[0].AreaId == levelCandidates[1].AreaId)
                    throw new InvalidOperationException("Administrator access candidates must use two different areas: " + levelTag);
                string firstContext = FindTagWithPrefix(levelCandidates[0], "context_");
                string secondContext = FindTagWithPrefix(levelCandidates[1], "context_");
                if (string.IsNullOrEmpty(firstContext) || string.IsNullOrEmpty(secondContext) ||
                    firstContext == secondContext)
                    throw new InvalidOperationException("Administrator access candidates must use different contexts: " + levelTag);
            }

            if (!IsAcyclic(progression))
                throw new InvalidOperationException("Progression graph must be acyclic.");
            if (progression.Nodes.Count != CampaignBeatIds.Length)
                throw new InvalidOperationException("Progression requires exactly six canonical nodes.");
            for (int i = 0; i < progression.Nodes.Count; i++)
            {
                ProgressionNode node = progression.Nodes[i];
                if (node.Id != ProgressionNodeIds[i] || node.CampaignRole != CampaignRoles[i])
                    throw new InvalidOperationException("Progression nodes must retain the canonical campaign order.");
                if (FindArea(areas, node.AreaId) == null || !ContainsSlot(slots, node.SlotId))
                    throw new InvalidOperationException("Progression node is not bound to generated content: " + node.Id);
                string[] expectedModes = AllowedModeNamesForBeat(CampaignBeatIds[i]);
                if (!SameStringSet(node.ResolutionModes, expectedModes))
                    throw new InvalidOperationException("Progression modes do not match executable campaign abilities: " + node.Id);
                if (node.CandidatePaths.Count == 0)
                    throw new InvalidOperationException("Progression node has no executable slot path: " + node.Id);
                var candidateSlotIds = new HashSet<string>();
                var candidateModes = new HashSet<string>();
                for (int pathIndex = 0; pathIndex < node.CandidatePaths.Count; pathIndex++)
                {
                    ProgressionCandidatePath path = node.CandidatePaths[pathIndex];
                    PlacementSlot candidateSlot = slots.Find(slot => slot.Id == path.SlotId);
                    bool accessCandidate = node.RewardAdminAccess > 0 && candidateSlot != null &&
                        HasTag(candidateSlot, "admin_level_" + node.RewardAdminAccess);
                    if (candidateSlot == null || (!accessCandidate && candidateSlot.AreaId != node.AreaId) ||
                        !candidateSlotIds.Add(path.SlotId) || path.AcquisitionModes.Count == 0)
                        throw new InvalidOperationException("Progression slot path is invalid: " + node.Id);
                    var uniquePathModes = new HashSet<string>();
                    for (int modeIndex = 0; modeIndex < path.AcquisitionModes.Count; modeIndex++)
                    {
                        string mode = path.AcquisitionModes[modeIndex];
                        if (!uniquePathModes.Add(mode) || !ContainsString(expectedModes, mode))
                            throw new InvalidOperationException("Progression slot path uses a non-executable mode: " + node.Id);
                        candidateModes.Add(mode);
                    }
                    HashSet<string> candidateStageAreas = ReachableAreaIds(routes, areas[roleArea[0]].Id,
                        node.RequiredAdminAccess, TokensForLevel(node.RequiredAdminAccess));
                    if (!candidateStageAreas.Contains(candidateSlot.AreaId))
                        throw new InvalidOperationException("Progression candidate is spatially unreachable: " + path.SlotId);
                }
                if (!candidateSlotIds.Contains(node.SlotId) ||
                    !SameStringSet(candidateModes, expectedModes))
                    throw new InvalidOperationException("Progression slot paths do not cover every canonical mode: " + node.Id);
                HashSet<string> stageAreas = ReachableAreaIds(routes, areas[roleArea[0]].Id,
                    node.RequiredAdminAccess, TokensForLevel(node.RequiredAdminAccess));
                if (!stageAreas.Contains(node.AreaId))
                    throw new InvalidOperationException("Progression stage is spatially unreachable: " + node.Id);
            }

            HashSet<string> beforeRoot = ReachableAreaIds(routes, areas[roleArea[0]].Id, 0,
                Array.Empty<string>());
            HashSet<string> afterRoot = ReachableAreaIds(routes, areas[roleArea[0]].Id, 3,
                AccessTokens);
            if (beforeRoot.Contains(areas[roleArea[5]].Id) || !afterRoot.Contains(areas[roleArea[5]].Id) ||
                afterRoot.Count != areas.Count)
                throw new InvalidOperationException("Finale route gate failed stage-aware reachability.");

            PlacementSlot finaleAnchor = slots.Find(slot => slot.Id == "slot-finale-anchor");
            var puzzle = slots.FindAll(slot => HasTag(slot, "finale_puzzle"));
            if (finaleAnchor == null || puzzle.Count != 7)
                throw new InvalidOperationException("Finale puzzle requires exactly seven bounded components.");
            for (int i = 0; i < puzzle.Count; i++)
                if (puzzle[i].Coord.ManhattanDistance(finaleAnchor.Coord) > 12)
                    throw new InvalidOperationException("Finale puzzle components must form one spatial cluster.");

            var summary = new ValidationSummary
            {
                ConnectedTileCount = reachable.Count,
                LoopRouteCount = loopCount
            };
            summary.Checks.Add("six_biomes_represented");
            summary.Checks.Add("six_codria_region_axes_bound_independently");
            summary.Checks.Add("mst_and_loop_routes_connected");
            summary.Checks.Add("major_route_width_at_least_three");
            summary.Checks.Add("all_slots_unique_inside_area_and_reachable");
            summary.Checks.Add("pre_finale_routes_and_slots_avoid_finale_before_gate");
            summary.Checks.Add("progression_acyclic_with_executable_slot_paths");
            summary.Checks.Add("root_system_locked_before_admin_access_3_and_reachable_after");
            summary.Checks.Add("finale_components_bounded");
            return summary;
        }

        private static bool RouteFootprintTouchesArea(WorldRoute route, WorldArea area)
        {
            int radius = route.Width / 2;
            for (int pointIndex = 0; pointIndex < route.Path.Count; pointIndex++)
            {
                GridCoord point = route.Path[pointIndex];
                for (int y = point.Y - radius; y <= point.Y + radius; y++)
                    for (int x = point.X - radius; x <= point.X + radius; x++)
                        if (area.Contains(new GridCoord(x, y))) return true;
            }
            return false;
        }

        private static bool SameStringSet(IEnumerable<string> left, IEnumerable<string> right)
        {
            var leftSet = new HashSet<string>(StringComparer.Ordinal);
            var rightSet = new HashSet<string>(StringComparer.Ordinal);
            int leftCount = 0;
            int rightCount = 0;
            foreach (string value in left ?? Array.Empty<string>())
            {
                leftCount++;
                leftSet.Add(value);
            }
            foreach (string value in right ?? Array.Empty<string>())
            {
                rightCount++;
                rightSet.Add(value);
            }
            return leftSet.Count == leftCount && rightSet.Count == rightCount &&
                   leftSet.SetEquals(rightSet);
        }

        private static bool ContainsString(IReadOnlyList<string> values, string expected)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], expected, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsAcyclic(ProgressionGraph graph)
        {
            var indegree = new Dictionary<string, int>();
            var outgoing = new Dictionary<string, List<string>>();
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                indegree[graph.Nodes[i].Id] = 0;
                outgoing[graph.Nodes[i].Id] = new List<string>();
            }
            for (int i = 0; i < graph.Edges.Count; i++)
            {
                ProgressionEdge edge = graph.Edges[i];
                if (!indegree.ContainsKey(edge.From) || !indegree.ContainsKey(edge.To)) return false;
                indegree[edge.To]++;
                outgoing[edge.From].Add(edge.To);
            }
            var queue = new Queue<string>();
            foreach (KeyValuePair<string, int> pair in indegree)
                if (pair.Value == 0) queue.Enqueue(pair.Key);
            int visited = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                visited++;
                for (int i = 0; i < outgoing[current].Count; i++)
                {
                    string next = outgoing[current][i];
                    indegree[next]--;
                    if (indegree[next] == 0) queue.Enqueue(next);
                }
            }
            return visited == graph.Nodes.Count;
        }

        private static HashSet<string> ReachableAreaIds(List<WorldRoute> routes, string startAreaId,
            int adminAccess, IEnumerable<string> tokens)
        {
            var tokenSet = new HashSet<string>(tokens ?? Array.Empty<string>());
            var adjacency = new Dictionary<string, List<string>>();
            for (int i = 0; i < routes.Count; i++)
            {
                WorldRoute route = routes[i];
                bool allowed = !route.IsGated || (adminAccess >= route.RequiredAdminAccess &&
                    ContainsAll(tokenSet, route.RequiredAccessTokens));
                if (!allowed) continue;
                AddNeighbor(adjacency, route.FromAreaId, route.ToAreaId);
                AddNeighbor(adjacency, route.ToAreaId, route.FromAreaId);
            }
            var visited = new HashSet<string> { startAreaId };
            var queue = new Queue<string>();
            queue.Enqueue(startAreaId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string> nextValues)) continue;
                for (int i = 0; i < nextValues.Count; i++)
                    if (visited.Add(nextValues[i])) queue.Enqueue(nextValues[i]);
            }
            return visited;
        }

        private static void AddNeighbor(Dictionary<string, List<string>> adjacency, string from, string to)
        {
            if (!adjacency.TryGetValue(from, out List<string> values))
            {
                values = new List<string>();
                adjacency[from] = values;
            }
            values.Add(to);
        }

        private static bool ContainsAll(HashSet<string> values, IReadOnlyList<string> required)
        {
            for (int i = 0; i < required.Count; i++) if (!values.Contains(required[i])) return false;
            return true;
        }

        private static string[] TokensForLevel(int level)
        {
            if (level <= 0) return Array.Empty<string>();
            if (level == 1) return new[] { AccessTokens[0] };
            if (level == 2) return new[] { AccessTokens[0], AccessTokens[1] };
            return (string[])AccessTokens.Clone();
        }

        private static WorldArea FindArea(List<WorldArea> areas, string id)
        {
            for (int i = 0; i < areas.Count; i++)
                if (string.Equals(areas[i].Id, id, StringComparison.Ordinal)) return areas[i];
            return null;
        }

        private static bool ContainsSlot(List<PlacementSlot> slots, string slotId)
        {
            for (int i = 0; i < slots.Count; i++)
                if (string.Equals(slots[i].Id, slotId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int AreaIndexFor(int x, int y, int width, int height)
        {
            int column = Math.Min(AreaColumns - 1, x * AreaColumns / width);
            int row = Math.Min(AreaRows - 1, y * AreaRows / height);
            return row * AreaColumns + column;
        }

        private static string LandmarkFor(string role, string biome, int index)
        {
            switch (role)
            {
                case CampaignCatalog.ArrivalCatalystRole: return index % 2 == 0 ? "bug_grove" : "crash_debugger";
                case CampaignCatalog.LocalStakesRole: return index % 2 == 0 ? "buffer_settlement" : "queue_outpost";
                case CampaignCatalog.RelationshipConflictRole: return index % 2 == 0 ? "deadlock_citadel" : "mutex_crossroads";
                case CampaignCatalog.HiddenTruthRole: return index % 2 == 0 ? "data_archive" : "record_vault";
                case CampaignCatalog.ConsequenceReturnRole: return index % 2 == 0 ? "legacy_fortress" : "debt_ruin";
                case CampaignCatalog.FinalConvergenceRole: return index % 2 == 0 ? "root_terminal" : "kernel_chamber";
                default: return index % 3 == 0 ? "camp" : biome == "ancient_ruins" ? "ruins" : "shrine";
            }
        }

        private static string DisplayNameFor(string role, string biomeName, int index)
        {
            switch (role)
            {
                case CampaignCatalog.ArrivalCatalystRole: return "버그 숲 · " + biomeName;
                case CampaignCatalog.LocalStakesRole: return "버퍼 마을 · " + biomeName;
                case CampaignCatalog.RelationshipConflictRole: return "교착 도시 · " + biomeName;
                case CampaignCatalog.HiddenTruthRole: return "데이터 대도서관 · " + biomeName;
                case CampaignCatalog.ConsequenceReturnRole: return "레거시 성채 · " + biomeName;
                case CampaignCatalog.FinalConvergenceRole: return "루트 시스템 · " + biomeName;
                default: return biomeName + " 관심 지점 " + (index + 1);
            }
        }

        private static string DescriptionFor(string role, string biomeName)
        {
            if (role == CampaignCatalog.ArrivalCatalystRole) return "넙죽이가 관리자 키보드를 깨우는 버그 숲 지역 축";
            if (role == CampaignCatalog.LocalStakesRole) return "버퍼 과부하와 주민 관계를 다루는 관리자 권한 후보 지역";
            if (role == CampaignCatalog.RelationshipConflictRole) return "교착된 파벌의 전투·협상·배치 해법을 제공하는 지역";
            if (role == CampaignCatalog.HiddenTruthRole) return "관리자 통제 내부의 붕괴 원인을 보존한 기록 지역";
            if (role == CampaignCatalog.ConsequenceReturnRole) return "과거 편집의 기술 부채와 책임이 역류하는 지역";
            if (role == CampaignCatalog.FinalConvergenceRole) return "관리자 권한 3단계와 내부 단서 뒤에 진입하는 ROOT_SYSTEM";
            return biomeName + "의 기존 지형과 에셋 안에서 사건 역할을 배정할 수 있는 관심 지점";
        }

        private static IEnumerable<string> TagsFor(string role, string landmark)
        {
            var tags = new List<string> { "poi", landmark, "encounter_space" };
            if (!string.IsNullOrEmpty(role)) tags.Add("campaign_role");
            if (role == CampaignCatalog.LocalStakesRole || role == CampaignCatalog.RelationshipConflictRole ||
                role == CampaignCatalog.ConsequenceReturnRole) tags.Add("admin_access_region_candidate");
            if (role == CampaignCatalog.HiddenTruthRole) tags.Add("truth_candidate");
            if (role == CampaignCatalog.FinalConvergenceRole) tags.Add("finale_candidate");
            return tags;
        }

        private static bool HasTag(PlacementSlot slot, string tag)
        {
            for (int i = 0; i < slot.Tags.Length; i++)
                if (string.Equals(slot.Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string FindTagWithPrefix(PlacementSlot slot, string prefix)
        {
            for (int i = 0; i < slot.Tags.Length; i++)
                if (slot.Tags[i].StartsWith(prefix, StringComparison.Ordinal)) return slot.Tags[i];
            return string.Empty;
        }

        private static GridCoord Offset(GridCoord coord, int x, int y)
        {
            return new GridCoord(coord.X + x, coord.Y + y);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static void Shuffle(int[] values, StableRandom random)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                int value = values[i]; values[i] = values[swap]; values[swap] = value;
            }
        }

        private static string ShortStableId(long worldSeed, string label)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    worldSeed + "|" + CurrentVersion + "|" + label));
                var value = new StringBuilder(10);
                for (int i = 0; i < 5; i++) value.Append(hash[i].ToString("x2"));
                return value.ToString();
            }
        }

        public static string ComputeLayoutHash(RegionMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var tiles = new BaseTile[checked(map.Width * map.Height)];
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    tiles[y * map.Width + x] = map.GetTile(new GridCoord(x, y));
            return ComputeLayoutHashCore(map.RegionId, map.GeneratorVersion, map.RegionSeed,
                map.RegionKey, map.Width, map.Height, map.Start, map.Exit, map.StartAreaId,
                map.RootAreaId, tiles, map.PlacementSlots, map.Areas, map.Biomes, map.Routes,
                map.Progression, map.GenerationReport);
        }

        private static string ComputeLayoutHashCore(Guid regionId, string generatorVersion, long seed,
            string key, int width, int height, GridCoord start, GridCoord exit, string startAreaId,
            string rootAreaId, BaseTile[] tiles, IReadOnlyList<PlacementSlot> slots,
            IReadOnlyList<WorldArea> areas, IReadOnlyList<BiomeDescriptor> biomes,
            IReadOnlyList<WorldRoute> routes, ProgressionGraph progression, GenerationReport report)
        {
            var builder = new StringBuilder();
            AppendField(builder, "seeded_local_layout_v3");
            AppendField(builder, regionId.ToString("N"));
            AppendField(builder, generatorVersion);
            AppendField(builder, seed.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, key);
            AppendField(builder, width.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, height.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, start.Pack().ToString(CultureInfo.InvariantCulture));
            AppendField(builder, exit.Pack().ToString(CultureInfo.InvariantCulture));
            AppendField(builder, startAreaId);
            AppendField(builder, rootAreaId);
            AppendField(builder, tiles.Length.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < tiles.Length; i++)
            {
                AppendField(builder, ((byte)tiles[i].Kind).ToString(CultureInfo.InvariantCulture));
                AppendField(builder, tiles[i].MovementCost.ToString(CultureInfo.InvariantCulture));
            }

            var orderedSlots = new List<PlacementSlot>(slots);
            orderedSlots.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            AppendField(builder, orderedSlots.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedSlots.Count; i++)
            {
                PlacementSlot slot = orderedSlots[i];
                AppendField(builder, slot.Id);
                AppendField(builder, slot.Type);
                AppendField(builder, slot.Coord.Pack().ToString(CultureInfo.InvariantCulture));
                AppendField(builder, slot.AreaId);
                AppendSortedStrings(builder, slot.Tags);
            }

            var orderedAreas = new List<WorldArea>(areas);
            orderedAreas.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            AppendField(builder, orderedAreas.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedAreas.Count; i++)
            {
                WorldArea area = orderedAreas[i];
                AppendField(builder, area.Id);
                AppendField(builder, area.DisplayName);
                AppendField(builder, area.Biome);
                AppendField(builder, area.Description);
                AppendField(builder, area.Min.Pack().ToString(CultureInfo.InvariantCulture));
                AppendField(builder, area.Max.Pack().ToString(CultureInfo.InvariantCulture));
                AppendField(builder, area.LandmarkType);
                AppendField(builder, area.CampaignRole);
                AppendField(builder, area.TravelCost.ToString(CultureInfo.InvariantCulture));
                AppendField(builder, area.RequiredAdminAccess.ToString(CultureInfo.InvariantCulture));
                AppendSortedStrings(builder, area.Neighbors);
                AppendSortedStrings(builder, area.Tags);
            }

            var orderedBiomes = new List<BiomeDescriptor>(biomes);
            orderedBiomes.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            AppendField(builder, orderedBiomes.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedBiomes.Count; i++)
            {
                BiomeDescriptor biome = orderedBiomes[i];
                AppendField(builder, biome.Id);
                AppendField(builder, biome.DisplayName);
                AppendField(builder, ((byte)biome.BaseTile).ToString(CultureInfo.InvariantCulture));
                AppendSortedStrings(builder, biome.TerrainTags);
            }

            var orderedRoutes = new List<WorldRoute>(routes);
            orderedRoutes.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            AppendField(builder, orderedRoutes.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedRoutes.Count; i++)
            {
                WorldRoute route = orderedRoutes[i];
                AppendField(builder, route.Id);
                AppendField(builder, route.FromAreaId);
                AppendField(builder, route.ToAreaId);
                AppendField(builder, route.Kind);
                AppendField(builder, route.Width.ToString(CultureInfo.InvariantCulture));
                AppendField(builder, route.IsLoop.ToString());
                AppendField(builder, route.IsGated.ToString());
                AppendField(builder, route.RequiredAdminAccess.ToString(CultureInfo.InvariantCulture));
                AppendSortedStrings(builder, route.RequiredAccessTokens);
                AppendField(builder, route.Path.Count.ToString(CultureInfo.InvariantCulture));
                for (int point = 0; point < route.Path.Count; point++)
                    AppendField(builder, route.Path[point].Pack().ToString(CultureInfo.InvariantCulture));
            }

            AppendField(builder, progression.Version);
            AppendField(builder, progression.RootRequiredAdminAccess.ToString(CultureInfo.InvariantCulture));
            AppendSortedStrings(builder, progression.RootRequiredAccessTokens);
            var orderedNodes = new List<ProgressionNode>(progression.Nodes);
            orderedNodes.Sort((left, right) =>
            {
                int stage = left.Stage.CompareTo(right.Stage);
                return stage != 0 ? stage : string.CompareOrdinal(left.Id, right.Id);
            });
            AppendField(builder, orderedNodes.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                ProgressionNode node = orderedNodes[i];
                AppendField(builder, node.Id);
                AppendField(builder, node.CampaignRole);
                AppendField(builder, node.AreaId);
                AppendField(builder, node.SlotId);
                AppendField(builder, node.Stage.ToString(CultureInfo.InvariantCulture));
                AppendField(builder, node.RequiredAdminAccess.ToString(CultureInfo.InvariantCulture));
                AppendField(builder, node.RewardAdminAccess.ToString(CultureInfo.InvariantCulture));
                AppendField(builder, node.RewardAccessToken);
                AppendSortedStrings(builder, node.Requires);
                AppendSortedStrings(builder, node.ResolutionModes);
                var orderedPaths = new List<ProgressionCandidatePath>(node.CandidatePaths);
                orderedPaths.Sort((left, right) => string.CompareOrdinal(left.SlotId, right.SlotId));
                AppendField(builder, orderedPaths.Count.ToString(CultureInfo.InvariantCulture));
                for (int pathIndex = 0; pathIndex < orderedPaths.Count; pathIndex++)
                {
                    AppendField(builder, orderedPaths[pathIndex].SlotId);
                    AppendSortedStrings(builder, orderedPaths[pathIndex].AcquisitionModes);
                }
            }
            var orderedEdges = new List<ProgressionEdge>(progression.Edges);
            orderedEdges.Sort((left, right) =>
            {
                int from = string.CompareOrdinal(left.From, right.From);
                return from != 0 ? from : string.CompareOrdinal(left.To, right.To);
            });
            AppendField(builder, orderedEdges.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedEdges.Count; i++)
            {
                AppendField(builder, orderedEdges[i].From);
                AppendField(builder, orderedEdges[i].To);
            }

            AppendField(builder, report.Pipeline);
            AppendField(builder, report.GeneratorVersion);
            AppendField(builder, report.IsValid.ToString());
            AppendField(builder, report.Attempts.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, report.ConnectedTileCount.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, report.RouteCount.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, report.LoopRouteCount.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, report.RepairedIssueCount.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, report.CriticalIssueCount.ToString(CultureInfo.InvariantCulture));
            AppendSortedStrings(builder, report.Checks);
            var orderedRepairs = new List<GenerationRepair>(report.Repairs);
            orderedRepairs.Sort((left, right) =>
            {
                int type = string.CompareOrdinal(left.Type, right.Type);
                if (type != 0) return type;
                int target = string.CompareOrdinal(left.TargetId, right.TargetId);
                return target != 0 ? target : string.CompareOrdinal(left.Description, right.Description);
            });
            AppendField(builder, orderedRepairs.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < orderedRepairs.Count; i++)
            {
                AppendField(builder, orderedRepairs[i].Type);
                AppendField(builder, orderedRepairs[i].TargetId);
                AppendField(builder, orderedRepairs[i].Description);
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        private static void AppendField(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(value).Append(';');
        }

        private static void AppendSortedStrings(StringBuilder builder, IEnumerable<string> values)
        {
            var ordered = new List<string>(values ?? Array.Empty<string>());
            ordered.Sort(StringComparer.Ordinal);
            AppendField(builder, ordered.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < ordered.Count; i++) AppendField(builder, ordered[i]);
        }

        private sealed class DisjointSet
        {
            private readonly int[] _parents;

            public DisjointSet(int count)
            {
                _parents = new int[count];
                for (int i = 0; i < count; i++) _parents[i] = i;
            }

            public bool Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot == rightRoot) return false;
                _parents[rightRoot] = leftRoot;
                return true;
            }

            private int Find(int value)
            {
                while (_parents[value] != value)
                {
                    _parents[value] = _parents[_parents[value]];
                    value = _parents[value];
                }
                return value;
            }
        }
    }

    public static class DeterministicGuid
    {
        public static Guid Create(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var bytes = new byte[16];
                Array.Copy(hash, bytes, bytes.Length);
                return new Guid(bytes);
            }
        }
    }

    internal sealed class StableRandom
    {
        private uint _state;

        public StableRandom(uint seed) { _state = seed == 0 ? 0x6d2b79f5u : seed; }

        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMax);
        }
    }
}
