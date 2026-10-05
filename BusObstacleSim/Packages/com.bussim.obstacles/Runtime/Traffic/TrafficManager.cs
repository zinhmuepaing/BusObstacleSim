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
        private const float KmhPerMetrePerSecond = 3.6f;
        private const float HitReleaseSeconds = 25f;
        private const int PrewarmPerPrefab = 2;
        private const int EventIndexBase = 5000;

        [SerializeField] private RoadSampler mainRoad;
        [Tooltip("Optional. One entry per T-junction on the left of the main road. The junction position is where the side road meets the main road.")]
        [SerializeField] private List<RoadSampler> sideRoads = new List<RoadSampler>();
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
        private float[] junctionS = new float[0];

        public RoadSampler MainRoad => mainRoad;
        public TrafficSettings Settings => settings;
        public IReadOnlyList<TrafficSpawn> Plan => plan;
        public int ActiveCount => active.Count;

        /// <summary>Lane changes started and chasers released this run, for logs and tests.</summary>
        public int LaneChangesStarted { get; private set; }
        public int ChasersReleased { get; private set; }
        public int JunctionCount => junctionS.Length;
        public float SameDirectionCruiseSpeed => settings.sameDirectionMaxKmh / KmhPerMetrePerSecond;

        public float JunctionS(int index) => junctionS[index];
        public float TurnEndS(int index) => junctionS[index] + settings.turnEndPast;
        public RoadSampler SideRoad(int index) => sideRoads[index];

        public void Configure(RoadSampler main, IEnumerable<RoadSampler> sides, ObstacleSpawner obstacleSpawner, RoadZones roadZones, TrafficSettings trafficSettings)
        {
            mainRoad = main;
            sideRoads = new List<RoadSampler>(sides);
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
            LaneChangesStarted = 0;
            ChasersReleased = 0;
            pool?.Clear();
            pool = new ObjectPool(transform);
            foreach (GameObject prefab in settings.vehiclePrefabs)
            {
                pool.Prewarm(prefab, PrewarmPerPrefab);
            }

            lastObstaclePlan = obstaclePlan;
            FindJunctions();
            plan = TrafficPlanner.Plan(spawner.Seed, settings, mainRoad.Length, junctionS, settings.vehiclePrefabs.Count);
            spawned = new bool[plan.Count];
        }

        /// <summary>A junction is where each side road starts on the main road's edge.</summary>
        private void FindJunctions()
        {
            sideRoads.RemoveAll(side => side == null);
            junctionS = new float[sideRoads.Count];
            for (int i = 0; i < sideRoads.Count; i++)
            {
                junctionS[i] = mainRoad.ProjectToRoad(sideRoads[i].GetPoint(0f, 0f)).s;
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
                if (entry.Kind == TrafficKind.Chaser)
                {
                    if (playerS >= entry.S)
                    {
                        spawned[i] = true;
                        SpawnChaser(entry, playerS);
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
                entry.SpeedMetresPerSecond, TrafficVehicle.Phase.Cruising, 0f, VehicleId(entry), entry.Index, entry.Aggressive);
            active.Add(vehicle);
            hitTimes.Add(-1f);
        }

        /// <summary>An aggressive driver appears behind the driven vehicle, if there is room, and drives up to its tail.</summary>
        private void SpawnChaser(TrafficSpawn entry, float playerS)
        {
            float s = playerS - settings.chaserDistance;
            if (s < 0f)
            {
                return;
            }
            entry.S = s;
            if (!LaneIsClear(entry))
            {
                return;
            }
            SpawnLaneVehicle(entry);
            ChasersReleased++;
        }

        private void SpawnEntry(TrafficSpawn entry)
        {
            if (entry.JunctionIndex >= sideRoads.Count)
            {
                return;
            }
            TrafficVehicle vehicle = TakeVehicle(entry);
            if (vehicle == null)
            {
                return;
            }
            // Inbound traffic on the side road keeps left of its own travel, which is its +t side.
            RoadSampler side = sideRoads[entry.JunctionIndex];
            float laneT = side.Settings.GetLaneCentreT(side.Settings.laneCount - 1);
            vehicle.Init(this, settings, side, laneT, -1, settings.sideRoadStartS, entry.SpeedMetresPerSecond,
                entry.SpeedMetresPerSecond, TrafficVehicle.Phase.GivingWay, entry.AcceptedGapSeconds, VehicleId(entry), entry.Index,
                false, entry.JunctionIndex);
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

        /// <summary>The other lane of this car's carriageway, if it is clear enough to move into now.</summary>
        public bool TryPickLane(TrafficVehicle car, out float laneT)
        {
            LaneCentres(car.Direction, out float first, out float second);
            laneT = Mathf.Abs(car.LaneT - first) < Mathf.Abs(car.LaneT - second) ? second : first;
            bool clear = LaneChangeClear(car, laneT);
            LaneChangesStarted += clear ? 1 : 0;
            return clear;
        }

        private void LaneCentres(int direction, out float first, out float second)
        {
            RoadSettings road = mainRoad.Settings;
            if (direction > 0)
            {
                first = road.GetLaneCentreT(0);
                second = road.GetLaneCentreT(1);
            }
            else
            {
                first = road.GetOncomingLaneCentreT(0);
                second = road.GetOncomingLaneCentreT(1);
            }
        }

        /// <summary>
        /// True if nothing in the target lane is too close ahead or behind the car (the margin grows with the speed
        /// difference), including the driven vehicle and static obstacles.
        /// </summary>
        private bool LaneChangeClear(TrafficVehicle car, float targetT)
        {
            float laneHalf = mainRoad.Settings.laneWidth * 0.5f;
            foreach (TrafficVehicle other in active)
            {
                if (other == car || !other.OnMainRoad || other.Direction != car.Direction)
                {
                    continue;
                }
                bool inTarget = Mathf.Abs(other.LaneT - targetT) < laneHalf || Mathf.Abs(other.TargetLaneT - targetT) < laneHalf;
                if (inTarget && !SpaceFor(car, other.MainRoadS, other.Length, other.Speed))
                {
                    return false;
                }
            }

            if (car.Direction > 0)
            {
                if (Mathf.Abs(spawner.VehicleT - targetT) < laneHalf + LaneBandMargin
                    && !SpaceFor(car, spawner.VehicleS, settings.playerLength, Mathf.Max(spawner.VehicleSpeed, 0f)))
                {
                    return false;
                }

                float reach = settings.overtakeGap + car.Speed * settings.laneChangeGapSeconds;
                float carFront = car.S + car.Length * 0.5f;
                foreach (Footprint footprint in obstacleFootprints)
                {
                    bool inLane = Mathf.Abs(footprint.T - targetT) < footprint.Width * 0.5f + car.Width * 0.5f;
                    if (inLane && footprint.SMax > car.S - car.Length && footprint.SMin < carFront + reach)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Is there room for `car` beside or between a vehicle at s with this length and speed?</summary>
        private bool SpaceFor(TrafficVehicle car, float otherS, float otherLength, float otherSpeed)
        {
            float ahead = (otherS - car.S) * car.Direction;
            float halfLengths = (car.Length + otherLength) * 0.5f;
            float closing = ahead >= 0f ? car.Speed - otherSpeed : otherSpeed - car.Speed;
            float needed = settings.laneChangeClearance + Mathf.Max(0f, closing) * settings.laneChangeGapSeconds;
            return Mathf.Abs(ahead) - halfLengths >= needed;
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
            float junction = junctionS[car.JunctionIndex];
            float turnEnd = TurnEndS(car.JunctionIndex);

            if (Mathf.Abs(spawner.VehicleT - lane0) < laneHalf + LaneBandMargin)
            {
                float distance = junction - spawner.VehicleFrontS;
                float arrival = distance / Mathf.Max(spawner.VehicleSpeed, 0.5f);
                bool inTurnArea = spawner.VehicleFrontS > junction - ConflictAhead && spawner.VehicleFrontS - settings.playerLength < turnEnd;
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
                bool inLane0 = Mathf.Abs(other.LaneT - lane0) < laneHalf || Mathf.Abs(other.TargetLaneT - lane0) < laneHalf;
                if (!turning && !inLane0)
                {
                    continue;
                }

                float front = other.MainRoadS + other.Length * 0.5f;
                float rear = other.MainRoadS - other.Length * 0.5f;
                if (front > junction - ConflictAhead && rear < turnEnd)
                {
                    return false; // someone is in or at the turn area
                }
                float distance = junction - front;
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
            float junction = junctionS[car.JunctionIndex];
            Vector3 start = car.transform.position;
            start.y = mainRoad.GetPoint(junction, lane0).y;
            Vector3 end = mainRoad.GetPoint(TurnEndS(car.JunctionIndex), lane0);
            Vector3 corner = mainRoad.GetPoint(junction + sideRoads[car.JunctionIndex].Settings.laneWidth * 0.5f, lane0);

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
