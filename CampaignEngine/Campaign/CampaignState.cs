using System.Collections.Generic;
using CampaignEngine.Core;
using CampaignEngine.Map;
using CampaignEngine.Tasks;
using CampaignEngine.World;

namespace CampaignEngine.Campaign
{
    /// <summary>Everything that makes up a campaign in progress. This is what gets saved.</summary>
    public class CampaignState
    {
        public int Version = 1;
        public CampaignSettings Settings = new CampaignSettings();
        public MapInfo Map;

        /// <summary>Seconds since the campaign began.</summary>
        public double Time;
        public double StartHour = 6;

        public Dice Dice = new Dice(1);
        public SectorGrid Grid;

        public List<KeySite> Sites = new List<KeySite>();
        public List<UnitGroup> Groups = new List<UnitGroup>();
        public List<CampaignTask> Tasks = new List<CampaignTask>();
        public List<ActivePatrol> Patrols = new List<ActivePatrol>();
        public Force[] Forces = { new Force { Side = Side.Allied }, new Force { Side = Side.Enemy } };
        public List<LogEntry> Log = new List<LogEntry>();

        public int NextId = 1;
        public int PlayerGroupId = -1;

        /// <summary>Next run time per side and task type: [side * TaskTypes.Count + type].</summary>
        public double[] NextGenerationAt = new double[2 * TaskTypes.Count];
        public double NextAssignAt;
        public double NextRegenAirAt;
        public double NextRegenGroundAt;
        public double NextMapUpdateAt;

        public Side? Winner;
        public string WinReason;

        public int NewId() => NextId++;

        public Force Force(Side side) => Forces[(int)side];

        public KeySite Site(int id) => Sites.Find(s => s.Id == id);

        public UnitGroup Group(int id) => Groups.Find(g => g.Id == id);

        public CampaignTask Task(int id) => Tasks.Find(t => t.Id == id);

        public string Clock() => GameClock.DayAndTime(Time, StartHour);

        public void AddLog(string text, int level = 1, Side? side = null)
        {
            Log.Add(new LogEntry { Time = Time, Text = text, Level = level, Side = side.HasValue ? (int)side.Value : -1 });
            // Kept long enough for the desktop log to scroll back through a long campaign.
            if (Log.Count > 20000)
                Log.RemoveRange(0, Log.Count - 20000);
        }
    }
}
