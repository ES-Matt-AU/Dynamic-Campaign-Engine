using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.World;
using Microsoft.Win32;

namespace CampaignDesktop
{
    public partial class MainWindow : Window
    {
        private const string DefaultMapPath = @"F:\SteamLibrary\steamapps\common\VTOL VR\CustomMaps\afMtnsHills\afMtnsHills.vtm";

        /// <summary>Real-time interval between simulation steps.</summary>
        private const double TimerSeconds = 0.1;

        private static readonly (string Label, double GameMinutesPerSecond)[] Speeds =
        {
            ("1 min per second", 1),
            ("5 min per second", 5),
            ("15 min per second", 15),
            ("1 hour per second", 60),
        };

        private readonly DispatcherTimer timer;
        private readonly MapRenderer map;
        private readonly ObservableCollection<LogLine> logLines = new ObservableCollection<LogLine>();
        private readonly ObservableCollection<MissionLine> missionLines = new ObservableCollection<MissionLine>();
        private readonly ObservableCollection<MissionLine> activeLines = new ObservableCollection<MissionLine>();

        /// <summary>Every log entry seen this session, so the detail filter can be changed at any time.</summary>
        private readonly List<LogEntry> allEntries = new List<LogEntry>();

        private Campaign campaign;
        private LogEntry lastSeen;
        private bool running;
        private double gameMinutesPerSecond = 5;

        public MainWindow()
        {
            InitializeComponent();

            string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Title = $"VTOL VR Dynamic Campaign (prototype)  version {version}";

            LogList.ItemsSource = logLines;
            MissionList.ItemsSource = missionLines;
            ActiveList.ItemsSource = activeLines;
            map = new MapRenderer(MapCanvas);

            foreach (var speed in Speeds)
                SpeedBox.Items.Add(speed.Label);
            SpeedBox.SelectedIndex = 1;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TimerSeconds) };
            timer.Tick += Timer_Tick;

            Loaded += (s, e) => StartNewCampaign(DefaultMapPath);
            Closing += (s, e) => timer.Stop();
            UpdateRunState();
        }

        private static string SavesFolder
        {
            get
            {
                // Share the console app's Saves folder: the folder holding VTOLCampaign.sln.
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VTOLCampaign.sln")))
                    dir = dir.Parent;
                string root = dir?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(root, "Saves");
            }
        }

        // ---------------------------------------------------------------- simulation

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (campaign == null)
                return;

            campaign.AdvanceTime(gameMinutesPerSecond * 60 * TimerSeconds);
            PullNewMessages();
            RefreshView();
            CheckForWinner();
        }

        private void CheckForWinner()
        {
            if (!campaign.State.Winner.HasValue)
                return;
            SetRunning(false);
            MessageBox.Show(this, $"{campaign.State.Winner} wins.\n\n{campaign.State.WinReason}", "Campaign over");
        }

        private void SetRunning(bool run)
        {
            running = run && campaign != null && !campaign.State.Winner.HasValue;
            if (running)
                timer.Start();
            else
                timer.Stop();
            UpdateRunState();
        }

        private void UpdateRunState()
        {
            RunButton.Content = running ? "Pause" : "Run";
            LogStatus.Text = running
                ? "Running: the newest messages are shown as they arrive. Pause to scroll back through earlier ones."
                : "Paused: use the scrollbar to read all earlier messages.";
        }

        // ---------------------------------------------------------------- message log

        /// <summary>Copies any log entries added since the last check into the message list.</summary>
        private void PullNewMessages()
        {
            var log = campaign.State.Log;
            int start = lastSeen == null ? 0 : log.LastIndexOf(lastSeen) + 1;
            bool added = false;

            for (int i = start; i < log.Count; i++)
            {
                allEntries.Add(log[i]);
                if (Include(log[i]))
                {
                    logLines.Add(ToLine(log[i]));
                    added = true;
                }
            }
            lastSeen = log.Count > 0 ? log[log.Count - 1] : null;

            if (added && running)
                LogList.ScrollIntoView(logLines[logLines.Count - 1]);
        }

        private void RebuildLog()
        {
            logLines.Clear();
            foreach (var entry in allEntries.Where(Include))
                logLines.Add(ToLine(entry));
            if (logLines.Count > 0)
                LogList.ScrollIntoView(logLines[logLines.Count - 1]);
        }

        private bool Include(LogEntry entry) => entry.Level >= 1 || DetailBox.IsChecked == true;

        private LogLine ToLine(LogEntry entry)
        {
            Brush brush = entry.Level >= 2 ? Palette.Important
                : entry.Level == 0 ? Palette.Detail
                : entry.Side == (int)Side.Allied ? Palette.Allied
                : entry.Side == (int)Side.Enemy ? Palette.Enemy
                : Palette.Neutral;
            return new LogLine { Text = $"{GameClock.DayAndTime(entry.Time, campaign.State.StartHour)}  {entry.Text}", Brush = brush };
        }

        // ---------------------------------------------------------------- missions

        /// <summary>
        /// Brings both mission lists up to date. Entries are updated in place when the set of missions
        /// is unchanged, so the selection and scroll position stay put while the simulation runs.
        /// </summary>
        private void RefreshMissionLists()
        {
            var available = campaign.PlayerMissions().Select(t => MissionText.Available(campaign, t)).ToList();
            int? selectedId = (MissionList.SelectedItem as MissionLine)?.Id;
            if (Sync(missionLines, available) && selectedId.HasValue)
            {
                MissionList.SelectedItem = missionLines.FirstOrDefault(m => m.Id == selectedId.Value);
                if (MissionList.SelectedItem == null)
                    ShowSelectedMission();
            }

            var active = MissionText.ActiveMissions(campaign.State).Select(t => MissionText.Active(campaign, t)).ToList();
            Sync(activeLines, active);
        }

        /// <summary>Returns true if the list had to be rebuilt.</summary>
        private static bool Sync(ObservableCollection<MissionLine> target, List<MissionLine> fresh)
        {
            if (target.Select(m => m.Id).SequenceEqual(fresh.Select(m => m.Id)))
            {
                for (int i = 0; i < fresh.Count; i++)
                {
                    target[i].Text = fresh[i].Text;
                    target[i].Brush = fresh[i].Brush;
                }
                return false;
            }

            target.Clear();
            foreach (var line in fresh)
                target.Add(line);
            return true;
        }

        private void MissionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedMission();

        private void ShowSelectedMission()
        {
            var task = SelectedTask();
            AcceptButton.IsEnabled = task != null && !campaign.State.Winner.HasValue;
            MissionDetails.Text = task != null ? MissionText.Briefing(campaign, task) : "Select a mission to see its briefing.";
        }

        private CampaignEngine.Tasks.CampaignTask SelectedTask()
        {
            if (campaign == null || !(MissionList.SelectedItem is MissionLine line))
                return null;
            var task = campaign.State.Task(line.Id);
            return task != null && task.State == CampaignEngine.Tasks.TaskState.Available ? task : null;
        }

        /// <summary>Pauses the simulation, flies the selected mission, then shows the report.</summary>
        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            var task = SelectedTask();
            if (task == null)
                return;

            SetRunning(false);
            CampaignEngine.Sim.MissionReport report;
            try
            {
                report = campaign.FlyPlayerMission(task.Id);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "Cannot fly this mission");
                RefreshView();
                return;
            }

            PullNewMessages();
            if (logLines.Count > 0)
                LogList.ScrollIntoView(logLines[logLines.Count - 1]);
            RefreshView();
            new ReportWindow(MissionText.Report(campaign.State, report)) { Owner = this }.ShowDialog();
            CheckForWinner();
        }

        // ---------------------------------------------------------------- view

        private void RefreshView()
        {
            if (campaign == null)
                return;

            var st = campaign.State;
            map.Draw(st);
            RefreshMissionLists();
            TimeText.Text = $"{st.Clock()}   Map: {st.Map.MapId}" + (st.Winner.HasValue ? $"   CAMPAIGN OVER: {st.Winner} wins" : "");
            AlliedText.Text = SideSummary(Side.Allied);
            EnemyText.Text = SideSummary(Side.Enemy);
        }

        private string SideSummary(Side side)
        {
            var st = campaign.State;
            var force = st.Force(side);
            int bases = st.Sites.Count(s => s.Side == side && s.Info.HostsAircraft);
            int basesUp = st.Sites.Count(s => s.Side == side && s.Info.HostsAircraft && s.IsUsable);
            int aircraft = st.Groups.Where(g => g.Side == side && g.Info.IsAir && !g.IsPlayer).Sum(g => g.Members);
            int ground = st.Groups.Where(g => g.Side == side && !g.Info.IsAir).Sum(g => g.Members);
            int objectivesDown = force.ObjectiveSiteIds.Count(id =>
            {
                var site = st.Site(id);
                return site == null || !site.IsUsable || site.Side == side;
            });
            return $"{side}: bases {basesUp}/{bases}  aircraft {aircraft} (+{force.AircraftReserve})  ground {ground} (+{force.GroundReserve})  " +
                   $"troops {force.TroopReserve}  objectives {objectivesDown}/{force.ObjectiveSiteIds.Count}";
        }

        // ---------------------------------------------------------------- campaign handling

        private void StartNewCampaign(string mapPath)
        {
            if (!File.Exists(mapPath))
            {
                var dialog = new OpenFileDialog { Title = "Choose a VTOL VR map (.vtm)", Filter = "VTOL VR map (*.vtm)|*.vtm" };
                if (dialog.ShowDialog(this) != true)
                    return;
                mapPath = dialog.FileName;
            }

            try
            {
                int seed = Environment.TickCount & 0x7FFFFFFF;
                SetCampaign(Campaign.New(mapPath, seed));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not start the campaign:\n{ex.Message}", "New campaign");
            }
        }

        private void SetCampaign(Campaign newCampaign)
        {
            SetRunning(false);
            campaign = newCampaign;
            allEntries.Clear();
            logLines.Clear();
            missionLines.Clear();
            activeLines.Clear();
            lastSeen = null;
            PullNewMessages();
            if (logLines.Count > 0)
                LogList.ScrollIntoView(logLines[logLines.Count - 1]);
            RefreshView();
            ShowSelectedMission();
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            if (campaign != null && MessageBox.Show(this, "Start a new campaign? Unsaved progress will be lost.", "New campaign", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            StartNewCampaign(DefaultMapPath);
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            SetRunning(false);
            Directory.CreateDirectory(SavesFolder);
            var dialog = new OpenFileDialog { Title = "Load campaign", Filter = "Campaign save (*.json)|*.json", InitialDirectory = SavesFolder };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                SetCampaign(Campaign.Load(dialog.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not load the campaign:\n{ex.Message}", "Load");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (campaign == null)
                return;
            SetRunning(false);
            Directory.CreateDirectory(SavesFolder);
            var dialog = new SaveFileDialog { Title = "Save campaign", Filter = "Campaign save (*.json)|*.json", InitialDirectory = SavesFolder, FileName = "campaign.json" };
            if (dialog.ShowDialog(this) != true)
                return;
            campaign.Save(dialog.FileName);
        }

        private void RunButton_Click(object sender, RoutedEventArgs e) => SetRunning(!running);

        private void SpeedBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedBox.SelectedIndex >= 0)
                gameMinutesPerSecond = Speeds[SpeedBox.SelectedIndex].GameMinutesPerSecond;
        }

        private void DetailBox_Changed(object sender, RoutedEventArgs e)
        {
            if (campaign != null)
                RebuildLog();
        }
    }
}
