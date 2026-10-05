using System.Collections.Generic;
using System.Text;
using BusSim.Obstacles;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>FR10: logs the seed, planned and activated counts per type, and rejected placements.</summary>
    [RequireComponent(typeof(ObstacleSpawner))]
    public class RunLogger : MonoBehaviour
    {
        private readonly Dictionary<string, int> activated = new Dictionary<string, int>();
        private ObstacleSpawner spawner;
        private SpawnPlan plan;

        private void OnEnable()
        {
            spawner = GetComponent<ObstacleSpawner>();
            spawner.PlanBuilt += OnPlanBuilt;
            spawner.ObstacleActivated += OnActivated;
        }

        private void OnDisable()
        {
            spawner.PlanBuilt -= OnPlanBuilt;
            spawner.ObstacleActivated -= OnActivated;
            LogActivated();
        }

        private void OnPlanBuilt(SpawnPlan builtPlan)
        {
            LogActivated();
            plan = builtPlan;
            activated.Clear();
            Debug.Log($"BusSim RunLogger: seed {plan.Seed}. Planned per type:{Format(plan.CountByType())}. " +
                $"Rejected placements {plan.RejectedPlacements}, skipped events {plan.SkippedEvents}.", this);
        }

        private void OnActivated(SpawnEvent spawnEvent, ObstacleBehaviour behaviour)
        {
            activated.TryGetValue(spawnEvent.Definition.id, out int count);
            activated[spawnEvent.Definition.id] = count + 1;
        }

        private void LogActivated()
        {
            if (plan == null)
            {
                return;
            }
            Debug.Log($"BusSim RunLogger: seed {plan.Seed}. Activated per type:{Format(activated)}.", this);
        }

        private static string Format(Dictionary<string, int> counts)
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                builder.Append($" {pair.Key}={pair.Value}");
            }
            return builder.Length > 0 ? builder.ToString() : " none";
        }
    }
}
