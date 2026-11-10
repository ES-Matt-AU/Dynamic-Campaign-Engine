using System.Collections.Generic;
using CampaignEngine.Core;

namespace CampaignEngine.World
{
    /// <summary>One side's reserves, campaign objectives and running totals.</summary>
    public class Force
    {
        public Side Side;

        /// <summary>Replacement aircraft not yet delivered. The real limit on air reinforcement.</summary>
        public int AircraftReserve;

        /// <summary>Replacement vehicles and crews for ground units not yet delivered.</summary>
        public int GroundReserve;

        /// <summary>Troops available for insertions by air. -1 in saves made before troop insertion existed.</summary>
        public int TroopReserve = -1;

        /// <summary>Enemy sites this side must put out of action (all at once) to win.</summary>
        public List<int> ObjectiveSiteIds = new List<int>();

        public int AircraftLost;
        public int GroundUnitsLost;
        public int AircraftKilled;
        public int GroundUnitsKilled;
        public int StructuresDestroyed;
        public int MissionsFlown;
        public int MissionsSucceeded;
        public int PlayerEjections;
    }

    /// <summary>A combat air patrol in progress. It spots and engages enemy flights passing within range.</summary>
    public class ActivePatrol
    {
        public int TaskId;
        public Side Side;
        public int GroupId;
        public Vec2 Position;
        public double StartTime;
        public double EndTime;
    }

    public class LogEntry
    {
        public double Time;

        /// <summary>Side the event belongs to, or -1 if neutral.</summary>
        public int Side = -1;

        /// <summary>0 = detail, 1 = normal, 2 = important.</summary>
        public int Level = 1;

        public string Text;
    }
}
