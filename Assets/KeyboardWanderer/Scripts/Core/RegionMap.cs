using System;
using System.Collections.Generic;

namespace KeyboardWanderer.Core
{
    public enum TileKind : byte
    {
        Floor = 0,
        Wall = 1,
        Hazard = 2,
        Grass = 3,
        Dirt = 4,
        Water = 5,
        Bridge = 6,
        Ruin = 7,
        DarkGrass = 8
    }

    public readonly struct BaseTile
    {
        public TileKind Kind { get; }
        public byte MovementCost { get; }

        public BaseTile(TileKind kind, byte movementCost)
        {
            Kind = kind;
            MovementCost = movementCost;
        }

        public bool IsWalkable => Kind != TileKind.Wall && Kind != TileKind.Water;
    }

    public sealed class WorldArea
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Biome { get; }
        public string Description { get; }
        public GridCoord Min { get; }
        public GridCoord Max { get; }
        public string LandmarkType { get; }
        public string CampaignRole { get; }
        public IReadOnlyList<string> Neighbors { get; }
        public int TravelCost { get; }
        public IReadOnlyList<string> Tags { get; }
        public int RequiredAdminAccess { get; }
        public GridCoord Center => new GridCoord((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);

        public WorldArea(string id, string displayName, string biome, string description, GridCoord min, GridCoord max)
            : this(id, displayName, biome, description, min, max, "wilderness", string.Empty,
                Array.Empty<string>(), 1, Array.Empty<string>(), 0)
        {
        }

        public WorldArea(string id, string displayName, string biome, string description, GridCoord min, GridCoord max,
            string landmarkType, string campaignRole, IEnumerable<string> neighbors, int travelCost,
            IEnumerable<string> tags, int requiredAdminAccess = 0)
        {
            Id = id;
            DisplayName = displayName;
            Biome = biome;
            Description = description;
            Min = min;
            Max = max;
            LandmarkType = landmarkType ?? "wilderness";
            CampaignRole = campaignRole ?? string.Empty;
            Neighbors = new List<string>(neighbors ?? Array.Empty<string>());
            TravelCost = Math.Max(1, travelCost);
            Tags = new List<string>(tags ?? Array.Empty<string>());
            RequiredAdminAccess = Math.Max(0, Math.Min(3, requiredAdminAccess));
        }

        public bool Contains(GridCoord coord)
        {
            return coord.X >= Min.X && coord.X <= Max.X && coord.Y >= Min.Y && coord.Y <= Max.Y;
        }
    }

    public sealed class BiomeDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public TileKind BaseTile { get; }
        public IReadOnlyList<string> TerrainTags { get; }

        public BiomeDescriptor(string id, string displayName, TileKind baseTile, params string[] terrainTags)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            BaseTile = baseTile;
            TerrainTags = new List<string>(terrainTags ?? Array.Empty<string>());
        }
    }

    public sealed class PlacementSlot
    {
        public string Id { get; }
        public string Type { get; }
        public GridCoord Coord { get; }
        public string AreaId { get; }
        public string[] Tags { get; }

        public PlacementSlot(string id, string type, GridCoord coord)
            : this(id, type, coord, string.Empty, Array.Empty<string>())
        {
        }

        public PlacementSlot(string id, string type, GridCoord coord, string areaId, params string[] tags)
        {
            Id = id;
            Type = type;
            Coord = coord;
            AreaId = areaId ?? string.Empty;
            Tags = tags ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Immutable logical route. The tile path is rasterized into the terrain, while this metadata
    /// preserves graph topology, width, loop classification, and access gates for validation/UI.
    /// </summary>
    public sealed class WorldRoute
    {
        private readonly List<GridCoord> _path;
        private readonly List<string> _requiredAccessTokens;

        public string Id { get; }
        public string FromAreaId { get; }
        public string ToAreaId { get; }
        public string Kind { get; }
        public int Width { get; }
        public bool IsLoop { get; }
        public bool IsGated { get; }
        public int RequiredAdminAccess { get; }
        public IReadOnlyList<string> RequiredAccessTokens => _requiredAccessTokens;
        public IReadOnlyList<GridCoord> Path => _path;

        public WorldRoute(string id, string fromAreaId, string toAreaId, string kind, int width,
            bool isLoop, bool isGated, int requiredAdminAccess, IEnumerable<string> requiredAccessTokens,
            IEnumerable<GridCoord> path)
        {
            Id = id ?? string.Empty;
            FromAreaId = fromAreaId ?? string.Empty;
            ToAreaId = toAreaId ?? string.Empty;
            Kind = string.IsNullOrWhiteSpace(kind) ? "major" : kind;
            Width = Math.Max(1, width);
            IsLoop = isLoop;
            IsGated = isGated;
            RequiredAdminAccess = Math.Max(0, Math.Min(3, requiredAdminAccess));
            _requiredAccessTokens = new List<string>(requiredAccessTokens ?? Array.Empty<string>());
            _path = new List<GridCoord>(path ?? Array.Empty<GridCoord>());
        }

        public bool Connects(string areaId)
        {
            return string.Equals(FromAreaId, areaId, StringComparison.Ordinal) ||
                   string.Equals(ToAreaId, areaId, StringComparison.Ordinal);
        }
    }

    public sealed class ProgressionCandidatePath
    {
        private readonly List<string> _acquisitionModes;

        public string SlotId { get; }
        public IReadOnlyList<string> AcquisitionModes => _acquisitionModes;

        public ProgressionCandidatePath(string slotId, IEnumerable<string> acquisitionModes)
        {
            SlotId = slotId ?? string.Empty;
            _acquisitionModes = new List<string>(acquisitionModes ?? Array.Empty<string>());
        }
    }

    public sealed class ProgressionNode
    {
        private readonly List<string> _requires;
        private readonly List<string> _resolutionModes;
        private readonly List<ProgressionCandidatePath> _candidatePaths;

        public string Id { get; }
        public string CampaignRole { get; }
        public string AreaId { get; }
        public string SlotId { get; }
        public int Stage { get; }
        public int RequiredAdminAccess { get; }
        public int RewardAdminAccess { get; }
        public string RewardAccessToken { get; }
        public IReadOnlyList<string> Requires => _requires;
        public IReadOnlyList<string> ResolutionModes => _resolutionModes;
        public IReadOnlyList<ProgressionCandidatePath> CandidatePaths => _candidatePaths;

        public ProgressionNode(string id, string campaignRole, string areaId, string slotId, int stage,
            int requiredAdminAccess, int rewardAdminAccess, string rewardAccessToken,
            IEnumerable<string> requires, IEnumerable<string> resolutionModes,
            IEnumerable<ProgressionCandidatePath> candidatePaths = null)
        {
            Id = id ?? string.Empty;
            CampaignRole = campaignRole ?? string.Empty;
            AreaId = areaId ?? string.Empty;
            SlotId = slotId ?? string.Empty;
            Stage = Math.Max(0, stage);
            RequiredAdminAccess = Math.Max(0, Math.Min(3, requiredAdminAccess));
            RewardAdminAccess = Math.Max(0, Math.Min(3, rewardAdminAccess));
            RewardAccessToken = rewardAccessToken ?? string.Empty;
            _requires = new List<string>(requires ?? Array.Empty<string>());
            _resolutionModes = new List<string>(resolutionModes ?? Array.Empty<string>());
            _candidatePaths = new List<ProgressionCandidatePath>(
                candidatePaths ?? Array.Empty<ProgressionCandidatePath>());
            if (_candidatePaths.Count == 0 && !string.IsNullOrEmpty(SlotId))
                _candidatePaths.Add(new ProgressionCandidatePath(SlotId, _resolutionModes));
        }
    }

    public sealed class ProgressionEdge
    {
        public string From { get; }
        public string To { get; }

        public ProgressionEdge(string from, string to)
        {
            From = from ?? string.Empty;
            To = to ?? string.Empty;
        }
    }

    public sealed class ProgressionGraph
    {
        private readonly List<ProgressionNode> _nodes;
        private readonly List<ProgressionEdge> _edges;
        private readonly List<string> _rootRequiredAccessTokens;

        public static ProgressionGraph Empty { get; } = new ProgressionGraph(
            string.Empty, Array.Empty<ProgressionNode>(), Array.Empty<ProgressionEdge>(), 0,
            Array.Empty<string>());

        public string Version { get; }
        public IReadOnlyList<ProgressionNode> Nodes => _nodes;
        public IReadOnlyList<ProgressionEdge> Edges => _edges;
        public int RootRequiredAdminAccess { get; }
        public IReadOnlyList<string> RootRequiredAccessTokens => _rootRequiredAccessTokens;

        public ProgressionGraph(string version, IEnumerable<ProgressionNode> nodes,
            IEnumerable<ProgressionEdge> edges, int rootRequiredAdminAccess,
            IEnumerable<string> rootRequiredAccessTokens)
        {
            Version = version ?? string.Empty;
            _nodes = new List<ProgressionNode>(nodes ?? Array.Empty<ProgressionNode>());
            _edges = new List<ProgressionEdge>(edges ?? Array.Empty<ProgressionEdge>());
            RootRequiredAdminAccess = Math.Max(0, Math.Min(3, rootRequiredAdminAccess));
            _rootRequiredAccessTokens = new List<string>(
                rootRequiredAccessTokens ?? Array.Empty<string>());
        }

        public bool TryGetNode(string nodeId, out ProgressionNode node)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (string.Equals(_nodes[i].Id, nodeId, StringComparison.Ordinal))
                {
                    node = _nodes[i];
                    return true;
                }
            }
            node = null;
            return false;
        }
    }

    public sealed class GenerationRepair
    {
        public string Type { get; }
        public string TargetId { get; }
        public string Description { get; }

        public GenerationRepair(string type, string targetId, string description)
        {
            Type = type ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    public sealed class GenerationReport
    {
        private readonly List<string> _checks;
        private readonly List<GenerationRepair> _repairs;

        public static GenerationReport Empty { get; } = new GenerationReport(
            string.Empty, string.Empty, false, 0, 0, 0, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<GenerationRepair>());

        public string Pipeline { get; }
        public string GeneratorVersion { get; }
        public bool IsValid { get; }
        public int Attempts { get; }
        public int ConnectedTileCount { get; }
        public int RouteCount { get; }
        public int LoopRouteCount { get; }
        public int RepairedIssueCount { get; }
        public int CriticalIssueCount { get; }
        public IReadOnlyList<string> Checks => _checks;
        public IReadOnlyList<GenerationRepair> Repairs => _repairs;

        public GenerationReport(string pipeline, string generatorVersion, bool isValid, int attempts,
            int connectedTileCount, int routeCount, int loopRouteCount, int repairedIssueCount,
            int criticalIssueCount, IEnumerable<string> checks, IEnumerable<GenerationRepair> repairs)
        {
            Pipeline = pipeline ?? string.Empty;
            GeneratorVersion = generatorVersion ?? string.Empty;
            IsValid = isValid;
            Attempts = Math.Max(0, attempts);
            ConnectedTileCount = Math.Max(0, connectedTileCount);
            RouteCount = Math.Max(0, routeCount);
            LoopRouteCount = Math.Max(0, loopRouteCount);
            RepairedIssueCount = Math.Max(0, repairedIssueCount);
            CriticalIssueCount = Math.Max(0, criticalIssueCount);
            _checks = new List<string>(checks ?? Array.Empty<string>());
            _repairs = new List<GenerationRepair>(repairs ?? Array.Empty<GenerationRepair>());
        }
    }

    public sealed class RegionMap
    {
        private readonly BaseTile[] _tiles;
        private readonly List<PlacementSlot> _placementSlots;
        private readonly List<WorldArea> _areas;
        private readonly List<BiomeDescriptor> _biomes;
        private readonly List<WorldRoute> _routes;

        public Guid RegionId { get; }
        public string RegionKey { get; }
        public long RegionSeed { get; }
        public string GeneratorVersion { get; }
        public int Width { get; }
        public int Height { get; }
        public GridCoord Start { get; }
        public GridCoord Exit { get; }
        public string LayoutHash { get; }
        public IReadOnlyList<PlacementSlot> PlacementSlots => _placementSlots;
        public IReadOnlyList<WorldArea> Areas => _areas;
        public IReadOnlyList<BiomeDescriptor> Biomes => _biomes;
        public IReadOnlyList<WorldRoute> Routes => _routes;
        public ProgressionGraph Progression { get; }
        public GenerationReport GenerationReport { get; }
        public string StartAreaId { get; }
        public string RootAreaId { get; }

        public RegionMap(
            Guid regionId,
            string regionKey,
            long regionSeed,
            string generatorVersion,
            int width,
            int height,
            GridCoord start,
            GridCoord exit,
            BaseTile[] tiles,
            List<PlacementSlot> placementSlots,
            string layoutHash)
            : this(regionId, regionKey, regionSeed, generatorVersion, width, height, start, exit, tiles,
                placementSlots, new List<WorldArea>(), new List<BiomeDescriptor>(), string.Empty, string.Empty,
                layoutHash, new List<WorldRoute>(), ProgressionGraph.Empty, GenerationReport.Empty)
        {
        }

        public RegionMap(
            Guid regionId,
            string regionKey,
            long regionSeed,
            string generatorVersion,
            int width,
            int height,
            GridCoord start,
            GridCoord exit,
            BaseTile[] tiles,
            List<PlacementSlot> placementSlots,
            List<WorldArea> areas,
            string layoutHash)
            : this(regionId, regionKey, regionSeed, generatorVersion, width, height, start, exit, tiles,
                placementSlots, areas, new List<BiomeDescriptor>(), string.Empty, string.Empty, layoutHash)
        {
        }

        public RegionMap(
            Guid regionId,
            string regionKey,
            long regionSeed,
            string generatorVersion,
            int width,
            int height,
            GridCoord start,
            GridCoord exit,
            BaseTile[] tiles,
            List<PlacementSlot> placementSlots,
            List<WorldArea> areas,
            List<BiomeDescriptor> biomes,
            string startAreaId,
            string rootAreaId,
            string layoutHash)
            : this(regionId, regionKey, regionSeed, generatorVersion, width, height, start, exit, tiles,
                placementSlots, areas, biomes, startAreaId, rootAreaId, layoutHash,
                new List<WorldRoute>(), ProgressionGraph.Empty, GenerationReport.Empty)
        {
        }

        public RegionMap(
            Guid regionId,
            string regionKey,
            long regionSeed,
            string generatorVersion,
            int width,
            int height,
            GridCoord start,
            GridCoord exit,
            BaseTile[] tiles,
            List<PlacementSlot> placementSlots,
            List<WorldArea> areas,
            List<BiomeDescriptor> biomes,
            string startAreaId,
            string rootAreaId,
            string layoutHash,
            List<WorldRoute> routes,
            ProgressionGraph progression,
            GenerationReport generationReport)
        {
            if (tiles == null || tiles.Length != checked(width * height))
                throw new ArgumentException("Tile array size does not match region dimensions.", nameof(tiles));

            RegionId = regionId;
            RegionKey = regionKey;
            RegionSeed = regionSeed;
            GeneratorVersion = generatorVersion;
            Width = width;
            Height = height;
            Start = start;
            Exit = exit;
            _tiles = (BaseTile[])tiles.Clone();
            _placementSlots = placementSlots == null
                ? new List<PlacementSlot>()
                : new List<PlacementSlot>(placementSlots);
            _areas = areas == null ? new List<WorldArea>() : new List<WorldArea>(areas);
            _biomes = biomes == null ? new List<BiomeDescriptor>() : new List<BiomeDescriptor>(biomes);
            _routes = routes == null ? new List<WorldRoute>() : new List<WorldRoute>(routes);
            StartAreaId = startAreaId ?? string.Empty;
            RootAreaId = rootAreaId ?? string.Empty;
            LayoutHash = layoutHash;
            Progression = progression ?? ProgressionGraph.Empty;
            GenerationReport = generationReport ?? GenerationReport.Empty;
        }

        public bool Contains(GridCoord coord)
        {
            return coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Height;
        }

        public int ToIndex(GridCoord coord)
        {
            if (!Contains(coord))
                throw new ArgumentOutOfRangeException(nameof(coord), "Coordinate is outside this region.");

            return checked(coord.Y * Width + coord.X);
        }

        public BaseTile GetTile(GridCoord coord)
        {
            return _tiles[ToIndex(coord)];
        }

        public bool IsWalkable(GridCoord coord)
        {
            return Contains(coord) && GetTile(coord).IsWalkable;
        }

        public int MovementCost(GridCoord coord)
        {
            return IsWalkable(coord) ? Math.Max(1, (int)GetTile(coord).MovementCost) : int.MaxValue;
        }

        public BaseTile[] CopyTiles()
        {
            return (BaseTile[])_tiles.Clone();
        }

        public WorldArea AreaAt(GridCoord coord)
        {
            for (int i = 0; i < _areas.Count; i++)
            {
                if (_areas[i].Contains(coord))
                    return _areas[i];
            }
            return null;
        }

        public bool TryGetPlacementSlot(string slotId, out PlacementSlot slot)
        {
            for (int i = 0; i < _placementSlots.Count; i++)
            {
                if (string.Equals(_placementSlots[i].Id, slotId, StringComparison.Ordinal))
                {
                    slot = _placementSlots[i];
                    return true;
                }
            }
            slot = null;
            return false;
        }
    }
}
