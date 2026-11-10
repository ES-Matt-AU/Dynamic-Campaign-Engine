using System.ComponentModel;
using System.Windows.Media;

namespace CampaignDesktop
{
    /// <summary>Text colours shared by the message log, mission lists and report.</summary>
    public static class Palette
    {
        public static readonly Brush Allied = Make(0x6C, 0xB4, 0xFF);
        public static readonly Brush Enemy = Make(0xFF, 0x70, 0x70);
        public static readonly Brush Important = Make(0xFF, 0xD7, 0x50);
        public static readonly Brush Neutral = Make(0xCC, 0xCC, 0xCC);
        public static readonly Brush Detail = Make(0x88, 0x88, 0x88);
        public static readonly Brush Good = Make(0x7C, 0xD6, 0x7C);

        private static Brush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>One line in the message log or a report.</summary>
    public class LogLine
    {
        public string Text { get; set; }
        public Brush Brush { get; set; }
    }

    /// <summary>One entry in a mission list. Its text is refreshed in place, so selection is kept.</summary>
    public class MissionLine : INotifyPropertyChanged
    {
        private string text;
        private Brush brush;

        public int Id { get; set; }

        public string Text
        {
            get => text;
            set { if (text != value) { text = value; Changed(nameof(Text)); } }
        }

        public Brush Brush
        {
            get => brush;
            set { if (brush != value) { brush = value; Changed(nameof(Brush)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
