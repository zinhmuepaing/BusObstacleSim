using System;

namespace BusSim.Road
{
    /// <summary>
    /// A side road joining the left of this road. The left kerb and footpath are removed over
    /// s - gapHalfLength .. s + gapHalfLength, and curved kerb returns join the two roads.
    /// The return radius is gapHalfLength minus sideHalfWidth.
    /// </summary>
    [Serializable]
    public struct JunctionSpec
    {
        public float s;
        public float sideHalfWidth;
        public float gapHalfLength;

        public JunctionSpec(float s, float sideHalfWidth, float gapHalfLength)
        {
            this.s = s;
            this.sideHalfWidth = sideHalfWidth;
            this.gapHalfLength = gapHalfLength;
        }

        public float Radius => gapHalfLength - sideHalfWidth;
    }
}
