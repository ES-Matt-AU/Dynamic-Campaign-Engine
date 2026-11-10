using CampaignEngine.Core;
using Newtonsoft.Json;

namespace CampaignEngine.Tasks
{
    public enum TaskState
    {
        Available,
        Assigned,
        Completed,
        Expired
    }

    public enum TaskResult
    {
        None,
        Success,
        Partial,
        Failure
    }

    /// <summary>A mission in the shared pool. AI flights and the player draw from the same list.</summary>
    public class CampaignTask
    {
        public int Id;
        public TaskType Type;
        public Side Side;

        public TaskTargetKind TargetKind;
        public int TargetId = -1;
        public Vec2 TargetPosition;
        public string TargetName;

        /// <summary>Base the mission is flown from.</summary>
        public int OriginSiteId = -1;

        public double Priority;
        public double Rating;
        public bool Critical;

        /// <summary>Expected losses along the route from threats the side knows about (higher is worse).</summary>
        public double KnownThreat;

        public double CreatedAt;
        public double ExpiresAt;

        /// <summary>When the flight took off.</summary>
        public double StartedAt;

        /// <summary>When the flight lands back (set at take-off, as missions are simulated in one go).</summary>
        public double FinishedAt;

        public TaskState State = TaskState.Available;
        public TaskResult Result = TaskResult.None;
        public int AssignedGroupId = -1;
        public bool FlownByPlayer;

        /// <summary>Why the mission exists, e.g. "Generated" or "Reaction: ...".</summary>
        public string Reason;

        /// <summary>For an Escort mission: the troop insertion it protects.</summary>
        public int EscortedTaskId = -1;

        /// <summary>For a troop insertion: its Escort mission, if one was created.</summary>
        public int EscortTaskId = -1;

        [JsonIgnore] public TaskTypeInfo Info => TaskTypes.Get(Type);
        [JsonIgnore] public bool IsActive => State == TaskState.Available || State == TaskState.Assigned;
    }
}
