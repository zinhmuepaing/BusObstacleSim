namespace BusSim.Obstacles
{
    /// <summary>Receives every vehicle-obstacle collision (RunLogger and tests implement it).</summary>
    public interface ICollisionSink
    {
        void OnCollision(in CollisionRecord record);
    }
}
