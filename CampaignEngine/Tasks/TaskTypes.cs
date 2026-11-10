using System;
using CampaignEngine.World;

namespace CampaignEngine.Tasks
{
    public enum TaskType
    {
        Recon,
        Strike,
        Sead,
        Bai,
        Cap,

        // Added for troop insertion. New values go at the end so older saves still load.
        Capture,
        Reinforce,
        Escort
    }

    public enum TaskTargetKind
    {
        Site,
        Group,
        Area
    }

    public class TaskTypeInfo
    {
        public string Name;
        public string Code;
        public string Description;

        /// <summary>0 to 10, as in EECH's task table.</summary>
        public double BasePriority;
        public double ExpiryMinutes;
        public int MinMembers;

        /// <summary>Time spent over the target (on station for CAP, landing and unloading for troop missions).</summary>
        public double LoiterMinutes;

        /// <summary>
        /// If the side's knowledge of the target sector is below this (0 to 1), a Recon mission
        /// is created instead.
        /// </summary>
        public double IntelThreshold;

        /// <summary>Share of the target destroyed for partial success and for full success.</summary>
        public double PartialFraction;
        public double SuccessFraction;

        /// <summary>Flown by transports carrying troops.</summary>
        public bool IsTroopMission;

        /// <summary>
        /// Known risk (0 to 1) at or above which an Escort mission is created alongside, as EECH did
        /// for insertions. 0 means never.
        /// </summary>
        public double EscortRiskThreshold;

        /// <summary>How suited a group type is to this mission (0 to 1).</summary>
        public Func<GroupTypeInfo, double> Capability;
    }

    public static class TaskTypes
    {
        public const int Count = 8;

        // Order must match the TaskType enum.
        private static readonly TaskTypeInfo[] Table =
        {
            new TaskTypeInfo { Name = "Reconnaissance", Code = "RECON", Description = "Fly over the target area to find out what is there.", BasePriority = 6, ExpiryMinutes = 90, MinMembers = 1, LoiterMinutes = 1, IntelThreshold = 0, Capability = g => g.Recon },
            new TaskTypeInfo { Name = "Strike", Code = "STRIKE", Description = "Destroy buildings at an enemy key site.", BasePriority = 9, ExpiryMinutes = 120, MinMembers = 2, LoiterMinutes = 1.5, IntelThreshold = 0.25, PartialFraction = 0.10, SuccessFraction = 0.25, Capability = g => g.Ground },
            new TaskTypeInfo { Name = "SEAD", Code = "SEAD", Description = "Suppress or destroy an enemy air defence site.", BasePriority = 9, ExpiryMinutes = 120, MinMembers = 2, LoiterMinutes = 1.5, IntelThreshold = 0.25, PartialFraction = 0.25, SuccessFraction = 0.75, Capability = g => g.Sead },
            new TaskTypeInfo { Name = "Battlefield air interdiction", Code = "BAI", Description = "Attack enemy ground forces near the front.", BasePriority = 4, ExpiryMinutes = 90, MinMembers = 1, LoiterMinutes = 2, IntelThreshold = 0.5, PartialFraction = 0.25, SuccessFraction = 0.75, Capability = g => g.Ground },
            new TaskTypeInfo { Name = "Combat air patrol", Code = "CAP", Description = "Patrol an area and intercept enemy aircraft.", BasePriority = 5, ExpiryMinutes = 60, MinMembers = 1, LoiterMinutes = 30, IntelThreshold = 0, Capability = g => g.Air },
            new TaskTypeInfo { Name = "Troop insertion (capture)", Code = "CAPTURE", Description = "Fly troops in to seize a damaged enemy site.", BasePriority = 8, ExpiryMinutes = 90, MinMembers = 1, LoiterMinutes = 3, IntelThreshold = 0.2, IsTroopMission = true, EscortRiskThreshold = 0.08, Capability = g => g.Transport },
            new TaskTypeInfo { Name = "Troop insertion (reinforce)", Code = "REINF", Description = "Fly troops in to strengthen the garrison of a friendly site.", BasePriority = 7, ExpiryMinutes = 60, MinMembers = 1, LoiterMinutes = 3, IntelThreshold = 0, IsTroopMission = true, EscortRiskThreshold = 0.10, Capability = g => g.Transport },
            new TaskTypeInfo { Name = "Escort", Code = "ESCORT", Description = "Protect a transport flight on a troop insertion.", BasePriority = 6, ExpiryMinutes = 90, MinMembers = 1, LoiterMinutes = 3, IntelThreshold = 0, PartialFraction = 0.25, SuccessFraction = 0.75, Capability = g => g.Air },
        };

        public static TaskTypeInfo Get(TaskType type) => Table[(int)type];
    }
}
