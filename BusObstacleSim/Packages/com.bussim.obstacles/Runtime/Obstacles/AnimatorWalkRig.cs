using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Walk rig for one or more animated characters (a group crosses together). Every Animator controller has a
    /// "Speed" float that blends idle, walk and sprint, and a "Fall" trigger. Speed comes from the distance moved
    /// each physics step.
    /// </summary>
    public class AnimatorWalkRig : MonoBehaviour, IWalkRig
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int FallHash = Animator.StringToHash("Fall");
        private static readonly int MoveStateHash = Animator.StringToHash("Move");

        [SerializeField] private Animator[] animators = new Animator[0];
        [Tooltip("Damping on the speed parameter so animation blends smoothly between idle, walk and sprint.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.08f;

        private bool fallen;

        public Animator Animator => animators.Length > 0 ? animators[0] : null;

        public void SetAnimator(Animator target)
        {
            animators = new[] { target };
        }

        public void SetAnimators(Animator[] targets)
        {
            animators = targets;
        }

        public void Advance(float distance)
        {
            float speed = distance / Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            foreach (Animator animator in animators)
            {
                if (animator != null)
                {
                    animator.SetFloat(SpeedHash, speed, speedDampTime, Time.fixedDeltaTime);
                }
            }
        }

        public void Stand()
        {
            foreach (Animator animator in animators)
            {
                if (animator == null)
                {
                    continue;
                }
                if (fallen)
                {
                    // A pooled person comes back on their feet.
                    animator.ResetTrigger(FallHash);
                    animator.Play(MoveStateHash, 0, 0f);
                }
                animator.SetFloat(SpeedHash, 0f);
            }
            fallen = false;
        }

        public void Fall()
        {
            fallen = true;
            foreach (Animator animator in animators)
            {
                if (animator != null)
                {
                    animator.SetTrigger(FallHash);
                }
            }
        }
    }
}
