using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>Swings the limbs of a simple humanoid rig. Advance by the distance walked.</summary>
    public class WalkRig : MonoBehaviour, IWalkRig
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform legLeft;
        [SerializeField] private Transform legRight;
        [SerializeField] private Transform armLeft;
        [SerializeField] private Transform armRight;
        [SerializeField, Min(0.1f)] private float strideLength = 0.75f;
        [SerializeField] private float legSwingDegrees = 28f;
        [SerializeField] private float armSwingDegrees = 22f;
        [SerializeField, Min(0f)] private float bobHeight = 0.03f;

        private Vector3 bodyBasePosition;
        private float phase;

        public void SetRig(Transform bodyRoot, Transform leftLeg, Transform rightLeg, Transform leftArm, Transform rightArm)
        {
            body = bodyRoot;
            legLeft = leftLeg;
            legRight = rightLeg;
            armLeft = leftArm;
            armRight = rightArm;
        }

        private void Awake()
        {
            if (body != null)
            {
                bodyBasePosition = body.localPosition;
            }
        }

        public void Advance(float distance)
        {
            phase += distance / strideLength * Mathf.PI;
            Pose(Mathf.Sin(phase));
        }

        public void Stand()
        {
            phase = 0f;
            Pose(0f);
        }

        public void Fall()
        {
            Pose(0f);
        }

        private void Pose(float swing)
        {
            SetPitch(legLeft, swing * legSwingDegrees);
            SetPitch(legRight, -swing * legSwingDegrees);
            SetPitch(armLeft, -swing * armSwingDegrees);
            SetPitch(armRight, swing * armSwingDegrees);
            if (body != null)
            {
                body.localPosition = bodyBasePosition + Vector3.up * (Mathf.Abs(swing) * bobHeight);
            }
        }

        private static void SetPitch(Transform limb, float degrees)
        {
            if (limb != null)
            {
                limb.localRotation = Quaternion.Euler(degrees, 0f, 0f);
            }
        }
    }
}
