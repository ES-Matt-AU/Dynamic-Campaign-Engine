using CampaignEngine.Core;
using Newtonsoft.Json;

namespace CampaignEngine.World
{
    public enum SiteState
    {
        Usable,
        Unusable,
        Repairing
    }

    /// <summary>An airbase, FARP, factory or other fixed site that can be attacked.</summary>
    public class KeySite
    {
        public int Id;
        public string Name;
        public SiteType Type;
        public Side Side;
        public Vec2 Position;

        /// <summary>For airbases taken from the map: the .vtm prefab id. -1 for invented sites.</summary>
        public int MapPrefabId = -1;

        public int Structures;
        public int MaxStructures;
        public SiteState State = SiteState.Usable;
        public double UnusableSince;
        public double NextRepairAt;

        [JsonIgnore] public SiteTypeInfo Info => SiteTypes.Get(Type);
        [JsonIgnore] public double Efficiency => MaxStructures == 0 ? 0 : (double)Structures / MaxStructures;
        [JsonIgnore] public bool IsUsable => State == SiteState.Usable;
    }
}
