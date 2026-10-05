using UnityEngine;

namespace BusSim.Traffic
{
    /// <summary>
    /// Tells the driver what is coming up behind: a fast car closing in, a tailgater, or a car overtaking on one side.
    /// Draws a banner (with the side and the distance) and sounds a horn when a tailgater or overtaker is right there.
    /// It reads the traffic manager only, so any vehicle works.
    /// </summary>
    [RequireComponent(typeof(TrafficManager))]
    public class TrafficWarningHud : MonoBehaviour
    {
        public enum Warning
        {
            None,
            Approaching,
            Tailgating,
            Overtaking
        }

        private const int HornSampleRate = 22050;
        private const float HornSeconds = 0.5f;
        private const float HornLowHz = 392f;
        private const float HornHighHz = 494f;
        private const float HornVolume = 0.35f;
        private const float MetresPerBucket = 5f;

        [Header("Detection")]
        [Tooltip("Followers further behind than this (bumper to bumper) are ignored.")]
        [SerializeField, Min(5f)] private float range = 70f;
        [Tooltip("A follower closing faster than this (m/s) and nearer than approachGap raises the first warning.")]
        [SerializeField, Min(0.5f)] private float closingSpeedToWarn = 4f;
        [SerializeField, Min(5f)] private float approachGap = 55f;
        [Tooltip("A follower in the same lane closer than this is a tailgater.")]
        [SerializeField, Min(1f)] private float tailgateGap = 10f;
        [Tooltip("A follower in another lane within this distance behind, or still level with the vehicle, is overtaking.")]
        [SerializeField, Min(1f)] private float overtakeGap = 12f;
        [Tooltip("How far past level (metres) a passing car still counts as overtaking.")]
        [SerializeField, Min(0f)] private float alongsideAllowance = 8f;

        [Header("Banner")]
        [SerializeField, Min(100f)] private float bannerWidth = 560f;
        [SerializeField, Min(20f)] private float bannerHeight = 64f;
        [SerializeField, Min(0f)] private float topMargin = 24f;
        [SerializeField, Min(8)] private int fontSize = 22;
        [SerializeField, Min(0.5f)] private float pulseHz = 3f;
        [SerializeField] private Color approachColour = new Color(0.95f, 0.65f, 0.05f, 0.9f);
        [SerializeField] private Color dangerColour = new Color(0.85f, 0.1f, 0.1f, 0.92f);

        [Header("Horn")]
        [SerializeField] private bool horn = true;
        [Tooltip("Seconds between horn blasts.")]
        [SerializeField, Min(0.5f)] private float hornCooldown = 3f;

        private TrafficManager traffic;
        private AudioSource audioSource;
        private AudioClip hornClip;
        private Texture2D pixel;
        private GUIStyle style;
        private Warning warning;
        private string text = string.Empty;
        private int textKey = -1;
        private float lastHornTime = float.NegativeInfinity;

        private void Awake()
        {
            traffic = GetComponent<TrafficManager>();
            pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            hornClip = BuildHorn();
        }

        private void OnDestroy()
        {
            if (pixel != null)
            {
                Destroy(pixel);
            }
            if (hornClip != null)
            {
                Destroy(hornClip);
            }
        }

        private void Update()
        {
            TrafficManager.FollowerInfo follower = traffic.NearestBehind(range, alongsideAllowance);
            Warning next = Classify(follower);
            if (next != Warning.None)
            {
                // Rebuild the text only when what it says changes (state, side, or the distance bucket).
                int bucket = Mathf.Max(0, Mathf.RoundToInt(follower.Gap / MetresPerBucket));
                int key = ((int)next * 8 + follower.Side + 1) * 1000 + bucket + (follower.Aggressive ? 100000 : 0);
                if (key != textKey)
                {
                    textKey = key;
                    text = Describe(next, follower, bucket);
                }
            }
            else
            {
                textKey = -1;
            }

            if (horn && next >= Warning.Tailgating && warning < Warning.Tailgating && Time.unscaledTime - lastHornTime > hornCooldown)
            {
                lastHornTime = Time.unscaledTime;
                audioSource.PlayOneShot(hornClip, HornVolume);
            }
            warning = next;
        }

        public Warning Classify(TrafficManager.FollowerInfo follower)
        {
            if (!follower.Present)
            {
                return Warning.None;
            }
            if (follower.Side != 0 && follower.Gap < overtakeGap)
            {
                return Warning.Overtaking;
            }
            if (follower.Side == 0 && follower.Gap < tailgateGap && follower.ClosingSpeed > -closingSpeedToWarn)
            {
                return Warning.Tailgating;
            }
            if (follower.ClosingSpeed > closingSpeedToWarn && follower.Gap < approachGap)
            {
                return Warning.Approaching;
            }
            return Warning.None;
        }

        public static string Describe(Warning kind, TrafficManager.FollowerInfo follower, int bucket)
        {
            int metres = bucket * (int)MetresPerBucket;
            string side = follower.Side < 0 ? "LEFT" : "RIGHT";
            switch (kind)
            {
                case Warning.Overtaking:
                    return follower.Side < 0 ? $"<<  CAR OVERTAKING ON YOUR {side}" : $"CAR OVERTAKING ON YOUR {side}  >>";
                case Warning.Tailgating:
                    return $"TAILGATER BEHIND YOU  -  {metres} m";
                default:
                    string lane = follower.Side == 0 ? "IN YOUR LANE" : (follower.Side < 0 ? "ON YOUR LEFT" : "ON YOUR RIGHT");
                    return follower.Aggressive
                        ? $"CAR BEHIND SPEEDING UP, {lane}  -  {metres} m"
                        : $"CAR APPROACHING FROM BEHIND, {lane}  -  {metres} m";
            }
        }

        private void OnGUI()
        {
            if (warning == Warning.None || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = Color.white;
            }

            Color colour = warning == Warning.Approaching ? approachColour : dangerColour;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f);
            colour.a *= pulse;

            Rect box = new Rect((Screen.width - bannerWidth) * 0.5f, topMargin, bannerWidth, bannerHeight);
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(box, pixel);
            GUI.color = previous;
            GUI.Label(box, text, style);
        }

        /// <summary>A two-tone car horn: two sine waves with a quick attack and release.</summary>
        private static AudioClip BuildHorn()
        {
            int count = Mathf.RoundToInt(HornSampleRate * HornSeconds);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / HornSampleRate;
                float envelope = Mathf.Clamp01(Mathf.Min(t / 0.02f, (HornSeconds - t) / 0.08f));
                float wave = Mathf.Sin(2f * Mathf.PI * HornLowHz * t) + Mathf.Sin(2f * Mathf.PI * HornHighHz * t);
                samples[i] = wave * 0.5f * envelope;
            }
            AudioClip clip = AudioClip.Create("Horn", count, 1, HornSampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
