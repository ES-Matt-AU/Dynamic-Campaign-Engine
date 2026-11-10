using System.Windows;

namespace CampaignDesktop
{
    /// <summary>Shows the report for a mission the player has just flown.</summary>
    public partial class ReportWindow : Window
    {
        public ReportWindow(IEnumerable<LogLine> lines)
        {
            InitializeComponent();
            ReportList.ItemsSource = lines.ToList();
        }
    }
}
