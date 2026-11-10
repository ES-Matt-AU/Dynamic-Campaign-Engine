using System.Windows.Media;
using CampaignEngine.Ai;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignDesktop
{
    /// <summary>
    /// Mission list entries, briefings and mission reports as coloured text, with the same content
    /// as the console version.
    /// </summary>
    public static class MissionText
    {
        /// <summary>A mission waiting in the pool, for the mission selector.</summary>
        public static MissionLine Available(Campaign campaign, CampaignTask t)
        {
            var st = campaign.State;
            var origin = st.Site(t.OriginSiteId);
            double dist = Vec2.Distance(origin.Position, t.TargetPosition) / 1000;
            string aircraft = t.Info.IsTroopMission ? "tilt-jet" : "fighter";
            string flags = (t.Critical ? "  CRITICAL" : "") + (t.EscortTaskId >= 0 && st.Task(t.EscortTaskId)?.IsActive == true ? "  escort planned" : "");

            return new MissionLine
            {
                Id = t.Id,
                Text = $"{t.Info.Code,-8}{aircraft,-9}{t.TargetName}\n" +
                       $"  from {origin.Name}, {dist:0} km, risk {MissionResolver.RiskLabel(t.KnownThreat)} {t.KnownThreat:P0}\n" +
                       $"  expires in {Minutes(t.ExpiresAt - st.Time)}, priority {t.Priority:0.0}{flags}",
                Brush = t.Critical ? Palette.Important : t.Info.IsTroopMission || t.Type == TaskType.Escort ? Palette.Good : Palette.Neutral
            };
        }

        /// <summary>Time left in whole minutes, e.g. "22 min" or "1 h 27 min".</summary>
        private static string Minutes(double seconds)
        {
            int minutes = (int)Math.Floor(seconds / 60);
            if (minutes < 1)
                return "under 1 min";
            return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
        }

        /// <summary>An AI flight that is airborne on an Allied mission.</summary>
        public static MissionLine Active(Campaign campaign, CampaignTask t)
        {
            var st = campaign.State;
            var group = st.Group(t.AssignedGroupId);
            var origin = st.Site(t.OriginSiteId);
            string flight = group?.Name ?? "unknown flight";
            return new MissionLine
            {
                Id = t.Id,
                Text = $"{t.Info.Code,-8}{flight} to {t.TargetName}\n" +
                       $"  took off {GameClock.TimeOfDay(t.StartedAt, st.StartHour)} from {origin?.Name}, due back {GameClock.TimeOfDay(t.FinishedAt, st.StartHour)}",
                Brush = Palette.Allied
            };
        }

        /// <summary>Allied AI missions currently in the air, soonest back first.</summary>
        public static IEnumerable<CampaignTask> ActiveMissions(CampaignState st) =>
            st.Tasks
                .Where(t => t.Side == Side.Allied && t.State == TaskState.Completed && !t.FlownByPlayer && t.AssignedGroupId >= 0 && t.FinishedAt > st.Time)
                .OrderBy(t => t.FinishedAt);

        public static string Briefing(Campaign campaign, CampaignTask task)
        {
            var st = campaign.State;
            var lines = new List<string>();
            var origin = st.Site(task.OriginSiteId);
            var player = campaign.PlayerGroup;
            var aircraft = Campaign.PlayerAircraftFor(task);
            var transport = task.Type == TaskType.Escort ? campaign.EscortedTransport(task) : null;
            double speed = transport != null ? transport.Info.Speed : aircraft.Speed;
            double dist = Vec2.Distance(origin.Position, task.TargetPosition);
            double flightTime = dist * 2 / speed + task.Info.LoiterMinutes * 60;

            lines.Add(task.Type == TaskType.Escort ? $"BRIEFING: {task.TargetName}" : $"BRIEFING: {task.Info.Name} on {task.TargetName}");
            lines.Add(task.Info.Description);
            lines.Add($"Take off from {origin.Name}. Target is {dist / 1000:0} km away. Expected flight time {GameClock.Duration(flightTime)}.");
            lines.Add($"Why: {task.Reason}");
            lines.Add($"Known risk: {MissionResolver.RiskLabel(task.KnownThreat)} ({task.KnownThreat:P0} per aircraft if detected).");

            var known = st.Groups
                .Where(g => g.Side == Side.Enemy && g.IsAlive && g.Info.IsAirDefence)
                .Where(g => Intel.Knowledge(st, Side.Allied, g.Position) >= 0.5)
                .Where(g => Vec2.Distance(g.Position, task.TargetPosition) <= 30000)
                .ToList();
            lines.Add(known.Count > 0
                ? $"Known air defences within 30 km of the target: {string.Join(", ", known.Select(g => $"{g.Name} ({g.Info.Name})"))}."
                : "No known air defences within 30 km of the target (intel may be stale).");

            int available = Math.Min(player.MaxMembers, player.Members + st.Force(Side.Allied).AircraftReserve);
            string plural = aircraft.MemberName + (available > 1 ? "s" : "");
            lines.Add($"Your flight: {available} {plural} ({(available > 1 ? "you and your wingman" : "you alone; no wingman available")}).");

            if (task.Info.IsTroopMission)
            {
                int troops = Math.Min(available * aircraft.TroopCapacity, st.Force(Side.Allied).TroopReserve);
                lines.Add($"Troops: {troops} from the reserve ({aircraft.TroopCapacity} per aircraft). Troops in an aircraft that is shot down before landing are lost.");
                var escort = campaign.AvailableEscortFor(task);
                lines.Add(escort != null
                    ? $"Escort: {escort.Name} ({escort.Members} {escort.Info.MemberName}s) will fly with you."
                    : "Escort: none available; you fly alone.");
            }
            if (task.Type == TaskType.Escort && transport != null)
                lines.Add($"You are protecting {transport.Name} ({transport.Members} {transport.Info.MemberName}s) carrying up to {transport.Members * transport.Info.TroopCapacity} troops.");

            var escorted = task.Type == TaskType.Escort ? st.Task(task.EscortedTaskId) : null;
            var targetSite = task.Type == TaskType.Capture ? st.Site(task.TargetId)
                : escorted?.Type == TaskType.Capture ? st.Site(escorted.TargetId)
                : null;
            if (targetSite != null)
            {
                int troops = task.Type == TaskType.Escort && transport != null
                    ? Math.Min(transport.Members * transport.Info.TroopCapacity, st.Force(Side.Allied).TroopReserve)
                    : Math.Min(available * aircraft.TroopCapacity, st.Force(Side.Allied).TroopReserve);
                var seen = SiteCapture.Odds(st, targetSite, troops, detected: true, knownBy: Side.Allied);
                var unseen = SiteCapture.Odds(st, targetSite, troops, detected: false, knownBy: Side.Allied);
                lines.Add($"{targetSite.Name}: {targetSite.Efficiency:P0} strength, {targetSite.State.ToString().ToLowerInvariant()}, " +
                          $"known defenders worth {seen.Defenders:0.#} troops (needs {targetSite.Info.TroopsToCapture} troops for full effect).");
                lines.Add($"Estimated capture chance if all troops land: {unseen.Chance:P0} if you are not detected, {seen.Chance:P0} if you are.");
            }

            return string.Join("\n", lines);
        }

        public static List<LogLine> Report(CampaignState st, MissionReport r)
        {
            var lines = new List<LogLine>();
            void Add(string text, Brush brush = null) => lines.Add(new LogLine { Text = text, Brush = brush ?? Palette.Neutral });

            Add($"MISSION REPORT: {TaskTypes.Get(r.TaskType).Code} on {r.TargetName}{(r.EscortIsPlayer ? $" (you escorted {r.FlightName})" : "")}", Palette.Important);
            Add("");
            Add("Timeline");
            foreach (string line in r.Timeline)
                Add($"  {line}", line.Contains("shot down") ? Palette.Important : line.Contains("DETECTED") ? Palette.Enemy : Palette.Neutral);

            Add("");
            Add("Detection");
            if (r.Sightings == 0)
                Add("  No enemy sensor saw the flight.");
            else
            {
                Add($"  Watched for {GameClock.Duration(r.ObservedSeconds)} of the {GameClock.Duration(r.Duration)} flight, {r.Sightings} separate sighting(s).");
                foreach (var o in r.Observers.Take(8))
                    Add($"    {o.Name,-32} {GameClock.Duration(o.Seconds),-13} {o.Sightings} sighting(s)   score {o.Score:0.00}", Palette.Detail);
            }
            Add($"  Exposure score {r.ExposureScore:0.00}, giving a {r.DetectionChance:P0} chance of detection.");
            if (r.Detected)
                Add($"  Roll {r.DetectionRoll * 100:0}: DETECTED at {GameClock.TimeOfDay(r.DetectedAt, st.StartHour)} by {r.DetectedBy}.", Palette.Enemy);
            else
                Add($"  Roll {r.DetectionRoll * 100:0}: not detected (needed under {r.DetectionChance * 100:0}).", Palette.Good);

            if (r.TroopsAboard > 0)
            {
                Add("");
                Add("Troops");
                Add($"  {r.TroopsAboard} aboard, {r.TroopsLost} lost in aircraft shot down, {r.TroopsLanded} landed.");
                if (r.CaptureChance >= 0)
                    Add($"  Capture chance {r.CaptureChance:P0}, roll {r.CaptureRoll * 100:0}: {(r.Captured ? "SITE CAPTURED" : "beaten off")}.",
                        r.Captured ? Palette.Good : Palette.Enemy);
            }

            Add("");
            var result = r.EscortIsPlayer ? r.EscortResult : r.Result;
            var resultBrush = result == TaskResult.Success ? Palette.Good : result == TaskResult.Partial ? Palette.Important : Palette.Enemy;
            if (r.EscortIsPlayer)
            {
                Add($"Escort result: {result.ToString().ToUpperInvariant()}. {r.FlightName} kept {r.AircraftStart - r.AircraftLost} of {r.AircraftStart} aircraft.", resultBrush);
                Add($"Their mission: {r.Result}. {r.Effect}");
            }
            else
                Add($"Result: {result.ToString().ToUpperInvariant()}. {r.Effect}", resultBrush);

            int yourStart = r.EscortIsPlayer ? r.EscortStart : r.AircraftStart;
            int yourLost = r.EscortIsPlayer ? r.EscortLost : r.AircraftLost;
            if (yourLost == 0)
                Add("Your losses: none.");
            else
                Add($"Your losses: {yourLost} of {yourStart} aircraft." +
                    (r.PlayerShotDown ? " You ejected and were recovered; a replacement aircraft will be issued if the reserve allows." : ""), Palette.Important);
            if (r.EscortName != null && !r.EscortIsPlayer)
                Add($"Escort {r.EscortName}: lost {r.EscortLost} of {r.EscortStart}; escort result {r.EscortResult}.");
            if (r.EnemyAircraftKilled > 0)
                Add($"Air-to-air kills: {r.EnemyAircraftKilled}.", Palette.Good);

            Add("");
            Add("Consequences");
            if (r.Reactions.Count == 0)
                Add("  None.");
            foreach (string line in r.Reactions)
                Add($"  {line}");

            Add("");
            Add($"While you were airborne and turning round (now {st.Clock()}):");
            if (r.WorldEvents.Count == 0)
                Add("  Nothing of note.");
            foreach (string line in r.WorldEvents)
                Add($"  {line}", Palette.Detail);

            return lines;
        }
    }
}
