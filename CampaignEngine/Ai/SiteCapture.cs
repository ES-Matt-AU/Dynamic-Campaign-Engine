using System;
using System.Linq;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// Troops taking and holding key sites (EECH entity/mobile/mb_msgs.c and keysite.c capture_keysite).
    /// </summary>
    public static class SiteCapture
    {
        public class CaptureOdds
        {
            public double Chance;
            public double SiteResistance;
            public double Defenders;
            public double Resistance;
            public double TroopFactor;
        }

        /// <summary>
        /// Chance that the troops take the site. If knownBy is given, only defenders that side has fresh
        /// intel on are counted (for briefings).
        /// </summary>
        public static CaptureOdds Odds(CampaignState state, KeySite site, int troops, bool detected, Side? knownBy = null)
        {
            var s = state.Settings;
            var odds = new CaptureOdds
            {
                SiteResistance = MathUtil.Clamp01((site.Efficiency - s.MinEfficiency) / (1 - s.MinEfficiency)),
                Defenders = DefenderStrength(state, site, knownBy)
            };

            odds.Resistance = MathUtil.Clamp01(odds.SiteResistance + s.GarrisonWeight * odds.Defenders / Math.Max(1, troops));
            if (!detected)
                odds.Resistance *= s.CaptureSurpriseFactor;
            odds.TroopFactor = MathUtil.Clamp01((double)troops / site.Info.TroopsToCapture);
            odds.Chance = odds.TroopFactor * (1 - odds.Resistance);
            return odds;
        }

        /// <summary>
        /// Whether the AI thinks a capture is worth attempting: a full load of troops from one transport flight,
        /// against the defenders it knows about, assuming it is not detected.
        /// </summary>
        public static bool WorthTrying(CampaignState state, Side side, KeySite site)
        {
            var transport = GroupTypes.Get(GroupType.TransportFlight);
            int troops = Math.Min(transport.DefaultMembers * transport.TroopCapacity, state.Force(side).TroopReserve);
            return troops > 0 && Odds(state, site, troops, detected: false, knownBy: side).Chance >= state.Settings.MinCaptureChanceForAi;
        }

        /// <summary>The owner's ground units around the site, weighted by how well they can fight off troops.</summary>
        public static double DefenderStrength(CampaignState state, KeySite site, Side? knownBy = null)
        {
            double total = 0;
            foreach (var g in state.Groups)
            {
                if (g.Side != site.Side || !g.IsAlive || g.Info.IsAir)
                    continue;
                if (Vec2.Distance(g.Position, site.Position) > state.Settings.CaptureOverrunRadius)
                    continue;
                if (knownBy.HasValue && Intel.Knowledge(state, knownBy.Value, g.Position) < 0.5)
                    continue;
                double weight = g.Info.IsGroundCombat ? 1.0 : g.Type == GroupType.AaaSite ? 0.5 : 0.3;
                total += g.Members * weight;
            }
            return total;
        }

        /// <summary>
        /// The site changes hands. Follows EECH's capture_keysite: the old owner's units there are overrun,
        /// its aircraft move to another base or are lost, some buildings are put back into use, and the
        /// new owner gains hangar space (as empty flights that reinforcements will fill).
        /// </summary>
        public static void Capture(CampaignState state, KeySite site, Side newSide, int troops)
        {
            var s = state.Settings;
            var oldSide = site.Side;
            site.Side = newSide;

            site.Structures = Math.Min(site.MaxStructures, site.Structures + s.CaptureBuildingsRestored);
            if (site.Structures >= site.MaxStructures)
            {
                site.State = SiteState.Usable;
                site.NextRepairAt = 0;
            }
            else
            {
                site.State = SiteState.Repairing;
                site.NextRepairAt = state.Time + s.RepairStepMinutes * 60;
            }

            // Old owner's ground units at the site are overrun.
            foreach (var g in state.Groups.Where(g => g.Side == oldSide && g.IsAlive && !g.Info.IsAir))
            {
                if (Vec2.Distance(g.Position, site.Position) > s.CaptureOverrunRadius)
                    continue;
                state.Force(oldSide).GroundUnitsLost += g.Members;
                state.Force(newSide).GroundUnitsKilled += g.Members;
                g.Members = 0;
                g.Mode = GroupMode.Destroyed;
                state.AddLog($"{g.Name} ({oldSide}) was overrun at {site.Name}.", 1, oldSide);
            }

            // Old owner's flights based there move to the nearest working base in range, or are lost.
            foreach (var g in state.Groups.Where(g => g.Side == oldSide && g.Info.IsAir && g.HomeSiteId == site.Id).ToList())
            {
                var newHome = state.Sites
                    .Where(b => b.Side == oldSide && b.IsUsable && b.Info.HostsAircraft && b.Id != site.Id)
                    .Where(b => g.IsPlayer || Vec2.Distance(b.Position, site.Position) <= g.Info.CombatRadius)
                    .OrderBy(b => Vec2.Distance(b.Position, site.Position))
                    .FirstOrDefault();

                if (newHome != null)
                {
                    g.HomeSiteId = newHome.Id;
                    g.Position = newHome.Position;
                    state.AddLog($"{g.Name} withdrew from {site.Name} to {newHome.Name}.", 0, oldSide);
                }
                else if (!g.IsPlayer && g.IsAlive)
                {
                    state.Force(oldSide).AircraftLost += g.Members;
                    state.Force(newSide).AircraftKilled += g.Members;
                    state.AddLog($"{g.Name} ({oldSide}) had nowhere to go and was lost at {site.Name}.", 1, oldSide);
                    g.Members = 0;
                    g.Mode = GroupMode.Destroyed;
                }
            }

            // EECH moves hangar capacity to the captor. Here: empty flights that reinforcements will fill.
            if (site.Type == SiteType.Airbase)
            {
                AddEmptyFlight(state, site, GroupType.FighterFlight);
                AddEmptyFlight(state, site, GroupType.StrikeFlight);
                AddEmptyFlight(state, site, GroupType.TransportFlight);
            }
            else if (site.Type == SiteType.Farp)
            {
                AddEmptyFlight(state, site, GroupType.AttackHelicopterFlight);
            }

            Garrison(state, site, troops);
            Intel.Reveal(state, newSide, site.Position, 10000);
            InfluenceMaps.Recalculate(state);
            state.AddLog($"{site.Name} has been CAPTURED by {newSide}.", 2, newSide);
        }

        /// <summary>Adds troops to the site's garrison, creating it if needed.</summary>
        public static void Garrison(CampaignState state, KeySite site, int troops)
        {
            if (troops <= 0)
                return;
            string name = $"{site.Name} garrison";
            var garrison = state.Groups.Find(g => g.Name == name && g.Side == site.Side);
            if (garrison == null)
            {
                garrison = CampaignSetup.AddGroup(state, GroupType.InfantryCompany, site.Side, site.Position, name);
                garrison.Members = 0;
            }

            garrison.Members += troops;
            garrison.MaxMembers = Math.Max(Math.Max(garrison.MaxMembers, garrison.Members), state.Settings.MinGarrisonSize);
            garrison.Mode = GroupMode.Idle;
            garrison.Position = site.Position;
        }

        private static void AddEmptyFlight(CampaignState state, KeySite site, GroupType type)
        {
            var g = CampaignSetup.AddGroup(state, type, site.Side, site.Position, $"{site.Name} {GroupTypes.Get(type).Name.ToLowerInvariant()}", site.Id);
            g.Members = 0;
            g.Mode = GroupMode.Destroyed;
        }
    }
}
