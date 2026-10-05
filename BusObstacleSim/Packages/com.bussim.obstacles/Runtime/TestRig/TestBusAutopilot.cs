using BusSim.Obstacles;
using BusSim.Road;
using UnityEngine;

namespace BusSim.TestRig
{
    /// <summary>
    /// Drives the test bus down one lane by pure pursuit. A test aid: it lets the drive-the-whole-road
    /// check run without a keyboard. Disabled by default so manual driving works.
    /// </summary>
    [RequireComponent(typeof(TestBusController))]
    public class TestBusAutopilot : MonoBehaviour
    {
        [SerializeField] private RoadSampler road;
        [SerializeField, Min(0)] private int laneIndexFromLeft = 0;
        [SerializeField, Min(1f)] private float targetSpeedKmh = 50f;
        [SerializeField, Min(1f)] private float lookAheadMetres = 15f;
        [Tooltip("Heading error in degrees that gives full steering input.")]
        [SerializeField, Min(1f)] private float fullSteerErrorDegrees = 20f;
        [SerializeField, Min(0.01f)] private float speedGain = 0.5f;
        [SerializeField, Min(0f)] private float stopBeforeEndMetres = 2f;

        [Header("Obstacle braking")]
        [Tooltip("Brake to a stop when an obstacle collider is in the bus's path within stopping distance.")]
        [SerializeField] private bool brakeForObstacles = true;
        [SerializeField] private string obstacleLayerName = "Obstacles";
        [SerializeField, Min(0f)] private float reactionTimeSeconds = 1f;
        [Tooltip("Deceleration assumed when working out the look-ahead distance.")]
        [SerializeField, Min(0.1f)] private float assumedDeceleration = 6f;
        [SerializeField, Min(0f)] private float stopMarginMetres = 3f;
        [SerializeField, Min(0f)] private float busFrontOffset = 6f;
        [Tooltip("Half width of the bus's own path. Anything overlapping it ahead is braked for.")]
        [SerializeField, Min(0.1f)] private float pathHalfWidth = 1.5f;
        [Tooltip("Half width of the area scanned ahead. Moving obstacles anywhere on the carriageway are braked for.")]
        [SerializeField, Min(0.1f)] private float scanHalfWidth = 6f;
        [SerializeField, Min(0.1f)] private float probeHeight = 1f;

        private readonly RaycastHit[] probeHits = new RaycastHit[16];

        private TestBusController controller;
        private bool finished;
        private int obstacleMask;

        public float BusS { get; private set; }
        public float BusT { get; private set; }
        public float MaxAbsT { get; private set; }
        public bool Finished => finished;

        /// <summary>True while braking because an obstacle is in the path.</summary>
        public bool BrakingForObstacle { get; private set; }

        public void SetBrakeForObstacles(bool brake)
        {
            brakeForObstacles = brake;
        }

        private void Awake()
        {
            controller = GetComponent<TestBusController>();
            obstacleMask = LayerMask.GetMask(obstacleLayerName);
        }

        private void OnEnable()
        {
            finished = false;
            MaxAbsT = 0f;
            if (road == null)
            {
                road = Object.FindAnyObjectByType<RoadSampler>();
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.ClearInputOverride();
            }
        }

        private void FixedUpdate()
        {
            if (road == null)
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

            float laneT = road.Settings != null ? road.Settings.GetLaneCentreT(laneIndexFromLeft) : 0f;
            Vector3 aim = road.GetPoint(s + lookAheadMetres, laneT) - transform.position;
            aim.y = 0f;
            float headingError = Vector3.SignedAngle(transform.forward, aim, Vector3.up);
            float steer = Mathf.Clamp(headingError / fullSteerErrorDegrees, -1f, 1f);

            BrakingForObstacle = brakeForObstacles && ObstacleInPath();
            float targetSpeed = finished || BrakingForObstacle ? 0f : targetSpeedKmh / 3.6f;
            float throttle = BrakingForObstacle
                ? -1f
                : Mathf.Clamp((targetSpeed - controller.SpeedMetresPerSecond) * speedGain, -1f, 1f);
            controller.SetInputOverride(new Vector2(steer, throttle));
        }

        private bool ObstacleInPath()
        {
            if (obstacleMask == 0)
            {
                return false;
            }

            float speed = Mathf.Max(controller.SpeedMetresPerSecond, 0f);
            float lookAhead = speed * reactionTimeSeconds + speed * speed / (2f * assumedDeceleration) + stopMarginMetres;
            Vector3 origin = transform.position + transform.forward * busFrontOffset + Vector3.up * probeHeight;
            Vector3 halfExtents = new Vector3(scanHalfWidth, probeHeight * 0.9f, 0.1f);
            int count = Physics.BoxCastNonAlloc(origin, halfExtents, transform.forward, probeHits, transform.rotation,
                lookAhead, obstacleMask, QueryTriggerInteraction.Collide);

            float halfRoad = road.Settings != null ? road.Settings.HalfRoadWidth : scanHalfWidth;
            for (int i = 0; i < count; i++)
            {
                Collider hit = probeHits[i].collider;
                (float s, float t) = road.ProjectToRoad(hit.bounds.center);
                if (s < BusS)
                {
                    continue;
                }

                ObstacleBehaviour obstacle = hit.GetComponentInParent<ObstacleBehaviour>();
                bool known = obstacle != null && obstacle.Event.Definition != null;
                float halfWidth = known ? obstacle.Event.Definition.footprintWidth * 0.5f : hit.bounds.extents.x;
                bool inPath = Mathf.Abs(t - BusT) < pathHalfWidth + halfWidth;
                bool movingOnRoad = known && obstacle.Event.Definition.isDynamic && Mathf.Abs(t) - halfWidth < halfRoad;
                if (inPath || movingOnRoad)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
