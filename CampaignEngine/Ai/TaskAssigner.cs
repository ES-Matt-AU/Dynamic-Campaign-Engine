using System.Linq;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// Hands waiting missions to idle AI flights at each base (EECH ai/taskgen/assign.c).
    /// AI missions are resolved straight away by the mission simulator.
    /// </summary>
    public static class TaskAssigner
    {
        public static void Run(CampaignState state)
        {
            var s = state.Settings;

            foreach (var site in state.Sites.ToList())
            {
                if (!site.IsUsable || !site.Info.HostsAircraft)
                    continue;

                var waiting = state.Tasks
                    .Where(t => t.State == TaskState.Available && t.OriginSiteId == site.Id && t.Side == site.Side)
                    .Where(t => t.Type != TaskType.Escort) // escorts go out with the flight they protect
                    .OrderByDescending(t => (t.Critical ? 2 : 1) * t.Priority)
                    .ThenBy(t => t.CreatedAt)
                    .ToList();

                int reserve = site.Side == Side.Allied && site.Type == SiteType.Airbase ? s.PlayerReservePerAirbase : 0;
                int limit = site.Type == SiteType.Airbase ? s.AirbaseAssignPerPass : s.FarpAssignPerPass;
                int assigned = 0;

                foreach (var task in waiting)
                {
                    if (assigned >= limit)
                        break;
                    if (task.State != TaskState.Available)
                        continue;
                    if (task.Info.IsTroopMission && state.Force(site.Side).TroopReserve <= 0)
                        continue;

                    // Keep the best few non-urgent missions for the player while they have time to take them.
                    if (reserve > 0 && !task.Critical && task.ExpiresAt - state.Time > 30 * 60)
                    {
                        reserve--;
                        continue;
                    }

                    if (task.Type == TaskType.Capture)
                    {
                        // Look before landing: with stale intel the garrison may have been reinforced unseen.
                        var target = state.Site(task.TargetId);
                        if (Intel.Knowledge(state, task.Side, target.Position) < 0.5)
                        {
                            TaskFactory.Create(state, task.Side, TaskType.Recon, TaskTargetKind.Site, target.Id, target.Position, target.Name,
                                1, true, $"Recon needed before troops land at {target.Name}.");
                            continue;
                        }

                        // Re-check the odds before launching; the picture may have changed.
                        if (!SiteCapture.WorthTrying(state, task.Side, target))
                            continue;
                    }

                    var group = FindGroup(state, site, task);
                    if (group == null)
                        continue;

                    // An insertion with an escort planned waits for a free escort until close to expiry.
                    if (task.Info.IsTroopMission && !EscortReadyOrNotNeeded(state, site, task))
                        continue;

                    Execute(state, task, group);
                    assigned++;
                }
            }
        }

        /// <summary>
        /// The least over-qualified idle flight that can do the job (EECH picks the lowest suitability
        /// that still meets the requirement, keeping the best aircraft free).
        /// </summary>
        internal static UnitGroup FindGroup(CampaignState state, KeySite site, CampaignTask task)
        {
            var info = task.Info;
            double distance = Vec2.Distance(site.Position, task.TargetPosition);

            return state.Groups
                .Where(g => g.HomeSiteId == site.Id && g.Side == site.Side && !g.IsPlayer && g.Mode == GroupMode.Idle && g.IsAlive)
                .Where(g => g.Members >= info.MinMembers && g.Info.CombatRadius >= distance && info.Capability(g.Info) >= 0.5)
                .OrderBy(g => info.Capability(g.Info))
                .ThenByDescending(g => g.Members)
                .FirstOrDefault();
        }

        private static bool EscortReadyOrNotNeeded(CampaignState state, KeySite site, CampaignTask task)
        {
            var escortTask = task.EscortTaskId >= 0 ? state.Task(task.EscortTaskId) : null;
            if (escortTask == null || escortTask.State != TaskState.Available)
                return true;
            if (task.ExpiresAt - state.Time <= state.Settings.EscortWaitCutoffMinutes * 60)
                return true;
            return FindGroup(state, site, escortTask) != null;
        }

        internal static FlightSpec Spec(CampaignState state, UnitGroup group) => new FlightSpec
        {
            Side = group.Side,
            Name = group.Name,
            Members = group.Members,
            Info = group.Info,
            IsPlayer = group.IsPlayer,
            Group = group,
            Origin = state.Site(group.HomeSiteId)
        };

        internal static void MarkAssigned(CampaignTask task, UnitGroup group)
        {
            task.State = TaskState.Assigned;
            task.AssignedGroupId = group.Id;
            group.Mode = GroupMode.OnMission;
            group.TaskId = task.Id;
        }

        /// <summary>
        /// Finds an AI escort for a troop insertion at the same base, if it has an Escort mission waiting.
        /// If none is free the insertion goes alone and the escort mission is withdrawn.
        /// </summary>
        internal static (UnitGroup Group, CampaignTask Task) TakeEscort(CampaignState state, KeySite site, CampaignTask escorted)
        {
            var escortTask = escorted.EscortTaskId >= 0 ? state.Task(escorted.EscortTaskId) : null;
            if (escortTask == null || escortTask.State != TaskState.Available)
                return (null, null);

            var group = FindGroup(state, site, escortTask);
            if (group == null)
            {
                escortTask.State = TaskState.Expired;
                escortTask.FinishedAt = state.Time;
                state.AddLog($"{escorted.Side} {escorted.Info.Code} flight to {escorted.TargetName} left without an escort (none free).", 0, escorted.Side);
                return (null, null);
            }

            MarkAssigned(escortTask, group);
            return (group, escortTask);
        }

        private static void Execute(CampaignState state, CampaignTask task, UnitGroup group)
        {
            var site = state.Site(group.HomeSiteId);
            MarkAssigned(task, group);
            var escort = task.Info.IsTroopMission ? TakeEscort(state, site, task) : (null, null);

            var report = MissionResolver.Fly(state, task, Spec(state, group),
                escort.Group != null ? Spec(state, escort.Group) : null, escort.Task);

            double busyUntil = state.Time + report.Duration + state.Settings.AiTurnaroundMinutes * 60;
            if (group.Mode != GroupMode.Destroyed)
                group.BusyUntil = busyUntil;
            if (escort.Group != null && escort.Group.Mode != GroupMode.Destroyed)
                escort.Group.BusyUntil = busyUntil;

            Reactions.AfterMission(state, task, report);

            string losses = report.AircraftLost > 0 ? $" Lost {report.AircraftLost} aircraft." : "";
            string escortLosses = report.EscortLost > 0 ? $" Escort lost {report.EscortLost}." : "";
            string kills = report.EnemyAircraftKilled > 0 ? $" Shot down {report.EnemyAircraftKilled}." : "";
            string escorted = escort.Group != null ? $" (escorted by {escort.Group.Name})" : "";
            string seen = report.Detected ? " Detected." : "";
            state.AddLog($"{group.Side} {task.Info.Code} by {group.Name}{escorted} on {task.TargetName}: {report.Result}. {report.Effect}{losses}{escortLosses}{kills}{seen}",
                report.Result == TaskResult.Success || report.AircraftLost > 0 || report.Captured ? 1 : 0, group.Side);
        }
    }
}
