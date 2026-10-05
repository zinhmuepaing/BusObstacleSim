using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// A vehicle parked at the kerb that signals, then pulls out into the driven lane in front of the approaching
    /// vehicle and drives off. Used for the taxi and the bus leaving a stop. It is a solid body from the start,
    /// so the approaching vehicle can run into it before and after it moves.
    /// </summary>
    public class VehiclePullOut : ObstacleBehaviour
    {
        private enum Phase
        {
            Parked,
            Signal,
            PullOut,
            Drive
        }

        [SerializeField, Min(0f)] private float signalSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float acceleration = 1.3f;
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 8f;
        [Tooltip("Distance travelled while it eases from the kerb into the lane.")]
        [SerializeField, Min(1f)] private float mergeDistance = 18f;
        [Tooltip("Metres ahead of the approaching vehicle at which it is considered gone and can be released.")]
        [SerializeField, Min(1f)] private float finishAhead = 120f;
        [Tooltip("Counts as blocking the lane while it is out of the kerb and slower than this (m/s).")]
        [SerializeField, Min(0f)] private float blockingSpeed = 5f;
        [SerializeField, Min(0.05f)] private float indicatorInterval = 0.35f;
        [SerializeField] private Renderer[] indicators = new Renderer[0];

        private Phase phase;
        private float currentS;
        private float currentT;
        private float kerbT;
        private float laneT;
        private float speed;
        private float timer;
        private float travelled;
        private float blinkTimer;
        private bool blinkOn;

        protected override float ScriptedS => currentS;

        protected override void OnInit()
        {
            phase = Phase.Parked;
            currentS = Event.S;
            currentT = Event.T;
            kerbT = Event.T;
            laneT = Context.Settings.GetLaneCentreT(0);
            speed = 0f;
            timer = 0f;
            travelled = 0f;
            SetIndicators(false);
            PlaceAt(currentS, currentT, 0f);
        }

        protected override void OnTick(float deltaTime)
        {
            float previousT = currentT;
            switch (phase)
            {
                case Phase.Parked:
                    float timeToArrival = TimeToArrival(Event.S);
                    if (timeToArrival <= Event.Definition.triggerTimeToArrival)
                    {
                        HasTriggered = true;
                        TriggerTimeToArrival = timeToArrival;
                        phase = Phase.Signal;
                        timer = 0f;
                    }
                    break;

                case Phase.Signal:
                    timer += deltaTime;
                    Blink(deltaTime);
                    if (timer >= signalSeconds)
                    {
                        phase = Phase.PullOut;
                    }
                    break;

                case Phase.PullOut:
                    Blink(deltaTime);
                    Accelerate(deltaTime);
                    float u = Mathf.Clamp01(travelled / mergeDistance);
                    currentT = Mathf.Lerp(kerbT, laneT, u * u * (3f - 2f * u));
                    if (u >= 1f)
                    {
                        phase = Phase.Drive;
                        SetIndicators(false);
                    }
                    break;

                case Phase.Drive:
                    Accelerate(deltaTime);
                    if (currentS - Context.Vehicle.VehicleFrontS > finishAhead)
                    {
                        IsFinished = true;
                    }
                    break;
            }

            float lateralSpeed = deltaTime > 0f ? (currentT - previousT) / deltaTime : 0f;
            PlaceAt(currentS, currentT, Mathf.Atan2(lateralSpeed, Mathf.Max(speed, 0.1f)) * Mathf.Rad2Deg);
        }

        protected override bool IsOnCarriageway()
        {
            return phase >= Phase.PullOut && Mathf.Abs(currentT - kerbT) > Event.Definition.footprintWidth * 0.5f && speed < blockingSpeed;
        }

        private void Accelerate(float deltaTime)
        {
            speed = Mathf.MoveTowards(speed, cruiseSpeed, acceleration * deltaTime);
            float step = speed * deltaTime;
            currentS += step;
            travelled += step;
        }

        private void Blink(float deltaTime)
        {
            blinkTimer += deltaTime;
            if (blinkTimer >= indicatorInterval)
            {
                blinkTimer -= indicatorInterval;
                SetIndicators(!blinkOn);
            }
        }

        private void SetIndicators(bool on)
        {
            blinkOn = on;
            foreach (Renderer indicator in indicators)
            {
                if (indicator != null)
                {
                    indicator.enabled = on;
                }
            }
        }
    }
}
