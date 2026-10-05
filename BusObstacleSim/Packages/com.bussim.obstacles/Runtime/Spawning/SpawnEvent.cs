using BusSim.Obstacles;

namespace BusSim.Spawning
{
    /// <summary>One planned obstacle: what, where in road space, and its own replay seed.</summary>
    public struct SpawnEvent
    {
        public ObstacleDefinition Definition;
        public float S;
        public float T;
        public float YawDegrees;
        public int EventIndex;
        public int Seed;

        public Footprint Footprint => new Footprint(S, T, Definition.footprintWidth, Definition.footprintLength);
    }
}
