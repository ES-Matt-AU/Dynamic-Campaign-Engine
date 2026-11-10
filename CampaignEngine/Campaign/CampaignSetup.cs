using System;
using System.Collections.Generic;
using System.Linq;
using CampaignEngine.Ai;
using CampaignEngine.Core;
using CampaignEngine.Map;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Campaign
{
    /// <summary>
    /// Builds the opening position of a war on a VTOL VR map. Airbases come from the map file;
    /// the southern half go to the Allied side and the northern half to the Enemy. Other key sites
    /// and units are placed in each side's territory using the campaign's dice, so the same seed
    /// always gives the same war.
    /// </summary>
    public static class CampaignSetup
    {
        private const double Km = 1000;

        private static readonly string[] AlliedAirbaseNames = { "Granite", "Kestrel", "Bastion", "Harbour", "Longmoor", "Redcliff" };
        private static readonly string[] EnemyAirbaseNames = { "Volkov", "Zarya", "Sokol", "Krasny", "Belaya", "Ozerny" };

        private static readonly Dictionary<SiteType, string[]> AlliedSiteNames = new Dictionary<SiteType, string[]>
        {
            { SiteType.Farp, new[] { "Falcon FARP", "Raven FARP", "Merlin FARP" } },
            { SiteType.Factory, new[] { "Ironbridge Works" } },
            { SiteType.MilitaryBase, new[] { "Camp Halberd" } },
            { SiteType.PowerStation, new[] { "Westfield Power Station" } },
            { SiteType.Refinery, new[] { "Ashdown Refinery" } },
            { SiteType.RadarStation, new[] { "Skywatch Radar" } },
        };

        private static readonly Dictionary<SiteType, string[]> EnemySiteNames = new Dictionary<SiteType, string[]>
        {
            { SiteType.Farp, new[] { "Berkut FARP", "Grach FARP", "Sova FARP" } },
            { SiteType.Factory, new[] { "Tractor Works No. 4" } },
            { SiteType.MilitaryBase, new[] { "Garrison Yarovoy" } },
            { SiteType.PowerStation, new[] { "Dneprova Power Station" } },
            { SiteType.Refinery, new[] { "Neftegorsk Refinery" } },
            { SiteType.RadarStation, new[] { "Kupol Radar" } },
        };

        private static readonly Dictionary<GroupType, string> AlliedCallsigns = new Dictionary<GroupType, string>
        {
            { GroupType.FighterFlight, "Eagle" }, { GroupType.StrikeFlight, "Hammer" }, { GroupType.SeadFlight, "Weasel" },
            { GroupType.ReconFlight, "Shadow" }, { GroupType.AttackHelicopterFlight, "Saber" }, { GroupType.TransportFlight, "Lift" }
        };

        private static readonly Dictionary<GroupType, string> EnemyCallsigns = new Dictionary<GroupType, string>
        {
            { GroupType.FighterFlight, "Molniya" }, { GroupType.StrikeFlight, "Grom" }, { GroupType.SeadFlight, "Shtorm" },
            { GroupType.ReconFlight, "Orel" }, { GroupType.AttackHelicopterFlight, "Kobra" }, { GroupType.TransportFlight, "Aist" }
        };

        public static CampaignState Create(MapInfo map, int seed)
        {
            if (map.Airbases.Count < 2)
                throw new InvalidOperationException($"Map {map.MapId} has {map.Airbases.Count} airbase(s); a campaign needs at least two.");

            var state = new CampaignState
            {
                Map = map,
                Dice = new Dice(seed),
                Grid = new SectorGrid(map.SizeChunks, map.SizeChunks, MapInfo.ChunkSizeMetres)
            };
            var settings = state.Settings;
            var callsignCounters = new Dictionary<string, int>();

            // Airbases from the map
            var ordered = map.Airbases.OrderBy(a => a.Position.Z).ToList();
            int alliedCount = ordered.Count / 2;
            for (int i = 0; i < ordered.Count; i++)
            {
                var side = i < alliedCount ? Side.Allied : Side.Enemy;
                var names = side == Side.Allied ? AlliedAirbaseNames : EnemyAirbaseNames;
                int n = side == Side.Allied ? i : i - alliedCount;
                var site = AddSite(state, SiteType.Airbase, side, ordered[i].Position, $"{names[n % names.Length]} Airbase");
                site.MapPrefabId = ordered[i].PrefabId;
            }
            InfluenceMaps.Recalculate(state);

            // Other key sites, placed by how firmly each side holds the ground
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var names = side == Side.Allied ? AlliedSiteNames : EnemySiteNames;
                PlaceSite(state, side, SiteType.Factory, names[SiteType.Factory][0], 0.80, 1.00);
                PlaceSite(state, side, SiteType.PowerStation, names[SiteType.PowerStation][0], 0.75, 1.00);
                PlaceSite(state, side, SiteType.Refinery, names[SiteType.Refinery][0], 0.75, 1.00);
                PlaceSite(state, side, SiteType.MilitaryBase, names[SiteType.MilitaryBase][0], 0.62, 0.85);
                PlaceSite(state, side, SiteType.RadarStation, names[SiteType.RadarStation][0], 0.60, 0.80);
                PlaceSite(state, side, SiteType.Farp, names[SiteType.Farp][0], 0.53, 0.65);
                PlaceSite(state, side, SiteType.Farp, names[SiteType.Farp][1], 0.53, 0.65);
            }
            InfluenceMaps.Recalculate(state);

            // Units at each site
            foreach (var site in state.Sites.ToList())
            {
                var callsigns = site.Side == Side.Allied ? AlliedCallsigns : EnemyCallsigns;
                string Flight(GroupType t) => NextCallsign(callsigns[t], callsignCounters);

                if (site.Type == SiteType.Airbase)
                {
                    AddGroup(state, GroupType.FighterFlight, site.Side, site.Position, Flight(GroupType.FighterFlight), site.Id);
                    AddGroup(state, GroupType.FighterFlight, site.Side, site.Position, Flight(GroupType.FighterFlight), site.Id);
                    AddGroup(state, GroupType.StrikeFlight, site.Side, site.Position, Flight(GroupType.StrikeFlight), site.Id);
                    AddGroup(state, GroupType.SeadFlight, site.Side, site.Position, Flight(GroupType.SeadFlight), site.Id);
                    AddGroup(state, GroupType.ReconFlight, site.Side, site.Position, Flight(GroupType.ReconFlight), site.Id);
                    AddGroup(state, GroupType.TransportFlight, site.Side, site.Position, Flight(GroupType.TransportFlight), site.Id);
                    AddGroup(state, GroupType.SamSite, site.Side, Offset(state, site.Position, 3 * Km), $"{site.Name} SAM");
                }
                else if (site.Type == SiteType.Farp)
                {
                    AddGroup(state, GroupType.AttackHelicopterFlight, site.Side, site.Position, Flight(GroupType.AttackHelicopterFlight), site.Id);
                }

                AddGroup(state, GroupType.AaaSite, site.Side, Offset(state, site.Position, 0.5 * Km), $"{site.Name} AAA");

                // Every site starts with an infantry garrison, as EECH's key sites had defending troops.
                SiteCapture.Garrison(state, site, site.Info.TroopsToCapture);
            }

            PlaceFrontLineForces(state);

            // Reserves and objectives
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var force = state.Force(side);
                force.AircraftReserve = settings.StartingAircraftReserve;
                force.GroundReserve = settings.StartingGroundReserve;
                force.TroopReserve = settings.StartingTroopReserve;
                force.ObjectiveSiteIds = state.Sites
                    .Where(s => s.Side == side.Opponent())
                    .OrderByDescending(s => s.Info.Importance)
                    .Take(3)
                    .Select(s => s.Id)
                    .ToList();
            }

            // The player's flight (player plus an AI wingman), based at the first Allied airbase
            var home = state.Sites.First(s => s.Side == Side.Allied && s.Type == SiteType.Airbase);
            state.PlayerGroupId = AddGroup(state, GroupType.PlayerFlight, Side.Allied, home.Position, "Your flight", home.Id).Id;

            // Stagger the first runs of each generator, as EECH does
            for (int side = 0; side < 2; side++)
            {
                state.NextGenerationAt[side * TaskTypes.Count + (int)TaskType.Cap] = 60 + side * 30;
                state.NextGenerationAt[side * TaskTypes.Count + (int)TaskType.Strike] = 120 + side * 30;
                state.NextGenerationAt[side * TaskTypes.Count + (int)TaskType.Sead] = 240 + side * 30;
                state.NextGenerationAt[side * TaskTypes.Count + (int)TaskType.Bai] = 360 + side * 30;
                state.NextGenerationAt[side * TaskTypes.Count + (int)TaskType.Capture] = 480 + side * 30;
            }
            state.NextAssignAt = 5 * 60;
            state.NextRegenAirAt = settings.RegenAirMinutes * 60;
            state.NextRegenGroundAt = settings.RegenGroundMinutes * 60;

            Intel.Update(state, 0);
            state.AddLog($"The campaign begins on {map.MapId}.", 2);
            return state;
        }

        private static KeySite AddSite(CampaignState state, SiteType type, Side side, Vec2 pos, string name)
        {
            var info = SiteTypes.Get(type);
            var site = new KeySite
            {
                Id = state.NewId(),
                Name = name,
                Type = type,
                Side = side,
                Position = pos,
                Structures = info.Structures,
                MaxStructures = info.Structures
            };
            state.Sites.Add(site);
            return site;
        }

        internal static UnitGroup AddGroup(CampaignState state, GroupType type, Side side, Vec2 pos, string name, int homeSiteId = -1)
        {
            var info = GroupTypes.Get(type);
            var group = new UnitGroup
            {
                Id = state.NewId(),
                Name = name,
                Type = type,
                Side = side,
                Position = pos,
                HomeSiteId = homeSiteId,
                Members = info.DefaultMembers,
                MaxMembers = info.DefaultMembers
            };
            state.Groups.Add(group);
            return group;
        }

        /// <summary>
        /// Places a site in a sector the side holds with an ownership share between minRatio and maxRatio,
        /// away from other sites. The band is relaxed step by step if nothing fits.
        /// </summary>
        private static void PlaceSite(CampaignState state, Side side, SiteType type, string name, double minRatio, double maxRatio)
        {
            var grid = state.Grid;
            double margin = 10 * Km;
            double size = state.Map.SizeMetres();

            for (int attempt = 0; attempt < 6; attempt++)
            {
                double lo = Math.Max(0.5, minRatio - attempt * 0.05);
                double hi = Math.Min(1.0, maxRatio + attempt * 0.05);
                double spacing = Math.Max(6 * Km, 15 * Km - attempt * 2 * Km);
                var candidates = new List<int>();

                for (int index = 0; index < grid.Count; index++)
                {
                    var c = grid.Centre(index);
                    if (c.X < margin || c.Z < margin || c.X > size - margin || c.Z > size - margin)
                        continue;
                    double ratio = grid.OwnershipRatio(side, index);
                    if (ratio < lo || ratio > hi)
                        continue;
                    if (state.Sites.Any(s => Vec2.Distance(s.Position, c) < spacing))
                        continue;
                    candidates.Add(index);
                }

                if (candidates.Count > 0)
                {
                    var centre = grid.Centre(candidates[state.Dice.Range(0, candidates.Count)]);
                    AddSite(state, type, side, Offset(state, centre, 1 * Km), name);
                    return;
                }
            }
        }

        /// <summary>Ground forces along the boundary between the two sides' territory.</summary>
        private static void PlaceFrontLineForces(CampaignState state)
        {
            var grid = state.Grid;

            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var ownBases = state.Sites.Where(s => s.Side == side && s.Type == SiteType.Airbase).ToList();
                var front = new List<int>();
                for (int index = 0; index < grid.Count; index++)
                {
                    if (grid.Owner(index) != side)
                        continue;
                    int x = index % grid.Width, z = index / grid.Width;
                    bool touchesEnemy =
                        (x > 0 && grid.Owner(grid.Index(x - 1, z)) != side) ||
                        (x < grid.Width - 1 && grid.Owner(grid.Index(x + 1, z)) != side) ||
                        (z > 0 && grid.Owner(grid.Index(x, z - 1)) != side) ||
                        (z < grid.Height - 1 && grid.Owner(grid.Index(x, z + 1)) != side);
                    if (touchesEnemy)
                        front.Add(index);
                }

                // Pick up to four well-spaced front-line points, in random order
                var points = new List<Vec2>();
                while (front.Count > 0 && points.Count < 4)
                {
                    int pick = state.Dice.Range(0, front.Count);
                    var c = grid.Centre(front[pick]);
                    front.RemoveAt(pick);
                    if (c.X < 10 * Km || c.Z < 10 * Km || c.X > state.Map.SizeMetres() - 10 * Km || c.Z > state.Map.SizeMetres() - 10 * Km)
                        continue;
                    if (points.Any(p => Vec2.Distance(p, c) < 15 * Km))
                        continue;
                    points.Add(c);
                }

                Vec2 Back(Vec2 p, double distance)
                {
                    var home = ownBases.OrderBy(b => Vec2.Distance(b.Position, p)).First().Position;
                    return p + (home - p).Normalised() * distance;
                }

                bool allied = side == Side.Allied;
                for (int i = 0; i < points.Count; i++)
                {
                    var pos = Back(points[i], 3 * Km);
                    if (i < 2)
                        AddGroup(state, GroupType.ArmourCompany, side, pos, $"{Ordinal(i + 1)} {(allied ? "Armoured" : "Tank")} Company");
                    else
                        AddGroup(state, GroupType.InfantryCompany, side, pos, $"{Ordinal(i - 1)} {(allied ? "Rifle" : "Motor Rifle")} Company");

                    if (i < 2)
                        AddGroup(state, GroupType.MobileSam, side, Back(points[i], 8 * Km), $"{(allied ? "Allied" : "Enemy")} Mobile SAM {i + 1}");
                }
            }
        }

        private static Vec2 Offset(CampaignState state, Vec2 pos, double distance)
        {
            double angle = state.Dice.NextDouble() * Math.PI * 2;
            return pos + new Vec2(Math.Cos(angle), Math.Sin(angle)) * distance;
        }

        private static string NextCallsign(string callsign, Dictionary<string, int> counters)
        {
            counters.TryGetValue(callsign, out int n);
            counters[callsign] = n + 1;
            return $"{callsign} {n + 1}";
        }

        private static string Ordinal(int n) => n + (n == 1 ? "st" : n == 2 ? "nd" : n == 3 ? "rd" : "th");
    }
}
