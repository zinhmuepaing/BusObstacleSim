using BusSim.Road;
using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Scene view debug tool. Draws a line from the road centreline to the point under the mouse
    /// and labels it with road coordinates s and t. Toggle from the BusSim/Road menu.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadCursorProbe
    {
        private const string EnabledPrefKey = "BusSim.RoadCursorProbe.Enabled";
        private const string ToggleMenuPath = "BusSim/Road/Cursor Probe (s, t)";
        private const float LineThickness = 3f;
        private const float HitMarkerRadius = 0.4f;
        private const float LabelHeight = 1.2f;
        private static readonly Color OnRoadColour = new Color(0.1f, 0.9f, 0.3f);
        private static readonly Color OffRoadColour = new Color(1f, 0.35f, 0.2f);

        private static RoadSampler sampler;

        static RoadCursorProbe()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        [MenuItem(ToggleMenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(ToggleMenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenuPath, Enabled);
            return true;
        }

        private static void OnSceneGui(SceneView view)
        {
            if (!Enabled || Application.isPlaying)
            {
                return;
            }

            if (sampler == null)
            {
                sampler = Object.FindAnyObjectByType<RoadSampler>();
                if (sampler == null)
                {
                    return;
                }
            }

            Event current = Event.current;
            if (current.type == EventType.MouseMove)
            {
                view.Repaint();
            }
            if (current.type != EventType.Repaint)
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            Plane roadPlane = new Plane(Vector3.up, sampler.GetPoint(0f, 0f));
            if (!roadPlane.Raycast(ray, out float distance))
            {
                return;
            }

            Vector3 hit = ray.GetPoint(distance);
            (float s, float t) = sampler.ProjectToRoad(hit);
            Vector3 centre = sampler.GetPoint(s, 0f);
            bool onRoad = sampler.Settings == null || Mathf.Abs(t) <= sampler.Settings.HalfRoadWidth;

            Handles.color = onRoad ? OnRoadColour : OffRoadColour;
            Handles.DrawLine(centre, hit, LineThickness);
            Handles.DrawWireDisc(hit, Vector3.up, HitMarkerRadius);
            Handles.DrawWireDisc(centre, Vector3.up, HitMarkerRadius);
            Handles.Label(hit + Vector3.up * LabelHeight, $"s = {s:F1} m    t = {t:F2} m");
        }
    }
}
