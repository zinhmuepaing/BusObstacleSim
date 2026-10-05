using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Motorcycle that appears behind the bus and filters past it along the lane line, faster
    /// than the bus. Hidden (and without colliders) until triggered. It never slows in front of the
    /// bus, so it does not count as blocking; the hazard is the close pass alongside.
    /// </summary>
    public class MotorcycleFilter : ObstacleBehaviour
    {
        [SerializeField, Min(0f)] private float speedAboveVehicle = 5f;
        [SerializeField, Min(0f)] private float startBehindFront = 25f;
        [SerializeField, Min(1f)] private float finishAhead = 80f;
        [SerializeField] private GameObject visual;

        private float currentS;
        private float speed;

        protected override float ScriptedS => currentS;

        protected override void OnInit()
        {
            currentS = Event.S;
            speed = 0f;
            Show(false);
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
                float appearS = Context.Vehicle.VehicleFrontS - startBehindFront;
                if (!AreaFree(appearS, Event.T))
                {
                    return; // wait for a clear gap rather than appear inside the vehicle
                }

                HasTriggered = true;
                TriggerTimeToArrival = timeToArrival;
                currentS = appearS;
                PlaceAtImmediately(currentS, Event.T, 0f);
                Show(true);
            }

            speed = Mathf.Max(speed, Context.Vehicle.VehicleSpeed + speedAboveVehicle);
            currentS += speed * deltaTime;
            PlaceAt(currentS, Event.T, 0f);
            if (currentS > Context.Vehicle.VehicleFrontS + finishAhead)
            {
                IsFinished = true;
            }
        }

        private void Show(bool shown)
        {
            if (visual != null)
            {
                visual.SetActive(shown);
            }
            SetCollidersEnabled(shown);
        }
    }
}
