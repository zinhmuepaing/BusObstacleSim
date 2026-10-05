using System.Collections.Generic;
using UnityEngine;

namespace BusSim.Traffic
{
    /// <summary>All tunables for ambient traffic: density, speeds, car following and give-way behaviour.</summary>
    [CreateAssetMenu(menuName = "BusSim/Traffic Settings", fileName = "TrafficSettings")]
    public class TrafficSettings : ScriptableObject
    {
        [Header("Density (vehicles per km of road)")]
        [Min(0f)] public float sameDirectionPerKm = 5f;
        [Min(0f)] public float oncomingPerKm = 9f;
        [Tooltip("Side-road vehicles that arrive at the give-way line over one run.")]
        [Min(0)] public int sideRoadEntries = 3;
        [Tooltip("Same-lane vehicles of one kind are planned at least this far apart.")]
        [Min(1f)] public float minSpacing = 30f;

        [Header("Speeds (km/h)")]
        [Min(5f)] public float sameDirectionMinKmh = 25f;
        [Min(5f)] public float sameDirectionMaxKmh = 45f;
        [Min(5f)] public float oncomingMinKmh = 30f;
        [Min(5f)] public float oncomingMaxKmh = 50f;
        [Min(5f)] public float sideRoadKmh = 25f;
        [Min(1f)] public float turnKmh = 15f;

        [Header("Spawn window (metres from the driven vehicle)")]
        [Min(10f)] public float spawnAhead = 160f;
        [Min(0f)] public float despawnBehind = 60f;
        [Min(0f)] public float startBuffer = 60f;
        [Min(0f)] public float endBuffer = 40f;
        [Tooltip("Side-road vehicles are released this far before the junction, measured along the main road.")]
        [Min(10f)] public float entryReleaseMin = 40f;
        [Min(10f)] public float entryReleaseMax = 260f;
        [Tooltip("Where on the side road an entering vehicle appears, metres from the junction.")]
        [Min(10f)] public float sideRoadStartS = 80f;

        [Header("Car following (IDM)")]
        [Min(0.1f)] public float timeHeadway = 1.5f;
        [Min(0f)] public float minGap = 2.5f;
        [Min(0.1f)] public float maxAcceleration = 2f;
        [Min(0.1f)] public float comfortableBraking = 3f;
        [Min(1f)] public float emergencyBraking = 9f;
        [Tooltip("Length of the driven vehicle, used when it is the lead.")]
        [Min(1f)] public float playerLength = 4f;

        [Header("Give way at the junction")]
        [Tooltip("A side-road driver accepts a gap on the main road of at least this many seconds. Drawn per driver between min and max.")]
        [Min(0.5f)] public float acceptedGapMin = 2f;
        [Min(0.5f)] public float acceptedGapMax = 5f;
        [Tooltip("Distance from the junction (along the side road) of the give-way line.")]
        [Min(0f)] public float stopLineS = 2.9f;
        [Tooltip("Main-road distance past the junction where the turn ends and the vehicle joins the lane.")]
        [Min(5f)] public float turnEndPast = 18f;
        [Tooltip("A waiting driver may go if the gap is fine and the line is closer than this.")]
        [Min(1f)] public float goWithin = 12f;

        [Header("Aggressive drivers (both carriageways)")]
        [Tooltip("Share of the same-direction and oncoming vehicles driven aggressively.")]
        [Range(0f, 1f)] public float aggressiveShare = 0.3f;
        [Min(5f)] public float aggressiveMinKmh = 60f;
        [Min(5f)] public float aggressiveMaxKmh = 85f;
        [Tooltip("Aggressive drivers keep a short time gap and brake late.")]
        [Min(0.1f)] public float aggressiveTimeHeadway = 0.6f;
        [Min(0f)] public float aggressiveMinGap = 1.2f;
        [Min(0.1f)] public float aggressiveAcceleration = 3.5f;
        [Tooltip("Aggressive same-direction drivers released behind the driven vehicle, per run.")]
        [Min(0)] public int chasers = 4;
        [Tooltip("A chaser appears this far behind the driven vehicle.")]
        [Min(10f)] public float chaserDistance = 45f;
        [Tooltip("An aggressive driver tries to change lane when the car ahead is closer than this and slower than it wants to go.")]
        [Min(5f)] public float overtakeGap = 28f;
        [Tooltip("Seconds an aggressive driver waits between lane changes.")]
        [Min(0.5f)] public float laneChangeCooldown = 4f;
        [Tooltip("Seconds a lane change takes.")]
        [Min(0.3f)] public float laneChangeSeconds = 1.4f;
        [Tooltip("Lane change is refused unless the target lane is clear this far ahead and behind (metres, plus speed difference times the gap time).")]
        [Min(2f)] public float laneChangeClearance = 8f;
        [Min(0f)] public float laneChangeGapSeconds = 1.5f;

        [Header("Vehicles")]
        public List<GameObject> vehiclePrefabs = new List<GameObject>();
    }
}
