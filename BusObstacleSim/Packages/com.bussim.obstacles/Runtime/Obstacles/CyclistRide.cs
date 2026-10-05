using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Cyclist or PMD rider close to the kerb, riding with the traffic. Wobbles slightly and may
    /// swerve toward the lane once. Counts as blocking only while outside the kerbside edge zone.
    /// </summary>
    public class CyclistRide : ObstacleBehaviour
    {
        [SerializeField, Min(0.1f)] private float rideSpeed = 4f;
        [SerializeField, Min(0f)] private float wobbleAmplitude = 0.12f;
        [SerializeField, Min(0.01f)] private float wobbleFrequency = 0.6f;
        [SerializeField, Range(0f, 1f)] private float swerveChance = 0.5f;
        [SerializeField, Min(0f)] private float swerveDistance = 0.6f;
        [SerializeField, Min(0.1f)] private float swerveSeconds = 1.6f;
        [Tooltip("Width from the kerb that a cyclist may use without counting as blocking the road.")]
        [SerializeField, Min(0f)] private float edgeZoneWidth = 1.2f;
        [SerializeField, Min(0.05f)] private float wheelRadius = 0.34f;
        [SerializeField] private Transform frontWheel;
        [SerializeField] private Transform rearWheel;

        private const float SwerveDelayMin = 1f;
        private const float SwerveDelayRange = 4f;
        private static readonly Quaternion WheelBase = Quaternion.Euler(0f, 0f, 90f);

        private float currentS;
        private float currentT;
        private float rideTime;
        private float swerveStart;
        private float swerveDirection;
        private float wheelAngle;
        private bool willSwerve;

        public override float CurrentS => currentS;

        protected override void OnInit()
        {
            currentS = Event.S;
            currentT = Event.T;
            rideTime = 0f;
            wheelAngle = 0f;
            willSwerve = Context.Rng.NextDouble() < swerveChance;
            swerveStart = SwerveDelayMin + SwerveDelayRange * (float)Context.Rng.NextDouble();
            swerveDirection = -Mathf.Sign(Event.T);
            PlaceAt(currentS, currentT, 0f);
        }

        protected override void OnTick(float deltaTime)
        {
            if (!HasTriggered)
            {
                float timeToArrival = TimeToArrival(currentS);
                if (timeToArrival > Event.Definition.triggerTimeToArrival)
                {
                    return;
                }
                HasTriggered = true;
                TriggerTimeToArrival = timeToArrival;
            }

            rideTime += deltaTime;
            currentS += rideSpeed * deltaTime;
            float lateral = wobbleAmplitude * Mathf.Sin(2f * Mathf.PI * wobbleFrequency * rideTime);
            if (willSwerve)
            {
                float u = (rideTime - swerveStart) / swerveSeconds;
                if (u > 0f && u < 1f)
                {
                    lateral += swerveDirection * swerveDistance * Mathf.Sin(Mathf.PI * u);
                }
            }
            currentT = Event.T + lateral;
            PlaceAt(currentS, currentT, 0f);

            wheelAngle += rideSpeed * deltaTime / wheelRadius * Mathf.Rad2Deg;
            Quaternion spin = Quaternion.Euler(wheelAngle, 0f, 0f) * WheelBase;
            if (frontWheel != null)
            {
                frontWheel.localRotation = spin;
            }
            if (rearWheel != null)
            {
                rearWheel.localRotation = spin;
            }
        }

        protected override bool IsOnCarriageway()
        {
            float halfWidth = Event.Definition.footprintWidth * 0.5f;
            float innerEdgeFromKerb = Context.Settings.HalfRoadWidth - Mathf.Abs(currentT) + halfWidth;
            return HasTriggered && innerEdgeFromKerb > edgeZoneWidth;
        }
    }
}
