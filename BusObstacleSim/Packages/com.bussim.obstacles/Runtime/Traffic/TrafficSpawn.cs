namespace BusSim.Traffic
{
    public enum TrafficKind
    {
        SameDirection = 0,
        Oncoming = 1,
        SideRoadEntry = 2
    }

    /// <summary>One planned ambient vehicle.</summary>
    public struct TrafficSpawn
    {
        public TrafficKind Kind;

        /// <summary>
        /// Main-road s where the vehicle starts (same direction and oncoming). For a side-road entry it is
        /// the driven vehicle's s at which the vehicle is released.
        /// </summary>
        public float S;

        /// <summary>Lane index: 0 or 1 of the driven carriageway, or 0 or 1 of the oncoming carriageway (0 is next to the median).</summary>
        public int Lane;

        public float SpeedMetresPerSecond;
        public int ModelIndex;

        /// <summary>Gap this driver accepts at the give-way line, in seconds. Only used by side-road entries.</summary>
        public float AcceptedGapSeconds;

        public int Index;
    }
}
