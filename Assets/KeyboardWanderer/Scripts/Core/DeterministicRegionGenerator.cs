using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace KeyboardWanderer.Core
{
    public static class DeterministicRegionGenerator
    {
        public const string CurrentVersion = "map.v1";

        public static RegionMap Generate(long worldSeed, string regionKey, int width, int height)
        {
            if (width < 7 || height < 7)
                throw new ArgumentOutOfRangeException(nameof(width), "Regions must be at least 7x7.");

            long regionSeed = DeriveSeed(worldSeed, regionKey, CurrentVersion);
            Guid regionId = DeterministicGuid.Create("region:" + worldSeed + ":" + regionKey + ":" + CurrentVersion);
            var random = new StableRandom(unchecked((uint)regionSeed));
            var tiles = new BaseTile[checked(width * height)];
            var start = new GridCoord(1, 1);
            var exit = new GridCoord(width - 2, height - 2);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    bool obstacle = !border && random.Next(100) < 16;
                    TileKind kind = border || obstacle ? TileKind.Wall : TileKind.Floor;
                    tiles[y * width + x] = new BaseTile(kind, 1);
                }
            }

            // A deterministic guaranteed corridor keeps every generated region playable.
            for (int x = start.X; x <= exit.X; x++)
                tiles[start.Y * width + x] = new BaseTile(TileKind.Floor, 1);
            for (int y = start.Y; y <= exit.Y; y++)
                tiles[y * width + exit.X] = new BaseTile(TileKind.Floor, 1);

            // Add a few passable high-cost cells away from entry/exit.
            for (int i = 0; i < Math.Max(1, width * height / 28); i++)
            {
                int x = 1 + random.Next(width - 2);
                int y = 1 + random.Next(height - 2);
                var coord = new GridCoord(x, y);
                if (coord != start && coord != exit && tiles[y * width + x].IsWalkable)
                    tiles[y * width + x] = new BaseTile(TileKind.Hazard, 3);
            }

            var slots = new List<PlacementSlot>
            {
                new PlacementSlot("slot-player-entry", "entry", start),
                new PlacementSlot("slot-exit", "exit", exit),
                new PlacementSlot("slot-prop-1", "prop", FindWalkable(tiles, width, height, new GridCoord(3, 2))),
                new PlacementSlot("slot-prop-2", "prop", FindWalkable(tiles, width, height, new GridCoord(width - 4, 2))),
                new PlacementSlot("slot-npc-1", "npc", FindWalkable(tiles, width, height, new GridCoord(width / 2, height / 2)))
            };

            string layoutHash = ComputeLayoutHash(regionSeed, regionKey, width, height, tiles, slots);
            return new RegionMap(regionId, regionKey, regionSeed, CurrentVersion, width, height, start, exit, tiles, slots, layoutHash);
        }

        public static long DeriveSeed(long worldSeed, string regionKey, string generatorVersion)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(worldSeed + "|" + regionKey + "|" + generatorVersion);
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToInt64(hash, 0);
            }
        }

        private static GridCoord FindWalkable(BaseTile[] tiles, int width, int height, GridCoord preferred)
        {
            if (preferred.X > 0 && preferred.Y > 0 && preferred.X < width - 1 && preferred.Y < height - 1 &&
                tiles[preferred.Y * width + preferred.X].IsWalkable)
                return preferred;

            for (int radius = 1; radius < Math.Max(width, height); radius++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        var candidate = new GridCoord(x, y);
                        if (candidate.ManhattanDistance(preferred) == radius && tiles[y * width + x].IsWalkable)
                            return candidate;
                    }
                }
            }

            throw new InvalidOperationException("Generated region has no walkable placement slot.");
        }

        private static string ComputeLayoutHash(
            long seed,
            string key,
            int width,
            int height,
            BaseTile[] tiles,
            List<PlacementSlot> slots)
        {
            var builder = new StringBuilder();
            builder.Append(CurrentVersion).Append('|').Append(seed).Append('|').Append(key)
                .Append('|').Append(width).Append('x').Append(height).Append('|');
            for (int i = 0; i < tiles.Length; i++)
                builder.Append((byte)tiles[i].Kind).Append(':').Append(tiles[i].MovementCost).Append(';');
            for (int i = 0; i < slots.Count; i++)
                builder.Append(slots[i].Id).Append('@').Append(slots[i].Coord.Pack()).Append(';');

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
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

        public StableRandom(uint seed)
        {
            _state = seed == 0 ? 0x6d2b79f5u : seed;
        }

        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMax);
        }
    }
}
