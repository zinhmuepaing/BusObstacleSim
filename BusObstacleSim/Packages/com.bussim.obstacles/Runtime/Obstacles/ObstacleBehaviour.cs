using BusSim.Spawning;
using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Base class for every obstacle type. The spawner calls Init when the pooled instance is taken,
    /// Activate once, Tick every physics step while active, and Finish before releasing it.
    ///
    /// Physics: obstacles are real bodies. Types that move by script (pedestrians, cyclists, cars)
    /// have a dynamic Rigidbody on the root that is steered to the scripted pose by velocity, so a
    /// collision exchanges momentum with the vehicle. When hit, scripting stops and the body is thrown
    /// by the impact and falls under gravity. Types that do not move (cones, debris, parked cars) just
    /// have bodies the vehicle can push.
    /// </summary>
    public abstract class ObstacleBehaviour : MonoBehaviour
    {
        private const float MinSpeedForArrival = 0.1f;
        private const float KerbRampMetres = 0.3f;
        private const float SpawnClearMargin = 0.5f;
        private const float SpawnProbeHalfHeight = 0.7f;

        [Header("Hit reaction (scripted movers only)")]
        [Tooltip("Share of the vehicle's velocity given to a hit body.")]
        [SerializeField, Range(0f, 1.5f)] private float knockVelocityShare = 0.9f;
        [Tooltip("Upward launch speed (m/s) given to a hit body.")]
        [SerializeField, Min(0f)] private float knockLaunchSpeed = 2f;
        [Tooltip("The knock acts this far above the body's centre of mass, so riders and walkers topple.")]
        [SerializeField, Min(0f)] private float knockLeverHeight = 0.4f;
        [Tooltip("Ground drag on a knocked body, so it slides and tumbles to a stop instead of rolling on.")]
        [SerializeField, Min(0f)] private float hitLinearDamping = 1f;
        [SerializeField, Min(0f)] private float hitAngularDamping = 4f;
        [SerializeField, Min(0f)] private float settleSeconds = 8f;
        [SerializeField, Min(0f)] private float settleSpeed = 0.1f;
        [SerializeField, Min(0f)] private float lostDistance = 30f;

        private Collider[] colliders;
        private ObstacleCollisionReporter[] reporters;
        private PooledPhysicsReset resetter;
        private Rigidbody rootBody;

        protected ObstacleContext Context { get; private set; }
        public SpawnEvent Event => Context.Event;

        /// <summary>True between Init and Finish.</summary>
        public bool IsActiveInRun { get; private set; }

        /// <summary>True once the vehicle hit this obstacle. Scripted movement stops for good.</summary>
        public bool WasHit { get; private set; }
        public float HitTime { get; private set; }

        /// <summary>Seconds this obstacle has spent on the carriageway since it started moving.</summary>
        public float BlockingSeconds { get; private set; }

        /// <summary>Longest continuous stretch on the carriageway, for the maxBlockSeconds check.</summary>
        public float LongestBlockSeconds { get; private set; }

        /// <summary>True once the behaviour's trigger condition fired.</summary>
        public bool HasTriggered { get; protected set; }

        /// <summary>Vehicle time to arrival at the moment of triggering (seconds).</summary>
        public float TriggerTimeToArrival { get; protected set; } = float.PositiveInfinity;

        /// <summary>When true the spawner may release the instance even before the vehicle passes it.</summary>
        public bool IsFinished { get; protected set; }

        /// <summary>Current road s: the scripted position, or the real position once knocked about.</summary>
        public float CurrentS => WasHit ? Context.Road.ProjectToRoad(BodyPosition).s : ScriptedS;

        protected virtual float ScriptedS => Event.S;

        /// <summary>True for types that move themselves by script and need the hit knock.</summary>
        protected virtual bool IsScriptedMover => true;

        /// <summary>A hit body that has come to rest, or been flung away from the road, can be released.</summary>
        public bool IsSettledOrLost
        {
            get
            {
                if (!WasHit)
                {
                    return false;
                }
                (float s, float t) = Context.Road.ProjectToRoad(BodyPosition);
                bool lost = Mathf.Abs(t) > Context.Settings.HalfRoadWidth + lostDistance;
                bool still = Time.time - HitTime > settleSeconds && !IsMoving();
                return lost || still;
            }
        }

        /// <summary>Physics position of the root body, or the transform for types without one.</summary>
        private Vector3 BodyPosition => rootBody != null ? rootBody.position : transform.position;

        private bool IsMoving()
        {
            if (rootBody != null && rootBody.linearVelocity.sqrMagnitude > settleSpeed * settleSpeed)
            {
                return true;
            }
            return resetter != null && resetter.AnyBodyMoving(settleSpeed);
        }

        protected virtual void Awake()
        {
            rootBody = GetComponent<Rigidbody>();
            resetter = GetComponent<PooledPhysicsReset>();
            reporters = GetComponentsInChildren<ObstacleCollisionReporter>(true);
        }

        public void Init(ObstacleContext context)
        {
            Context = context;
            BlockingSeconds = 0f;
            LongestBlockSeconds = 0f;
            HasTriggered = false;
            TriggerTimeToArrival = float.PositiveInfinity;
            IsFinished = false;
            WasHit = false;
            HitTime = 0f;

            if (resetter != null)
            {
                resetter.ResetForReuse(Event.Definition.mirrorOnRightSide && Event.T > 0f);
            }
            foreach (ObstacleCollisionReporter reporter in reporters)
            {
                reporter.Sink = context.Sink;
                reporter.ResetHit();
            }

            IsActiveInRun = true;
            PlaceAt(Event.S, Event.T, Event.YawDegrees, true);
            OnInit();
        }

        public virtual void Activate()
        {
        }

        public void Tick(float deltaTime)
        {
            if (!WasHit)
            {
                OnTick(deltaTime);
            }

            if (!WasHit && IsOnCarriageway())
            {
                BlockingSeconds += deltaTime;
                LongestBlockSeconds = Mathf.Max(LongestBlockSeconds, BlockingSeconds);
            }
            else
            {
                BlockingSeconds = 0f;
            }
        }

        public virtual void Finish()
        {
            IsActiveInRun = false;
        }

        /// <summary>Called by the collision reporter when the vehicle hits this obstacle.</summary>
        public void OnHit(Rigidbody vehicle, Vector3 relativeVelocity, Vector3 point)
        {
            if (WasHit)
            {
                return;
            }
            WasHit = true;
            HitTime = Time.time;

            if (IsScriptedMover && rootBody != null)
            {
                rootBody.isKinematic = false;
                rootBody.useGravity = true;
                rootBody.constraints = RigidbodyConstraints.None;
                rootBody.linearDamping = hitLinearDamping;
                rootBody.angularDamping = hitAngularDamping;
                Vector3 push = vehicle != null ? vehicle.linearVelocity : -relativeVelocity;
                push.y = 0f;
                Vector3 leverPoint = rootBody.worldCenterOfMass + Vector3.up * knockLeverHeight;
                rootBody.AddForceAtPosition(push * knockVelocityShare + Vector3.up * knockLaunchSpeed, leverPoint, ForceMode.VelocityChange);
            }
            OnHitReaction();
        }

        protected virtual void OnInit()
        {
        }

        protected virtual void OnTick(float deltaTime)
        {
        }

        protected virtual void OnHitReaction()
        {
        }

        /// <summary>Dynamic types report whether any part of them is on the carriageway right now.</summary>
        protected virtual bool IsOnCarriageway()
        {
            return false;
        }

        /// <summary>Hidden-until-triggered types switch their colliders off so nothing reacts to them early.</summary>
        protected void SetCollidersEnabled(bool collidersEnabled)
        {
            if (colliders == null)
            {
                colliders = GetComponentsInChildren<Collider>(true);
            }
            foreach (Collider item in colliders)
            {
                item.enabled = collidersEnabled;
            }
        }

        /// <summary>Vehicle front time to reach road position s. Infinity if stopped or past it.</summary>
        protected float TimeToArrival(float s)
        {
            float distance = s - Context.Vehicle.VehicleFrontS;
            float speed = Context.Vehicle.VehicleSpeed;
            if (distance < 0f || speed < MinSpeedForArrival)
            {
                return float.PositiveInfinity;
            }
            return distance / speed;
        }

        /// <summary>Jumps to road coordinates with no velocity. Use when a hidden mover appears.</summary>
        protected void PlaceAtImmediately(float s, float t, float yawDegrees)
        {
            PlaceAt(s, t, yawDegrees, true);
        }

        /// <summary>
        /// True if nothing on the vehicle layer occupies this obstacle's footprint (plus a margin) at road
        /// coordinates (s, t). A hidden mover waits for this before it appears, so it never spawns inside the vehicle.
        /// </summary>
        protected bool AreaFree(float s, float t, float yawDegrees = 0f)
        {
            int vehicleMask = LayerMask.GetMask(PhysicsSetup.VehicleLayer);
            if (vehicleMask == 0)
            {
                return true;
            }

            Context.Road.GetFrame(s, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Vector3 half = new Vector3(Event.Definition.footprintWidth * 0.5f + SpawnClearMargin, SpawnProbeHalfHeight,
                Event.Definition.footprintLength * 0.5f + SpawnClearMargin);
            Vector3 position = centre + right * t + Vector3.up * SpawnProbeHalfHeight;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f);
            return !Physics.CheckBox(position, half, rotation, vehicleMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Places the obstacle at road coordinates. Scripted movers are steered there by velocity.</summary>
        protected void PlaceAt(float s, float t, float yawDegrees)
        {
            PlaceAt(s, t, yawDegrees, false);
        }

        private void PlaceAt(float s, float t, float yawDegrees, bool teleport)
        {
            Context.Road.GetFrame(s, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Vector3 position = centre + right * t + Vector3.up * SurfaceRise(t);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f);

            if (rootBody == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                if (teleport)
                {
                    Physics.SyncTransforms();
                }
                return;
            }

            if (teleport)
            {
                rootBody.linearVelocity = Vector3.zero;
                rootBody.angularVelocity = Vector3.zero;
                rootBody.position = position;
                rootBody.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();
                return;
            }

            // Steer the dynamic body to the scripted pose. Gravity is off while scripted.
            rootBody.useGravity = false;
            rootBody.linearVelocity = (position - rootBody.position) / Time.fixedDeltaTime;
            rootBody.angularVelocity = Vector3.zero;
            rootBody.MoveRotation(rotation);
        }

        /// <summary>Height above the carriageway surface: zero on the road, the kerb height on the footpath.</summary>
        private float SurfaceRise(float t)
        {
            float half = Context.Settings.HalfRoadWidth;
            float blend = Mathf.InverseLerp(half, half + KerbRampMetres, Mathf.Abs(t));
            return Context.Settings.kerbHeight * blend;
        }
    }
}
