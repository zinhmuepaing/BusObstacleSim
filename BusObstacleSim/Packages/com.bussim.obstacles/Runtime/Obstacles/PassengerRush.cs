using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Passenger at a bus stop who, as the bus approaches, runs along the footpath toward it and
    /// steps off the kerb to flag it, waits, then steps back. Blocks only the kerbside edge.
    /// </summary>
    [RequireComponent(typeof(WalkRig))]
    public class PassengerRush : ObstacleBehaviour
    {
        private enum Phase
        {
            Waiting,
            Running,
            AtKerb,
            Returning,
            Done
        }

        [SerializeField, Min(0.1f)] private float runSpeed = 3f;
        [SerializeField, Min(0.1f)] private float runSeconds = 2.5f;
        [Tooltip("How far past the kerb line onto the carriageway the passenger steps.")]
        [SerializeField, Min(0f)] private float kerbOvershoot = 0.4f;
        [SerializeField, Min(0f)] private float waitSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float stepBackSpeed = 1f;
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.3f;

        private WalkRig rig;
        private Phase phase;
        private float currentS;
        private float currentT;
        private float kerbT;
        private float timer;

        public override float CurrentS => currentS;

        private void Awake()
        {
            rig = GetComponent<WalkRig>();
        }

        protected override void OnInit()
        {
            phase = Phase.Waiting;
            currentS = Event.S;
            currentT = Event.T;
            kerbT = Mathf.Sign(Event.T) * (Context.Settings.HalfRoadWidth - kerbOvershoot);
            timer = 0f;
            PlaceAt(currentS, currentT, 180f);
            rig.Stand();
        }

        protected override void OnTick(float deltaTime)
        {
            switch (phase)
            {
                case Phase.Waiting:
                    float timeToArrival = TimeToArrival(currentS);
                    if (timeToArrival <= Event.Definition.triggerTimeToArrival)
                    {
                        HasTriggered = true;
                        TriggerTimeToArrival = timeToArrival;
                        phase = Phase.Running;
                    }
                    break;

                case Phase.Running:
                    timer += deltaTime;
                    float lateralSpeed = (kerbT - Event.T) / runSeconds;
                    currentT += lateralSpeed * deltaTime;
                    currentS -= runSpeed * deltaTime;
                    rig.Advance(runSpeed * deltaTime);
                    PlaceAt(currentS, currentT, Mathf.Atan2(lateralSpeed, -runSpeed) * Mathf.Rad2Deg);
                    if (timer >= runSeconds)
                    {
                        currentT = kerbT;
                        phase = Phase.AtKerb;
                        timer = 0f;
                        rig.Stand();
                    }
                    break;

                case Phase.AtKerb:
                    timer += deltaTime;
                    if (timer >= waitSeconds)
                    {
                        phase = Phase.Returning;
                    }
                    break;

                case Phase.Returning:
                    float step = stepBackSpeed * deltaTime;
                    currentT = Mathf.MoveTowards(currentT, Event.T, step);
                    rig.Advance(step);
                    PlaceAt(currentS, currentT, Event.T < 0f ? -90f : 90f);
                    if (Mathf.Approximately(currentT, Event.T))
                    {
                        phase = Phase.Done;
                        rig.Stand();
                    }
                    break;
            }
        }

        protected override bool IsOnCarriageway()
        {
            return HasTriggered && Mathf.Abs(currentT) - bodyRadius < Context.Settings.HalfRoadWidth;
        }
    }
}
