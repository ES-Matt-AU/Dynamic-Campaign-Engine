using System.Collections.Generic;
using CampaignEngine.Core;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Sim
{
    /// <summary>The flight being sent on a mission.</summary>
    public class FlightSpec
    {
        public Side Side;
        public string Name;
        public int Members;
        public GroupTypeInfo Info;
        public bool IsPlayer;
        public UnitGroup Group;
        public KeySite Origin;
    }

    /// <summary>How long one enemy observer watched the flight.</summary>
    public class ObserverExposure
    {
        public string Name;
        public double Seconds;
        public int Sightings;
        public double Score;
    }

    /// <summary>Everything that happened on one simulated sortie.</summary>
    public class MissionReport
    {
        public int TaskId;
        public TaskType TaskType;
        public Side Side;
        public string FlightName;
        public string TargetName;
        public string OriginName;

        /// <summary>The player flew the main flight.</summary>
        public bool IsPlayer;

        public double StartTime;
        public double Duration;
        public double DistanceKm;

        public int AircraftStart;
        public int AircraftLost;
        public bool PlayerShotDown;
        public int EnemyAircraftKilled;

        // Escort (if one flew)
        public string EscortName;
        public int EscortStart;
        public int EscortLost;
        public bool EscortIsPlayer;
        public TaskResult EscortResult;

        // Troops (insertions only)
        public int TroopsAboard;
        public int TroopsLost;
        public int TroopsLanded;

        /// <summary>-1 if the mission was not a capture attempt.</summary>
        public double CaptureChance = -1;
        public double CaptureRoll;
        public bool Captured;

        public List<string> Timeline = new List<string>();

        // Detection
        public double ExposureScore;
        public int Sightings;
        public double ObservedSeconds;
        public List<ObserverExposure> Observers = new List<ObserverExposure>();
        public double DetectionChance;

        /// <summary>The roll (0 to 1). The flight is detected once the chance climbs past it.</summary>
        public double DetectionRoll;
        public bool Detected;
        public double DetectedAt;
        public string DetectedBy;
        public Vec2 DetectedPosition;
        public List<Vec2> ObservedPositions = new List<Vec2>();

        // Outcome
        public TaskResult Result;
        public string Effect;
        public List<string> Reactions = new List<string>();

        /// <summary>For the player: notable events elsewhere while they were airborne and turning round.</summary>
        public List<string> WorldEvents = new List<string>();
    }
}
