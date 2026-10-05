using UnityEngine;
using UnityEngine.InputSystem;

namespace BusSim.Vehicle
{
    /// <summary>
    /// Physical test car on four WheelColliders. Drive with W A S D or the arrow keys. All forces
    /// act through the wheels and the Rigidbody, so collisions behave physically.
    /// Wheel order is front left, front right, rear left, rear right.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour, IVehicleInput
    {
        private const int WheelCount = 4;
        private const int FrontLeft = 0;
        private const int FrontRight = 1;
        private const int RearLeft = 2;
        private const int RearRight = 3;
        private const float MetresPerSecondToKmh = 3.6f;
        private const float MinSteerAngle = 1e-4f;
        private const float FrictionExtremumSlip = 0.4f;
        private const float FrictionExtremumValue = 1f;
        private const float FrictionAsymptoteSlip = 0.8f;
        private const float FrictionAsymptoteValue = 0.5f;

        [System.Serializable]
        public struct Wheel
        {
            public WheelCollider collider;
            [Tooltip("Pivot that follows the wheel pose. Its child holds the wheel mesh.")]
            public Transform visual;
            public bool steers;
            public bool drives;
        }

        [SerializeField] private VehicleSettings settings;
        [SerializeField] private Wheel[] wheels = new Wheel[0];

        private Rigidbody body;
        private InputAction driveAction;
        private InputAction brakeAction;
        private float steerAngle;
        private float flippedSeconds;
        private float wheelbase;
        private float trackWidth;
        private bool awaitingBrakeRelease;
        private bool useInputOverride;
        private Vector2 inputOverride;

        public VehicleSettings Settings => settings;
        public float SpeedMetresPerSecond { get; private set; }
        public float SpeedKmh => SpeedMetresPerSecond * MetresPerSecondToKmh;
        public float MaxSpeedKmh => settings != null ? settings.maxSpeedKmh : 0f;
        public float Width => settings.chassisWidth;
        public float Length => settings.chassisLength;
        public float FrontOffset => settings.FrontOffset;
        public float Wheelbase => wheelbase;
        public bool HasFlipped { get; private set; }

        /// <summary>Largest steer angle (degrees) available at the current speed.</summary>
        public float MaxSteerAngleNow => SteerLimit(SpeedMetresPerSecond);

        public void Configure(VehicleSettings vehicleSettings, Wheel[] vehicleWheels)
        {
            settings = vehicleSettings;
            wheels = vehicleWheels;
        }

        public void SetDrive(float throttle, float steer)
        {
            useInputOverride = true;
            inputOverride = new Vector2(steer, throttle);
        }

        public void ClearDrive()
        {
            useInputOverride = false;
            inputOverride = Vector2.zero;
        }

        /// <summary>Same as SetDrive with the older (steer, throttle) vector.</summary>
        public void SetInputOverride(Vector2 input)
        {
            SetDrive(input.y, input.x);
        }

        public void ClearInputOverride()
        {
            ClearDrive();
        }

        /// <summary>Applies VehicleSettings to the Rigidbody and WheelColliders. Safe to call again.</summary>
        public void ApplySettings()
        {
            body = GetComponent<Rigidbody>();
            body.mass = settings.mass;
            body.centerOfMass = settings.centreOfMass;
            body.angularDamping = settings.angularDamping;

            WheelFrictionCurve forward = Friction(settings.forwardGrip);
            WheelFrictionCurve sideways = Friction(settings.sidewaysGrip);
            JointSpring spring = new JointSpring
            {
                spring = settings.springRate,
                damper = settings.damperRate,
                targetPosition = settings.suspensionTarget
            };

            foreach (Wheel wheel in wheels)
            {
                WheelCollider collider = wheel.collider;
                collider.mass = settings.wheelMass;
                collider.radius = settings.wheelRadius;
                collider.suspensionDistance = settings.suspensionDistance;
                collider.suspensionSpring = spring;
                collider.forwardFriction = forward;
                collider.sidewaysFriction = sideways;
                collider.wheelDampingRate = settings.wheelDamping;
            }

            if (wheels.Length == WheelCount)
            {
                float frontZ = (wheels[FrontLeft].collider.transform.localPosition.z + wheels[FrontRight].collider.transform.localPosition.z) * 0.5f;
                float rearZ = (wheels[RearLeft].collider.transform.localPosition.z + wheels[RearRight].collider.transform.localPosition.z) * 0.5f;
                wheelbase = Mathf.Max(frontZ - rearZ, MinSteerAngle);
                trackWidth = Mathf.Abs(wheels[FrontLeft].collider.transform.localPosition.x - wheels[FrontRight].collider.transform.localPosition.x);
            }
        }

        private static WheelFrictionCurve Friction(float stiffness)
        {
            // Unity's default curve shape, scaled by stiffness.
            return new WheelFrictionCurve
            {
                extremumSlip = FrictionExtremumSlip,
                extremumValue = FrictionExtremumValue,
                asymptoteSlip = FrictionAsymptoteSlip,
                asymptoteValue = FrictionAsymptoteValue,
                stiffness = stiffness
            };
        }

        private void Awake()
        {
            if (settings == null || wheels.Length != WheelCount)
            {
                Debug.LogError("CarController needs VehicleSettings and four wheels. Use BusSim > Car > Create Car.", this);
                enabled = false;
                return;
            }

            ApplySettings();
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
            brakeAction = new InputAction("Brake", InputActionType.Button, "<Keyboard>/space");
        }

        private void OnEnable()
        {
            driveAction?.Enable();
            brakeAction?.Enable();
        }

        private void OnDisable()
        {
            driveAction?.Disable();
            brakeAction?.Disable();
        }

        private void OnDestroy()
        {
            driveAction?.Dispose();
            brakeAction?.Dispose();
        }

        private void FixedUpdate()
        {
            Tick(Time.fixedDeltaTime);
        }

        private void Update()
        {
            SyncVisuals();
        }

        /// <summary>One physics step of control. Public so tests and the harness can drive it.</summary>
        public void Tick(float deltaTime)
        {
            if (settings == null || wheels.Length != WheelCount)
            {
                return;
            }

            Vector2 input = useInputOverride ? inputOverride : (driveAction != null ? driveAction.ReadValue<Vector2>() : Vector2.zero);
            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            SpeedMetresPerSecond = forwardSpeed;
            bool spaceBrake = !useInputOverride && brakeAction != null && brakeAction.IsPressed();

            ApplyDrive(input.y, forwardSpeed, spaceBrake);
            ApplySteering(input.x, forwardSpeed, deltaTime);
            ApplyStability(forwardSpeed);
            CheckFlip(deltaTime);
        }

        private void ApplyDrive(float throttle, float forwardSpeed, bool spaceBrake)
        {
            float motor = 0f;
            float brake = 0f;
            if (throttle >= 0f)
            {
                awaitingBrakeRelease = false;
            }

            if (spaceBrake)
            {
                // The brake pedal: full braking whatever the throttle says, and the car holds still once stopped.
                brake = Mathf.Abs(forwardSpeed) < settings.stoppedSpeed ? settings.parkBrakeTorque : settings.brakeTorque;
            }
            else if (throttle > 0f)
            {
                if (forwardSpeed < -settings.reverseSwitchSpeed)
                {
                    brake = throttle * settings.brakeTorque;
                }
                else
                {
                    float room = Mathf.Clamp01((settings.MaxSpeedMetresPerSecond - forwardSpeed) / settings.speedTaper);
                    motor = throttle * settings.motorTorque * room;
                }
            }
            else if (throttle < 0f)
            {
                float demand = -throttle;
                if (awaitingBrakeRelease)
                {
                    brake = settings.parkBrakeTorque;
                }
                else if (forwardSpeed > settings.reverseSwitchSpeed)
                {
                    brake = demand * settings.brakeTorque;
                }
                else if (forwardSpeed > settings.stoppedSpeed)
                {
                    brake = settings.parkBrakeTorque;
                    awaitingBrakeRelease = true;
                }
                else
                {
                    float room = Mathf.Clamp01((settings.MaxReverseMetresPerSecond + forwardSpeed) / settings.speedTaper);
                    motor = -demand * settings.motorTorque * settings.reverseTorqueShare * room;
                }
            }
            else
            {
                brake = Mathf.Abs(forwardSpeed) < settings.stoppedSpeed ? settings.parkBrakeTorque : settings.coastBrakeTorque;
            }

            int driven = 0;
            foreach (Wheel wheel in wheels)
            {
                driven += wheel.drives ? 1 : 0;
            }
            float perWheelMotor = driven > 0 ? motor / driven : 0f;
            foreach (Wheel wheel in wheels)
            {
                wheel.collider.motorTorque = wheel.drives ? perWheelMotor : 0f;
                wheel.collider.brakeTorque = brake;
            }
        }

        private float SteerLimit(float speed)
        {
            return settings.maxSteerAngle / (1f + settings.steerSpeedFalloff * speed * speed);
        }

        private void ApplySteering(float steerInput, float forwardSpeed, float deltaTime)
        {
            float target = steerInput * SteerLimit(forwardSpeed);
            steerAngle = Mathf.MoveTowards(steerAngle, target, settings.steerRate * deltaTime);

            // Ackermann: the inside wheel turns more than the outside wheel.
            float centre = steerAngle * Mathf.Deg2Rad;
            float left = steerAngle;
            float right = steerAngle;
            if (Mathf.Abs(centre) > MinSteerAngle)
            {
                float radius = wheelbase / Mathf.Tan(centre);
                left = Mathf.Atan(wheelbase / (radius + trackWidth * 0.5f)) * Mathf.Rad2Deg;
                right = Mathf.Atan(wheelbase / (radius - trackWidth * 0.5f)) * Mathf.Rad2Deg;
            }

            wheels[FrontLeft].collider.steerAngle = wheels[FrontLeft].steers ? left : 0f;
            wheels[FrontRight].collider.steerAngle = wheels[FrontRight].steers ? right : 0f;
        }

        private void ApplyStability(float forwardSpeed)
        {
            body.AddForce(-transform.forward * (Mathf.Sign(forwardSpeed) * settings.dragCoefficient * forwardSpeed * forwardSpeed));
            ApplyAntiRoll(wheels[FrontLeft].collider, wheels[FrontRight].collider);
            ApplyAntiRoll(wheels[RearLeft].collider, wheels[RearRight].collider);
        }

        private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            float travelLeft = SuspensionTravel(left, out bool groundedLeft);
            float travelRight = SuspensionTravel(right, out bool groundedRight);
            float force = (travelLeft - travelRight) * settings.antiRoll;
            if (groundedLeft)
            {
                body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            }
            if (groundedRight)
            {
                body.AddForceAtPosition(right.transform.up * force, right.transform.position);
            }
        }

        private float SuspensionTravel(WheelCollider wheel, out bool grounded)
        {
            grounded = wheel.GetGroundHit(out WheelHit hit);
            if (!grounded)
            {
                return 1f;
            }
            float compressionAxis = -wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius;
            return Mathf.Clamp01(compressionAxis / wheel.suspensionDistance);
        }

        private void CheckFlip(float deltaTime)
        {
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < settings.flipUpThreshold;
            flippedSeconds = flipped ? flippedSeconds + deltaTime : 0f;
            HasFlipped = flipped;
            if (flippedSeconds >= settings.flipRecoverSeconds)
            {
                Recover();
            }
        }

        /// <summary>Puts the car upright in place and stops it. Used after a roll-over.</summary>
        public void Recover()
        {
            flippedSeconds = 0f;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < MinSteerAngle)
            {
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            }
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position += Vector3.up * settings.recoverLift;
            body.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>Teleports the car to a pose, at rest, with the steering centred.</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            steerAngle = 0f;
            SpeedMetresPerSecond = 0f;
            awaitingBrakeRelease = false;
            flippedSeconds = 0f;

            // Toggling a WheelCollider clears its internal spin and suspension state, so a restarted
            // car behaves exactly like a fresh one.
            foreach (Wheel wheel in wheels)
            {
                wheel.collider.motorTorque = 0f;
                wheel.collider.brakeTorque = 0f;
                wheel.collider.steerAngle = 0f;
                wheel.collider.enabled = false;
                wheel.collider.enabled = true;
            }
            Physics.SyncTransforms();
            SyncVisuals();
        }

        /// <summary>Moves the wheel meshes to the wheel colliders' current pose.</summary>
        public void SyncVisuals()
        {
            foreach (Wheel wheel in wheels)
            {
                if (wheel.collider == null || wheel.visual == null)
                {
                    continue;
                }
                wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
                wheel.visual.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
