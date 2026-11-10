using System;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>Creates missions and answers questions about the mission pool.</summary>
    public static class TaskFactory
    {
        public static CampaignTask Create(CampaignState state, Side side, TaskType type, TaskTargetKind kind, int targetId,
            Vec2 position, string targetName, double ratingFraction, bool critical, string reason)
        {
            if (kind != TaskTargetKind.Area && IsTargeted(state, side, type, kind, targetId))
                return null;

            var info = TaskTypes.Get(type);
            if (info.IsTroopMission && state.Force(side).TroopReserve <= 0)
                return null;

            var origin = FindOriginSite(state, side, type, position);
            if (origin == null)
                return null;

            double expiryMinutes = critical ? Math.Min(info.ExpiryMinutes, 60) : info.ExpiryMinutes;
            var task = new CampaignTask
            {
                Id = state.NewId(),
                Type = type,
                Side = side,
                TargetKind = kind,
                TargetId = targetId,
                TargetPosition = position,
                TargetName = targetName,
                OriginSiteId = origin.Id,
                Rating = ratingFraction,
                Critical = critical,
                Priority = critical ? info.BasePriority : MathUtil.Clamp01(ratingFraction) * info.BasePriority,
                CreatedAt = state.Time,
                ExpiresAt = state.Time + expiryMinutes * 60,
                Reason = reason
            };
            var flightType = info.IsTroopMission ? GroupType.PlayerTransport : GroupType.PlayerFlight;
            task.KnownThreat = MissionResolver.EstimateKnownRisk(state, side, origin.Position, position,
                GroupTypes.Get(flightType).Speed, info.LoiterMinutes);

            state.Tasks.Add(task);
            state.AddLog($"{side} {info.Code} mission created: {targetName}, from {origin.Name}{(critical ? " [critical]" : "")}. {reason}", 0, side);

            if (info.EscortRiskThreshold > 0 && task.KnownThreat >= info.EscortRiskThreshold)
                CreateEscort(state, task);
            return task;
        }

        /// <summary>
        /// An Escort mission tied to a troop insertion, flown from the same base at the same time
        /// (EECH creates one when an insertion's difficulty passes a threshold).
        /// </summary>
        private static void CreateEscort(CampaignState state, CampaignTask escorted)
        {
            var info = TaskTypes.Get(TaskType.Escort);
            var escort = new CampaignTask
            {
                Id = state.NewId(),
                Type = TaskType.Escort,
                Side = escorted.Side,
                TargetKind = TaskTargetKind.Area,
                TargetId = escorted.Id,
                TargetPosition = escorted.TargetPosition,
                TargetName = $"Escort to {escorted.TargetName}",
                OriginSiteId = escorted.OriginSiteId,
                Rating = escorted.Rating,
                Critical = escorted.Critical,
                Priority = escorted.Priority * 0.8,
                KnownThreat = escorted.KnownThreat,
                CreatedAt = state.Time,
                ExpiresAt = escorted.ExpiresAt,
                EscortedTaskId = escorted.Id,
                Reason = $"Protect the {escorted.Info.Code} flight to {escorted.TargetName}."
            };
            escorted.EscortTaskId = escort.Id;
            state.Tasks.Add(escort);
            state.AddLog($"{escort.Side} ESCORT mission created for the {escorted.Info.Code} flight to {escorted.TargetName}.", 0, escort.Side);
        }

        /// <summary>
        /// True if the side already has a live mission of the same family against this target.
        /// Recon, strike, SEAD and BAI count as one family (look, then attack); troop missions are separate,
        /// so a capture can be planned while a strike on the same site is still waiting.
        /// </summary>
        public static bool IsTargeted(CampaignState state, Side side, TaskType type, TaskTargetKind kind, int targetId)
        {
            int family = Family(type);
            return state.Tasks.Exists(t => t.IsActive && t.Side == side && t.TargetKind == kind && t.TargetId == targetId && Family(t.Type) == family);
        }

        private static int Family(TaskType type)
        {
            switch (type)
            {
                case TaskType.Recon:
                case TaskType.Strike:
                case TaskType.Sead:
                case TaskType.Bai:
                    return 0;
                case TaskType.Capture:
                case TaskType.Reinforce:
                    return 1;
                default:
                    return 2;
            }
        }

        /// <summary>EECH allows only one mission of each type per sector per side.</summary>
        public static bool SectorHasTask(CampaignState state, Side side, TaskType type, Vec2 position)
        {
            int index = state.Grid.IndexOf(position);
            return state.Tasks.Exists(t => t.IsActive && t.Side == side && t.Type == type && state.Grid.IndexOf(t.TargetPosition) == index);
        }

        public static bool HasCapNear(CampaignState state, Side side, Vec2 position, double radius)
        {
            if (state.Tasks.Exists(t => t.IsActive && t.Side == side && t.Type == TaskType.Cap && Vec2.Distance(t.TargetPosition, position) <= radius))
                return true;
            return state.Patrols.Exists(p => p.Side == side && p.EndTime > state.Time && Vec2.Distance(p.Position, position) <= radius);
        }

        /// <summary>
        /// Picks the base to fly a mission from, after EECH's find_most_suitable_keysite_for_task:
        /// favour bases with idle suitable flights, then nearer bases. Allied airbases can always offer
        /// a mission to the player if it is within the player's range.
        /// </summary>
        public static KeySite FindOriginSite(CampaignState state, Side side, TaskType type, Vec2 target)
        {
            var info = TaskTypes.Get(type);
            var playerInfo = GroupTypes.Get(info.IsTroopMission ? GroupType.PlayerTransport : GroupType.PlayerFlight);
            KeySite best = null;
            double bestScore = 0;

            foreach (var site in state.Sites)
            {
                if (site.Side != side || !site.IsUsable || !site.Info.HostsAircraft)
                    continue;

                double distance = Vec2.Distance(site.Position, target);
                int idle = 0, busy = 0;
                foreach (var group in state.Groups)
                {
                    if (group.HomeSiteId != site.Id || group.IsPlayer || !group.IsAlive)
                        continue;
                    if (info.Capability(group.Info) < 0.5 || group.Info.CombatRadius < distance)
                        continue;
                    if (group.Mode == GroupMode.Idle && group.Members >= info.MinMembers)
                        idle++;
                    else
                        busy++;
                }

                double score = Math.Min(12, idle * 5 + busy * 0.5);
                if (side == Side.Allied && site.Type == SiteType.Airbase && playerInfo.CombatRadius >= distance)
                    score = Math.Max(score, 1);
                if (score <= 0)
                    continue;

                score *= Math.Max(0.1, 1.5 - distance / 200000);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = site;
                }
            }

            return best;
        }

        /// <summary>Whether the mission's target is still worth flying to.</summary>
        public static bool TargetStillValid(CampaignState state, CampaignTask task)
        {
            switch (task.Type)
            {
                case TaskType.Escort:
                    var escorted = state.Task(task.EscortedTaskId);
                    return escorted != null && escorted.State == TaskState.Available;

                case TaskType.Capture:
                    var enemySite = state.Site(task.TargetId);
                    return enemySite != null && enemySite.Side != task.Side;

                case TaskType.Reinforce:
                    var ownSite = state.Site(task.TargetId);
                    return ownSite != null && ownSite.Side == task.Side;
            }

            switch (task.TargetKind)
            {
                case TaskTargetKind.Site:
                    var site = state.Site(task.TargetId);
                    if (site == null || site.Side == task.Side)
                        return false;
                    return task.Type == TaskType.Recon || site.State != SiteState.Unusable;
                case TaskTargetKind.Group:
                    var group = state.Group(task.TargetId);
                    return group != null && group.IsAlive && group.Side != task.Side;
                default:
                    return true;
            }
        }
    }
}
