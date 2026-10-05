using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Walk rig for an animated character. The Animator controller has a "Speed" float that blends idle,
    /// walk and sprint, and a "Fall" trigger. Speed comes from the distance moved each physics step.
    /// </summary>
    public class AnimatorWalkRig : MonoBehaviour, IWalkRig
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int FallHash = Animator.StringToHash("Fall");
        private static readonly int MoveStateHash = Animator.StringToHash("Move");

        [SerializeField] private Animator animator;
        [Tooltip("Damping on the speed parameter so animation blends smoothly between idle, walk and sprint.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.08f;

        private bool fallen;

        public Animator Animator => animator;

        public void SetAnimator(Animator target)
        {
            animator = target;
        }

        public void Advance(float distance)
        {
            if (animator == null)
            {
                return;
            }
            float speed = distance / Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            animator.SetFloat(SpeedHash, speed, speedDampTime, Time.fixedDeltaTime);
        }

        public void Stand()
        {
            if (animator == null)
            {
                return;
            }
            if (fallen)
            {
                // A pooled person comes back on their feet.
                animator.ResetTrigger(FallHash);
                animator.Play(MoveStateHash, 0, 0f);
                fallen = false;
            }
            animator.SetFloat(SpeedHash, 0f);
        }

        public void Fall()
        {
            if (animator == null)
            {
                return;
            }
            fallen = true;
            animator.SetTrigger(FallHash);
        }
    }
}
