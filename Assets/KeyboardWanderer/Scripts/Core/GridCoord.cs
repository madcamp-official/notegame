using System;

namespace KeyboardWanderer.Core
{
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public int X { get; }
        public int Y { get; }

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int ManhattanDistance(GridCoord other)
        {
            return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        }

        public long Pack()
        {
            return ((long)X << 32) | (uint)Y;
        }

        public static GridCoord Unpack(long value)
        {
            return new GridCoord((int)(value >> 32), unchecked((int)(uint)value));
        }

        public bool Equals(GridCoord other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }

        public static bool operator ==(GridCoord left, GridCoord right) => left.Equals(right);
        public static bool operator !=(GridCoord left, GridCoord right) => !left.Equals(right);
    }
}
