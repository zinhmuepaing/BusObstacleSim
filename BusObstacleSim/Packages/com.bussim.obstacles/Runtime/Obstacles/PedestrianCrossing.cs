using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// A pedestrian who waits on one footpath and walks across to the other once the vehicle's
    /// time to arrival drops below the definition's trigger. Optionally pauses once mid-road
    /// (elderly crosser). Used for adult jaywalkers, elderly crossers and running children.
    /// </summary>
    [RequireComponent(typeof(WalkRig))]
    public class PedestrianCrossing : ObstacleBehaviour
    {
        [SerializeField, Min(0.1f)] private float walkSpeed = 1.4f;
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.3f;
        [Tooltip("Chance of one pause somewhere between 35 and 65 percent of the crossing.")]
        [SerializeField, Range(0f, 1f)] private float pauseChance;
        [SerializeField, Min(0f)] private float pauseSeconds = 1f;

        private WalkRig rig;
        private float currentT;
        private float targetT;
        private float direction;
        private float pauseAtT;
        private float pauseRemaining;
        private bool willPause;
        private bool pausing;
        private bool arrived;

        public float CurrentT => currentT;
        public float WalkSpeed => walkSpeed;
        public float BodyRadius => bodyRadius;
        public bool Arrived => arrived;

        public void SetMovement(float speed, float chanceOfPause, float secondsOfPause)
        {
            walkSpeed = speed;
            pauseChance = chanceOfPause;
            pauseSeconds = secondsOfPause;
        }

        protected override void Awake()
        {
            base.Awake();
            rig = GetComponent<WalkRig>();
        }

        protected override void OnInit()
        {
            currentT = Event.T;
            targetT = -Event.T;
            direction = Mathf.Sign(targetT - currentT);
            willPause = Context.Rng.NextDouble() < pauseChance;
            pauseAtT = Mathf.Lerp(currentT, targetT, 0.35f + 0.3f * (float)Context.Rng.NextDouble());
            pauseRemaining = pauseSeconds;
            pausing = false;
            arrived = false;
            PlaceAt(Event.S, currentT, FacingYaw());
            rig.Stand();
        }

        protected override void OnTick(float deltaTime)
        {
            if (!HasTriggered)
            {
                float timeToArrival = TimeToArrival(Event.S);
                if (timeToArrival > Event.Definition.triggerTimeToArrival)
                {
                    return;
                }
                HasTriggered = true;
                TriggerTimeToArrival = timeToArrival;
            }

            if (arrived)
            {
                return;
            }

            if (willPause && !pausing && (currentT - pauseAtT) * direction >= 0f)
            {
                pausing = true;
            }
            if (pausing && pauseRemaining > 0f)
            {
                pauseRemaining -= deltaTime;
                rig.Stand();
                return;
            }

            float step = walkSpeed * deltaTime;
            currentT += direction * step;
            if ((currentT - targetT) * direction >= 0f)
            {
                currentT = targetT;
                arrived = true;
            }

            PlaceAt(Event.S, currentT, FacingYaw());
            if (arrived)
            {
                rig.Stand();
            }
            else
            {
                rig.Advance(step);
            }
        }

        protected override bool IsOnCarriageway()
        {
            return HasTriggered && Mathf.Abs(currentT) - bodyRadius < Context.Settings.HalfRoadWidth;
        }

        private float FacingYaw()
        {
            return direction > 0f ? 90f : -90f;
        }
    }
}
