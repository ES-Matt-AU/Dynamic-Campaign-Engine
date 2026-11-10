using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CampaignEngine.Ai;
using CampaignEngine.Campaign;
using CampaignEngine.Core;
using CampaignEngine.World;

namespace CampaignDesktop
{
    /// <summary>
    /// Draws the campaign map onto a canvas: territory shading from the sector grid, then key sites,
    /// known ground units and air patrols on top. North is up.
    /// </summary>
    public class MapRenderer
    {
        private static readonly Color AlliedTerritory = Color.FromRgb(0x2A, 0x5A, 0x9A);
        private static readonly Color EnemyTerritory = Color.FromRgb(0x9A, 0x32, 0x32);
        private static readonly Color AlliedMarker = Color.FromRgb(0x6C, 0xB4, 0xFF);
        private static readonly Color EnemyMarker = Color.FromRgb(0xFF, 0x70, 0x70);
        private static readonly Color OutOfAction = Color.FromRgb(0xE0, 0xA0, 0x30);

        private readonly Canvas canvas;
        private WriteableBitmap territory;

        public MapRenderer(Canvas canvas)
        {
            this.canvas = canvas;
        }

        private double Size => canvas.Width;

        public void Draw(CampaignState st)
        {
            canvas.Children.Clear();
            double mapSize = st.Map.SizeMetres();

            DrawTerritory(st);
            DrawGridLines(st, mapSize);

            foreach (var patrol in st.Patrols.Where(p => p.StartTime <= st.Time && (p.Side == Side.Allied || Intel.Knowledge(st, Side.Allied, p.Position) >= 0.5)))
                DrawPatrol(st, patrol, mapSize);

            foreach (var g in st.Groups.Where(g => g.IsAlive && !g.Info.IsAir))
            {
                if (g.Side == Side.Enemy && Intel.Knowledge(st, Side.Allied, g.Position) < 0.5)
                    continue;
                DrawUnit(g, mapSize);
            }

            foreach (var site in st.Sites)
                DrawSite(st, site, mapSize);
        }

        private Point ToCanvas(Vec2 p, double mapSize) => new Point(p.X / mapSize * Size, (1 - p.Z / mapSize) * Size);

        /// <summary>One pixel per sector, scaled up smoothly so the front line blends.</summary>
        private void DrawTerritory(CampaignState st)
        {
            var grid = st.Grid;
            if (territory == null || territory.PixelWidth != grid.Width || territory.PixelHeight != grid.Height)
                territory = new WriteableBitmap(grid.Width, grid.Height, 96, 96, PixelFormats.Bgra32, null);

            var pixels = new int[grid.Width * grid.Height];
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int index = grid.Index(x, z);
                    double ratio = grid.OwnershipRatio(Side.Allied, index);
                    var c = Lerp(EnemyTerritory, AlliedTerritory, ratio);

                    // Enemy ground the Allies have no fresh intel on is shown darker.
                    if (ratio < 0.5 && Intel.Knowledge(st, Side.Allied, grid.Centre(index)) < 0.25)
                        c = Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55));

                    int row = grid.Height - 1 - z;
                    pixels[row * grid.Width + x] = (255 << 24) | (c.R << 16) | (c.G << 8) | c.B;
                }
            }
            territory.WritePixels(new Int32Rect(0, 0, grid.Width, grid.Height), pixels, grid.Width * 4, 0);

            var image = new Image { Source = territory, Width = Size, Height = Size, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            canvas.Children.Add(image);
        }

        /// <summary>Faint lines every 25 km or so, labelled in km.</summary>
        private void DrawGridLines(CampaignState st, double mapSize)
        {
            var brush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            double stepKm = 25;
            for (double km = stepKm; km * 1000 < mapSize; km += stepKm)
            {
                double p = km * 1000 / mapSize * Size;
                canvas.Children.Add(new Line { X1 = p, X2 = p, Y1 = 0, Y2 = Size, Stroke = brush, StrokeThickness = 1 });
                canvas.Children.Add(new Line { X1 = 0, X2 = Size, Y1 = Size - p, Y2 = Size - p, Stroke = brush, StrokeThickness = 1 });
                AddText($"{km:0}", p + 2, Size - 14, 9, Color.FromArgb(120, 255, 255, 255));
                AddText($"{km:0}", 2, Size - p - 12, 9, Color.FromArgb(120, 255, 255, 255));
            }
        }

        private void DrawSite(CampaignState st, KeySite site, double mapSize)
        {
            var pt = ToCanvas(site.Position, mapSize);
            var colour = !site.IsUsable ? OutOfAction : site.Side == Side.Allied ? AlliedMarker : EnemyMarker;
            bool objective = st.Forces.Any(f => f.Side != site.Side && f.ObjectiveSiteIds.Contains(site.Id));

            var marker = new Border
            {
                Width = 18,
                Height = 18,
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                BorderBrush = new SolidColorBrush(colour),
                BorderThickness = new Thickness(objective ? 3 : 2),
                CornerRadius = new CornerRadius(site.Info.HostsAircraft ? 9 : 2),
                Child = new TextBlock
                {
                    Text = site.Info.Symbol.ToString(),
                    Foreground = new SolidColorBrush(colour),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{site.Name}\n{site.Info.Name} ({site.Side})\n{StateText(site)}, {site.Efficiency:P0} strength" +
                          (objective ? "\nCampaign objective" : "")
            };
            Canvas.SetLeft(marker, pt.X - 9);
            Canvas.SetTop(marker, pt.Y - 9);
            canvas.Children.Add(marker);

            AddText(site.Name, pt.X + 11, pt.Y - 7, 10, colour);
        }

        private void DrawUnit(UnitGroup g, double mapSize)
        {
            var pt = ToCanvas(g.Position, mapSize);
            var colour = g.Side == Side.Allied ? AlliedMarker : EnemyMarker;
            double size = g.Info.IsAirDefence ? 7 : 6;
            Shape shape = g.Info.IsAirDefence
                ? new Polygon { Points = new PointCollection { new Point(size / 2, 0), new Point(size, size), new Point(0, size) } }
                : new Ellipse { Width = size, Height = size };
            shape.Fill = new SolidColorBrush(colour);
            shape.Stroke = Brushes.Black;
            shape.StrokeThickness = 0.5;
            shape.ToolTip = $"{g.Name}\n{g.Info.Name} ({g.Side})\n{g.Members} of {g.MaxMembers} {g.Info.MemberName}s";
            Canvas.SetLeft(shape, pt.X - size / 2);
            Canvas.SetTop(shape, pt.Y - size / 2);
            canvas.Children.Add(shape);
        }

        private void DrawPatrol(CampaignState st, ActivePatrol patrol, double mapSize)
        {
            var group = st.Group(patrol.GroupId);
            if (group == null || !group.IsAlive)
                return;
            var pt = ToCanvas(patrol.Position, mapSize);
            double radius = GroupTypes.Get(GroupType.FighterFlight).ThreatRange / mapSize * Size;
            var colour = patrol.Side == Side.Allied ? AlliedMarker : EnemyMarker;
            var ring = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(160, colour.R, colour.G, colour.B)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                ToolTip = $"{group.Name} on patrol until {GameClock.TimeOfDay(patrol.EndTime, st.StartHour)}"
            };
            Canvas.SetLeft(ring, pt.X - radius);
            Canvas.SetTop(ring, pt.Y - radius);
            canvas.Children.Add(ring);
        }

        private void AddText(string text, double x, double y, double size, Color colour)
        {
            var tb = new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(colour), IsHitTestVisible = false };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        private static string StateText(KeySite site) => site.State switch
        {
            SiteState.Usable => "working",
            SiteState.Repairing => "being repaired",
            _ => "out of action"
        };

        private static Color Lerp(Color a, Color b, double t)
        {
            t = MathUtil.Clamp01(t);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }
    }
}
