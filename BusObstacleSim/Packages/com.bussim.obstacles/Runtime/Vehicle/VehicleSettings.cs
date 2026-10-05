using UnityEngine;

namespace BusSim.Vehicle
{
    /// <summary>
    /// Every tunable of the test car: body, suspension, tyres, drive, steering and chase camera.
    /// Defaults describe a 1300 kg compact car.
    /// </summary>
    [CreateAssetMenu(menuName = "BusSim/Vehicle Settings", fileName = "VehicleSettings")]
    public class VehicleSettings : ScriptableObject
    {
        [Header("Body (metres, kilograms)")]
        [Min(0.5f)] public float chassisWidth = 1.8f;
        [Min(1f)] public float chassisLength = 4.2f;
        [Min(0.5f)] public float chassisHeight = 1.4f;
        [Tooltip("Free space each side of the body that an obstacle must still leave (spec: 0.35 m).")]
        [Min(0f)] public float sideClearanceMargin = 0.35f;
        [Tooltip("Gap between the ground and the underside of the chassis collider.")]
        [Min(0f)] public float groundClearance = 0.3f;
        [Min(100f)] public float mass = 1300f;
        [Tooltip("Centre of mass relative to the car root (ground centre). Low keeps the car stable.")]
        public Vector3 centreOfMass = new Vector3(0f, 0.55f, 0f);
        [Min(0f)] public float angularDamping = 0.5f;
        [Tooltip("Aerodynamic drag force per (m/s) squared.")]
        [Min(0f)] public float dragCoefficient = 0.42f;

        [Header("Wheels and suspension")]
        [Min(1f)] public float wheelbase = 2.6f;
        [Min(0.5f)] public float trackWidth = 1.56f;
        [Min(0.1f)] public float wheelRadius = 0.32f;
        [Min(1f)] public float wheelMass = 20f;
        [Min(0.05f)] public float suspensionDistance = 0.2f;
        [Range(0f, 1f)] public float suspensionTarget = 0.5f;
        [Min(1f)] public float springRate = 35000f;
        [Min(1f)] public float damperRate = 4500f;
        [Min(0f)] public float forwardGrip = 1.5f;
        [Min(0f)] public float sidewaysGrip = 1.5f;
        [Min(0f)] public float wheelDamping = 0.25f;
        [Tooltip("Roll stiffness per axle: force per unit of left/right suspension travel difference.")]
        [Min(0f)] public float antiRoll = 8000f;

        [Header("Drive")]
        [Min(1f)] public float maxSpeedKmh = 50f;
        [Min(0f)] public float maxReverseKmh = 10f;
        [Tooltip("Total motor torque in newton metres, split over the driven wheels.")]
        [Min(1f)] public float motorTorque = 900f;
        [Range(0.1f, 1f)] public float reverseTorqueShare = 0.6f;
        [Tooltip("Motor torque fades to zero over this many m/s below the speed cap.")]
        [Min(0.1f)] public float speedTaper = 1.5f;
        [Min(1f)] public float brakeTorque = 3000f;
        [Tooltip("Light engine braking while coasting.")]
        [Min(0f)] public float coastBrakeTorque = 150f;
        [Tooltip("Holding brake when stopped with no input.")]
        [Min(0f)] public float parkBrakeTorque = 1500f;
        [Tooltip("Below this speed (m/s) the car counts as stopped.")]
        [Min(0.01f)] public float stoppedSpeed = 0.3f;
        [Tooltip("Below this speed (m/s) pressing the brake key switches to reverse.")]
        [Min(0.01f)] public float reverseSwitchSpeed = 0.5f;

        [Header("Steering")]
        [Range(1f, 60f)] public float maxSteerAngle = 32f;
        [Min(1f)] public float steerRate = 120f;
        [Tooltip("Steer angle shrinks as 1 / (1 + falloff x speed squared).")]
        [Min(0f)] public float steerSpeedFalloff = 0.003f;

        [Header("Recovery")]
        [Tooltip("The car counts as flipped when its up vector's world-up component is below this.")]
        [Range(-1f, 1f)] public float flipUpThreshold = 0.3f;
        [Min(0.5f)] public float flipRecoverSeconds = 3f;
        [Min(0f)] public float recoverLift = 0.5f;

        [Header("Chase camera")]
        [Min(1f)] public float cameraDistance = 8f;
        [Min(0f)] public float cameraHeight = 3f;
        [Min(0f)] public float cameraLookAhead = 6f;
        [Min(0f)] public float cameraLookHeight = 1.2f;

        public float MaxSpeedMetresPerSecond => maxSpeedKmh / 3.6f;
        public float MaxReverseMetresPerSecond => maxReverseKmh / 3.6f;
        public float FrontOffset => chassisLength * 0.5f;

        /// <summary>Narrowest gap the planner may leave: body width plus a margin each side.</summary>
        public float RequiredCorridor => chassisWidth + 2f * sideClearanceMargin;
    }
}
