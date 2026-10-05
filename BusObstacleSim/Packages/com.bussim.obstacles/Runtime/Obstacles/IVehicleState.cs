using UnityEngine;

namespace BusSim.Obstacles
{
    /// <summary>What obstacles may know about the driven vehicle. No dependency on a vehicle type.</summary>
    public interface IVehicleState
    {
        Transform VehicleTransform { get; }

        /// <summary>Road s of the vehicle's front bumper.</summary>
        float VehicleFrontS { get; }

        /// <summary>Road t of the vehicle's centre.</summary>
        float VehicleT { get; }

        /// <summary>Speed along the road in metres per second.</summary>
        float VehicleSpeed { get; }
    }
}
