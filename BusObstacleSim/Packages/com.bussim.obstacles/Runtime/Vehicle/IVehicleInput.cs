namespace BusSim.Vehicle
{
    /// <summary>
    /// Drive commands for a vehicle. The keyboard, the autopilot and tests all write through this.
    /// </summary>
    public interface IVehicleInput
    {
        /// <summary>Throttle from -1 to 1 (negative brakes, then reverses) and steering from -1 (left) to 1 (right).</summary>
        void SetDrive(float throttle, float steer);

        /// <summary>Hands control back to the keyboard.</summary>
        void ClearDrive();
    }
}
