using System;
using System.Collections.Generic;
using System.Linq;
using CampaignEngine.Ai;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Sim
{
    /// <summary>
    /// Simulates a sortie instead of flying it in VTOL VR. The flight is walked along its route in
    /// short steps. At each step every enemy sensor in range adds to the exposure score, and every
    /// weapon in range gets a chance to shoot. Detection is a single roll made at take-off: the flight
    /// is detected at the moment its detection chance climbs past that roll, and from then on the
    /// defences fire at full effect instead of a reduced "surprise" rate. An escort, if any, flies the
    /// same route, takes its own share of fire and keeps enemy fighters off the main flight.
    /// </summary>
    public static class MissionResolver
    {
        private class Observer
        {
            public string Name;
            public Vec2 Position;
            public double SensorRange;
            public double SensorQuality;
            public double ThreatRange;
            public double Lethality;
            public bool IsAirDefence;
            public UnitGroup Group;
            public ActivePatrol Patrol;

            public bool WasInRange;
            public double SecondsSeen;
            public int Sightings;
            public double Score;

            public double Strength => Group != null ? Group.StrengthFraction : 1;
            public bool Alive => Group == null || Group.IsAlive;
        }

        /// <summary>One flight's state during the walk.</summary>
        private class FlightRun
        {
            public FlightSpec Spec;
            public bool[] Alive;
            public int AliveCount;
            public int Lost;
            public int[] Troops;

            public FlightRun(FlightSpec spec)
            {
                Spec = spec;
                Alive = Enumerable.Repeat(true, spec.Members).ToArray();
                AliveCount = spec.Members;
                Troops = new int[spec.Members];
            }

            public double AliveFraction => Spec.Members == 0 ? 0 : (double)AliveCount / Spec.Members;

            public string Who(int i) => Spec.IsPlayer ? (i == 0 ? "You were" : "Your wingman was") : $"{Spec.Name} aircraft {i + 1} was";
        }

        public static MissionReport Fly(CampaignState state, CampaignTask task, FlightSpec flight, FlightSpec escort = null, CampaignTask escortTask = null)
        {
            var settings = state.Settings;
            var dice = state.Dice;
            var info = task.Info;

            Vec2 origin = flight.Origin.Position;
            Vec2 target = task.TargetPosition;
            double legTime = Vec2.Distance(origin, target) / flight.Info.Speed;
            double loiter = info.LoiterMinutes * 60;
            double total = legTime * 2 + loiter;

            var main = new FlightRun(flight);
            var esc = escort != null ? new FlightRun(escort) : null;

            var report = new MissionReport
            {
                TaskId = task.Id,
                TaskType = task.Type,
                Side = flight.Side,
                FlightName = flight.Name,
                TargetName = task.TargetName,
                OriginName = flight.Origin.Name,
                IsPlayer = flight.IsPlayer,
                StartTime = state.Time,
                Duration = total,
                DistanceKm = Vec2.Distance(origin, target) * 2 / 1000,
                AircraftStart = flight.Members,
                EscortName = escort?.Name,
                EscortStart = escort?.Members ?? 0,
                EscortIsPlayer = escort?.IsPlayer ?? false,
                DetectionRoll = dice.NextDouble()
            };

            if (info.IsTroopMission)
                LoadTroops(state, main, report);

            var observers = BuildObservers(state, flight.Side, knownOnly: false);
            double exposure = 0;
            bool effectDone = false;
            Vec2 lastObserved = new Vec2(double.MaxValue, double.MaxValue);
            double dt = settings.RouteStepSeconds;

            string escortText = esc == null ? "" : escort.IsPlayer ? $", escorted by you ({escort.Members} aircraft)" : $", escorted by {escort.Name} ({escort.Members})";
            string troopText = report.TroopsAboard > 0 ? $" carrying {report.TroopsAboard} troops" : "";
            report.Timeline.Add($"{Clock(state, 0)}  {flight.Name} took off from {flight.Origin.Name} with {flight.Members} aircraft{troopText}{escortText}.");

            for (double t = 0; t < total && main.AliveCount > 0; t += dt)
            {
                double step = Math.Min(dt, total - t);
                double mid = t + step / 2;
                Vec2 pos;
                bool overTarget = false;
                if (mid < legTime)
                    pos = Vec2.Lerp(origin, target, mid / legTime);
                else if (mid < legTime + loiter)
                {
                    pos = target;
                    overTarget = true;
                }
                else
                    pos = Vec2.Lerp(target, origin, (mid - legTime - loiter) / legTime);

                if (overTarget && !effectDone)
                {
                    effectDone = true;
                    report.Timeline.Add($"{Clock(state, t)}  Arrived at {task.TargetName}.");
                    ApplyEffect(state, task, main, report, legTime, loiter);
                    report.Timeline.Add($"{Clock(state, t)}  {report.Effect}");
                }

                double now = state.Time + t;
                bool seenThisStep = false;
                foreach (var obs in observers)
                {
                    if (!obs.Alive || (obs.Patrol != null && (now < obs.Patrol.StartTime || now > obs.Patrol.EndTime)))
                    {
                        obs.WasInRange = false;
                        continue;
                    }

                    double d = Vec2.Distance(pos, obs.Position);

                    // Being watched
                    bool inRange = d <= obs.SensorRange;
                    if (inRange)
                    {
                        double gained = obs.SensorQuality * (1 - 0.8 * d / obs.SensorRange) * (1 - flight.Info.Stealth) * step / 60;
                        if (!obs.WasInRange)
                        {
                            obs.Sightings++;
                            report.Sightings++;
                            gained += settings.SightingWeight;
                            if (obs.Sightings == 1)
                                report.Timeline.Add($"{Clock(state, t)}  Seen by {obs.Name} at {d / 1000:0} km.");
                        }
                        obs.SecondsSeen += step;
                        obs.Score += gained;
                        exposure += gained;
                        if (!seenThisStep)
                        {
                            report.ObservedSeconds += step;
                            seenThisStep = true;
                        }
                        if (Vec2.Distance(pos, lastObserved) > 5000)
                        {
                            report.ObservedPositions.Add(pos);
                            lastObserved = pos;
                        }

                        double chance = 1 - Math.Exp(-settings.DetectionK * exposure);
                        if (!report.Detected && chance >= report.DetectionRoll)
                        {
                            report.Detected = true;
                            report.DetectedAt = now;
                            report.DetectedBy = obs.Name;
                            report.DetectedPosition = pos;
                            report.Timeline.Add($"{Clock(state, t)}  DETECTED by {obs.Name}. Enemy defences are now fully alert.");
                        }
                    }
                    obs.WasInRange = inRange;

                    // Being shot at
                    if (d > obs.ThreatRange)
                        continue;

                    double escortCover = esc != null && obs.Patrol != null
                        ? settings.EscortProtection * esc.Spec.Info.Air * esc.AliveFraction
                        : 0;
                    RollLosses(state, report, main, obs, step, escortCover, effectDone, t);
                    if (esc != null)
                        RollLosses(state, report, esc, obs, step, 0, true, t);

                    // Flights that meet a patrol shoot back; the escort is there for exactly this.
                    if (obs.Patrol != null && main.AliveCount > 0)
                    {
                        double back = settings.AirToAirLethality * (flight.Info.Air * main.AliveFraction + (esc != null ? esc.Spec.Info.Air * esc.AliveFraction : 0));
                        double pBack = 1 - Math.Exp(-back * step / 60);
                        int patrolMembers = obs.Group.Members;
                        for (int m = 0; m < patrolMembers && back > 0; m++)
                        {
                            if (!dice.Chance(pBack))
                                continue;
                            obs.Group.Members--;
                            report.EnemyAircraftKilled++;
                            report.Timeline.Add($"{Clock(state, t)}  Shot down an aircraft from {obs.Name}.");
                            if (!obs.Group.IsAlive)
                            {
                                obs.Group.Mode = GroupMode.Destroyed;
                                break;
                            }
                        }
                    }
                }
            }

            if (!effectDone)
            {
                report.Result = TaskResult.Failure;
                report.Effect = "The flight did not reach the target.";
                report.Timeline.Add($"{Clock(state, total)}  {report.Effect}");
            }
            else if (task.Type == TaskType.Cap)
            {
                report.Result = main.AliveCount == flight.Members ? TaskResult.Success : main.AliveCount > 0 ? TaskResult.Partial : TaskResult.Failure;
            }

            if (main.AliveCount > 0)
                report.Timeline.Add($"{Clock(state, total)}  {flight.Name} landed at {flight.Origin.Name} with {main.AliveCount} of {flight.Members} aircraft.");
            if (esc != null && esc.AliveCount > 0)
                report.Timeline.Add($"{Clock(state, total)}  {escort.Name} landed with {esc.AliveCount} of {escort.Members} aircraft.");

            report.AircraftLost = main.Lost;
            report.EscortLost = esc?.Lost ?? 0;
            report.ExposureScore = exposure;
            report.DetectionChance = 1 - Math.Exp(-settings.DetectionK * exposure);
            report.Observers = observers
                .Where(o => o.Sightings > 0)
                .OrderByDescending(o => o.Score)
                .Select(o => new ObserverExposure { Name = o.Name, Seconds = o.SecondsSeen, Sightings = o.Sightings, Score = o.Score })
                .ToList();

            // Any troops still aboard at the end (mission failed before landing) go home with the flight.
            int unused = main.Troops.Sum();
            if (unused > 0)
                state.Force(flight.Side).TroopReserve += unused;

            ApplyLosses(state, flight, main, report, report.EnemyAircraftKilled);
            FinishTask(state, task, report.Result, report.Duration);
            if (report.Result == TaskResult.Success)
                state.Force(flight.Side).MissionsSucceeded++;

            if (esc != null)
            {
                // EECH judges an escort on how much of the escorted flight survived.
                double kept = main.AliveFraction;
                var escInfo = TaskTypes.Get(TaskType.Escort);
                report.EscortResult = kept >= escInfo.SuccessFraction ? TaskResult.Success : kept >= escInfo.PartialFraction ? TaskResult.Partial : TaskResult.Failure;
                ApplyLosses(state, escort, esc, report, 0);
                if (escortTask != null)
                    FinishTask(state, escortTask, report.EscortResult, report.Duration);
                if (report.EscortResult == TaskResult.Success)
                    state.Force(escort.Side).MissionsSucceeded++;
            }

            return report;
        }

        private static void RollLosses(CampaignState state, MissionReport report, FlightRun run, Observer obs, double step, double escortCover, bool landed, double t)
        {
            var settings = state.Settings;
            var info = run.Spec.Info;

            double lambda = obs.Lethality * obs.Strength * (1 - 0.5 * info.Stealth);
            if (!report.Detected)
                lambda *= settings.UndetectedLethalityFactor;
            if (obs.IsAirDefence)
                lambda *= 1 - 0.4 * info.Sead;
            if (obs.Patrol != null)
                lambda *= (1 - 0.5 * info.Air) * (1 - escortCover);
            if (run.Spec.IsPlayer)
                lambda *= settings.PlayerLethalityFactor;
            double pKill = 1 - Math.Exp(-lambda * step / 60);

            for (int i = 0; i < run.Alive.Length; i++)
            {
                if (!run.Alive[i] || !state.Dice.Chance(pKill))
                    continue;
                run.Alive[i] = false;
                run.AliveCount--;
                run.Lost++;
                if (run.Spec.IsPlayer && i == 0)
                    report.PlayerShotDown = true;

                string troops = "";
                if (!landed && run.Troops[i] > 0)
                {
                    troops = $" {run.Troops[i]} troops aboard were lost.";
                    report.TroopsLost += run.Troops[i];
                    state.Force(run.Spec.Side).GroundUnitsLost += run.Troops[i];
                    state.Force(run.Spec.Side.Opponent()).GroundUnitsKilled += run.Troops[i];
                    run.Troops[i] = 0;
                }
                report.Timeline.Add($"{Clock(state, t)}  {run.Who(i)} shot down by {obs.Name}.{troops}");
            }
        }

        /// <summary>Fills the transports from the side's ground reserve.</summary>
        private static void LoadTroops(CampaignState state, FlightRun run, MissionReport report)
        {
            var force = state.Force(run.Spec.Side);
            for (int i = 0; i < run.Troops.Length; i++)
            {
                int load = Math.Min(run.Spec.Info.TroopCapacity, force.TroopReserve);
                run.Troops[i] = load;
                force.TroopReserve -= load;
                report.TroopsAboard += load;
            }
        }

        private static void ApplyLosses(CampaignState state, FlightSpec flight, FlightRun run, MissionReport report, int enemyKilled)
        {
            var own = state.Force(flight.Side);
            var enemy = state.Force(flight.Side.Opponent());

            own.AircraftLost += run.Lost;
            enemy.AircraftKilled += run.Lost;
            own.AircraftKilled += enemyKilled;
            enemy.AircraftLost += enemyKilled;
            own.MissionsFlown++;
            if (flight.IsPlayer && report.PlayerShotDown)
                own.PlayerEjections++;

            if (flight.Group != null)
            {
                flight.Group.Members = run.AliveCount;
                if (run.AliveCount == 0 && !flight.Group.IsPlayer)
                    flight.Group.Mode = GroupMode.Destroyed;
            }
        }

        private static void FinishTask(CampaignState state, CampaignTask task, TaskResult result, double duration)
        {
            task.State = TaskState.Completed;
            task.Result = result;
            task.StartedAt = state.Time;
            task.FinishedAt = state.Time + duration;
        }

        private static void ApplyEffect(CampaignState state, CampaignTask task, FlightRun run, MissionReport report, double legTime, double loiter)
        {
            var settings = state.Settings;
            var dice = state.Dice;
            var info = task.Info;
            var flight = run.Spec;
            int aliveCount = run.AliveCount;

            switch (task.Type)
            {
                case TaskType.Strike:
                {
                    var site = state.Site(task.TargetId);
                    if (site == null || site.Side == flight.Side || site.State == SiteState.Unusable)
                    {
                        report.Result = TaskResult.Partial;
                        report.Effect = "The target was already out of action.";
                        return;
                    }

                    int before = site.Structures;
                    int hits = 0;
                    double p = settings.StrikeHitChance * flight.Info.Ground;
                    for (int i = 0; i < aliveCount * settings.StrikeAttemptsPerAircraft; i++)
                        if (dice.Chance(p))
                            hits++;
                    int destroyed = Math.Min(hits, site.Structures);
                    site.Structures -= destroyed;
                    state.Force(flight.Side).StructuresDestroyed += destroyed;

                    bool knockedOut = site.Efficiency < settings.MinEfficiency;
                    if (knockedOut)
                    {
                        site.State = SiteState.Unusable;
                        site.UnusableSince = state.Time;
                        state.AddLog($"{site.Name} ({site.Side}) has been put out of action.", 2, site.Side);
                    }

                    double fraction = before == 0 ? 0 : (double)destroyed / before;
                    report.Result = knockedOut || fraction >= info.SuccessFraction ? TaskResult.Success
                        : fraction >= info.PartialFraction ? TaskResult.Partial : TaskResult.Failure;
                    report.Effect = $"{destroyed} of {before} buildings destroyed at {site.Name}"
                        + (knockedOut ? "; the site is out of action." : $" (now at {site.Efficiency:P0} strength).");
                    return;
                }

                case TaskType.Sead:
                case TaskType.Bai:
                {
                    var group = state.Group(task.TargetId);
                    if (group == null || !group.IsAlive)
                    {
                        report.Result = TaskResult.Partial;
                        report.Effect = "The target had already been destroyed.";
                        return;
                    }

                    bool sead = task.Type == TaskType.Sead;
                    double p = sead ? settings.SeadKillChance * flight.Info.Sead : settings.BaiKillChance * flight.Info.Ground;
                    int attempts = aliveCount * (sead ? settings.SeadAttemptsPerAircraft : settings.BaiAttemptsPerAircraft);
                    int before = group.Members;
                    int kills = 0;
                    for (int i = 0; i < attempts && kills < before; i++)
                        if (dice.Chance(p))
                            kills++;
                    group.Members -= kills;
                    state.Force(flight.Side).GroundUnitsKilled += kills;
                    state.Force(group.Side).GroundUnitsLost += kills;
                    if (!group.IsAlive)
                    {
                        group.Mode = GroupMode.Destroyed;
                        state.AddLog($"{group.Name} ({group.Side}) has been destroyed.", 2, group.Side);
                    }

                    double fraction = (double)kills / before;
                    bool success = before <= 4 ? !group.IsAlive : fraction >= info.SuccessFraction;
                    report.Result = success ? TaskResult.Success : fraction >= info.PartialFraction ? TaskResult.Partial : TaskResult.Failure;
                    report.Effect = $"{kills} of {before} {group.Info.MemberName}s destroyed in {group.Name}"
                        + (group.IsAlive ? "." : "; the unit is wiped out.");
                    return;
                }

                case TaskType.Recon:
                {
                    Intel.Reveal(state, flight.Side, task.TargetPosition, settings.ReconRevealRadius);
                    var enemy = flight.Side.Opponent();
                    double r = settings.ReconRevealRadius;
                    int units = state.Groups.Count(g => g.Side == enemy && g.IsAlive && !g.Info.IsAir && Vec2.Distance(g.Position, task.TargetPosition) <= r);
                    int sites = state.Sites.Count(s => s.Side == enemy && Vec2.Distance(s.Position, task.TargetPosition) <= r);
                    report.Result = TaskResult.Success;
                    report.Effect = $"Area photographed: {units} enemy units and {sites} sites within {r / 1000:0} km.";
                    return;
                }

                case TaskType.Cap:
                {
                    if (flight.Group != null)
                    {
                        state.Patrols.Add(new ActivePatrol
                        {
                            TaskId = task.Id,
                            Side = flight.Side,
                            GroupId = flight.Group.Id,
                            Position = task.TargetPosition,
                            StartTime = state.Time + legTime,
                            EndTime = state.Time + legTime + loiter
                        });
                    }
                    report.Effect = $"On station for {GameClock.Duration(loiter)}.";
                    return;
                }

                case TaskType.Capture:
                case TaskType.Reinforce:
                {
                    int landed = run.Troops.Sum();
                    Array.Clear(run.Troops, 0, run.Troops.Length);
                    report.TroopsLanded = landed;
                    var site = state.Site(task.TargetId);

                    if (site == null || landed == 0)
                    {
                        state.Force(flight.Side).TroopReserve += landed;
                        report.Result = TaskResult.Failure;
                        report.Effect = "No troops could be landed.";
                        return;
                    }

                    if (task.Type == TaskType.Reinforce || site.Side == flight.Side)
                    {
                        if (site.Side != flight.Side)
                        {
                            state.Force(flight.Side).TroopReserve += landed;
                            report.Result = TaskResult.Failure;
                            report.Effect = $"{site.Name} had already fallen; the troops were flown back.";
                            return;
                        }
                        SiteCapture.Garrison(state, site, landed);
                        report.Result = TaskResult.Success;
                        report.Effect = $"{landed} troops landed and joined the garrison at {site.Name}.";
                        return;
                    }

                    var odds = SiteCapture.Odds(state, site, landed, report.Detected);
                    report.CaptureChance = odds.Chance;
                    report.CaptureRoll = dice.NextDouble();
                    string detail = $"(capture chance {odds.Chance:P0}, roll {report.CaptureRoll * 100:0})";

                    if (report.CaptureRoll < odds.Chance)
                    {
                        report.Captured = true;
                        report.Result = TaskResult.Success;
                        report.Effect = $"{landed} troops landed and CAPTURED {site.Name} {detail}.";
                        SiteCapture.Capture(state, site, flight.Side, landed);
                    }
                    else
                    {
                        state.Force(flight.Side).GroundUnitsLost += landed;
                        state.Force(site.Side).GroundUnitsKilled += landed;
                        report.Result = TaskResult.Failure;
                        report.Effect = $"{landed} troops landed but were beaten off at {site.Name} {detail}.";
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Expected losses per aircraft (as a 0 to 1 chance) on a straight route to the target and back,
        /// counting only threats the side knows about and assuming it is detected. Used to show risk in the mission list.
        /// </summary>
        public static double EstimateKnownRisk(CampaignState state, Side side, Vec2 origin, Vec2 target, double speed, double loiterMinutes)
        {
            var observers = BuildObservers(state, side, knownOnly: true);
            double legTime = Vec2.Distance(origin, target) / speed;
            double loiter = loiterMinutes * 60;
            double total = legTime * 2 + loiter;
            double dt = state.Settings.RouteStepSeconds;
            double sum = 0;

            for (double t = 0; t < total; t += dt)
            {
                double step = Math.Min(dt, total - t);
                double mid = t + step / 2;
                Vec2 pos = mid < legTime ? Vec2.Lerp(origin, target, mid / legTime)
                    : mid < legTime + loiter ? target
                    : Vec2.Lerp(target, origin, (mid - legTime - loiter) / legTime);

                foreach (var obs in observers)
                    if (Vec2.Distance(pos, obs.Position) <= obs.ThreatRange)
                        sum += obs.Lethality * obs.Strength * step / 60;
            }

            return 1 - Math.Exp(-sum);
        }

        public static string RiskLabel(double risk)
        {
            if (risk < 0.05) return "Low";
            if (risk < 0.15) return "Medium";
            if (risk < 0.30) return "High";
            return "Very high";
        }

        private static List<Observer> BuildObservers(CampaignState state, Side attacker, bool knownOnly)
        {
            var hostile = attacker.Opponent();
            var list = new List<Observer>();

            foreach (var site in state.Sites)
            {
                if (site.Side != hostile || site.Info.SensorRange <= 0)
                    continue;
                var obs = new Observer
                {
                    Name = site.Name,
                    Position = site.Position,
                    SensorRange = site.Info.SensorRange,
                    SensorQuality = site.Info.SensorQuality * (site.IsUsable ? 1 : 0.5)
                };
                ApplyAlert(state, hostile, obs);
                list.Add(obs);
            }

            foreach (var group in state.Groups)
            {
                if (group.Side != hostile || !group.IsAlive || group.Info.IsAir || group.Info.SensorRange <= 0)
                    continue;
                if (knownOnly && Intel.Knowledge(state, attacker, group.Position) < 0.5)
                    continue;
                var obs = new Observer
                {
                    Name = group.Name,
                    Position = group.Position,
                    SensorRange = group.Info.SensorRange,
                    SensorQuality = group.Info.SensorQuality,
                    ThreatRange = group.Info.ThreatRange,
                    Lethality = group.Info.Lethality,
                    IsAirDefence = group.Info.IsAirDefence,
                    Group = group
                };
                ApplyAlert(state, hostile, obs);
                list.Add(obs);
            }

            if (!knownOnly)
            {
                foreach (var patrol in state.Patrols)
                {
                    var group = state.Group(patrol.GroupId);
                    if (patrol.Side != hostile || group == null || !group.IsAlive)
                        continue;
                    var gi = GroupTypes.Get(GroupType.FighterFlight);
                    list.Add(new Observer
                    {
                        Name = $"{group.Name} (CAP)",
                        Position = patrol.Position,
                        SensorRange = gi.SensorRange,
                        SensorQuality = gi.SensorQuality,
                        ThreatRange = gi.ThreatRange,
                        Lethality = gi.Lethality * Math.Max(group.Info.Air, 0.3),
                        Group = group,
                        Patrol = patrol
                    });
                }
            }

            return list;
        }

        private static void ApplyAlert(CampaignState state, Side observerSide, Observer obs)
        {
            if (!Intel.IsAlerted(state, observerSide, obs.Position))
                return;
            obs.SensorQuality = Math.Min(1, obs.SensorQuality * 1.2);
            obs.Lethality *= state.Settings.AlertLethalityFactor;
        }

        private static string Clock(CampaignState state, double offset) => GameClock.TimeOfDay(state.Time + offset, state.StartHour);
    }
}
