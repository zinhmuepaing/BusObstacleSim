using System.Collections.Generic;
using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Remembers the prefab's rigidbodies and the pose of every child, so a pooled obstacle that
    /// was knocked about comes back exactly as built. Optionally mirrors the layout left to right.
    /// </summary>
    public class PooledPhysicsReset : MonoBehaviour
    {
        private struct BodyState
        {
            public Rigidbody Body;
            public bool Kinematic;
            public bool UseGravity;
            public RigidbodyConstraints Constraints;
            public float Mass;
        }

        private struct PoseState
        {
            public Transform Target;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private readonly List<BodyState> bodies = new List<BodyState>();
        private readonly List<PoseState> poses = new List<PoseState>();
        private bool captured;

        public int RigidbodyCount => bodies.Count;

        private void Awake()
        {
            Capture();
        }

        private void Capture()
        {
            if (captured)
            {
                return;
            }
            captured = true;

            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            {
                bodies.Add(new BodyState
                {
                    Body = body,
                    Kinematic = body.isKinematic,
                    UseGravity = body.useGravity,
                    Constraints = body.constraints,
                    Mass = body.mass
                });
            }

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform)
                {
                    continue;
                }
                poses.Add(new PoseState { Target = child, LocalPosition = child.localPosition, LocalRotation = child.localRotation });
            }
        }

        /// <summary>
        /// Stops every body, restores poses, and puts rigidbody settings back to their prefab values.
        /// With mirrored set, the layout is reflected across the forward axis (x becomes -x).
        /// </summary>
        public void ResetForReuse(bool mirrored)
        {
            Capture();

            foreach (BodyState state in bodies)
            {
                Rigidbody body = state.Body;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = state.Kinematic;
                body.useGravity = state.UseGravity;
                body.constraints = state.Constraints;
                body.mass = state.Mass;
            }

            foreach (PoseState pose in poses)
            {
                Vector3 position = pose.LocalPosition;
                Quaternion rotation = pose.LocalRotation;
                if (mirrored)
                {
                    position.x = -position.x;
                    Vector3 euler = rotation.eulerAngles;
                    rotation = Quaternion.Euler(euler.x, -euler.y, -euler.z);
                }
                pose.Target.SetLocalPositionAndRotation(position, rotation);
            }

            foreach (BodyState state in bodies)
            {
                if (!state.Body.isKinematic)
                {
                    state.Body.Sleep();
                }
            }
        }

        /// <summary>True if any dynamic body is still moving faster than the threshold (m/s).</summary>
        public bool AnyBodyMoving(float speedThreshold)
        {
            float limit = speedThreshold * speedThreshold;
            foreach (BodyState state in bodies)
            {
                if (!state.Body.isKinematic && state.Body.linearVelocity.sqrMagnitude > limit)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
