using System.Collections.Generic;
using System.Text;

namespace BusSim.Spawning
{
    /// <summary>The full list of spawn events for one run, built once from the seed.</summary>
    public sealed class SpawnPlan
    {
        public SpawnPlan(int seed, float plannedLength)
        {
            Seed = seed;
            PlannedLength = plannedLength;
        }

        public int Seed { get; }

        /// <summary>Length of road (metres) the planner could place events on.</summary>
        public float PlannedLength { get; }

        /// <summary>Events in increasing s.</summary>
        public List<SpawnEvent> Events { get; } = new List<SpawnEvent>();

        /// <summary>Every candidate footprint that failed the clearance rule.</summary>
        public List<Footprint> RejectedFootprints { get; } = new List<Footprint>();

        /// <summary>Events skipped because every attempt failed.</summary>
        public int SkippedEvents { get; set; }

        public int RejectedPlacements => RejectedFootprints.Count;

        public float EventsPerKm => PlannedLength > 0f ? Events.Count * 1000f / PlannedLength : 0f;

        public Dictionary<string, int> CountByType()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (SpawnEvent spawnEvent in Events)
            {
                string id = spawnEvent.Definition.id;
                counts.TryGetValue(id, out int count);
                counts[id] = count + 1;
            }
            return counts;
        }

        public string Summary()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append($"seed {Seed}, {Events.Count} events ({EventsPerKm:F1}/km over {PlannedLength:F0} m), ");
            builder.Append($"rejected placements {RejectedPlacements}, skipped events {SkippedEvents}. Per type:");
            foreach (KeyValuePair<string, int> pair in CountByType())
            {
                builder.Append($" {pair.Key}={pair.Value}");
            }
            return builder.ToString();
        }
    }
}
