using System;
using CampaignEngine.Core;
using Newtonsoft.Json;

namespace CampaignEngine.World
{
    /// <summary>
    /// The map divided into square sectors, each holding per-side values (the "heat maps").
    /// Arrays are flattened as [side * Count + index]. Derived maps are rebuilt rather than saved.
    /// </summary>
    public class SectorGrid
    {
        public int Width;
        public int Height;
        public double SectorSize;

        /// <summary>Seconds of intelligence freshness left, per side and sector (fog of war).</summary>
        public double[] Intel;

        /// <summary>Time until which each side's defences in a sector are on alert.</summary>
        public double[] AlertUntil;

        /// <summary>Raw ownership pull from each side's key sites.</summary>
        [JsonIgnore] public double[] Ownership;

        /// <summary>0 to 1: how valuable the area is to that side.</summary>
        [JsonIgnore] public double[] Importance;

        /// <summary>0 to 1: how close that side's air bases are (1 = on top of one).</summary>
        [JsonIgnore] public double[] AirCover;

        /// <summary>0 to 1: strength of that side's air defences.</summary>
        [JsonIgnore] public double[] AirDefence;

        public SectorGrid()
        {
        }

        public SectorGrid(int width, int height, double sectorSize)
        {
            Width = width;
            Height = height;
            SectorSize = sectorSize;
            EnsureArrays();
        }

        [JsonIgnore] public int Count => Width * Height;

        public void EnsureArrays()
        {
            int n = Count * 2;
            if (Intel == null || Intel.Length != n) Intel = new double[n];
            if (AlertUntil == null || AlertUntil.Length != n) AlertUntil = new double[n];
            if (Ownership == null || Ownership.Length != n) Ownership = new double[n];
            if (Importance == null || Importance.Length != n) Importance = new double[n];
            if (AirCover == null || AirCover.Length != n) AirCover = new double[n];
            if (AirDefence == null || AirDefence.Length != n) AirDefence = new double[n];
        }

        public int Index(int x, int z) => z * Width + x;

        public int Slot(Side side, int index) => (int)side * Count + index;

        public int IndexOf(Vec2 pos)
        {
            int x = MathUtil.Clamp((int)Math.Floor(pos.X / SectorSize), 0, Width - 1);
            int z = MathUtil.Clamp((int)Math.Floor(pos.Z / SectorSize), 0, Height - 1);
            return Index(x, z);
        }

        public Vec2 Centre(int index) => new Vec2((index % Width + 0.5) * SectorSize, (index / Width + 0.5) * SectorSize);

        /// <summary>0 to 1: share of the sector's ownership pull that belongs to this side.</summary>
        public double OwnershipRatio(Side side, int index)
        {
            double own = Ownership[Slot(side, index)];
            double other = Ownership[Slot(side.Opponent(), index)];
            double total = own + other;
            return total <= 0 ? 0.5 : own / total;
        }

        public Side Owner(int index) => OwnershipRatio(Side.Allied, index) >= 0.5 ? Side.Allied : Side.Enemy;

        public double Get(double[] map, Side side, Vec2 pos) => map[Slot(side, IndexOf(pos))];

        /// <summary>Calls action for every sector whose centre lies within radius of pos.</summary>
        public void ForEachWithin(Vec2 pos, double radius, Action<int> action)
        {
            int x0 = MathUtil.Clamp((int)Math.Floor((pos.X - radius) / SectorSize), 0, Width - 1);
            int x1 = MathUtil.Clamp((int)Math.Floor((pos.X + radius) / SectorSize), 0, Width - 1);
            int z0 = MathUtil.Clamp((int)Math.Floor((pos.Z - radius) / SectorSize), 0, Height - 1);
            int z1 = MathUtil.Clamp((int)Math.Floor((pos.Z + radius) / SectorSize), 0, Height - 1);
            double r2 = radius * radius;
            int own = IndexOf(pos);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int index = Index(x, z);
                    var c = Centre(index);
                    double dx = c.X - pos.X, dz = c.Z - pos.Z;
                    if (dx * dx + dz * dz <= r2 || index == own)
                        action(index);
                }
            }
        }
    }
}
