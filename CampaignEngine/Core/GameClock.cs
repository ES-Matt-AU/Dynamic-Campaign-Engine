using System;

namespace CampaignEngine.Core
{
    /// <summary>Formats campaign time (seconds since the campaign began) for display.</summary>
    public static class GameClock
    {
        public static string DayAndTime(double seconds, double startHour)
        {
            double total = seconds + startHour * 3600;
            int day = (int)(total / 86400) + 1;
            return $"Day {day} {TimeOfDay(seconds, startHour)}";
        }

        public static string TimeOfDay(double seconds, double startHour)
        {
            double total = seconds + startHour * 3600;
            int minutes = (int)Math.Floor(total / 60) % (24 * 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        public static string Duration(double seconds)
        {
            int total = (int)Math.Round(seconds);
            if (total >= 3600)
                return $"{total / 3600} h {total % 3600 / 60:00} min";
            if (total >= 60)
                return total % 60 == 0 ? $"{total / 60} min" : $"{total / 60} min {total % 60} s";
            return $"{total} s";
        }
    }
}
