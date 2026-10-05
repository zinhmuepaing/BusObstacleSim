using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>Hazard lights: switches a set of renderers on and off at a fixed interval.</summary>
    public class BlinkingLights : MonoBehaviour
    {
        [SerializeField] private Renderer[] lights = new Renderer[0];
        [SerializeField, Min(0.05f)] private float intervalSeconds = 0.4f;

        private float timer;
        private bool lit = true;

        private void OnEnable()
        {
            timer = 0f;
            SetLit(true);
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer >= intervalSeconds)
            {
                timer -= intervalSeconds;
                SetLit(!lit);
            }
        }

        private void SetLit(bool on)
        {
            lit = on;
            foreach (Renderer light in lights)
            {
                if (light != null)
                {
                    light.enabled = on;
                }
            }
        }
    }
}
