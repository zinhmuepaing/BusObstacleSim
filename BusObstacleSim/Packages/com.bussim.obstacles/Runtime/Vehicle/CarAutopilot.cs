using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using BusSim.Traffic;
using UnityEngine;

namespace BusSim.Vehicle
{
    /// <summary>
    /// Drives the car down one lane by pure pursuit and behaves like a careful driver: it steers into the
    /// free gap past static obstacles when the gap is wide enough, and brakes to a stop for moving
    /// obstacles on its path or when the road ahead is truly blocked. A test aid, disabled by default
    /// so manual driving works.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarAutopilot : MonoBehaviour
    {
        private const float MetresPerSecondPerKmh = 1f / 3.6f;

        [SerializeField] private RoadSampler road;
        [SerializeField, Min(0)] private int laneIndexFromLeft = 0;
        [SerializeField, Min(1f)] private float targetSpeedKmh = 50f;
        [Tooltip("Look-ahead distance for pure pursuit is speed times this, but at least the minimum.")]
        [SerializeField, Min(0.1f)] private float lookAheadSeconds = 0.9f;
        [SerializeField, Min(1f)] private float lookAheadMinMetres = 6f;
        [SerializeField, Min(0.01f)] private float speedGain = 0.6f;
        [SerializeField, Min(0f)] private float stopBeforeEndMetres = 2f;

        [Header("Obstacle handling")]
        [Tooltip("Brake to a stop when a moving obstacle or an unpassable blockage is ahead.")]
        [SerializeField] private bool brakeForObstacles = true;
        [Tooltip("Steer around static obstacles through the free gap instead of stopping behind them.")]
        [SerializeField] private bool steerAroundStatics = true;
        [SerializeField] private string obstacleLayerName = "Obstacles";
        [SerializeField, Min(0f)] private float reactionTimeSeconds = 1f;
        [Tooltip("Deceleration assumed when working out the look-ahead distance.")]
        [SerializeField, Min(0.1f)] private float assumedDeceleration = 6f;
        [SerializeField, Min(0f)] private float stopMarginMetres = 3f;
        [Tooltip("Clearance kept each side of the car's own width, in the path and when squeezing past.")]
        [SerializeField, Min(0f)] private float pathMargin = 0.5f;
        [Tooltip("Half width of the area scanned ahead.")]
        [SerializeField, Min(0.1f)] private float scanHalfWidth = 6f;
        [SerializeField, Min(0.1f)] private float probeHeight = 1f;
        [Tooltip("How fast the aim point may shift sideways, in metres per second.")]
        [SerializeField, Min(0.1f)] private float lateralShiftRate = 2f;

        [Header("Following traffic")]
        [SerializeField] private string trafficLayerName = "Traffic";
        [Tooltip("Closer than this to the car ahead means emergency braking.")]
        [SerializeField, Min(0.5f)] private float followMinGap = 4f;
        [Tooltip("Time gap kept to a slower car ahead.")]
        [SerializeField, Min(0.1f)] private float followTimeHeadway = 1.8f;
        [Tooltip("Extra distance beyond the stopping distance at which a slower car ahead is noticed.")]
        [SerializeField, Min(0f)] private float followExtraReach = 15f;

        private readonly RaycastHit[] probeHits = new RaycastHit[32];
        private const int TrafficProbeCapacity = 32;

        private readonly RaycastHit[] trafficHits = new RaycastHit[TrafficProbeCapacity];
        private int trafficMask;
        private readonly List<Footprint> statics = new List<Footprint>();
        private readonly List<Footprint> gapScratch = new List<Footprint>();
        private CarController car;
        private bool finished;
        private int obstacleMask;
        private float aimT;
        private bool aimInitialised;

        public float BusS { get; private set; }
        public float BusT { get; private set; }
        public float MaxAbsT { get; private set; }
        public bool Finished => finished;

        /// <summary>True while braking because of an obstacle or blockage.</summary>
        public bool BrakingForObstacle { get; private set; }

        public float TargetSpeedKmh => targetSpeedKmh;

        public void SetBrakeForObstacles(bool brake)
        {
            brakeForObstacles = brake;
        }

        public void SetSteerAroundStatics(bool steerAround)
        {
            steerAroundStatics = steerAround;
        }

        public void SetTargetSpeedKmh(float kmh)
        {
            targetSpeedKmh = kmh;
        }

        private void Awake()
        {
            car = GetComponent<CarController>();
            obstacleMask = LayerMask.GetMask(obstacleLayerName);
            trafficMask = LayerMask.GetMask(trafficLayerName);
        }

        private void OnEnable()
        {
            finished = false;
            MaxAbsT = 0f;
            aimInitialised = false;
            if (road == null)
            {
                road = Object.FindAnyObjectByType<RoadSampler>();
            }
        }

        private void OnDisable()
        {
            if (car != null)
            {
                car.ClearDrive();
            }
        }

        private void FixedUpdate()
        {
            Tick(Time.fixedDeltaTime);
        }

        /// <summary>One control step. Public so tests and the harness can drive it.</summary>
        public void Tick(float deltaTime)
        {
            if (road == null || car == null || car.Settings == null)
            {
                return;
            }

            (float s, float t) = road.ProjectToRoad(transform.position);
            BusS = s;
            BusT = t;
            MaxAbsT = Mathf.Max(MaxAbsT, Mathf.Abs(t));

            if (!finished && s >= road.Length - stopBeforeEndMetres)
            {
                finished = true;
                Debug.Log($"BusSim autopilot: reached s = {s:F1} m. Max |t| = {MaxAbsT:F2} m, top speed limit {targetSpeedKmh:F0} km/h.");
            }

            float speed = Mathf.Max(car.SpeedMetresPerSecond, 0f);
            float laneT = road.Settings != null ? road.Settings.GetLaneCentreT(laneIndexFromLeft) : 0f;
            if (!aimInitialised)
            {
                aimT = laneT;
                aimInitialised = true;
            }

            ScanAhead(speed, laneT, out bool mustBrake, out float wantedT);
            aimT = Mathf.MoveTowards(aimT, wantedT, lateralShiftRate * deltaTime);
            BrakingForObstacle = brakeForObstacles && mustBrake;

            float lookAhead = Mathf.Max(lookAheadMinMetres, speed * lookAheadSeconds);
            Vector3 aim = road.GetPoint(s + lookAhead, aimT) - transform.position;
            aim.y = 0f;

            // Pure pursuit: steer angle = atan(2 L sin(alpha) / lookAhead).
            float alpha = Vector3.SignedAngle(transform.forward, aim, Vector3.up) * Mathf.Deg2Rad;
            float steerDegrees = Mathf.Atan(2f * car.Wheelbase * Mathf.Sin(alpha) / lookAhead) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(steerDegrees / car.MaxSteerAngleNow, -1f, 1f);

            float followCap = FollowSpeedCap(speed, out bool tooClose);
            float cruise = Mathf.Min(targetSpeedKmh * MetresPerSecondPerKmh, followCap);
            float targetSpeed = finished || BrakingForObstacle ? 0f : cruise;
            bool brakeHard = BrakingForObstacle || (brakeForObstacles && tooClose);
            float throttle = brakeHard
                ? -1f
                : Mathf.Clamp((targetSpeed - car.SpeedMetresPerSecond) * speedGain, -1f, 1f);
            car.SetDrive(throttle, steer);
        }

        /// <summary>
        /// Speed cap that keeps a safe time gap to a slower car ahead in the same lane. Infinity if the lane is clear.
        /// </summary>
        private float FollowSpeedCap(float speed, out bool tooClose)
        {
            tooClose = false;
            if (trafficMask == 0)
            {
                return float.PositiveInfinity;
            }

            float reach = speed * reactionTimeSeconds + speed * speed / (2f * assumedDeceleration) + stopMarginMetres + followExtraReach;
            float halfWidth = car.Width * 0.5f + pathMargin;
            Vector3 origin = transform.position + transform.forward * car.FrontOffset + Vector3.up * probeHeight;
            Vector3 halfExtents = new Vector3(halfWidth, probeHeight * 0.9f, 0.1f);
            int count = Physics.BoxCastNonAlloc(origin, halfExtents, transform.forward, trafficHits, transform.rotation,
                reach, trafficMask, QueryTriggerInteraction.Ignore);

            float cap = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                TrafficVehicle other = trafficHits[i].collider.GetComponentInParent<TrafficVehicle>();
                if (other == null || other.Direction < 0)
                {
                    continue;
                }

                float gap = trafficHits[i].distance;
                Rigidbody otherBody = trafficHits[i].collider.attachedRigidbody;
                float leadSpeed = otherBody != null ? Mathf.Max(0f, Vector3.Dot(otherBody.linearVelocity, transform.forward)) : 0f;
                float allowed = leadSpeed + Mathf.Max(0f, gap - followMinGap) / followTimeHeadway;
                cap = Mathf.Min(cap, allowed);
                tooClose |= gap < followMinGap;
            }
            return cap;
        }

        /// <summary>
        /// Looks ahead for obstacles. Moving ones in the car's path demand braking. Static ones set a lateral
        /// aim through the widest gap; if no gap fits the car the road counts as blocked.
        /// </summary>
        private void ScanAhead(float speed, float laneT, out bool mustBrake, out float wantedT)
        {
            mustBrake = false;
            wantedT = laneT;
            if (obstacleMask == 0)
            {
                return;
            }

            float lookAhead = speed * reactionTimeSeconds + speed * speed / (2f * assumedDeceleration) + stopMarginMetres;
            Vector3 origin = transform.position + transform.forward * car.FrontOffset + Vector3.up * probeHeight;
            Vector3 halfExtents = new Vector3(scanHalfWidth, probeHeight * 0.9f, 0.1f);
            int count = Physics.BoxCastNonAlloc(origin, halfExtents, transform.forward, probeHits, transform.rotation,
                lookAhead, obstacleMask, QueryTriggerInteraction.Collide);

            float halfRoad = road.Settings != null ? road.Settings.HalfRoadWidth : scanHalfWidth;
            float halfCar = car.Width * 0.5f;
            float pathHalfWidth = halfCar + pathMargin;
            statics.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider hit = probeHits[i].collider;
                ObstacleBehaviour obstacle = hit.GetComponentInParent<ObstacleBehaviour>();
                if (obstacle == null || obstacle.Event.Definition == null || obstacle.WasHit)
                {
                    continue;
                }

                ObstacleDefinition definition = obstacle.Event.Definition;
                (float s, float t) = road.ProjectToRoad(hit.bounds.center);
                if (s < BusS)
                {
                    continue;
                }

                float halfWidth = definition.footprintWidth * 0.5f;
                if (definition.isDynamic)
                {
                    bool inPath = Mathf.Abs(t - BusT) < pathHalfWidth + halfWidth;
                    bool movingOnRoad = Mathf.Abs(t) - halfWidth < halfRoad;
                    mustBrake |= inPath || movingOnRoad;
                }
                else
                {
                    statics.Add(obstacle.Event.Footprint);
                }
            }

            if (statics.Count == 0)
            {
                return;
            }

            if (!steerAroundStatics)
            {
                foreach (Footprint footprint in statics)
                {
                    mustBrake |= Mathf.Abs(footprint.T - BusT) < pathHalfWidth + footprint.Width * 0.5f;
                }
                return;
            }

            // Widest free gap across all the static obstacles ahead.
            float gap = ClearanceValidator.LargestGap(statics[0], statics, road.Settings.RoadWidth, 0f, out float gapStart, gapScratch);
            if (gap < 2f * pathHalfWidth)
            {
                mustBrake = true;
                return;
            }

            // Aim for the lane centre if the car fits through there, else the nearest position that does.
            float minT = gapStart + pathHalfWidth;
            float maxT = gapStart + gap - pathHalfWidth;
            wantedT = Mathf.Clamp(laneT, minT, maxT);
        }
    }
}
