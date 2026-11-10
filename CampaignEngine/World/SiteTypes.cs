namespace CampaignEngine.World
{
    public enum SiteType
    {
        Airbase,
        Farp,
        Factory,
        MilitaryBase,
        PowerStation,
        Refinery,
        RadarStation
    }

    /// <summary>
    /// Fixed data for each kind of key site. Importance, radii and ownership strengths follow
    /// Enemy Engaged's keysite table, scaled down where EECH's values assume a much larger map.
    /// </summary>
    public class SiteTypeInfo
    {
        public string Name;
        public char Symbol;

        /// <summary>How much the site matters (0 to 1). Feeds the importance map.</summary>
        public double Importance;
        public double ImportanceRadius;

        /// <summary>Radius within which aircraft from this site can cover the ground. 0 if it hosts no aircraft.</summary>
        public double AirCoverRadius;

        /// <summary>Strength of the site's pull on sector ownership.</summary>
        public double OwnershipStrength;

        /// <summary>Number of buildings (targets). The site is out of action below the minimum efficiency.</summary>
        public int Structures;

        /// <summary>Troops that must land for the best chance of capturing the site.</summary>
        public int TroopsToCapture;

        public bool HostsAircraft;
        public bool HostsHelicopters;

        /// <summary>Air search radar or lookout range for spotting aircraft.</summary>
        public double SensorRange;
        public double SensorQuality;
    }

    public static class SiteTypes
    {
        private const double Km = 1000;

        // Order must match the SiteType enum.
        private static readonly SiteTypeInfo[] Table =
        {
            new SiteTypeInfo { Name = "Airbase", Symbol = 'A', Importance = 1.0, ImportanceRadius = 80 * Km, AirCoverRadius = 150 * Km, OwnershipStrength = 80, Structures = 12, TroopsToCapture = 12, HostsAircraft = true, HostsHelicopters = true, SensorRange = 20 * Km, SensorQuality = 0.5 },
            new SiteTypeInfo { Name = "FARP", Symbol = 'F', Importance = 0.4, ImportanceRadius = 40 * Km, AirCoverRadius = 60 * Km, OwnershipStrength = 40, Structures = 4, TroopsToCapture = 4, HostsAircraft = true, HostsHelicopters = true, SensorRange = 6 * Km, SensorQuality = 0.4 },
            new SiteTypeInfo { Name = "Factory", Symbol = 'I', Importance = 0.5, ImportanceRadius = 40 * Km, OwnershipStrength = 20, Structures = 8, TroopsToCapture = 6, SensorRange = 5 * Km, SensorQuality = 0.3 },
            new SiteTypeInfo { Name = "Military base", Symbol = 'M', Importance = 0.8, ImportanceRadius = 30 * Km, OwnershipStrength = 30, Structures = 8, TroopsToCapture = 10, SensorRange = 6 * Km, SensorQuality = 0.4 },
            new SiteTypeInfo { Name = "Power station", Symbol = 'P', Importance = 0.3, ImportanceRadius = 30 * Km, OwnershipStrength = 10, Structures = 6, TroopsToCapture = 6, SensorRange = 5 * Km, SensorQuality = 0.3 },
            new SiteTypeInfo { Name = "Refinery", Symbol = 'O', Importance = 0.4, ImportanceRadius = 30 * Km, OwnershipStrength = 20, Structures = 6, TroopsToCapture = 6, SensorRange = 5 * Km, SensorQuality = 0.3 },
            new SiteTypeInfo { Name = "Radar station", Symbol = 'R', Importance = 0.5, ImportanceRadius = 30 * Km, OwnershipStrength = 30, Structures = 3, TroopsToCapture = 4, SensorRange = 60 * Km, SensorQuality = 0.7 },
        };

        public static SiteTypeInfo Get(SiteType type) => Table[(int)type];
    }
}
