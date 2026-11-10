namespace CampaignEngine.World
{
    public enum GroupType
    {
        PlayerFlight,
        FighterFlight,
        StrikeFlight,
        SeadFlight,
        ReconFlight,
        AttackHelicopterFlight,
        SamSite,
        MobileSam,
        AaaSite,
        ArmourCompany,
        InfantryCompany,

        // Added after the first milestone. New values go at the end so older saves still load.
        PlayerTransport,
        TransportFlight
    }

    /// <summary>
    /// Fixed data for each kind of unit group. Mission capabilities are 0 to 1.
    /// Lethality is the kill rate per minute against one aircraft inside threat range,
    /// at full strength, once the defenders know the flight is there.
    /// </summary>
    public class GroupTypeInfo
    {
        public string Name;
        public char Symbol;
        public bool IsAir;
        public bool IsAirDefence;
        public bool IsGroundCombat;

        public double Air;
        public double Ground;
        public double Sead;
        public double Recon;

        /// <summary>Ability to carry troops (0 to 1).</summary>
        public double Transport;

        /// <summary>Troops carried by each aircraft.</summary>
        public int TroopCapacity;

        /// <summary>Cruise speed in m/s.</summary>
        public double Speed;
        public double CombatRadius;

        /// <summary>0 to 1. Reduces how well the flight is seen and how easily it is hit.</summary>
        public double Stealth;

        /// <summary>Range at which this group spots aircraft.</summary>
        public double SensorRange;
        public double SensorQuality;

        /// <summary>Range at which this group can shoot at aircraft.</summary>
        public double ThreatRange;
        public double Lethality;

        /// <summary>Range at which this group reports enemy ground activity (feeds intel).</summary>
        public double GroundSight;

        public int DefaultMembers;
        public string MemberName;
    }

    public static class GroupTypes
    {
        private const double Km = 1000;

        // Order must match the GroupType enum.
        private static readonly GroupTypeInfo[] Table =
        {
            new GroupTypeInfo { Name = "Player flight", Symbol = 'P', IsAir = true, Air = 0.9, Ground = 0.9, Sead = 0.8, Recon = 0.7, Speed = 240, CombatRadius = 220 * Km, Stealth = 0.15, DefaultMembers = 2, MemberName = "multirole fighter" },
            new GroupTypeInfo { Name = "Fighter flight", Symbol = 'f', IsAir = true, Air = 1.0, Ground = 0.3, Sead = 0.1, Recon = 0.2, Speed = 250, CombatRadius = 180 * Km, Stealth = 0.1, SensorRange = 30 * Km, SensorQuality = 0.6, ThreatRange = 25 * Km, Lethality = 0.06, DefaultMembers = 2, MemberName = "fighter" },
            new GroupTypeInfo { Name = "Strike flight", Symbol = 's', IsAir = true, Air = 0.3, Ground = 1.0, Sead = 0.3, Recon = 0.2, Speed = 230, CombatRadius = 170 * Km, Stealth = 0.1, DefaultMembers = 2, MemberName = "strike aircraft" },
            new GroupTypeInfo { Name = "SEAD flight", Symbol = 'w', IsAir = true, Air = 0.4, Ground = 0.6, Sead = 1.0, Recon = 0.2, Speed = 240, CombatRadius = 170 * Km, Stealth = 0.1, DefaultMembers = 2, MemberName = "SEAD aircraft" },
            new GroupTypeInfo { Name = "Recon flight", Symbol = 'r', IsAir = true, Air = 0.1, Ground = 0.0, Sead = 0.0, Recon = 1.0, Speed = 260, CombatRadius = 200 * Km, Stealth = 0.4, DefaultMembers = 1, MemberName = "recon aircraft" },
            new GroupTypeInfo { Name = "Attack helicopter flight", Symbol = 'h', IsAir = true, Air = 0.1, Ground = 0.8, Sead = 0.3, Recon = 0.5, Speed = 70, CombatRadius = 70 * Km, Stealth = 0.2, DefaultMembers = 2, MemberName = "attack helicopter" },
            new GroupTypeInfo { Name = "SAM site", Symbol = '^', IsAirDefence = true, SensorRange = 35 * Km, SensorQuality = 0.7, ThreatRange = 25 * Km, Lethality = 0.015, GroundSight = 5 * Km, DefaultMembers = 4, MemberName = "SAM launcher" },
            new GroupTypeInfo { Name = "Mobile SAM", Symbol = '^', IsAirDefence = true, SensorRange = 15 * Km, SensorQuality = 0.6, ThreatRange = 10 * Km, Lethality = 0.025, GroundSight = 5 * Km, DefaultMembers = 2, MemberName = "SAM vehicle" },
            new GroupTypeInfo { Name = "AAA site", Symbol = 'x', IsAirDefence = true, SensorRange = 6 * Km, SensorQuality = 0.4, ThreatRange = 3.5 * Km, Lethality = 0.03, GroundSight = 5 * Km, DefaultMembers = 3, MemberName = "AA gun" },
            new GroupTypeInfo { Name = "Armour company", Symbol = '*', IsGroundCombat = true, SensorRange = 5 * Km, SensorQuality = 0.3, ThreatRange = 3 * Km, Lethality = 0.01, GroundSight = 6 * Km, DefaultMembers = 8, MemberName = "tank" },
            new GroupTypeInfo { Name = "Infantry company", Symbol = '*', IsGroundCombat = true, SensorRange = 4 * Km, SensorQuality = 0.4, ThreatRange = 2 * Km, Lethality = 0.006, GroundSight = 5 * Km, DefaultMembers = 10, MemberName = "infantry section" },

            // Tilt-jet transports (the AV-42C in VTOL VR). Speed and troop capacity are tuning values, not the real aircraft's figures.
            new GroupTypeInfo { Name = "Player tilt-jet flight", Symbol = 'P', IsAir = true, Air = 0.1, Ground = 0.2, Sead = 0.0, Recon = 0.3, Transport = 1.0, TroopCapacity = 8, Speed = 150, CombatRadius = 180 * Km, Stealth = 0.1, DefaultMembers = 2, MemberName = "tilt-jet" },
            new GroupTypeInfo { Name = "Transport flight", Symbol = 't', IsAir = true, Air = 0.05, Ground = 0.1, Sead = 0.0, Recon = 0.2, Transport = 1.0, TroopCapacity = 8, Speed = 150, CombatRadius = 180 * Km, Stealth = 0.1, DefaultMembers = 2, MemberName = "tilt-jet" },
        };

        public static GroupTypeInfo Get(GroupType type) => Table[(int)type];
    }
}
