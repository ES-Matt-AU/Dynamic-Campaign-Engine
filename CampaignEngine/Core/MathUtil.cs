namespace CampaignEngine.Core
{
    public static class MathUtil
    {
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        public static double Clamp01(double value) => Clamp(value, 0, 1);
    }
}
