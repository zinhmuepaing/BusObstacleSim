using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>One vehicle-obstacle collision.</summary>
    public struct CollisionRecord
    {
        public float Time;
        public string ObstacleId;
        public int EventIndex;
        public float RelativeSpeed;
        public float VehicleSpeed;
        public Vector3 Point;
    }
}
