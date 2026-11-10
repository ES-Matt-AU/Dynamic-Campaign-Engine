using System.Collections.Generic;
using System.Linq;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// Periodic mission generators, one per mission type and side (EECH ai/highlevl/highlevl.c).
    /// Each rates candidate targets from the sector maps, keeps the best few, and creates missions,
    /// or a Recon mission first if the side does not know enough about the target area.
    /// </summary>
    public static class TaskGenerator
    {
        private class Candidate
        {
            public TaskTargetKind Kind;
            public int Id;
            public Vec2 Position;
            public string Name;
            public double Rating;
        }

        public static void Run(CampaignState state)
        {
            var s = state.Settings;
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                RunIfDue(state, side, TaskType.Strike, s.StrikeGenerationMinutes, () => GenerateStrike(state, side));
                RunIfDue(state, side, TaskType.Sead, s.SeadGenerationMinutes, () => GenerateSead(state, side));
                RunIfDue(state, side, TaskType.Bai, s.BaiGenerationMinutes, () => GenerateBai(state, side));
                RunIfDue(state, side, TaskType.Cap, s.CapGenerationMinutes, () => GenerateCap(state, side));
                RunIfDue(state, side, TaskType.Capture, s.CaptureGenerationMinutes, () => GenerateCapture(state, side));
            }
        }

        private static void RunIfDue(CampaignState state, Side side, TaskType type, double periodMinutes, System.Action generate)
        {
            int slot = (int)side * TaskTypes.Count + (int)type;
            if (state.Time < state.NextGenerationAt[slot])
                return;
            state.NextGenerationAt[slot] = state.Time + periodMinutes * 60;
            generate();
        }

        private static void GenerateStrike(CampaignState state, Side side)
        {
            var enemy = side.Opponent();
            var grid = state.Grid;
            var candidates = new List<Candidate>();

            foreach (var site in state.Sites)
            {
                if (site.Side != enemy || site.State == SiteState.Unusable)
                    continue;
                int index = grid.IndexOf(site.Position);
                double airDefence = grid.AirDefence[grid.Slot(enemy, index)];
                double cover = grid.AirCover[grid.Slot(side, index)];
                double ratio = grid.OwnershipRatio(side, index);
                // EECH: 1·(1 - AD) + 4·baseDist + 2·(1 - efficiency) + 2·sideRatio. Importance added so a
                // power station does not rate the same as an airbase.
                double rating = (1 - airDefence) + 4 * cover + 2 * (1 - site.Efficiency) + 2 * ratio + 2 * site.Info.Importance;
                candidates.Add(new Candidate { Kind = TaskTargetKind.Site, Id = site.Id, Position = site.Position, Name = site.Name, Rating = rating });
            }

            CreateFromCandidates(state, side, TaskType.Strike, candidates, state.Settings.StrikeTopN, 11);
        }

        private static void GenerateSead(CampaignState state, Side side)
        {
            var enemy = side.Opponent();
            var grid = state.Grid;
            var candidates = new List<Candidate>();

            foreach (var group in state.Groups)
            {
                if (group.Side != enemy || !group.IsAlive || !group.Info.IsAirDefence)
                    continue;
                int index = grid.IndexOf(group.Position);
                double rating = 4 * grid.AirCover[grid.Slot(side, index)] + 3 * grid.OwnershipRatio(side, index);
                if (group.Type == GroupType.AaaSite)
                    rating *= 0.6;
                candidates.Add(new Candidate { Kind = TaskTargetKind.Group, Id = group.Id, Position = group.Position, Name = group.Name, Rating = rating });
            }

            CreateFromCandidates(state, side, TaskType.Sead, candidates, state.Settings.SeadTopN, 7);
        }

        private static void GenerateBai(CampaignState state, Side side)
        {
            var enemy = side.Opponent();
            var grid = state.Grid;
            var candidates = new List<Candidate>();

            foreach (var group in state.Groups)
            {
                if (group.Side != enemy || !group.IsAlive || !group.Info.IsGroundCombat)
                    continue;
                int index = grid.IndexOf(group.Position);
                double rating = 2 * (1 - grid.AirDefence[grid.Slot(enemy, index)]) + 3 * grid.AirCover[grid.Slot(side, index)] + 2 * grid.OwnershipRatio(side, index);
                candidates.Add(new Candidate { Kind = TaskTargetKind.Group, Id = group.Id, Position = group.Position, Name = group.Name, Rating = rating });
            }

            CreateFromCandidates(state, side, TaskType.Bai, candidates, state.Settings.BaiTopN, 7);
        }

        /// <summary>
        /// Troop insertions against battered enemy sites (EECH's troop insertion generator). Only sites that
        /// are out of action, being repaired, or close to it are worth trying.
        /// </summary>
        private static void GenerateCapture(CampaignState state, Side side)
        {
            if (state.Force(side).TroopReserve <= 0)
                return;

            var enemy = side.Opponent();
            var grid = state.Grid;
            double damagedBelow = state.Settings.MinEfficiency + 0.2;
            var candidates = new List<Candidate>();

            foreach (var site in state.Sites)
            {
                if (site.Side != enemy || site.Efficiency >= damagedBelow || !SiteCapture.WorthTrying(state, side, site))
                    continue;
                int index = grid.IndexOf(site.Position);
                double rating = 3 * grid.AirCover[grid.Slot(side, index)] + 2 * grid.OwnershipRatio(side, index)
                                + 2 * (1 - site.Efficiency) + 2 * site.Info.Importance;
                candidates.Add(new Candidate { Kind = TaskTargetKind.Site, Id = site.Id, Position = site.Position, Name = site.Name, Rating = rating });
            }

            CreateFromCandidates(state, side, TaskType.Capture, candidates, state.Settings.CaptureTopN, 9);
        }

        /// <summary>One CAP per cycle over the most important own site within reach of enemy aircraft.</summary>
        private static void GenerateCap(CampaignState state, Side side)
        {
            var enemy = side.Opponent();
            var grid = state.Grid;
            var enemyBases = state.Sites.Where(s => s.Side == enemy && s.Info.HostsAircraft).ToList();
            if (enemyBases.Count == 0)
                return;

            var best = state.Sites
                .Where(s => s.Side == side)
                .Select(s => new { Site = s, Rating = s.Info.Importance * (0.3 + grid.Get(grid.AirCover, enemy, s.Position)) })
                .OrderByDescending(x => x.Rating)
                .FirstOrDefault(x => !TaskFactory.HasCapNear(state, side, CapPosition(x.Site, enemyBases), 25000));
            if (best == null)
                return;

            var pos = CapPosition(best.Site, enemyBases);
            TaskFactory.Create(state, side, TaskType.Cap, TaskTargetKind.Area, best.Site.Id, pos, $"Patrol over {best.Site.Name}",
                best.Rating / 1.3, false, "Routine patrol.");
        }

        /// <summary>A point 10 km from the site towards the nearest enemy air base.</summary>
        public static Vec2 CapPosition(KeySite site, List<KeySite> enemyBases)
        {
            var nearest = enemyBases.OrderBy(b => Vec2.Distance(b.Position, site.Position)).First();
            return site.Position + (nearest.Position - site.Position).Normalised() * 10000;
        }

        private static void CreateFromCandidates(CampaignState state, Side side, TaskType type, List<Candidate> candidates, int topN, double maxRating)
        {
            if (candidates.Count == 0)
                return;
            candidates.Sort((a, b) => b.Rating.CompareTo(a.Rating));
            double best = candidates[0].Rating;
            var info = TaskTypes.Get(type);
            int created = 0;

            foreach (var c in candidates)
            {
                if (created >= topN || c.Rating < best * state.Settings.MinTaskCreationRatio)
                    break;
                if (TaskFactory.IsTargeted(state, side, type, c.Kind, c.Id))
                    continue;

                // Not enough intel on the target area: look before attacking.
                var actual = Intel.Knowledge(state, side, c.Position) < info.IntelThreshold ? TaskType.Recon : type;
                if (TaskFactory.SectorHasTask(state, side, actual, c.Position))
                    continue;

                string reason = actual == TaskType.Recon ? $"Recon needed before a {info.Code} mission." : "Generated.";
                if (TaskFactory.Create(state, side, actual, c.Kind, c.Id, c.Position, c.Name, c.Rating / maxRating, false, reason) != null)
                    created++;
            }
        }

        /// <summary>Expires missions that ran out of time or whose target is gone; drops old finished ones.</summary>
        public static void CleanUp(CampaignState state)
        {
            foreach (var task in state.Tasks)
            {
                if (task.State != TaskState.Available)
                    continue;

                if (state.Time >= task.ExpiresAt)
                    Expire(state, task, "time ran out");
                else if (!TaskFactory.TargetStillValid(state, task))
                    Expire(state, task, "target no longer valid");
                else if (task.Type == TaskType.Escort)
                    task.OriginSiteId = state.Task(task.EscortedTaskId).OriginSiteId;
                else
                {
                    var origin = state.Site(task.OriginSiteId);
                    if (origin == null || !origin.IsUsable || origin.Side != task.Side)
                    {
                        var newOrigin = TaskFactory.FindOriginSite(state, task.Side, task.Type, task.TargetPosition);
                        if (newOrigin == null)
                            Expire(state, task, "no base available");
                        else
                            task.OriginSiteId = newOrigin.Id;
                    }
                }
            }

            state.Tasks.RemoveAll(t => !t.IsActive && state.Time - t.FinishedAt > 2 * 3600);
        }

        private static void Expire(CampaignState state, CampaignTask task, string why)
        {
            task.State = TaskState.Expired;
            task.FinishedAt = state.Time;
            state.AddLog($"{task.Side} {task.Info.Code} mission against {task.TargetName} withdrawn ({why}).", 0, task.Side);
        }
    }
}
