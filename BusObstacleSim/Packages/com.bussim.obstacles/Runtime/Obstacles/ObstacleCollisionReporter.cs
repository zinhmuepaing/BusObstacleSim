using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Sits on an obstacle's solid Body child. Forwards collisions with the vehicle layer to the
    /// behaviour (hit reaction) and to the spawner's sinks (logging). Debounced per instance.
    /// </summary>
    public class ObstacleCollisionReporter : MonoBehaviour
    {
        [Tooltip("Impacts slower than this (m/s relative) do not count as a hit.")]
        [SerializeField, Min(0f)] private float minRelativeSpeed = 0.5f;
        [Tooltip("Ignore further contacts from the same obstacle for this long after a hit.")]
        [SerializeField, Min(0f)] private float debounceSeconds = 1f;

        private ObstacleBehaviour behaviour;
        private float lastHitTime = float.NegativeInfinity;

        /// <summary>Where records go. The spawner assigns it when the obstacle is taken from the pool.</summary>
        public ICollisionSink Sink { get; set; }

        private void Awake()
        {
            behaviour = GetComponentInParent<ObstacleBehaviour>();
        }

        public void ResetHit()
        {
            lastHitTime = float.NegativeInfinity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (behaviour == null || !behaviour.IsActiveInRun)
            {
                return;
            }

            int vehicleLayer = LayerMask.NameToLayer(PhysicsSetup.VehicleLayer);
            if (vehicleLayer < 0 || collision.gameObject.layer != vehicleLayer)
            {
                return;
            }

            float relativeSpeed = collision.relativeVelocity.magnitude;
            if (relativeSpeed < minRelativeSpeed || Time.time - lastHitTime < debounceSeconds)
            {
                return;
            }
            lastHitTime = Time.time;

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Rigidbody other = collision.rigidbody;
            float vehicleSpeed = other != null ? other.linearVelocity.magnitude : 0f;
            behaviour.OnHit(other, collision.relativeVelocity, point);

            Sink?.OnCollision(new CollisionRecord
            {
                Time = Time.time,
                ObstacleId = behaviour.Event.Definition != null ? behaviour.Event.Definition.id : name,
                EventIndex = behaviour.Event.EventIndex,
                RelativeSpeed = relativeSpeed,
                VehicleSpeed = vehicleSpeed,
                Point = point
            });
        }
    }
}
