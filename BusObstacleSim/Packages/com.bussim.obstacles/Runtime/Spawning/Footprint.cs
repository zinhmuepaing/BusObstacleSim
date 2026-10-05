namespace BusSim.Spawning
{
    /// <summary>Axis-aligned rectangle in road space. Width is along t, length along s.</summary>
    public readonly struct Footprint
    {
        public readonly float S;
        public readonly float T;
        public readonly float Width;
        public readonly float Length;

        public Footprint(float s, float t, float width, float length)
        {
            S = s;
            T = t;
            Width = width;
            Length = length;
        }

        public float SMin => S - Length * 0.5f;
        public float SMax => S + Length * 0.5f;
        public float TMin => T - Width * 0.5f;
        public float TMax => T + Width * 0.5f;

        public override string ToString()
        {
            return $"(s {S:F2}, t {T:F2}, {Width:F2} x {Length:F2})";
        }
    }
}
