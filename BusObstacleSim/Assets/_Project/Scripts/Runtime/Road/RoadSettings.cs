using UnityEngine;

namespace BusSim.Road
{
    /// <summary>
    /// All road tunables. Road space: s is metres along the spline, t is lateral offset from
    /// the centreline, positive to the right of travel.
    /// </summary>
    [CreateAssetMenu(menuName = "BusSim/Road Settings", fileName = "RoadSettings")]
    public class RoadSettings : ScriptableObject
    {
        [Header("Carriageway")]
        [Min(1)] public int laneCount = 2;
        [Min(0.1f)] public float laneWidth = 3.5f;
        [Min(1f)] public float roadLength = 1000f;
        [Tooltip("Road surface height above the road object's origin. The road is flat.")]
        public float surfaceY = 0f;

        [Header("Footpath and kerb")]
        [Min(0f)] public float footpathWidth = 2f;
        [Min(0f)] public float kerbHeight = 0.15f;

        [Tooltip("Height of the invisible collider wall at the outer footpath edge. Keeps vehicles in the road corridor.")]
        [Min(0.1f)] public float edgeGuardHeight = 1.5f;

        [Header("Lane markings")]
        [Min(0.01f)] public float markingWidth = 0.15f;
        [Min(0.1f)] public float dashLength = 3f;
        [Min(0.1f)] public float dashGap = 6f;
        [Tooltip("Distance from the carriageway edge to the outer side of the edge line.")]
        [Min(0f)] public float edgeLineInset = 0.3f;
        [Tooltip("Height of markings above the asphalt, prevents z-fighting.")]
        [Min(0.001f)] public float markingLift = 0.01f;

        [Header("Mesh")]
        [Min(0.25f)] public float meshStep = 1f;
        [Min(0.1f)] public float uvMetresPerTile = 4f;

        [Header("Ground")]
        [Min(0f)] public float groundDrop = 0.05f;
        [Min(0f)] public float groundPadding = 200f;
        [Min(0.1f)] public float groundMetresPerTile = 8f;
        [Tooltip("Thickness of the box collider under the ground quad.")]
        [Min(0.1f)] public float groundColliderThickness = 1f;

        public float RoadWidth => laneCount * laneWidth;
        public float HalfRoadWidth => RoadWidth * 0.5f;

        /// <summary>Lateral offset t of a lane centre. Lane 0 is the leftmost lane.</summary>
        public float GetLaneCentreT(int laneIndexFromLeft)
        {
            return -HalfRoadWidth + laneWidth * (laneIndexFromLeft + 0.5f);
        }
    }
}
