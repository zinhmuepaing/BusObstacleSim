using BusSim.Road;
using BusSim.Spawning;

namespace BusSim.Obstacles
{
    /// <summary>Everything a behaviour needs. Rng is seeded from (run seed, event index) for replays.</summary>
    public sealed class ObstacleContext
    {
        public ObstacleContext(RoadSampler road, IVehicleState vehicle, SpawnEvent spawnEvent)
        {
            Road = road;
            Vehicle = vehicle;
            Event = spawnEvent;
            Rng = new System.Random(spawnEvent.Seed);
        }

        public RoadSampler Road { get; }
        public IVehicleState Vehicle { get; }
        public SpawnEvent Event { get; }
        public System.Random Rng { get; }
        public RoadSettings Settings => Road.Settings;
    }
}
