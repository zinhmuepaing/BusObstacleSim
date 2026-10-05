namespace BusSim.Traffic
{
    public enum TrafficKind
    {
        SameDirection = 0,
        Oncoming = 1,
        SideRoadEntry = 2,
        Chaser = 3
    }

    /// <summary>One planned ambient vehicle.</summary>
    public struct TrafficSpawn
    {
        public TrafficKind Kind;

        /// <summary>
        /// Main-road s where the vehicle starts (same direction and oncoming). For a side-road entry or a chaser it is
        /// the driven vehicle's s at which the vehicle is released.
        /// </summary>
        public float S;

        /// <summary>Lane index: 0 or 1 of the driven carriageway, or 0 or 1 of the oncoming carriageway (0 is next to the median).</summary>
        public int Lane;

        public float SpeedMetresPerSecond;
        public int ModelIndex;

        /// <summary>Gap this driver accepts at the give-way line, in seconds. Only used by side-road entries.</summary>
        public float AcceptedGapSeconds;

        /// <summary>Drives fast, tailgates and weaves between lanes.</summary>
        public bool Aggressive;

        /// <summary>Which junction a side-road entry uses (index into the manager's junction list).</summary>
        public int JunctionIndex;

        public int Index;
    }
}
