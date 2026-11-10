namespace CampaignEngine.Core
{
    /// <summary>
    /// Random number source whose whole state is one number, so it is saved with the campaign
    /// and a loaded game carries on with the same sequence of rolls.
    /// </summary>
    public class Dice
    {
        public ulong State = 1;

        public Dice()
        {
        }

        public Dice(int seed)
        {
            State = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            if (State == 0)
                State = 1;
        }

        private ulong NextULong()
        {
            // xorshift64*
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform value in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Whole number in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
        }

        public bool Chance(double probability) => NextDouble() < probability;
    }
}
