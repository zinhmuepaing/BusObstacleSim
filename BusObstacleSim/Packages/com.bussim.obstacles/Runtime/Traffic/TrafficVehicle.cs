using BusSim.Obstacles;
using BusSim.Road;
using UnityEngine;

namespace BusSim.Traffic
{
    /// <summary>
    /// An ambient car. It drives along a lane of a road (fixed lateral offset, s moving up or down), follows the
    /// car ahead with the Intelligent Driver Model, and a side-road car gives way at the line, then turns left
    /// onto the main road. Like other scripted movers it is a dynamic body steered by velocity, so a collision
    /// with the player exchanges momentum, after which scripting stops and the car is just a body.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficVehicle : MonoBehaviour
    {
        public enum Phase
        {
            Cruising,
            GivingWay,
            Turning
        }

        public const int TurnSamples = 24;
        private const float FarGap = 10000f;
        private const float StoppedSpeed = 0.3f;

        [SerializeField, Min(1f)] private float length = 4f;
        [SerializeField, Min(0.5f)] private float width = 1.8f;
        [Tooltip("Impacts slower than this (m/s relative) do not count as a hit.")]
        [SerializeField, Min(0f)] private float hitMinSpeed = 0.5f;

        private Rigidbody body;
        private PooledPhysicsReset resetter;
        private TrafficManager manager;
        private TrafficSettings settings;
        private float desiredSpeed;
        private float turnDistance;
        private int vehicleLayer;

        public Vector3[] TurnPoints { get; } = new Vector3[TurnSamples + 1];
        public float[] TurnCumulative { get; } = new float[TurnSamples + 1];
        public float TurnLength { get; set; }

        public RoadSampler Sampler { get; private set; }
        public float LaneT { get; private set; }
        public int Direction { get; private set; }
        public float S { get; private set; }
        public float Speed { get; private set; }
        public Phase CurrentPhase { get; private set; }
        public bool WasHit { get; private set; }
        public float AcceptedGapSeconds { get; private set; }
        public string Id { get; private set; }
        public int PlanIndex { get; private set; }
        public float Length => length;
        public float Width => width;

        /// <summary>Position along the main road. While turning, how far through the turn the car is.</summary>
        public float MainRoadS { get; private set; }

        public bool OnMainRoad { get; private set; }

        public void Configure(float vehicleLength, float vehicleWidth)
        {
            length = vehicleLength;
            width = vehicleWidth;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            resetter = GetComponent<PooledPhysicsReset>();
            vehicleLayer = LayerMask.NameToLayer(PhysicsSetup.VehicleLayer);
        }

        public void Init(TrafficManager owner, TrafficSettings trafficSettings, RoadSampler sampler, float laneT, int direction,
            float s, float speed, float cruiseSpeed, Phase phase, float acceptedGap, string id, int planIndex)
        {
            manager = owner;
            settings = trafficSettings;
            Sampler = sampler;
            LaneT = laneT;
            Direction = direction;
            S = s;
            Speed = speed;
            desiredSpeed = cruiseSpeed;
            CurrentPhase = phase;
            AcceptedGapSeconds = acceptedGap;
            Id = id;
            PlanIndex = planIndex;
            WasHit = false;
            turnDistance = 0f;
            OnMainRoad = phase != Phase.GivingWay;
            MainRoadS = s;

            if (resetter != null)
            {
                resetter.ResetForReuse(false);
            }
            PlaceOnLane(true, 0f);
        }

        /// <summary>One physics step of driving. Called by the manager.</summary>
        public void Tick(float deltaTime)
        {
            if (WasHit || manager == null)
            {
                return;
            }

            if (CurrentPhase == Phase.Turning)
            {
                TickTurn(deltaTime);
            }
            else
            {
                TickLane(deltaTime);
            }
        }

        private void TickLane(float deltaTime)
        {
            manager.FindLead(this, out float gap, out float leadSpeed);

            if (CurrentPhase == Phase.GivingWay)
            {
                float toLine = DistanceToStopLine();
                bool close = toLine < settings.goWithin || Speed < StoppedSpeed;
                if (close && manager.GapAcceptable(this))
                {
                    StartTurn();
                    return;
                }
                if (toLine < gap)
                {
                    gap = Mathf.Max(toLine, 0.1f);
                    leadSpeed = 0f;
                }
            }

            float acceleration = IdmAcceleration(Speed, desiredSpeed, gap, leadSpeed);
            Speed = Mathf.Max(0f, Speed + acceleration * deltaTime);
            S += Direction * Speed * deltaTime;
            MainRoadS = S;
            PlaceOnLane(false, deltaTime);
        }

        private float DistanceToStopLine()
        {
            // The side-road car travels toward smaller s. Its front is half a length ahead of S.
            return S - length * 0.5f - settings.stopLineS;
        }

        private void StartTurn()
        {
            manager.BuildTurn(this);
            CurrentPhase = Phase.Turning;
            OnMainRoad = true;
            Direction = 1; // now travelling with the main road's left lane, so followers and the player see it as a lead
            turnDistance = 0f;
        }

        private void TickTurn(float deltaTime)
        {
            float turnSpeed = settings.turnKmh / 3.6f;
            Speed = Mathf.MoveTowards(Speed, turnSpeed, settings.maxAcceleration * deltaTime);
            turnDistance += Speed * deltaTime;

            float fraction = TurnLength > 0f ? Mathf.Clamp01(turnDistance / TurnLength) : 1f;
            MainRoadS = manager.JunctionS + fraction * (manager.TurnEndS - manager.JunctionS);

            if (turnDistance >= TurnLength)
            {
                // Joined the main road: carry on as ordinary traffic in the left lane.
                Sampler = manager.MainRoad;
                LaneT = Sampler.Settings.GetLaneCentreT(0);
                Direction = 1;
                S = manager.TurnEndS;
                MainRoadS = S;
                desiredSpeed = manager.SameDirectionCruiseSpeed;
                CurrentPhase = Phase.Cruising;
                PlaceOnLane(false, deltaTime);
                return;
            }

            Vector3 position = EvaluateTurn(turnDistance, out Vector3 heading);
            Apply(position, Quaternion.LookRotation(heading, Vector3.up), false, deltaTime);
        }

        private Vector3 EvaluateTurn(float distance, out Vector3 heading)
        {
            int segment = 0;
            while (segment < TurnSamples - 1 && TurnCumulative[segment + 1] < distance)
            {
                segment++;
            }
            float span = Mathf.Max(TurnCumulative[segment + 1] - TurnCumulative[segment], 0.0001f);
            float u = Mathf.Clamp01((distance - TurnCumulative[segment]) / span);
            Vector3 a = TurnPoints[segment];
            Vector3 b = TurnPoints[segment + 1];
            heading = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
            return Vector3.Lerp(a, b, u);
        }

        private void PlaceOnLane(bool teleport, float deltaTime)
        {
            Sampler.GetFrame(S, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Vector3 position = centre + right * LaneT;
            Quaternion rotation = Quaternion.LookRotation(forward * Direction, Vector3.up);
            Apply(position, rotation, teleport, deltaTime);
        }

        private void Apply(Vector3 position, Quaternion rotation, bool teleport, float deltaTime)
        {
            if (teleport)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = position;
                body.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
                Physics.SyncTransforms();
                return;
            }

            body.useGravity = false;
            body.linearVelocity = (position - body.position) / Mathf.Max(deltaTime, Mathf.Epsilon);
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(rotation);
        }

        /// <summary>Intelligent Driver Model acceleration for a car with the given speed, wanted speed and lead.</summary>
        private float IdmAcceleration(float speed, float wantedSpeed, float gap, float leadSpeed)
        {
            float free = 1f - Mathf.Pow(speed / Mathf.Max(wantedSpeed, 0.1f), 4f);
            float interaction = 0f;
            if (gap < FarGap)
            {
                float closing = speed - leadSpeed;
                float brakingTerm = speed * closing / (2f * Mathf.Sqrt(settings.maxAcceleration * settings.comfortableBraking));
                float desiredGap = settings.minGap + Mathf.Max(0f, speed * settings.timeHeadway + brakingTerm);
                interaction = Mathf.Pow(desiredGap / Mathf.Max(gap, 0.1f), 2f);
            }
            return Mathf.Max(settings.maxAcceleration * (free - interaction), -settings.emergencyBraking);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (WasHit || manager == null || collision.gameObject.layer != vehicleLayer)
            {
                return;
            }
            if (collision.relativeVelocity.magnitude < hitMinSpeed)
            {
                return;
            }

            WasHit = true;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
            manager.ReportHit(this, collision);
        }
    }
}
