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

        private TestBusController controller;
        private bool finished;

        public float BusS { get; private set; }
        public float BusT { get; private set; }
        public float MaxAbsT { get; private set; }
        public bool Finished => finished;

        private void Awake()
        {
            controller = GetComponent<TestBusController>();
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

            float targetSpeed = finished ? 0f : targetSpeedKmh / 3.6f;
            float throttle = Mathf.Clamp((targetSpeed - controller.SpeedMetresPerSecond) * speedGain, -1f, 1f);
            controller.SetInputOverride(new Vector2(steer, throttle));
        }
    }
}
