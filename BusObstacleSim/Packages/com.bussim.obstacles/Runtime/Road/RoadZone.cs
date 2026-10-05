using System;

namespace BusSim.Road
{
    /// <summary>A stretch of road between sStart and sEnd (metres along the spline).</summary>
    [Serializable]
    public struct RoadZone
    {
        public ZoneType type;
        public float sStart;
        public float sEnd;

        public RoadZone(ZoneType type, float sStart, float sEnd)
        {
            this.type = type;
            this.sStart = sStart;
            this.sEnd = sEnd;
        }

        /// <summary>True when the whole range [sMin, sMax] lies inside the zone.</summary>
        public bool Contains(float sMin, float sMax)
        {
            return sMin >= sStart && sMax <= sEnd;
        }
    }
}
