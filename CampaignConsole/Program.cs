using CampaignEngine.Ai;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignConsole
{
    /// <summary>Text front end for testing and tuning the campaign engine.</summary>
    internal static class Program
    {
        private const string DefaultMapPath = @"F:\SteamLibrary\steamapps\common\VTOL VR\CustomMaps\afMtnsHills\afMtnsHills.vtm";

        private static readonly ConsoleColor AlliedColour = ConsoleColor.Cyan;
        private static readonly ConsoleColor EnemyColour = ConsoleColor.Red;

        /// <summary>Set once for the whole solution in Directory.Build.props.</summary>
        private static readonly string Version =
            typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";

        private static string mapPath = DefaultMapPath;
        private static string savesFolder;
        private static Campaign campaign;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && args[0] == "--soak")
                return Soak(args);
            if (args.Length > 0)
                mapPath = args[0];
            savesFolder = Path.Combine(Environment.CurrentDirectory, "Saves");

            Heading($"VTOL VR Dynamic Campaign (prototype)  version {Version}");
            if (!StartMenu())
                return 0;

            while (true)
            {
                ShowHeader();
                Console.WriteLine("  1  Situation map");
                Console.WriteLine("  2  Mission list");
                Console.WriteLine("  3  Fly a mission");
                Console.WriteLine("  4  Advance time");
                Console.WriteLine("  5  Event log");
                Console.WriteLine("  6  Forces and key sites");
                Console.WriteLine("  7  Save campaign");
                Console.WriteLine("  8  Load campaign");
                Console.WriteLine("  0  Quit");

                switch (Prompt("Choice"))
                {
                    case "1": ShowMap(); break;
                    case "2": ShowMissions(); Pause(); break;
                    case "3": FlyMission(); break;
                    case "4": AdvanceTime(); break;
                    case "5": ShowLog(); break;
                    case "6": ShowForces(); break;
                    case "7": SaveCampaign(); break;
                    case "8": LoadCampaign(); break;
                    case "0": return 0;
                }
            }
        }

        // ---------------------------------------------------------------- start and save

        private static bool StartMenu()
        {
            while (true)
            {
                Console.WriteLine("  N  New campaign");
                Console.WriteLine("  L  Load campaign");
                Console.WriteLine("  Q  Quit");
                switch (Prompt("Choice").ToUpperInvariant())
                {
                    case "N":
                        if (NewCampaign())
                            return true;
                        break;
                    case "L":
                        if (LoadCampaign())
                            return true;
                        break;
                    case "Q":
                        return false;
                }
            }
        }

        private static bool NewCampaign()
        {
            if (!File.Exists(mapPath))
            {
                Colour($"Map file not found: {mapPath}", ConsoleColor.Yellow);
                mapPath = Prompt("Path to a VTOL VR .vtm map file");
                if (!File.Exists(mapPath))
                    return false;
            }

            string seedText = Prompt("Seed number (Enter for random)");
            int seed = int.TryParse(seedText, out int s) ? s : Environment.TickCount & 0x7FFFFFFF;

            try
            {
                campaign = Campaign.New(mapPath, seed);
                Colour($"New campaign on {campaign.State.Map.MapId}, seed {seed}.", ConsoleColor.Green);
                return true;
            }
            catch (Exception e)
            {
                Colour($"Could not start the campaign: {e.Message}", ConsoleColor.Yellow);
                return false;
            }
        }

        private static void SaveCampaign()
        {
            string name = Prompt("Save name (Enter for 'campaign')");
            if (string.IsNullOrWhiteSpace(name))
                name = "campaign";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            string path = Path.Combine(savesFolder, name + ".json");
            campaign.Save(path);
            Colour($"Saved to {path}", ConsoleColor.Green);
            Pause();
        }

        private static bool LoadCampaign()
        {
            var files = Directory.Exists(savesFolder)
                ? Directory.GetFiles(savesFolder, "*.json").OrderByDescending(File.GetLastWriteTime).ToArray()
                : Array.Empty<string>();
            if (files.Length == 0)
            {
                Colour($"No saves found in {savesFolder}", ConsoleColor.Yellow);
                return false;
            }

            for (int i = 0; i < files.Length; i++)
                Console.WriteLine($"  {i + 1}  {Path.GetFileNameWithoutExtension(files[i])}  ({File.GetLastWriteTime(files[i]):g})");
            if (!int.TryParse(Prompt("Load which (Enter to cancel)"), out int pick) || pick < 1 || pick > files.Length)
                return false;

            try
            {
                campaign = Campaign.Load(files[pick - 1]);
                Colour($"Loaded {Path.GetFileNameWithoutExtension(files[pick - 1])}.", ConsoleColor.Green);
                return true;
            }
            catch (Exception e)
            {
                Colour($"Could not load: {e.Message}", ConsoleColor.Yellow);
                return false;
            }
        }

        // ---------------------------------------------------------------- header

        private static void ShowHeader()
        {
            var st = campaign.State;
            Console.WriteLine();
            Heading($"{st.Clock()}   Map: {st.Map.MapId}");
            WriteSideSummary(Side.Allied);
            WriteSideSummary(Side.Enemy);
            if (st.Winner.HasValue)
                Colour($"  CAMPAIGN OVER: {st.Winner} wins. {st.WinReason}", ConsoleColor.Yellow);
            Console.WriteLine();
        }

        private static void WriteSideSummary(Side side)
        {
            var st = campaign.State;
            var force = st.Force(side);
            int bases = st.Sites.Count(s => s.Side == side && s.Info.HostsAircraft);
            int basesUp = st.Sites.Count(s => s.Side == side && s.Info.HostsAircraft && s.IsUsable);
            int aircraft = st.Groups.Where(g => g.Side == side && g.Info.IsAir && !g.IsPlayer).Sum(g => g.Members);
            int ground = st.Groups.Where(g => g.Side == side && !g.Info.IsAir).Sum(g => g.Members);
            int objectivesDown = force.ObjectiveSiteIds.Count(id => st.Site(id)?.IsUsable != true);

            Colour($"  {side,-6}  air bases {basesUp}/{bases} working   aircraft {aircraft} (+{force.AircraftReserve} reserve)   " +
                   $"ground units {ground} (+{force.GroundReserve})   troops in reserve {force.TroopReserve}   objectives down {objectivesDown}/{force.ObjectiveSiteIds.Count}",
                side == Side.Allied ? AlliedColour : EnemyColour);
        }

        // ---------------------------------------------------------------- map

        private static void ShowMap()
        {
            var st = campaign.State;
            var grid = st.Grid;
            var symbols = new Dictionary<int, (char Symbol, ConsoleColor Colour)>();

            // Units first, then sites on top, so sites win when they share a cell.
            foreach (var g in st.Groups.Where(g => g.IsAlive && !g.Info.IsAir))
            {
                if (g.Side == Side.Enemy && Intel.Knowledge(st, Side.Allied, g.Position) < 0.5)
                    continue;
                symbols[Cell(grid.IndexOf(g.Position))] = (g.Info.Symbol, g.Side == Side.Allied ? AlliedColour : EnemyColour);
            }
            foreach (var p in st.Patrols.Where(p => p.Side == Side.Allied || Intel.Knowledge(st, Side.Allied, p.Position) >= 0.5))
                symbols[Cell(grid.IndexOf(p.Position))] = ('c', p.Side == Side.Allied ? AlliedColour : EnemyColour);
            foreach (var s in st.Sites)
            {
                var colour = !s.IsUsable ? ConsoleColor.DarkYellow : s.Side == Side.Allied ? AlliedColour : EnemyColour;
                symbols[Cell(grid.IndexOf(s.Position))] = (s.Info.Symbol, colour);
            }

            Console.WriteLine();
            Console.WriteLine("  North is up. Each character is one sector wide and two sectors tall (about 3 x 6 km).");
            for (int row = 0; row < grid.Height / 2; row++)
            {
                int zTop = grid.Height - 1 - row * 2;
                Console.Write($"{zTop * grid.SectorSize / 1000,4:0} ");
                for (int x = 0; x < grid.Width; x++)
                {
                    int index = grid.Index(x, zTop);
                    if (symbols.TryGetValue(row * grid.Width + x, out var sym))
                    {
                        Colour(sym.Symbol.ToString(), sym.Colour, newLine: false);
                        continue;
                    }

                    double ratio = grid.OwnershipRatio(Side.Allied, index);
                    if (ratio > 0.55)
                        Colour(".", ConsoleColor.DarkCyan, newLine: false);
                    else if (ratio < 0.45)
                        Colour(Intel.Knowledge(st, Side.Allied, grid.Centre(index)) < 0.25 ? "," : ".", ConsoleColor.DarkRed, newLine: false);
                    else
                        Colour(":", ConsoleColor.DarkGray, newLine: false);
                }
                Console.WriteLine();
            }

            Console.Write("  km ");
            for (int x = 0; x < grid.Width; x += 8)
                Console.Write($"{x * grid.SectorSize / 1000,-8:0}");
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("  Key: A airbase  F FARP  I factory  M military base  P power station  O refinery  R radar");
            Console.WriteLine("       ^ SAM  x AAA  * ground forces  c air patrol   . territory  : contested  , enemy area with stale intel");
            Console.Write("       ");
            Colour("Allied", AlliedColour, newLine: false);
            Console.Write("  ");
            Colour("Enemy", EnemyColour, newLine: false);
            Console.Write("  ");
            Colour("Out of action", ConsoleColor.DarkYellow, newLine: false);
            Console.WriteLine("   (enemy units are only shown where Allied intel is fresh)");
            Pause();

            // Two sector rows share one character row: key by (character row, column).
            int Cell(int index)
            {
                int x = index % grid.Width, z = index / grid.Width;
                return (grid.Height - 1 - z) / 2 * grid.Width + x;
            }
        }

        // ---------------------------------------------------------------- missions

        private static List<CampaignTask> ShowMissions()
        {
            var st = campaign.State;
            var missions = campaign.PlayerMissions();
            Console.WriteLine();
            if (missions.Count == 0)
            {
                Colour("  No missions are waiting at working Allied airbases. Try advancing time.", ConsoleColor.Yellow);
                return missions;
            }

            Console.WriteLine($"  {"#",-3} {"Type",-8} {"Aircraft",-9} {"Target",-30} {"From",-18} {"Dist",6}  {"Known risk",-15} {"Pri",4}  {"Expires",-11} Notes");
            for (int i = 0; i < missions.Count; i++)
            {
                var t = missions[i];
                var origin = st.Site(t.OriginSiteId);
                double dist = Vec2.Distance(origin.Position, t.TargetPosition) / 1000;
                string risk = $"{MissionResolver.RiskLabel(t.KnownThreat)} {t.KnownThreat:P0}";
                string expires = Minutes(t.ExpiresAt - st.Time);
                string aircraft = t.Info.IsTroopMission ? "tilt-jet" : "fighter";
                string notes = (t.Critical ? "CRITICAL. " : "") + (t.EscortTaskId >= 0 && st.Task(t.EscortTaskId)?.IsActive == true ? "Escort planned. " : "") + t.Reason;
                var colour = t.Critical ? ConsoleColor.Yellow : t.Info.IsTroopMission || t.Type == TaskType.Escort ? ConsoleColor.Green : ConsoleColor.Gray;
                Colour($"  {i + 1,-3} {t.Info.Code,-8} {aircraft,-9} {Trim(t.TargetName, 30),-30} {Trim(origin.Name, 18),-18} {dist,4:0} km  {risk,-15} {t.Priority,4:0.0}  {expires,-11} {notes}", colour);
            }
            Console.WriteLine();
            Console.WriteLine("  Known risk: chance of losing each aircraft to threats Allied intel knows about, if the enemy detects you.");
            return missions;
        }

        private static void FlyMission()
        {
            var st = campaign.State;
            if (st.Winner.HasValue)
            {
                Colour("The campaign is over.", ConsoleColor.Yellow);
                return;
            }

            var missions = ShowMissions();
            if (missions.Count == 0)
            {
                Pause();
                return;
            }
            if (!int.TryParse(Prompt("Fly which mission (Enter to cancel)"), out int pick) || pick < 1 || pick > missions.Count)
                return;

            var task = missions[pick - 1];
            var origin = st.Site(task.OriginSiteId);
            var player = campaign.PlayerGroup;
            var aircraft = Campaign.PlayerAircraftFor(task);
            var transport = task.Type == TaskType.Escort ? campaign.EscortedTransport(task) : null;
            double speed = transport != null ? transport.Info.Speed : aircraft.Speed;
            double dist = Vec2.Distance(origin.Position, task.TargetPosition);
            double flightTime = dist * 2 / speed + task.Info.LoiterMinutes * 60;

            Console.WriteLine();
            Heading(task.Type == TaskType.Escort ? $"Briefing: {task.TargetName}" : $"Briefing: {task.Info.Name} on {task.TargetName}");
            Console.WriteLine($"  {task.Info.Description}");
            Console.WriteLine($"  Take off from {origin.Name}. Target is {dist / 1000:0} km away. Expected flight time {GameClock.Duration(flightTime)}.");
            Console.WriteLine($"  Why: {task.Reason}");
            Console.WriteLine($"  Known risk: {MissionResolver.RiskLabel(task.KnownThreat)} ({task.KnownThreat:P0} per aircraft if detected).");

            var known = st.Groups
                .Where(g => g.Side == Side.Enemy && g.IsAlive && g.Info.IsAirDefence)
                .Where(g => Intel.Knowledge(st, Side.Allied, g.Position) >= 0.5)
                .Where(g => Vec2.Distance(g.Position, task.TargetPosition) <= 30000)
                .ToList();
            if (known.Count > 0)
                Console.WriteLine($"  Known air defences within 30 km of the target: {string.Join(", ", known.Select(g => $"{g.Name} ({g.Info.Name})"))}.");
            else
                Console.WriteLine("  No known air defences within 30 km of the target (intel may be stale).");

            int available = Math.Min(player.MaxMembers, player.Members + st.Force(Side.Allied).AircraftReserve);
            string plural = aircraft.MemberName + (available > 1 ? "s" : "");
            Console.WriteLine($"  Your flight: {available} {plural} ({(available > 1 ? "you and your wingman" : "you alone; no wingman available")}).");

            if (task.Info.IsTroopMission)
            {
                int troops = Math.Min(available * aircraft.TroopCapacity, st.Force(Side.Allied).TroopReserve);
                Console.WriteLine($"  Troops: {troops} from the reserve ({aircraft.TroopCapacity} per aircraft). Troops in an aircraft that is shot down before landing are lost.");
                var escort = campaign.AvailableEscortFor(task);
                Console.WriteLine(escort != null
                    ? $"  Escort: {escort.Name} ({escort.Members} {escort.Info.MemberName}s) will fly with you."
                    : "  Escort: none available; you fly alone.");
            }
            if (task.Type == TaskType.Escort && transport != null)
                Console.WriteLine($"  You are protecting {transport.Name} ({transport.Members} {transport.Info.MemberName}s) carrying up to {transport.Members * transport.Info.TroopCapacity} troops.");

            var targetSite = (task.Type == TaskType.Capture || (task.Type == TaskType.Escort && st.Task(task.EscortedTaskId)?.Type == TaskType.Capture))
                ? st.Site(task.Type == TaskType.Escort ? st.Task(task.EscortedTaskId).TargetId : task.TargetId)
                : null;
            if (targetSite != null)
            {
                int troops = task.Type == TaskType.Escort
                    ? Math.Min(transport.Members * transport.Info.TroopCapacity, st.Force(Side.Allied).TroopReserve)
                    : Math.Min(available * aircraft.TroopCapacity, st.Force(Side.Allied).TroopReserve);
                var seen = SiteCapture.Odds(st, targetSite, troops, detected: true, knownBy: Side.Allied);
                var unseen = SiteCapture.Odds(st, targetSite, troops, detected: false, knownBy: Side.Allied);
                Console.WriteLine($"  {targetSite.Name}: {targetSite.Efficiency:P0} strength, {targetSite.State.ToString().ToLowerInvariant()}, " +
                                  $"known defenders worth {seen.Defenders:0.#} troops (needs {targetSite.Info.TroopsToCapture} troops for full effect).");
                Console.WriteLine($"  Estimated capture chance if all troops land: {unseen.Chance:P0} if you are not detected, {seen.Chance:P0} if you are.");
            }

            if (!Prompt("Fly this mission? (Y/N)").Equals("Y", StringComparison.OrdinalIgnoreCase))
                return;

            MissionReport report;
            try
            {
                report = campaign.FlyPlayerMission(task.Id);
            }
            catch (InvalidOperationException e)
            {
                Colour(e.Message, ConsoleColor.Yellow);
                Pause();
                return;
            }

            ShowReport(report);
        }

        private static void ShowReport(MissionReport r)
        {
            var st = campaign.State;
            Console.WriteLine();
            Heading($"Mission report: {TaskTypes.Get(r.TaskType).Code} on {r.TargetName}{(r.EscortIsPlayer ? $" (you escorted {r.FlightName})" : "")}");

            Console.WriteLine("  Timeline");
            foreach (string line in r.Timeline)
                Colour($"    {line}", line.Contains("shot down") ? ConsoleColor.Yellow : line.Contains("DETECTED") ? EnemyColour : ConsoleColor.Gray);

            Console.WriteLine();
            Console.WriteLine("  Detection");
            if (r.Sightings == 0)
                Console.WriteLine("    No enemy sensor saw the flight.");
            else
            {
                Console.WriteLine($"    Watched for {GameClock.Duration(r.ObservedSeconds)} of the {GameClock.Duration(r.Duration)} flight, {r.Sightings} separate sighting(s).");
                foreach (var o in r.Observers.Take(8))
                    Console.WriteLine($"      {Trim(o.Name, 32),-32} {GameClock.Duration(o.Seconds),-13} {o.Sightings} sighting(s)   score {o.Score:0.00}");
            }
            Console.WriteLine($"    Exposure score {r.ExposureScore:0.00}, giving a {r.DetectionChance:P0} chance of detection.");
            if (r.Detected)
                Colour($"    Roll {r.DetectionRoll * 100:0}: DETECTED at {GameClock.TimeOfDay(r.DetectedAt, st.StartHour)} by {r.DetectedBy}.", EnemyColour);
            else
                Colour($"    Roll {r.DetectionRoll * 100:0}: not detected (needed under {r.DetectionChance * 100:0}).", ConsoleColor.Green);

            if (r.TroopsAboard > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Troops");
                Console.WriteLine($"    {r.TroopsAboard} aboard, {r.TroopsLost} lost in aircraft shot down, {r.TroopsLanded} landed.");
                if (r.CaptureChance >= 0)
                    Colour($"    Capture chance {r.CaptureChance:P0}, roll {r.CaptureRoll * 100:0}: {(r.Captured ? "SITE CAPTURED" : "beaten off")}.",
                        r.Captured ? ConsoleColor.Green : EnemyColour);
            }

            Console.WriteLine();
            var result = r.EscortIsPlayer ? r.EscortResult : r.Result;
            var resultColour = result == TaskResult.Success ? ConsoleColor.Green : result == TaskResult.Partial ? ConsoleColor.Yellow : EnemyColour;
            if (r.EscortIsPlayer)
            {
                Colour($"  Escort result: {result.ToString().ToUpperInvariant()}. {r.FlightName} kept {r.AircraftStart - r.AircraftLost} of {r.AircraftStart} aircraft.", resultColour);
                Console.WriteLine($"  Their mission: {r.Result}. {r.Effect}");
            }
            else
                Colour($"  Result: {result.ToString().ToUpperInvariant()}. {r.Effect}", resultColour);

            int yourStart = r.EscortIsPlayer ? r.EscortStart : r.AircraftStart;
            int yourLost = r.EscortIsPlayer ? r.EscortLost : r.AircraftLost;
            if (yourLost == 0)
                Console.WriteLine("  Your losses: none.");
            else
                Colour($"  Your losses: {yourLost} of {yourStart} aircraft." +
                       (r.PlayerShotDown ? " You ejected and were recovered; a replacement aircraft will be issued if the reserve allows." : ""),
                    ConsoleColor.Yellow);
            if (r.EscortName != null && !r.EscortIsPlayer)
                Console.WriteLine($"  Escort {r.EscortName}: lost {r.EscortLost} of {r.EscortStart}; escort result {r.EscortResult}.");
            if (r.EnemyAircraftKilled > 0)
                Colour($"  Air-to-air kills: {r.EnemyAircraftKilled}.", ConsoleColor.Green);

            Console.WriteLine();
            Console.WriteLine("  Consequences");
            if (r.Reactions.Count == 0)
                Console.WriteLine("    None.");
            foreach (string line in r.Reactions)
                Console.WriteLine($"    {line}");

            Console.WriteLine();
            Console.WriteLine($"  While you were airborne and turning round (now {st.Clock()}):");
            if (r.WorldEvents.Count == 0)
                Console.WriteLine("    Nothing of note.");
            foreach (string line in r.WorldEvents.Take(20))
                Console.WriteLine($"    {line}");
            if (r.WorldEvents.Count > 20)
                Console.WriteLine($"    ...and {r.WorldEvents.Count - 20} more in the event log.");
            Pause();
        }

        // ---------------------------------------------------------------- time, log, forces

        private static void AdvanceTime()
        {
            if (campaign.State.Winner.HasValue)
            {
                Colour("The campaign is over.", ConsoleColor.Yellow);
                return;
            }

            Console.WriteLine("  1  30 minutes   2  1 hour   3  3 hours   4  6 hours   5  Other (minutes)");
            double minutes = Prompt("Advance by") switch
            {
                "1" => 30,
                "2" => 60,
                "3" => 180,
                "4" => 360,
                "5" => double.TryParse(Prompt("Minutes"), out double m) ? m : 0,
                _ => 0
            };
            if (minutes <= 0)
                return;

            int logStart = campaign.State.Log.Count;
            campaign.AdvanceTime(minutes * 60);
            var events = campaign.State.Log.Skip(logStart).Where(e => e.Level >= 1).ToList();
            Console.WriteLine();
            Console.WriteLine($"  Time is now {campaign.State.Clock()}. {events.Count} event(s):");
            foreach (var e in events.TakeLast(25))
                WriteLogEntry(e);
            Pause();
        }

        private static void ShowLog()
        {
            bool detail = Prompt("Include minor detail? (Y/N)").Equals("Y", StringComparison.OrdinalIgnoreCase);
            var entries = campaign.State.Log.Where(e => detail || e.Level >= 1).TakeLast(detail ? 60 : 40).ToList();
            Console.WriteLine();
            foreach (var e in entries)
                WriteLogEntry(e);
            Pause();
        }

        private static void WriteLogEntry(LogEntry e)
        {
            var colour = e.Level >= 2 ? ConsoleColor.Yellow : e.Side == (int)Side.Allied ? ConsoleColor.DarkCyan : e.Side == (int)Side.Enemy ? ConsoleColor.DarkRed : ConsoleColor.Gray;
            Colour($"  {GameClock.DayAndTime(e.Time, campaign.State.StartHour)}  {e.Text}", colour);
        }

        private static void ShowForces()
        {
            var st = campaign.State;
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var colour = side == Side.Allied ? AlliedColour : EnemyColour;
                var force = st.Force(side);
                Console.WriteLine();
                Heading($"{side} key sites");
                foreach (var s in st.Sites.Where(s => s.Side == side).OrderByDescending(s => s.Info.Importance))
                {
                    bool objective = st.Force(side.Opponent()).ObjectiveSiteIds.Contains(s.Id);
                    string state = s.State == SiteState.Usable ? "working" : s.State == SiteState.Repairing ? "repairing" : "OUT OF ACTION";
                    Colour($"  {s.Info.Symbol} {Trim(s.Name, 26),-26} {s.Info.Name,-14} {state,-14} {s.Efficiency,5:P0}   {s.Position}{(objective ? "   [enemy objective]" : "")}",
                        s.IsUsable ? colour : ConsoleColor.DarkYellow);
                }

                Console.WriteLine();
                Console.WriteLine($"  {side} units (aircraft reserve {force.AircraftReserve}, ground reserve {force.GroundReserve})");
                foreach (var g in st.Groups.Where(g => g.Side == side).OrderBy(g => g.Info.IsAir ? 0 : 1).ThenBy(g => g.Name))
                {
                    if (side == Side.Enemy && !g.Info.IsAir && Intel.Knowledge(st, Side.Allied, g.Position) < 0.5)
                        continue;
                    string where = g.Info.IsAir ? $"at {st.Site(g.HomeSiteId)?.Name}" : g.Position.ToString();
                    string status = g.Mode == GroupMode.OnMission ? $"on mission until {GameClock.TimeOfDay(g.BusyUntil, st.StartHour)}" : g.Mode.ToString().ToLowerInvariant();
                    if (g.IsPlayer)
                        status = "you";
                    Colour($"  {Trim(g.Name, 28),-28} {g.Info.Name,-26} {g.Members}/{g.MaxMembers}   {status,-26} {where}", g.IsAlive ? colour : ConsoleColor.DarkGray);
                }
                if (side == Side.Enemy)
                    Console.WriteLine("  (enemy ground units are only listed where Allied intel is fresh)");

                Console.WriteLine();
                Console.WriteLine($"  Totals: missions {force.MissionsFlown} flown, {force.MissionsSucceeded} succeeded. Aircraft lost {force.AircraftLost}, " +
                                  $"enemy aircraft shot down {force.AircraftKilled}. Ground units lost {force.GroundUnitsLost}, destroyed {force.GroundUnitsKilled}. " +
                                  $"Buildings destroyed {force.StructuresDestroyed}.");
            }
            Pause();
        }

        // ---------------------------------------------------------------- tuning

        /// <summary>
        /// Usage: --soak [hours] [seed] [--events]. Runs the AI war with no player and prints totals,
        /// for checking the pace of the campaign after changing settings. --events also lists key events.
        /// </summary>
        private static int Soak(string[] args)
        {
            double hours = args.Length > 1 && double.TryParse(args[1], out double h) ? h : 48;
            int seed = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 42;
            campaign = Campaign.New(mapPath, seed);
            var st = campaign.State;
            var missions = new List<string>();
            var keyEvents = new List<string>();
            bool showEvents = args.Contains("--events");
            LogEntry lastSeen = null;

            Heading($"Soak test: {hours} h, seed {seed}  (version {Version})");
            for (int hour = 1; hour <= hours && !st.Winner.HasValue; hour++)
            {
                campaign.AdvanceTime(3600);
                // The log keeps only its last 2000 entries, so collect mission lines as we go.
                int start = lastSeen == null ? 0 : st.Log.LastIndexOf(lastSeen) + 1;
                missions.AddRange(st.Log.Skip(start).Select(e => e.Text).Where(t => t.Contains(" by ") && t.Contains(": ")));
                foreach (var e in st.Log.Skip(start).Where(e => e.Level >= 2 || e.Text.Contains("CAPTURE") || e.Text.Contains("REINF") || e.Text.Contains("ESCORT")))
                    keyEvents.Add($"  {GameClock.DayAndTime(e.Time, st.StartHour)}  {e.Text}");
                lastSeen = st.Log.LastOrDefault();
                if (hour % 6 == 0)
                {
                    Console.WriteLine(st.Clock());
                    WriteSideSummary(Side.Allied);
                    WriteSideSummary(Side.Enemy);
                }
            }

            Console.WriteLine();
            foreach (var type in Enum.GetValues<TaskType>())
            {
                string code = TaskTypes.Get(type).Code;
                var lines = missions.Where(t => t.Contains($" {code} by ")).ToList();
                int lost = lines.Sum(t => Number(t, "Lost "));
                int kills = lines.Sum(t => Number(t, "Shot down "));
                int detected = lines.Count(t => t.EndsWith("Detected."));
                int success = lines.Count(t => t.Contains(": Success."));
                Console.WriteLine($"  {code,-7} missions {lines.Count,4}   success {success,4}   detected {detected,4}   aircraft lost {lost,3}   air kills {kills,3}");
            }
            foreach (Side side in new[] { Side.Allied, Side.Enemy })
            {
                var f = st.Force(side);
                Console.WriteLine($"  {side,-7} aircraft lost {f.AircraftLost}, ground units lost {f.GroundUnitsLost}, buildings destroyed {f.StructuresDestroyed}");
            }
            if (st.Winner.HasValue)
                Console.WriteLine($"  Winner at {st.Clock()}: {st.Winner}. {st.WinReason}");
            if (showEvents)
            {
                Console.WriteLine();
                Console.WriteLine("  Key events:");
                keyEvents.ForEach(Console.WriteLine);
            }
            return 0;

            static int Number(string text, string after)
            {
                int i = text.IndexOf(after, StringComparison.Ordinal);
                if (i < 0)
                    return 0;
                var digits = new string(text.Skip(i + after.Length).TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) ? n : 0;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void Heading(string text)
        {
            Colour($"== {text} ==", ConsoleColor.White);
        }

        private static void Colour(string text, ConsoleColor colour, bool newLine = true)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = colour;
            if (newLine)
                Console.WriteLine(text);
            else
                Console.Write(text);
            Console.ForegroundColor = old;
        }

        private static string Prompt(string text)
        {
            Console.Write($"{text}: ");
            return (Console.ReadLine() ?? "").Trim();
        }

        private static void Pause()
        {
            Console.Write("Press Enter to continue.");
            Console.ReadLine();
        }

        /// <summary>Time left in whole minutes, e.g. "22 min" or "1 h 27 min".</summary>
        private static string Minutes(double seconds)
        {
            int minutes = (int)Math.Floor(seconds / 60);
            if (minutes < 1)
                return "under 1 min";
            return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
        }

        private static string Trim(string text, int max) => text == null ? "" : text.Length <= max ? text : text.Substring(0, max - 1) + ".";
    }
}
