namespace BusSim.Road
{
    /// <summary>Special stretches of road. Some obstacle types only appear inside a matching zone.</summary>
    public enum ZoneType
    {
        None = 0,
        BusStop = 1,
        School = 2,
        Junction = 3
    }
}
