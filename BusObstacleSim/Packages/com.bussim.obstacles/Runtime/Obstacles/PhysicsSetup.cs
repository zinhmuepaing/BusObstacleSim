using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>
    /// Runtime collision filtering, so the package needs no ProjectSettings edits. Idempotent.
    /// Layers are created by the demo scene builder; missing layers are skipped.
    /// </summary>
    public static class PhysicsSetup
    {
        public const string ObstacleBodyLayer = "ObstacleBody";
        public const string VehicleLayer = "Vehicle";
        public const string TrafficLayer = "Traffic";

        /// <summary>Footprint triggers never collide; scripted AI traffic never pushes obstacle bodies.</summary>
        public static void Apply()
        {
            int body = LayerMask.NameToLayer(ObstacleBodyLayer);
            int traffic = LayerMask.NameToLayer(TrafficLayer);
            int obstacles = LayerMask.NameToLayer("Obstacles");

            // The footprint trigger layer never collides with anything, it is only for queries.
            if (obstacles >= 0)
            {
                for (int layer = 0; layer < 32; layer++)
                {
                    Physics.IgnoreLayerCollision(obstacles, layer, true);
                }
            }

            // AI traffic is kinematic and scripted, so it must not push obstacle bodies around.
            if (body >= 0 && traffic >= 0)
            {
                Physics.IgnoreLayerCollision(body, traffic, true);
            }
        }
    }
}
