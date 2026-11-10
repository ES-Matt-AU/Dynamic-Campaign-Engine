using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignEngine.Ai;
using CampaignEngine.Core;
using CampaignEngine.Map;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;
using Newtonsoft.Json;

namespace CampaignEngine.Campaign
{
    /// <summary>
    /// The campaign engine's front door: start or load a campaign, let time pass, and fly the
    /// player's missions. Front ends (the console now, a VTOL VR mod later) only talk to this class.
    /// </summary>
    public class Campaign
    {
        public CampaignState State { get; }

        private Campaign(CampaignState state)
        {
            State = state;
        }

        public static Campaign New(string mapPath, int seed)
        {
            var map = MapInfo.Load(mapPath);
            var campaign = new Campaign(CampaignSetup.Create(map, seed));

            // Let the AI get going so there are missions waiting when the player arrives.
            campaign.AdvanceTime(20 * 60);
            return campaign;
        }

        public static Campaign Load(string path)
        {
            var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
            var state = JsonConvert.DeserializeObject<CampaignState>(File.ReadAllText(path), settings);
            state.Grid.EnsureArrays();

            foreach (var force in state.Forces)
                if (force.TroopReserve < 0)
                    force.TroopReserve = state.Settings.StartingTroopReserve;

            // Saves from before new mission types were added have a shorter timer table.
            if (state.NextGenerationAt.Length != 2 * TaskTypes.Count)
            {
                var timers = new double[2 * TaskTypes.Count];
                int oldCount = state.NextGenerationAt.Length / 2;
                for (int side = 0; side < 2; side++)
                    for (int type = 0; type < oldCount && type < TaskTypes.Count; type++)
                        timers[side * TaskTypes.Count + type] = state.NextGenerationAt[side * oldCount + type];
                state.NextGenerationAt = timers;
            }
            InfluenceMaps.Recalculate(state);
            return new Campaign(state);
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonConvert.SerializeObject(State, Formatting.Indented));
        }

        public UnitGroup PlayerGroup => State.Group(State.PlayerGroupId);

        /// <summary>Allied missions the player can take, most urgent first.</summary>
        public List<CampaignTask> PlayerMissions()
        {
            return State.Tasks
                .Where(t => t.State == TaskState.Available && t.Side == Side.Allied)
                .Where(t =>
                {
                    var origin = State.Site(t.OriginSiteId);
                    if (origin == null || !origin.IsUsable || origin.Type != SiteType.Airbase)
                        return false;
                    if (t.Info.IsTroopMission && State.Force(Side.Allied).TroopReserve <= 0)
                        return false;
                    // The player can only escort if there is an AI transport ready to go.
                    return t.Type != TaskType.Escort || EscortedTransport(t) != null;
                })
                .OrderByDescending(t => t.Critical)
                .ThenByDescending(t => t.Priority)
                .ToList();
        }

        /// <summary>The aircraft the player flies on this mission: the tilt-jet for troop insertions, otherwise the fighter.</summary>
        public static GroupTypeInfo PlayerAircraftFor(CampaignTask task) =>
            GroupTypes.Get(task.Info.IsTroopMission ? GroupType.PlayerTransport : GroupType.PlayerFlight);

        /// <summary>For an Escort mission: the idle AI transport flight that would carry the troops.</summary>
        public UnitGroup EscortedTransport(CampaignTask escortTask)
        {
            var escorted = State.Task(escortTask.EscortedTaskId);
            var origin = State.Site(escortTask.OriginSiteId);
            if (escorted == null || escorted.State != TaskState.Available || origin == null)
                return null;
            if (State.Force(escorted.Side).TroopReserve <= 0)
                return null;
            return TaskAssigner.FindGroup(State, origin, escorted);
        }

        /// <summary>For a troop insertion: an idle AI fighter flight that would escort the player.</summary>
        public UnitGroup AvailableEscortFor(CampaignTask task)
        {
            var escortTask = task.EscortTaskId >= 0 ? State.Task(task.EscortTaskId) : null;
            var origin = State.Site(task.OriginSiteId);
            if (escortTask == null || escortTask.State != TaskState.Available || origin == null)
                return null;
            return TaskAssigner.FindGroup(State, origin, escortTask);
        }

        public void AdvanceTime(double seconds)
        {
            double end = State.Time + seconds;
            while (State.Time < end - 1e-6 && State.Winner == null)
                Tick(Math.Min(State.Settings.TickSeconds, end - State.Time));
        }

        /// <summary>
        /// Simulates the player's sortie, applies the results and the enemy's reaction, then moves the
        /// world on by the flight time plus turnaround.
        /// </summary>
        public MissionReport FlyPlayerMission(int taskId)
        {
            var task = State.Task(taskId);
            if (task == null || task.State != TaskState.Available || task.Side != Side.Allied)
                throw new InvalidOperationException("That mission is no longer available.");

            var origin = State.Site(task.OriginSiteId);
            if (origin == null || !origin.IsUsable)
                throw new InvalidOperationException("The mission's base is out of action.");

            var force = State.Force(Side.Allied);
            if (task.Info.IsTroopMission && force.TroopReserve <= 0)
                throw new InvalidOperationException("There are no troops left in the reserve.");

            // Escort missions need the AI transport they protect.
            UnitGroup transport = null;
            CampaignTask escorted = null;
            if (task.Type == TaskType.Escort)
            {
                transport = EscortedTransport(task);
                escorted = State.Task(task.EscortedTaskId);
                if (transport == null)
                    throw new InvalidOperationException("No AI transport is ready to fly this insertion.");
            }

            var group = PlayerGroup;
            group.Type = task.Info.IsTroopMission ? GroupType.PlayerTransport : GroupType.PlayerFlight;
            while (group.Members < group.MaxMembers && force.AircraftReserve > 0)
            {
                group.Members++;
                force.AircraftReserve--;
            }
            if (group.Members == 0)
                throw new InvalidOperationException("No aircraft are available: the reserve is empty.");

            group.HomeSiteId = origin.Id;
            group.Position = origin.Position;
            TaskAssigner.MarkAssigned(task, group);
            task.FlownByPlayer = true;

            var playerFlight = TaskAssigner.Spec(State, group);
            playerFlight.Name = "Your flight";
            MissionReport report;
            CampaignTask mainTask;

            if (task.Type == TaskType.Escort)
            {
                // You escort an AI transport flight.
                TaskAssigner.MarkAssigned(escorted, transport);
                mainTask = escorted;
                report = MissionResolver.Fly(State, escorted, TaskAssigner.Spec(State, transport), playerFlight, task);
                if (transport.Mode != GroupMode.Destroyed)
                    transport.BusyUntil = State.Time + report.Duration + State.Settings.AiTurnaroundMinutes * 60;
            }
            else
            {
                // You fly the mission; troop insertions take an AI escort along if one is free.
                mainTask = task;
                var escort = task.Info.IsTroopMission ? TaskAssigner.TakeEscort(State, origin, task) : (null, null);
                report = MissionResolver.Fly(State, task, playerFlight,
                    escort.Group != null ? TaskAssigner.Spec(State, escort.Group) : null, escort.Task);
                if (escort.Group != null && escort.Group.Mode != GroupMode.Destroyed)
                    escort.Group.BusyUntil = State.Time + report.Duration + State.Settings.AiTurnaroundMinutes * 60;
            }

            Reactions.AfterMission(State, mainTask, report);
            State.AddLog($"Your {task.Info.Code} mission ({task.TargetName}): {(task.Type == TaskType.Escort ? report.EscortResult : report.Result)}. {report.Effect}", 2, Side.Allied);

            int logStart = State.Log.Count;
            AdvanceTime(report.Duration + State.Settings.PlayerTurnaroundMinutes * 60);
            group.Mode = GroupMode.Idle;

            report.WorldEvents = State.Log
                .Skip(logStart)
                .Where(e => e.Level >= 1)
                .Select(e => $"{GameClock.TimeOfDay(e.Time, State.StartHour)}  {e.Text}")
                .ToList();
            return report;
        }

        private void Tick(double dt)
        {
            var s = State.Settings;
            State.Time += dt;

            foreach (var g in State.Groups)
                if (g.Mode == GroupMode.OnMission && !g.IsPlayer && State.Time >= g.BusyUntil)
                    g.Mode = g.IsAlive ? GroupMode.Idle : GroupMode.Destroyed;
            State.Patrols.RemoveAll(p => p.EndTime < State.Time);

            UpdateRepairs();
            UpdateReinforcements();

            if (State.Time >= State.NextMapUpdateAt)
            {
                InfluenceMaps.Recalculate(State);
                State.NextMapUpdateAt = State.Time + s.MapUpdateMinutes * 60;
            }

            Intel.Update(State, dt);
            TaskGenerator.CleanUp(State);
            TaskGenerator.Run(State);

            if (State.Time >= State.NextAssignAt)
            {
                TaskAssigner.Run(State);
                State.NextAssignAt = State.Time + s.AssignIntervalMinutes * 60;
            }

            CheckVictory();
        }

        /// <summary>
        /// Out-of-action sites start repairs after a delay and rebuild one building per step until whole.
        /// Damaged sites that still work are patched up slowly.
        /// </summary>
        private void UpdateRepairs()
        {
            var s = State.Settings;
            foreach (var site in State.Sites)
            {
                switch (site.State)
                {
                    case SiteState.Unusable:
                        if (State.Time - site.UnusableSince >= s.RepairDelayMinutes * 60)
                        {
                            site.State = SiteState.Repairing;
                            site.NextRepairAt = State.Time + s.RepairStepMinutes * 60;
                            State.AddLog($"Repairs have begun at {site.Name} ({site.Side}).", 1, site.Side);
                        }
                        break;

                    case SiteState.Repairing:
                        if (State.Time >= site.NextRepairAt)
                        {
                            site.Structures++;
                            if (site.Structures >= site.MaxStructures)
                            {
                                site.Structures = site.MaxStructures;
                                site.State = SiteState.Usable;
                                site.NextRepairAt = 0;
                                State.AddLog($"{site.Name} ({site.Side}) is back in action.", 2, site.Side);
                            }
                            else
                                site.NextRepairAt = State.Time + s.RepairStepMinutes * 60;
                        }
                        break;

                    case SiteState.Usable:
                        if (site.Structures >= site.MaxStructures)
                            site.NextRepairAt = 0;
                        else if (site.NextRepairAt <= 0)
                            site.NextRepairAt = State.Time + s.FieldRepairMinutes * 60;
                        else if (State.Time >= site.NextRepairAt)
                        {
                            site.Structures++;
                            site.NextRepairAt = site.Structures < site.MaxStructures ? State.Time + s.FieldRepairMinutes * 60 : 0;
                        }
                        break;
                }
            }
        }

        /// <summary>Working bases bring depleted units back up to strength while the side has reserves.</summary>
        private void UpdateReinforcements()
        {
            var s = State.Settings;

            if (State.Time >= State.NextRegenAirAt)
            {
                State.NextRegenAirAt = State.Time + s.RegenAirMinutes * 60;
                foreach (var site in State.Sites.Where(x => x.IsUsable && x.Info.HostsAircraft))
                {
                    var force = State.Force(site.Side);
                    if (force.AircraftReserve <= 0)
                        continue;
                    var group = State.Groups
                        .Where(g => g.HomeSiteId == site.Id && !g.IsPlayer && g.Mode != GroupMode.OnMission && g.Members < g.MaxMembers)
                        .OrderBy(g => g.StrengthFraction)
                        .FirstOrDefault();
                    if (group == null)
                        continue;
                    group.Members++;
                    force.AircraftReserve--;
                    if (group.Mode == GroupMode.Destroyed)
                        group.Mode = GroupMode.Idle;
                    State.AddLog($"Replacement aircraft delivered to {group.Name} at {site.Name}.", 0, site.Side);
                }
            }

            if (State.Time >= State.NextRegenGroundAt)
            {
                State.NextRegenGroundAt = State.Time + s.RegenGroundMinutes * 60;
                foreach (var site in State.Sites.Where(x => x.IsUsable && x.Type == SiteType.MilitaryBase))
                {
                    var force = State.Force(site.Side);
                    if (force.GroundReserve <= 0)
                        continue;
                    var group = State.Groups
                        .Where(g => g.Side == site.Side && !g.Info.IsAir && g.Members < g.MaxMembers)
                        .Where(g => Vec2.Distance(g.Position, site.Position) <= s.GroundRegenRadius)
                        .OrderBy(g => g.StrengthFraction)
                        .FirstOrDefault();
                    if (group == null)
                        continue;
                    group.Members++;
                    force.GroundReserve--;
                    if (group.Mode == GroupMode.Destroyed)
                        group.Mode = GroupMode.Idle;
                    State.AddLog($"Reinforcements reached {group.Name} from {site.Name}.", 0, site.Side);
                }
            }
        }

        /// <summary>
        /// A side wins when all its objective sites are out of action at the same time, or the other
        /// side has no working air bases (EECH's rules, without capture for now).
        /// </summary>
        private void CheckVictory()
        {
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var enemy = side.Opponent();
                var objectives = State.Force(side).ObjectiveSiteIds;
                // An objective counts if it is out of action or has been captured.
                bool allDown = objectives.Count > 0 && objectives.All(id =>
                {
                    var site = State.Site(id);
                    return site == null || !site.IsUsable || site.Side == side;
                });
                bool noBases = !State.Sites.Any(s => s.Side == enemy && s.Info.HostsAircraft && s.IsUsable);

                if (allDown || noBases)
                {
                    State.Winner = side;
                    State.WinReason = allDown
                        ? $"All {enemy} objectives are captured or out of action at the same time."
                        : $"{enemy} has no working air bases left.";
                    State.AddLog($"{side} wins: {State.WinReason}", 2);
                    return;
                }
            }
        }
    }
}
