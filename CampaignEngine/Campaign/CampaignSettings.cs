namespace CampaignEngine.Campaign
{
    /// <summary>
    /// Every tuning value in one place. Saved with the campaign, so a save keeps the values it was
    /// started with. Times are in minutes unless the name says otherwise.
    /// </summary>
    public class CampaignSettings
    {
        // Simulation
        public double TickSeconds = 60;
        public double MapUpdateMinutes = 5;
        public double RouteStepSeconds = 30;

        // Key sites
        public double MinEfficiency = 0.3;
        public double RepairDelayMinutes = 120;
        public double RepairStepMinutes = 30;
        public double FieldRepairMinutes = 180;

        // Intel (fog of war)
        public double IntelMaxSeconds = 4 * 3600;
        public double ReconRevealRadius = 15000;

        // Task generation (periods as in EECH's campaign mode)
        public double StrikeGenerationMinutes = 7.5;
        public double SeadGenerationMinutes = 12;
        public double BaiGenerationMinutes = 20;
        public double CapGenerationMinutes = 60;
        public int StrikeTopN = 3;
        public int SeadTopN = 2;
        public int BaiTopN = 2;
        public double MinTaskCreationRatio = 0.75;

        // Task assignment
        public double AssignIntervalMinutes = 3;
        public int AirbaseAssignPerPass = 3;
        public int FarpAssignPerPass = 1;

        /// <summary>Non-critical missions per Allied airbase held back for the player.</summary>
        public int PlayerReservePerAirbase = 2;
        public double AiTurnaroundMinutes = 90;
        public double PlayerTurnaroundMinutes = 30;

        // Reinforcement
        public double RegenAirMinutes = 90;
        public double RegenGroundMinutes = 120;
        public double GroundRegenRadius = 100000;
        public int StartingAircraftReserve = 30;
        public int StartingGroundReserve = 40;
        public int StartingTroopReserve = 80;

        // Detection: chance = 1 - exp(-DetectionK * score),
        // score = observed minutes (weighted by sensor quality and range) + SightingWeight * separate sightings.
        public double DetectionK = 0.35;
        public double SightingWeight = 0.25;

        /// <summary>Defences are this effective (fraction) against a flight they have not yet detected.</summary>
        public double UndetectedLethalityFactor = 0.35;
        public double AlertLethalityFactor = 1.3;
        public double AlertMinutes = 120;
        public double AlertRadius = 15000;

        /// <summary>Exposure score at which the enemy puts up a CAP in reaction.</summary>
        public double ReactionCapThreshold = 2.0;

        /// <summary>Exposure score at which the enemy also counter-strikes the base the flight came from.</summary>
        public double ReactionStrikeThreshold = 8.0;

        /// <summary>Scales threat lethality against the player's flight (player skill).</summary>
        public double PlayerLethalityFactor = 0.8;

        // Weapons effect per attempt (scaled by the flight's capability)
        public double StrikeHitChance = 0.5;
        public int StrikeAttemptsPerAircraft = 2;
        public double SeadKillChance = 0.55;
        public int SeadAttemptsPerAircraft = 2;
        public double BaiKillChance = 0.4;
        public int BaiAttemptsPerAircraft = 3;

        /// <summary>Air-to-air kill rate per minute for a flight with Air = 1 against a patrol it meets.</summary>
        public double AirToAirLethality = 0.06;

        /// <summary>An escort with Air = 1 at full strength cuts enemy fighters' effect on the escorted flight by this share.</summary>
        public double EscortProtection = 0.6;

        // Troop insertion and capture.
        // Capture chance = troop factor × (1 - resistance), where
        //   troop factor = troops landed / troops the site type needs (max 1),
        //   resistance  = EECH's (efficiency - minimum) / (1 - minimum) + GarrisonWeight × (defenders / troops landed),
        //   and resistance × CaptureSurpriseFactor if the insertion was not detected.
        public double CaptureGenerationMinutes = 30;
        public int CaptureTopN = 1;
        public double GarrisonWeight = 0.5;
        public double CaptureSurpriseFactor = 0.7;

        /// <summary>Old-side ground units within this distance are overrun when a site is captured. Also the garrison radius.</summary>
        public double CaptureOverrunRadius = 5000;

        /// <summary>Buildings put back into use straight away by the new owner (EECH repairs up to 5).</summary>
        public int CaptureBuildingsRestored = 5;
        public int MinGarrisonSize = 10;

        /// <summary>The AI only plans or launches a capture if its estimated chance (if undetected) is at least this.</summary>
        public double MinCaptureChanceForAi = 0.3;

        /// <summary>An insertion waits for its escort until this long before the mission expires, then goes alone.</summary>
        public double EscortWaitCutoffMinutes = 30;
    }
}
