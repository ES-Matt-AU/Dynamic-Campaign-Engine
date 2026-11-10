using System;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// Rebuilds the per-side sector maps that the task generators read.
    /// Formulas follow EECH (ai/highlevl/imaps.c).
    /// </summary>
    public static class InfluenceMaps
    {
        public static void Recalculate(CampaignState state)
        {
            var grid = state.Grid;
            grid.EnsureArrays();
            Array.Clear(grid.Ownership, 0, grid.Ownership.Length);
            Array.Clear(grid.Importance, 0, grid.Importance.Length);
            Array.Clear(grid.AirCover, 0, grid.AirCover.Length);
            Array.Clear(grid.AirDefence, 0, grid.AirDefence.Length);

            foreach (var site in state.Sites)
            {
                AddOwnership(grid, site);
                AddFalloff(grid, grid.Importance, site.Side, site.Position, site.Info.ImportanceRadius, site.Info.Importance, sum: true);
                if (site.IsUsable && site.Info.AirCoverRadius > 0)
                    AddFalloff(grid, grid.AirCover, site.Side, site.Position, site.Info.AirCoverRadius, 1.0, sum: false);
            }

            foreach (var group in state.Groups)
            {
                if (!group.IsAlive || group.Info.IsAir || group.Info.ThreatRange <= 0)
                    continue;
                double strength = group.Info.Lethality * group.StrengthFraction;
                AddFalloff(grid, grid.AirDefence, group.Side, group.Position, group.Info.ThreatRange * 1.5, strength, sum: true);
            }

            Normalise(grid, grid.Importance);
            Normalise(grid, grid.AirDefence);
        }

        /// <summary>EECH's inverse-square pull: strength / (dx² + dz²), dx = 0.3 × |cells apart| + 1.</summary>
        private static void AddOwnership(SectorGrid grid, KeySite site)
        {
            int siteIndex = grid.IndexOf(site.Position);
            int sx = siteIndex % grid.Width, sz = siteIndex / grid.Width;
            double strength = site.Info.OwnershipStrength;
            int offset = (int)site.Side * grid.Count;

            for (int z = 0; z < grid.Height; z++)
            {
                double dz = 0.3 * Math.Abs(z - sz) + 1;
                for (int x = 0; x < grid.Width; x++)
                {
                    double dx = 0.3 * Math.Abs(x - sx) + 1;
                    grid.Ownership[offset + grid.Index(x, z)] += strength / (dx * dx + dz * dz);
                }
            }
        }

        /// <summary>Adds value × (1 - r²/R²) around pos. Either sums or keeps the maximum.</summary>
        private static void AddFalloff(SectorGrid grid, double[] map, Side side, Vec2 pos, double radius, double value, bool sum)
        {
            if (radius <= 0 || value <= 0)
                return;
            double r2Max = radius * radius;
            int offset = (int)side * grid.Count;

            grid.ForEachWithin(pos, radius, index =>
            {
                var c = grid.Centre(index);
                double dx = c.X - pos.X, dz = c.Z - pos.Z;
                double v = value * Math.Max(0, 1 - (dx * dx + dz * dz) / r2Max);
                int slot = offset + index;
                if (sum)
                    map[slot] += v;
                else if (v > map[slot])
                    map[slot] = v;
            });
        }

        private static void Normalise(SectorGrid grid, double[] map)
        {
            for (int side = 0; side < 2; side++)
            {
                int offset = side * grid.Count;
                double max = 0;
                for (int i = 0; i < grid.Count; i++)
                    max = Math.Max(max, map[offset + i]);
                if (max <= 0)
                    continue;
                for (int i = 0; i < grid.Count; i++)
                    map[offset + i] /= max;
            }
        }
    }
}
