using System;
using CampaignEngine.Campaign;
using CampaignEngine.Core;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// Fog of war. Each side has "seconds of fresh intelligence" per sector, which decays over time.
    /// Own territory is always known; sites and ground units report what is near them; recon refreshes the target area.
    /// </summary>
    public static class Intel
    {
        public static void Update(CampaignState state, double dt)
        {
            var grid = state.Grid;
            double max = state.Settings.IntelMaxSeconds;

            for (int i = 0; i < grid.Intel.Length; i++)
                grid.Intel[i] = Math.Max(0, grid.Intel[i] - dt);

            for (int index = 0; index < grid.Count; index++)
            {
                var owner = grid.Owner(index);
                grid.Intel[grid.Slot(owner, index)] = max;
            }

            foreach (var site in state.Sites)
                Reveal(state, site.Side, site.Position, 5000);

            foreach (var group in state.Groups)
                if (group.IsAlive && group.Info.GroundSight > 0)
                    Reveal(state, group.Side, group.Position, group.Info.GroundSight);
        }

        public static void Reveal(CampaignState state, Side side, Vec2 pos, double radius)
        {
            var grid = state.Grid;
            double max = state.Settings.IntelMaxSeconds;
            grid.ForEachWithin(pos, radius, index => grid.Intel[grid.Slot(side, index)] = max);
        }

        /// <summary>0 to 1: how fresh this side's knowledge of the sector at pos is.</summary>
        public static double Knowledge(CampaignState state, Side side, Vec2 pos)
        {
            return state.Grid.Get(state.Grid.Intel, side, pos) / state.Settings.IntelMaxSeconds;
        }

        public static void Alert(CampaignState state, Side side, Vec2 pos)
        {
            var grid = state.Grid;
            double until = state.Time + state.Settings.AlertMinutes * 60;
            grid.ForEachWithin(pos, state.Settings.AlertRadius, index =>
            {
                int slot = grid.Slot(side, index);
                if (grid.AlertUntil[slot] < until)
                    grid.AlertUntil[slot] = until;
            });
        }

        public static bool IsAlerted(CampaignState state, Side side, Vec2 pos)
        {
            return state.Grid.Get(state.Grid.AlertUntil, side, pos) > state.Time;
        }
    }
}
