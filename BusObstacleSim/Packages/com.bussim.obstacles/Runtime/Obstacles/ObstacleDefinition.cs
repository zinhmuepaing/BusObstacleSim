using BusSim.Road;
using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// One obstacle type. Adding a type means a new asset and prefab, no spawner code change.
    /// The footprint is a rectangle in road space: width along t, length along s.
    /// </summary>
    [CreateAssetMenu(menuName = "BusSim/Obstacle Definition", fileName = "ObstacleDefinition")]
    public class ObstacleDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "NEW_TYPE";
        public string displayName = "New obstacle";
        public ObstacleCategory category = ObstacleCategory.Debris;
        public GameObject prefab;
        public bool isDynamic;
        [Range(1, 5)] public int dangerLevel = 1;

        [Header("Footprint (metres)")]
        [Min(0.05f)] public float footprintWidth = 1f;
        [Min(0.05f)] public float footprintLength = 1f;

        [Header("Placement")]
        [Tooltip("Allowed range for the footprint centre t (negative is left of travel).")]
        public float minT = -3f;
        public float maxT = 3f;
        [Tooltip("Place only at minT or maxT (for example one side of the road or the other).")]
        public bool placeAtRangeEndsOnly;
        [Tooltip("Mirror the prefab across its forward axis when placed right of the centreline.")]
        public bool mirrorOnRightSide;
        [Min(0f)] public float yawJitterDegrees;
        [Tooltip("None means anywhere. Otherwise the footprint must lie inside a zone of this type.")]
        public ZoneType requiredZone = ZoneType.None;

        [Header("Selection")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("0 easy, 1 normal, 2 hard. The type appears at this difficulty and above.")]
        [Range(0, 2)] public int minDifficulty;
        [Tooltip("Extra spacing in metres before the next event after this one.")]
        [Min(0f)] public float minGapAfter;

        [Header("Dynamic behaviour")]
        [Tooltip("Longest time the obstacle may stay on the carriageway once it starts moving.")]
        [Min(0f)] public float maxBlockSeconds;
        [Tooltip("The behaviour starts when the vehicle's time to arrival falls below this.")]
        [Min(0f)] public float triggerTimeToArrival = 3.5f;
    }
}
