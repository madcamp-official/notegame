using System;
using System.Collections.Generic;

namespace KeyboardWanderer.Core
{
    public enum TileKind : byte
    {
        Floor = 0,
        Wall = 1,
        Hazard = 2
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

        public bool IsWalkable => Kind != TileKind.Wall;
    }

    public sealed class PlacementSlot
    {
        public string Id { get; }
        public string Type { get; }
        public GridCoord Coord { get; }

        public PlacementSlot(string id, string type, GridCoord coord)
        {
            Id = id;
            Type = type;
            Coord = coord;
        }
    }

    public sealed class RegionMap
    {
        private readonly BaseTile[] _tiles;
        private readonly List<PlacementSlot> _placementSlots;

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
            LayoutHash = layoutHash;
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
    }
}
