using CampaignEngine.Core;
using Newtonsoft.Json;

namespace CampaignEngine.World
{
    public enum GroupMode
    {
        Idle,
        OnMission,
        Destroyed
    }

    /// <summary>A flight of aircraft or a ground unit (SAM site, armour company and so on).</summary>
    public class UnitGroup
    {
        public int Id;
        public string Name;
        public GroupType Type;
        public Side Side;
        public Vec2 Position;

        /// <summary>Base the flight operates from. -1 for ground units.</summary>
        public int HomeSiteId = -1;

        public int Members;
        public int MaxMembers;
        public GroupMode Mode = GroupMode.Idle;
        public double BusyUntil;
        public int TaskId = -1;

        [JsonIgnore] public GroupTypeInfo Info => GroupTypes.Get(Type);
        [JsonIgnore] public bool IsAlive => Members > 0;
        [JsonIgnore] public bool IsPlayer => Type == GroupType.PlayerFlight || Type == GroupType.PlayerTransport;
        [JsonIgnore] public double StrengthFraction => MaxMembers == 0 ? 0 : (double)Members / MaxMembers;
    }
}
