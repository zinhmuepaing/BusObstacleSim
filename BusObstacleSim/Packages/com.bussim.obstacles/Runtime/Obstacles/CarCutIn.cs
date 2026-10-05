using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Car that appears alongside the bus in the adjacent lane, overtakes, cuts into the bus's lane
    /// a short gap ahead and brakes hard, holds a crawl, then pulls away. Counts as blocking while
    /// it is in the bus's lane below blockingSpeed.
    /// </summary>
    public class CarCutIn : ObstacleBehaviour
    {
        private enum Phase
        {
            Hidden,
            Approach,
            Merge,
            Brake,
            Hold,
            Accelerate
        }

        [SerializeField, Min(0f)] private float startBehindFront = 2f;
        [SerializeField, Min(0f)] private float speedAboveVehicle = 3f;
        [Tooltip("Gap between the car's rear and the bus front when it starts to cut in.")]
        [SerializeField, Min(1f)] private float mergeGap = 12f;
        [SerializeField, Min(0.1f)] private float mergeSeconds = 2.5f;
        [SerializeField, Min(0.1f)] private float brakeDeceleration = 4f;
        [SerializeField, Min(0f)] private float slowSpeed = 4.2f;
        [SerializeField, Min(0f)] private float holdSeconds = 2f;
        [SerializeField, Min(0.1f)] private float acceleration = 2f;
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 13.9f;
        [SerializeField, Min(1f)] private float finishAhead = 120f;
        [SerializeField, Min(0f)] private float blockingSpeed = 8f;
        [SerializeField, Min(0.1f)] private float laneHalfWidth = 1.5f;
        [SerializeField] private GameObject visual;
        [SerializeField] private Renderer[] brakeLights = new Renderer[0];

        private Phase phase;
        private float currentS;
        private float currentT;
        private float speed;
        private float timer;
        private float fromT;
        private float toT;

        public override float CurrentS => currentS;

        protected override void OnInit()
        {
            phase = Phase.Hidden;
            currentS = Event.S;
            currentT = Event.T;
            speed = 0f;
            Show(false);
            SetBrakeLights(false);
        }

        protected override void OnTick(float deltaTime)
        {
            float halfLength = Event.Definition.footprintLength * 0.5f;
            float previousT = currentT;
            switch (phase)
            {
                case Phase.Hidden:
                    float timeToArrival = TimeToArrival(Event.S);
                    if (timeToArrival > Event.Definition.triggerTimeToArrival)
                    {
                        return;
                    }
                    HasTriggered = true;
                    TriggerTimeToArrival = timeToArrival;
                    fromT = Event.T;
                    toT = Context.Vehicle.VehicleT;
                    currentS = Context.Vehicle.VehicleFrontS - startBehindFront - halfLength;
                    speed = Context.Vehicle.VehicleSpeed + speedAboveVehicle;
                    phase = Phase.Approach;
                    Show(true);
                    break;

                case Phase.Approach:
                    if (currentS - halfLength - Context.Vehicle.VehicleFrontS >= mergeGap)
                    {
                        phase = Phase.Merge;
                        timer = 0f;
                    }
                    break;

                case Phase.Merge:
                    timer += deltaTime;
                    float u = Mathf.Clamp01(timer / mergeSeconds);
                    currentT = Mathf.Lerp(fromT, toT, u * u * (3f - 2f * u));
                    if (u >= 1f)
                    {
                        phase = Phase.Brake;
                        SetBrakeLights(true);
                    }
                    break;

                case Phase.Brake:
                    speed = Mathf.MoveTowards(speed, slowSpeed, brakeDeceleration * deltaTime);
                    if (speed <= slowSpeed)
                    {
                        phase = Phase.Hold;
                        timer = 0f;
                    }
                    break;

                case Phase.Hold:
                    timer += deltaTime;
                    if (timer >= holdSeconds)
                    {
                        phase = Phase.Accelerate;
                        SetBrakeLights(false);
                    }
                    break;

                case Phase.Accelerate:
                    speed = Mathf.MoveTowards(speed, cruiseSpeed, acceleration * deltaTime);
                    if (currentS - Context.Vehicle.VehicleFrontS > finishAhead)
                    {
                        IsFinished = true;
                    }
                    break;
            }

            currentS += speed * deltaTime;
            float lateralSpeed = deltaTime > 0f ? (currentT - previousT) / deltaTime : 0f;
            PlaceAt(currentS, currentT, Mathf.Atan2(lateralSpeed, Mathf.Max(speed, 0.1f)) * Mathf.Rad2Deg);
        }

        protected override bool IsOnCarriageway()
        {
            return phase >= Phase.Merge && Mathf.Abs(currentT - toT) < laneHalfWidth && speed < blockingSpeed;
        }

        private void Show(bool shown)
        {
            if (visual != null)
            {
                visual.SetActive(shown);
            }
            SetCollidersEnabled(shown);
        }

        private void SetBrakeLights(bool lit)
        {
            foreach (Renderer light in brakeLights)
            {
                if (light != null)
                {
                    light.enabled = lit;
                }
            }
        }
    }
}
