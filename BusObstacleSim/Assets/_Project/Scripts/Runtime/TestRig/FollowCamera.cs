using UnityEngine;

namespace BusSim.TestRig
{
    /// <summary>
    /// Chase camera. Sits behind the target along its heading and swings round to follow it.
    /// Step is public so the follow behaviour can be driven and tested without a render loop.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(1f)] private float distanceBehind = 18f;
        [SerializeField, Min(0f)] private float height = 6f;
        [SerializeField, Min(0f)] private float lookAhead = 8f;
        [SerializeField, Min(0f)] private float lookHeight = 2f;
        [Tooltip("Time in seconds the camera takes to swing round to the bus heading. Lower is tighter.")]
        [SerializeField, Min(0.01f)] private float headingSmoothTime = 0.2f;

        private float yawDegrees;
        private float yawVelocity;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>Camera heading in degrees, for tests.</summary>
        public float HeadingYawDegrees => yawDegrees;

        /// <summary>Jump straight to the target's current view, no smoothing.</summary>
        public void Snap()
        {
            if (target == null)
            {
                return;
            }

            yawDegrees = TargetYawDegrees();
            yawVelocity = 0f;
            Apply();
        }

        public void Step(float deltaTime)
        {
            if (target == null)
            {
                return;
            }

            yawDegrees = Mathf.SmoothDampAngle(
                yawDegrees, TargetYawDegrees(), ref yawVelocity, headingSmoothTime, Mathf.Infinity, deltaTime);
            Apply();
        }

        private void Start()
        {
            Snap();
        }

        private void LateUpdate()
        {
            Step(Time.deltaTime);
        }

        private float TargetYawDegrees()
        {
            Vector3 forward = target.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private void Apply()
        {
            Vector3 heading = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            Vector3 anchor = target.position;
            transform.position = anchor - heading * distanceBehind + Vector3.up * height;
            Vector3 lookPoint = anchor + heading * lookAhead + Vector3.up * lookHeight;
            transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        }
    }
}
