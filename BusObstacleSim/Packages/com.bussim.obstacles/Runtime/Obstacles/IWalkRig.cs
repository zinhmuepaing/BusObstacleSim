namespace BusSim.Obstacles
{
    /// <summary>
    /// Drives the look of a walking person, whatever their model: a primitive rig that swings limbs, or an
    /// animated character. Behaviours only talk to this, so models can be swapped freely.
    /// </summary>
    public interface IWalkRig
    {
        /// <summary>Called every physics step the person moves, with the distance covered this step.</summary>
        void Advance(float distance);

        /// <summary>Standing still.</summary>
        void Stand();

        /// <summary>The person has been knocked down.</summary>
        void Fall();
    }
}
