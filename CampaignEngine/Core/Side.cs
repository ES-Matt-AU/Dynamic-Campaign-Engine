namespace CampaignEngine.Core
{
    /// <summary>VTOL VR only has two teams, so the campaign uses the same two.</summary>
    public enum Side
    {
        Allied = 0,
        Enemy = 1
    }

    public static class SideExtensions
    {
        public static Side Opponent(this Side side) => side == Side.Allied ? Side.Enemy : Side.Allied;
    }
}
