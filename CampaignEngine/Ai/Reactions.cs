using System.Linq;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.Sim;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Ai
{
    /// <summary>
    /// What happens after a sortie: the defender reacts if it detected the flight, and successful
    /// missions lead on to the next step (EECH ai/highlevl/reaction.c). Unlike EECH, where the
    /// defender knows about an attack as soon as it is assigned, here it only reacts if it saw it.
    /// </summary>
    public static class Reactions
    {
        public static void AfterMission(CampaignState state, CampaignTask task, MissionReport report)
        {
            // A patrol over its own side's ground is not an intrusion, and reinforcing your own site is
            // not a threat, so only attacks, recon and capture attempts draw a reaction.
            if (report.Detected && task.Type != TaskType.Cap && task.Type != TaskType.Reinforce)
                DefenderReaction(state, task, report);

            if (task.Type == TaskType.Recon && report.Result == TaskResult.Success)
                FollowUpRecon(state, task, report);

            if (task.Type == TaskType.Strike && (report.Result == TaskResult.Success || report.Result == TaskResult.Partial))
            {
                var site = state.Site(task.TargetId);
                if (site == null || site.Side == task.Side)
                    return;

                if (site.State != SiteState.Unusable)
                {
                    Add(report, TaskFactory.Create(state, task.Side, TaskType.Strike, TaskTargetKind.Site, site.Id, site.Position, site.Name,
                        1, true, $"Follow-up: {site.Name} is damaged but still working."));
                }
                else
                {
                    // EECH: a site knocked below minimum efficiency invites a troop insertion,
                    // and its owner flies in troops to hold it.
                    if (SiteCapture.WorthTrying(state, task.Side, site))
                        Add(report, TaskFactory.Create(state, task.Side, TaskType.Capture, TaskTargetKind.Site, site.Id, site.Position, site.Name,
                            1, false, $"Follow-up: {site.Name} is out of action and can be taken."));
                    var defence = TaskFactory.Create(state, site.Side, TaskType.Reinforce, TaskTargetKind.Site, site.Id, site.Position, site.Name,
                        0.9, false, $"{site.Name} is out of action and must be held.");
                    if (defence != null)
                        report.Reactions.Add($"{site.Side} is flying troops in to hold {site.Name}.");
                }
            }
        }

        private static void DefenderReaction(CampaignState state, CampaignTask task, MissionReport report)
        {
            var s = state.Settings;
            var defender = task.Side.Opponent();
            double score = report.ExposureScore;

            // 1. Every detection puts the defences along the observed route on alert.
            foreach (var pos in report.ObservedPositions)
                Intel.Alert(state, defender, pos);
            report.Reactions.Add($"{defender} air defences along the route are on alert for {s.AlertMinutes / 60:0} h.");

            // 2. A spotted capture attempt that failed makes the defender reinforce the garrison.
            if (task.Type == TaskType.Capture && !report.Captured)
            {
                var site = state.Site(task.TargetId);
                if (site != null && site.Side == defender)
                {
                    var reinforce = TaskFactory.Create(state, defender, TaskType.Reinforce, TaskTargetKind.Site, site.Id, site.Position, site.Name,
                        1, true, $"Reaction: an enemy troop landing at {site.Name} was beaten off.");
                    if (reinforce != null)
                        report.Reactions.Add($"{defender} is flying in troops to reinforce {site.Name}.");
                }
            }

            // 3. A well-tracked flight draws a defensive patrol over the area it was heading for.
            if (score >= s.ReactionCapThreshold)
            {
                var capPos = task.TargetKind == TaskTargetKind.Area ? report.DetectedPosition : task.TargetPosition;
                if (!TaskFactory.HasCapNear(state, defender, capPos, 20000))
                {
                    var cap = TaskFactory.Create(state, defender, TaskType.Cap, TaskTargetKind.Area, -1, capPos,
                        $"Patrol near {task.TargetName}", 1, true, $"Reaction: {report.FlightName} was detected.");
                    if (cap != null)
                        report.Reactions.Add($"{defender} has ordered a combat air patrol near {task.TargetName}.");
                }
            }

            // 4. A flight tracked for a long time gives away where it came from.
            if (score >= s.ReactionStrikeThreshold)
            {
                var origin = state.Site(task.OriginSiteId);
                if (origin != null && origin.Side == task.Side && origin.State != SiteState.Unusable)
                {
                    Intel.Reveal(state, defender, origin.Position, 10000);
                    var strike = TaskFactory.Create(state, defender, TaskType.Strike, TaskTargetKind.Site, origin.Id, origin.Position, origin.Name,
                        1, true, $"Reaction: {report.FlightName} was tracked back to {origin.Name}.");
                    if (strike != null)
                        report.Reactions.Add($"{defender} tracked the flight back to {origin.Name} and is planning a counter-strike.");
                }
            }
        }

        /// <summary>After a successful recon: SEAD on nearby air defences, then the real attack or a troop insertion.</summary>
        private static void FollowUpRecon(CampaignState state, CampaignTask task, MissionReport report)
        {
            var side = task.Side;
            var enemy = side.Opponent();

            var defences = state.Groups
                .Where(g => g.Side == enemy && g.IsAlive && g.Info.IsAirDefence && g.Id != task.TargetId)
                .Where(g => Vec2.Distance(g.Position, task.TargetPosition) <= 15000)
                .OrderBy(g => Vec2.Distance(g.Position, task.TargetPosition))
                .Take(2);
            foreach (var g in defences)
                Add(report, TaskFactory.Create(state, side, TaskType.Sead, TaskTargetKind.Group, g.Id, g.Position, g.Name, 1, true,
                    $"Follow-up: recon found {g.Name} near {task.TargetName}."));

            if (task.TargetKind == TaskTargetKind.Site)
            {
                var site = state.Site(task.TargetId);
                if (site == null || site.Side != enemy)
                    return;

                if (site.IsUsable && site.Efficiency >= state.Settings.MinEfficiency + 0.2)
                    Add(report, TaskFactory.Create(state, side, TaskType.Strike, TaskTargetKind.Site, site.Id, site.Position, site.Name, 1, true,
                        $"Follow-up: recon of {site.Name} complete."));
                else if (SiteCapture.WorthTrying(state, side, site))
                    Add(report, TaskFactory.Create(state, side, TaskType.Capture, TaskTargetKind.Site, site.Id, site.Position, site.Name, 1, false,
                        $"Follow-up: recon shows {site.Name} is badly damaged and can be taken."));
            }
            else if (task.TargetKind == TaskTargetKind.Group)
            {
                var group = state.Group(task.TargetId);
                if (group != null && group.IsAlive && group.Side == enemy)
                {
                    var type = group.Info.IsAirDefence ? TaskType.Sead : TaskType.Bai;
                    Add(report, TaskFactory.Create(state, side, type, TaskTargetKind.Group, group.Id, group.Position, group.Name, 1, true,
                        $"Follow-up: recon of {group.Name} complete."));
                }
            }
        }

        private static void Add(MissionReport report, CampaignTask created)
        {
            if (created != null)
                report.Reactions.Add($"New {created.Info.Code} mission: {created.TargetName}.");
        }
    }
}
