using System.Collections.Generic;
using BusSim.Road;
using UnityEngine;

namespace BusSim.Spawning
{
    /// <summary>
    /// FR9 debug view. Draws each planned footprint, the guaranteed clear corridor around every
    /// static obstacle, rejected placements, and the spawn and despawn lines around the vehicle.
    /// Use "Preview Plan" on the spawner to see the plan without entering Play mode.
    /// </summary>
    [RequireComponent(typeof(ObstacleSpawner))]
    public class DebugGizmos : MonoBehaviour
    {
        [SerializeField] private bool showCorridor = true;
        [SerializeField] private bool showRejected = true;
        [SerializeField] private bool showSpawnWindow = true;
        [SerializeField, Min(0f)] private float drawHeight = 0.05f;
        [SerializeField] private Color staticColour = new Color(1f, 0.55f, 0f);
        [SerializeField] private Color dynamicColour = new Color(0f, 0.8f, 1f);
        [SerializeField] private Color corridorColour = new Color(0.1f, 1f, 0.3f);
        [SerializeField] private Color rejectedColour = new Color(1f, 0.1f, 0.1f);
        [SerializeField] private Color windowColour = new Color(1f, 1f, 0.2f);

        private readonly List<Footprint> statics = new List<Footprint>();
        private readonly List<Footprint> scratch = new List<Footprint>();

        private void OnDrawGizmos()
        {
            ObstacleSpawner spawner = GetComponent<ObstacleSpawner>();
            RoadSampler road = spawner.Road;
            SpawnPlan plan = spawner.Plan;
            if (road == null || road.Settings == null || plan == null)
            {
                return;
            }

            float roadWidth = road.Settings.RoadWidth;
            statics.Clear();
            foreach (SpawnEvent spawnEvent in plan.Events)
            {
                if (!spawnEvent.Definition.isDynamic)
                {
                    statics.Add(spawnEvent.Footprint);
                }
            }

            foreach (SpawnEvent spawnEvent in plan.Events)
            {
                Footprint footprint = spawnEvent.Footprint;
                Gizmos.color = spawnEvent.Definition.isDynamic ? dynamicColour : staticColour;
                DrawRect(road, footprint.SMin, footprint.SMax, footprint.TMin, footprint.TMax);

                if (showCorridor && !spawnEvent.Definition.isDynamic)
                {
                    float gap = ClearanceValidator.LargestGap(
                        footprint, statics, roadWidth, spawner.ClearanceWindowMargin, out float gapStart, scratch);
                    Gizmos.color = gap >= spawner.MinClearCorridor - ClearanceValidator.Tolerance ? corridorColour : rejectedColour;
                    DrawRect(road, footprint.SMin - spawner.ClearanceWindowMargin, footprint.SMax + spawner.ClearanceWindowMargin,
                        gapStart, gapStart + gap);
                }
            }

            if (showRejected)
            {
                Gizmos.color = rejectedColour;
                foreach (Footprint footprint in plan.RejectedFootprints)
                {
                    DrawRect(road, footprint.SMin, footprint.SMax, footprint.TMin, footprint.TMax);
                    Gizmos.DrawLine(Point(road, footprint.SMin, footprint.TMin), Point(road, footprint.SMax, footprint.TMax));
                }
            }

            if (showSpawnWindow && Application.isPlaying && spawner.VehicleTransform != null)
            {
                Gizmos.color = windowColour;
                float half = roadWidth * 0.5f;
                float spawnS = Mathf.Min(spawner.VehicleS + spawner.SpawnAhead, road.Length);
                float despawnS = Mathf.Max(spawner.VehicleS - spawner.DespawnBehind, 0f);
                Gizmos.DrawLine(Point(road, spawnS, -half), Point(road, spawnS, half));
                Gizmos.DrawLine(Point(road, despawnS, -half), Point(road, despawnS, half));
            }
        }

        private void DrawRect(RoadSampler road, float sMin, float sMax, float tMin, float tMax)
        {
            Vector3 a = Point(road, sMin, tMin);
            Vector3 b = Point(road, sMax, tMin);
            Vector3 c = Point(road, sMax, tMax);
            Vector3 d = Point(road, sMin, tMax);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }

        private Vector3 Point(RoadSampler road, float s, float t)
        {
            return road.GetPoint(s, t) + Vector3.up * drawHeight;
        }
    }
}
