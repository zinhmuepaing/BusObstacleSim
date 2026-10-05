using BusSim.Spawning;
using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Base class for every obstacle type. The spawner calls Init when the pooled instance is taken,
    /// Activate once, Tick every frame while active, and Finish before releasing it.
    /// </summary>
    public abstract class ObstacleBehaviour : MonoBehaviour
    {
        private const float MinSpeedForArrival = 0.1f;

        private Vector3 baseScale = Vector3.one;
        private bool baseScaleCaptured;
        private Collider[] colliders;

        /// <summary>Current road s. Moving types override this so despawning follows them.</summary>
        public virtual float CurrentS => Event.S;

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

        protected ObstacleContext Context { get; private set; }
        public SpawnEvent Event => Context.Event;

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

        public void Init(ObstacleContext context)
        {
            Context = context;
            BlockingSeconds = 0f;
            LongestBlockSeconds = 0f;
            HasTriggered = false;
            TriggerTimeToArrival = float.PositiveInfinity;
            IsFinished = false;
            PlaceAt(Event.S, Event.T, Event.YawDegrees);
            OnInit();
        }

        public virtual void Activate()
        {
        }

        public void Tick(float deltaTime)
        {
            OnTick(deltaTime);
            if (IsOnCarriageway())
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
        }

        protected virtual void OnInit()
        {
        }

        protected virtual void OnTick(float deltaTime)
        {
        }

        /// <summary>Dynamic types report whether any part of them is on the carriageway right now.</summary>
        protected virtual bool IsOnCarriageway()
        {
            return false;
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

        protected void PlaceAt(float s, float t, float yawDegrees)
        {
            if (!baseScaleCaptured)
            {
                baseScale = transform.localScale;
                baseScaleCaptured = true;
            }

            Context.Road.GetFrame(s, out Vector3 centre, out Vector3 forward, out Vector3 right);
            transform.SetPositionAndRotation(
                centre + right * t,
                Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, yawDegrees, 0f));

            bool mirror = Event.Definition.mirrorOnRightSide && t > 0f;
            transform.localScale = new Vector3(mirror ? -baseScale.x : baseScale.x, baseScale.y, baseScale.z);
        }
    }
}
