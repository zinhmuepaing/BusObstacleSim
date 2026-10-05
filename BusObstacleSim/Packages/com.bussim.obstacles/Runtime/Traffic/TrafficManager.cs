using System;
using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using UnityEngine;

namespace BusSim.Traffic
{
    /// <summary>
    /// Plans ambient traffic from the run seed, activates pooled cars as the driven vehicle approaches, and
    /// answers the questions cars ask: who is ahead of me, is the gap at the junction big enough, where do I turn.
    /// Same seed gives the same traffic (FR7). All randomness comes from TrafficPlanner's own seeded stream.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        private const float SpawnClearance = 12f;
        private const float PlayerSpawnClearance = 25f;
        private const float LaneBandMargin = 0.4f;
        private const float ConflictAhead = 5f;
        private const float OverlapGap = 0.1f;
        private const float HitReleaseSeconds = 25f;
        private const int PrewarmPerPrefab = 2;
        private const int EventIndexBase = 5000;

        [SerializeField] private RoadSampler mainRoad;
        [Tooltip("Optional. Side-road traffic only runs when the side road and a Junction zone exist.")]
        [SerializeField] private RoadSampler sideRoad;
        [SerializeField] private ObstacleSpawner spawner;
        [SerializeField] private RoadZones zones;
        [SerializeField] private TrafficSettings settings;
        [SerializeField] private bool trafficEnabled = true;

        private readonly List<TrafficVehicle> active = new List<TrafficVehicle>();
        private readonly List<float> hitTimes = new List<float>();
        private readonly List<Footprint> obstacleFootprints = new List<Footprint>();
        private List<TrafficSpawn> plan = new List<TrafficSpawn>();
        private bool[] spawned = new bool[0];
        private ObjectPool pool;
        private SpawnPlan lastObstaclePlan;
        private float junctionS;
        private bool hasJunction;

        public RoadSampler MainRoad => mainRoad;
        public RoadSampler SideRoad => sideRoad;
        public TrafficSettings Settings => settings;
        public IReadOnlyList<TrafficSpawn> Plan => plan;
        public int ActiveCount => active.Count;
        public float JunctionS => junctionS;
        public float TurnEndS => junctionS + settings.turnEndPast;
        public float SameDirectionCruiseSpeed => settings.sameDirectionMaxKmh / 3.6f;

        public void Configure(RoadSampler main, RoadSampler side, ObstacleSpawner obstacleSpawner, RoadZones roadZones, TrafficSettings trafficSettings)
        {
            mainRoad = main;
            sideRoad = side;
            spawner = obstacleSpawner;
            zones = roadZones;
            settings = trafficSettings;
        }

        private void FixedUpdate()
        {
            Step(Time.fixedDeltaTime);
        }

        /// <summary>One traffic update. Public so tools and tests can drive it without a render loop.</summary>
        public void Step(float deltaTime)
        {
            if (!trafficEnabled || settings == null || mainRoad == null || spawner == null || spawner.Plan == null)
            {
                return;
            }

            if (!ReferenceEquals(spawner.Plan, lastObstaclePlan))
            {
                Restart(spawner.Plan);
            }

            spawner.CollectStaticFootprints(obstacleFootprints);
            ActivatePlanned();

            for (int i = active.Count - 1; i >= 0; i--)
            {
                TrafficVehicle vehicle = active[i];
                vehicle.Tick(deltaTime);
                if (ShouldRelease(vehicle, i))
                {
                    Release(i);
                }
            }
        }

        private void Restart(SpawnPlan obstaclePlan)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Release(i);
            }
            pool?.Clear();
            pool = new ObjectPool(transform);
            foreach (GameObject prefab in settings.vehiclePrefabs)
            {
                pool.Prewarm(prefab, PrewarmPerPrefab);
            }

            lastObstaclePlan = obstaclePlan;
            FindJunction();
            plan = TrafficPlanner.Plan(spawner.Seed, settings, mainRoad.Length, junctionS, hasJunction && sideRoad != null, settings.vehiclePrefabs.Count);
            spawned = new bool[plan.Count];
        }

        private void FindJunction()
        {
            hasJunction = false;
            if (zones == null)
            {
                return;
            }
            foreach (RoadZone zone in zones.Zones)
            {
                if (zone.type == ZoneType.Junction)
                {
                    junctionS = (zone.sStart + zone.sEnd) * 0.5f;
                    hasJunction = true;
                    return;
                }
            }
        }

        // ---------------- activation ----------------

        private void ActivatePlanned()
        {
            float playerS = spawner.VehicleS;
            for (int i = 0; i < plan.Count; i++)
            {
                if (spawned[i])
                {
                    continue;
                }

                TrafficSpawn entry = plan[i];
                if (entry.Kind == TrafficKind.SideRoadEntry)
                {
                    if (playerS >= entry.S)
                    {
                        spawned[i] = true;
                        SpawnEntry(entry);
                    }
                    continue;
                }

                bool inWindow = playerS + settings.spawnAhead >= entry.S;
                bool alreadyPassed = entry.S < playerS + PlayerSpawnClearance;
                if (alreadyPassed)
                {
                    spawned[i] = true; // behind or too close to the vehicle: skip it for good
                }
                else if (inWindow && LaneIsClear(entry))
                {
                    spawned[i] = true;
                    SpawnLaneVehicle(entry);
                }
            }
        }

        private bool LaneIsClear(TrafficSpawn entry)
        {
            float laneT = LaneCentre(entry);
            foreach (TrafficVehicle other in active)
            {
                if (other.OnMainRoad && Mathf.Abs(other.LaneT - laneT) < mainRoad.Settings.laneWidth * 0.5f
                    && Mathf.Abs(other.MainRoadS - entry.S) < SpawnClearance + other.Length)
                {
                    return false;
                }
            }
            return true;
        }

        private float LaneCentre(TrafficSpawn entry)
        {
            return entry.Kind == TrafficKind.Oncoming
                ? mainRoad.Settings.GetOncomingLaneCentreT(entry.Lane)
                : mainRoad.Settings.GetLaneCentreT(entry.Lane);
        }

        private void SpawnLaneVehicle(TrafficSpawn entry)
        {
            bool oncoming = entry.Kind == TrafficKind.Oncoming;
            TrafficVehicle vehicle = TakeVehicle(entry);
            if (vehicle == null)
            {
                return;
            }
            vehicle.Init(this, settings, mainRoad, LaneCentre(entry), oncoming ? -1 : 1, entry.S, entry.SpeedMetresPerSecond,
                entry.SpeedMetresPerSecond, TrafficVehicle.Phase.Cruising, 0f, VehicleId(entry), entry.Index);
            active.Add(vehicle);
            hitTimes.Add(-1f);
        }

        private void SpawnEntry(TrafficSpawn entry)
        {
            TrafficVehicle vehicle = TakeVehicle(entry);
            if (vehicle == null)
            {
                return;
            }
            // Inbound traffic on the side road keeps left of its own travel, which is its +t side.
            float laneT = sideRoad.Settings.GetLaneCentreT(sideRoad.Settings.laneCount - 1);
            vehicle.Init(this, settings, sideRoad, laneT, -1, settings.sideRoadStartS, entry.SpeedMetresPerSecond,
                entry.SpeedMetresPerSecond, TrafficVehicle.Phase.GivingWay, entry.AcceptedGapSeconds, VehicleId(entry), entry.Index);
            active.Add(vehicle);
            hitTimes.Add(-1f);
        }

        private TrafficVehicle TakeVehicle(TrafficSpawn entry)
        {
            if (settings.vehiclePrefabs.Count == 0)
            {
                return null;
            }
            GameObject prefab = settings.vehiclePrefabs[entry.ModelIndex % settings.vehiclePrefabs.Count];
            GameObject instance = pool.Get(prefab);
            TrafficVehicle vehicle = instance.GetComponent<TrafficVehicle>();
            if (vehicle == null)
            {
                Debug.LogError($"BusSim traffic: prefab {prefab.name} has no TrafficVehicle.", prefab);
                pool.Release(prefab, instance);
            }
            return vehicle;
        }

        private string VehicleId(TrafficSpawn entry)
        {
            return $"TRAFFIC_{entry.Kind}_{entry.ModelIndex}_{entry.Index}";
        }

        // ---------------- release ----------------

        private bool ShouldRelease(TrafficVehicle vehicle, int index)
        {
            float playerS = spawner.VehicleS;
            if (vehicle.WasHit)
            {
                if (hitTimes[index] < 0f)
                {
                    hitTimes[index] = Time.time;
                }
                return Time.time - hitTimes[index] > HitReleaseSeconds;
            }

            if (!vehicle.OnMainRoad)
            {
                return false;
            }
            bool farBehind = vehicle.MainRoadS < playerS - settings.despawnBehind;
            bool pastEnd = vehicle.Direction > 0 && vehicle.MainRoadS > mainRoad.Length - 1f;
            return farBehind || pastEnd;
        }

        private void Release(int index)
        {
            TrafficVehicle vehicle = active[index];
            GameObject prefab = PrefabOf(vehicle);
            active.RemoveAt(index);
            hitTimes.RemoveAt(index);
            if (prefab != null)
            {
                pool.Release(prefab, vehicle.gameObject);
            }
            else
            {
                vehicle.gameObject.SetActive(false);
            }
        }

        private GameObject PrefabOf(TrafficVehicle vehicle)
        {
            if (vehicle.PlanIndex < 0 || vehicle.PlanIndex >= plan.Count || settings.vehiclePrefabs.Count == 0)
            {
                return null;
            }
            return settings.vehiclePrefabs[plan[vehicle.PlanIndex].ModelIndex % settings.vehiclePrefabs.Count];
        }

        // ---------------- questions cars ask ----------------

        /// <summary>The nearest thing ahead in this car's lane: another car, the driven vehicle, or a static obstacle.</summary>
        public void FindLead(TrafficVehicle car, out float gap, out float leadSpeed)
        {
            gap = float.PositiveInfinity;
            leadSpeed = 0f;
            float laneHalf = mainRoad.Settings.laneWidth * 0.5f;
            if (car.Sampler != mainRoad)
            {
                return;
            }

            float carFront = car.S + car.Direction * car.Length * 0.5f;
            foreach (TrafficVehicle other in active)
            {
                if (other == car || !other.OnMainRoad || other.Direction != car.Direction)
                {
                    continue;
                }
                if (Mathf.Abs(other.LaneT - car.LaneT) >= laneHalf && other.CurrentPhase != TrafficVehicle.Phase.Turning)
                {
                    continue;
                }

                float otherRear = other.MainRoadS - car.Direction * other.Length * 0.5f;
                float distance = (otherRear - carFront) * car.Direction;
                if (distance > 0f && distance < gap)
                {
                    gap = distance;
                    leadSpeed = other.WasHit ? 0f : other.Speed;
                }
            }

            if (car.Direction > 0)
            {
                ConsiderPlayer(car, carFront, laneHalf, ref gap, ref leadSpeed);
                ConsiderObstacles(car, carFront, ref gap, ref leadSpeed);
            }
        }

        private void ConsiderPlayer(TrafficVehicle car, float carFront, float laneHalf, ref float gap, ref float leadSpeed)
        {
            if (Mathf.Abs(spawner.VehicleT - car.LaneT) >= laneHalf + LaneBandMargin)
            {
                return;
            }
            float playerRear = spawner.VehicleFrontS - settings.playerLength;
            float distance = playerRear - carFront;
            if (distance > 0f && distance < gap)
            {
                gap = distance;
                leadSpeed = Mathf.Max(spawner.VehicleSpeed, 0f);
            }
        }

        private void ConsiderObstacles(TrafficVehicle car, float carFront, ref float gap, ref float leadSpeed)
        {
            foreach (Footprint footprint in obstacleFootprints)
            {
                float sideways = Mathf.Abs(footprint.T - car.LaneT);
                if (sideways >= footprint.Width * 0.5f + car.Width * 0.5f)
                {
                    continue;
                }
                float distance = footprint.SMin - carFront;
                if (distance <= 0f && carFront < footprint.SMax)
                {
                    distance = OverlapGap; // already touching: hold still rather than drive through it
                }
                if (distance > 0f && distance < gap)
                {
                    gap = distance;
                    leadSpeed = 0f;
                }
            }
        }

        /// <summary>
        /// True if a side-road driver may pull out: nothing in the main road's left lane would arrive at the junction
        /// sooner than the driver's accepted gap, and the turn area is clear.
        /// </summary>
        public bool GapAcceptable(TrafficVehicle car)
        {
            float lane0 = mainRoad.Settings.GetLaneCentreT(0);
            float laneHalf = mainRoad.Settings.laneWidth * 0.5f;
            float needed = car.AcceptedGapSeconds;

            if (Mathf.Abs(spawner.VehicleT - lane0) < laneHalf + LaneBandMargin)
            {
                float distance = junctionS - spawner.VehicleFrontS;
                float arrival = distance / Mathf.Max(spawner.VehicleSpeed, 0.5f);
                bool inTurnArea = spawner.VehicleFrontS > junctionS - ConflictAhead && spawner.VehicleFrontS - settings.playerLength < TurnEndS;
                if (inTurnArea || (distance > 0f && arrival < needed))
                {
                    return false;
                }
            }

            foreach (TrafficVehicle other in active)
            {
                if (other == car || !other.OnMainRoad || other.Direction < 0)
                {
                    continue;
                }
                bool turning = other.CurrentPhase == TrafficVehicle.Phase.Turning;
                if (!turning && Mathf.Abs(other.LaneT - lane0) >= laneHalf)
                {
                    continue;
                }

                float front = other.MainRoadS + other.Length * 0.5f;
                float rear = other.MainRoadS - other.Length * 0.5f;
                if (front > junctionS - ConflictAhead && rear < TurnEndS)
                {
                    return false; // someone is in or at the turn area
                }
                float distance = junctionS - front;
                if (distance > 0f && distance / Mathf.Max(other.Speed, 0.5f) < needed)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Fills the car's turn path: a smooth left turn from where it is on the side road into the main road's left lane.</summary>
        public void BuildTurn(TrafficVehicle car)
        {
            float lane0 = mainRoad.Settings.GetLaneCentreT(0);
            Vector3 start = car.transform.position;
            start.y = mainRoad.GetPoint(junctionS, lane0).y;
            Vector3 end = mainRoad.GetPoint(TurnEndS, lane0);
            Vector3 corner = mainRoad.GetPoint(junctionS + sideRoad.Settings.laneWidth * 0.5f, lane0);

            float total = 0f;
            Vector3 previous = start;
            for (int i = 0; i <= TrafficVehicle.TurnSamples; i++)
            {
                float u = (float)i / TrafficVehicle.TurnSamples;
                Vector3 point = (1f - u) * (1f - u) * start + 2f * (1f - u) * u * corner + u * u * end;
                total += i == 0 ? 0f : Vector3.Distance(previous, point);
                car.TurnPoints[i] = point;
                car.TurnCumulative[i] = total;
                previous = point;
            }
            car.TurnLength = total;
        }

        public void ReportHit(TrafficVehicle car, Collision collision)
        {
            Rigidbody other = collision.rigidbody;
            spawner.OnCollision(new CollisionRecord
            {
                Time = Time.time,
                ObstacleId = car.Id,
                EventIndex = EventIndexBase + car.PlanIndex,
                RelativeSpeed = collision.relativeVelocity.magnitude,
                VehicleSpeed = other != null ? other.linearVelocity.magnitude : 0f,
                Point = collision.contactCount > 0 ? collision.GetContact(0).point : car.transform.position
            });
        }
    }
}
