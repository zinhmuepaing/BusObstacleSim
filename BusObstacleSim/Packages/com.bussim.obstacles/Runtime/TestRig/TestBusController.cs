using UnityEngine;
using UnityEngine.InputSystem;

namespace BusSim.TestRig
{
    /// <summary>
    /// Simple arcade bus for testing obstacles. W/S or Up/Down accelerate and brake, A/D or
    /// Left/Right steer. Steering uses a bicycle model so the yaw rate follows speed.
    /// Not a vehicle simulation. Forward speed and heading are set directly each physics step.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TestBusController : MonoBehaviour
    {
        private const float KmhToMetresPerSecond = 1f / 3.6f;
        private const float MetresPerSecondToKmh = 3.6f;

        [Header("Speed")]
        [SerializeField, Min(1f)] private float maxSpeedKmh = 50f;
        [SerializeField, Min(0f)] private float maxReverseSpeedKmh = 8f;
        [SerializeField, Min(0.1f)] private float acceleration = 3f;
        [SerializeField, Min(0.1f)] private float brakeDeceleration = 8f;
        [SerializeField, Min(0f)] private float coastDeceleration = 1f;

        [Header("Steering")]
        [SerializeField, Min(0.1f)] private float wheelbase = 6f;
        [SerializeField, Range(1f, 60f)] private float maxSteerAngleDegrees = 35f;
        [SerializeField, Min(1f)] private float steerRateDegreesPerSecond = 120f;
        [Tooltip("Share of the steer angle still available at top speed.")]
        [SerializeField, Range(0.05f, 1f)] private float highSpeedSteerFactor = 0.5f;
        [Tooltip("Speed used for the turn rate when slower, so the bus still turns at a crawl.")]
        [SerializeField, Min(0f)] private float minSteerSpeed = 2f;

        private Rigidbody body;
        private InputAction driveAction;
        private float steerAngleDegrees;
        private bool awaitingBrakeRelease;
        private bool useInputOverride;
        private Vector2 inputOverride;

        public float MaxSpeedKmh => maxSpeedKmh;
        public float SpeedMetresPerSecond { get; private set; }
        public float SpeedKmh => SpeedMetresPerSecond * MetresPerSecondToKmh;

        /// <summary>Replaces keyboard input (x steer, y throttle). Used by the autopilot and tests.</summary>
        public void SetInputOverride(Vector2 input)
        {
            useInputOverride = true;
            inputOverride = input;
        }

        public void ClearInputOverride()
        {
            useInputOverride = false;
            inputOverride = Vector2.zero;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();

            driveAction = new InputAction("Drive", InputActionType.Value);
            driveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            driveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
        }

        private void OnEnable()
        {
            driveAction.Enable();
        }

        private void OnDisable()
        {
            driveAction.Disable();
        }

        private void OnDestroy()
        {
            driveAction.Dispose();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 input = useInputOverride ? inputOverride : driveAction.ReadValue<Vector2>();

            Vector3 velocity = body.linearVelocity;
            Vector3 heading = transform.forward;
            heading.y = 0f;
            heading.Normalize();

            // Re-read speed from the body so collisions (kerbs, obstacles) slow the bus.
            float speed = velocity.x * heading.x + velocity.z * heading.z;
            speed = ApplyLongitudinal(speed, input.y, dt);

            float speedFraction = Mathf.Clamp01(Mathf.Abs(speed) / (maxSpeedKmh * KmhToMetresPerSecond));
            float steerLimit = maxSteerAngleDegrees * Mathf.Lerp(1f, highSpeedSteerFactor, speedFraction);
            steerAngleDegrees = Mathf.MoveTowards(steerAngleDegrees, input.x * steerLimit, steerRateDegreesPerSecond * dt);

            // Only turn while moving or driving. A parked bus must not pivot on the spot.
            bool moving = Mathf.Abs(speed) > 0f || input.y != 0f;
            float steerSpeed = moving ? Mathf.Sign(speed == 0f ? input.y : speed) * Mathf.Max(Mathf.Abs(speed), minSteerSpeed) : 0f;
            float yawRate = steerSpeed / wheelbase * Mathf.Tan(steerAngleDegrees * Mathf.Deg2Rad);
            Quaternion rotation = body.rotation * Quaternion.Euler(0f, yawRate * Mathf.Rad2Deg * dt, 0f);
            body.MoveRotation(rotation);

            Vector3 newHeading = rotation * Vector3.forward;
            newHeading.y = 0f;
            newHeading.Normalize();
            body.linearVelocity = new Vector3(newHeading.x * speed, velocity.y, newHeading.z * speed);
            SpeedMetresPerSecond = speed;
        }

        private float ApplyLongitudinal(float speed, float throttle, float dt)
        {
            float maxForward = maxSpeedKmh * KmhToMetresPerSecond;
            float maxReverse = maxReverseSpeedKmh * KmhToMetresPerSecond;

            if (throttle >= 0f)
            {
                awaitingBrakeRelease = false;
            }

            if (throttle > 0f)
            {
                speed += (speed >= 0f ? acceleration : brakeDeceleration) * throttle * dt;
            }
            else if (throttle < 0f)
            {
                if (awaitingBrakeRelease)
                {
                    speed = 0f;
                }
                else if (speed > 0f)
                {
                    speed -= brakeDeceleration * -throttle * dt;
                    if (speed <= 0f)
                    {
                        speed = 0f;
                        awaitingBrakeRelease = true;
                    }
                }
                else
                {
                    speed -= acceleration * -throttle * dt;
                }
            }
            else
            {
                speed = Mathf.MoveTowards(speed, 0f, coastDeceleration * dt);
            }

            return Mathf.Clamp(speed, -maxReverse, maxForward);
        }
    }
}
