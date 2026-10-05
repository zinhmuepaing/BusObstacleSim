using System.Collections.Generic;
using BusSim.Spawning;
using NUnit.Framework;

namespace BusSim.Tests
{
    public class ClearanceValidatorTests
    {
        private const float RoadWidth = 7f;
        private const float HalfWidth = RoadWidth * 0.5f;
        private const float Corridor = 2.5f;
        private const float Margin = 15f;

        private static readonly List<Footprint> None = new List<Footprint>();

        /// <summary>Footprint flush with the left road edge with the given width.</summary>
        private static Footprint LeftFlush(float s, float width, float length)
        {
            return new Footprint(s, -HalfWidth + width * 0.5f, width, length);
        }

        [Test]
        public void EmptyRoad_SmallObstacleAtEdgePasses()
        {
            Footprint candidate = LeftFlush(100f, 0.5f, 1f);
            Assert.IsTrue(ClearanceValidator.IsValid(candidate, None, RoadWidth, Corridor, Margin));
            Assert.AreEqual(RoadWidth - 0.5f, ClearanceValidator.LargestGap(candidate, None, RoadWidth, Margin, out _), 1e-4f);
        }

        [Test]
        public void ObstacleLeavingExactlyTheCorridorPasses()
        {
            Footprint candidate = LeftFlush(100f, RoadWidth - Corridor, 4f);
            Assert.IsTrue(ClearanceValidator.IsValid(candidate, None, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void ObstacleLeavingJustUnderTheCorridorFails()
        {
            Footprint candidate = LeftFlush(100f, RoadWidth - (Corridor - 0.01f), 4f);
            Assert.IsFalse(ClearanceValidator.IsValid(candidate, None, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void TwoObstaclesThatTogetherBlockFail()
        {
            // Each alone leaves more than the corridor, but side by side the widest gap is 2.2 m.
            Footprint left = LeftFlush(100f, 2.4f, 4f);
            Footprint right = new Footprint(110f, HalfWidth - 1.2f, 2.4f, 4f);
            List<Footprint> accepted = new List<Footprint> { left };

            Assert.IsTrue(ClearanceValidator.IsValid(left, None, RoadWidth, Corridor, Margin));
            Assert.IsTrue(ClearanceValidator.IsValid(right, None, RoadWidth, Corridor, Margin));
            Assert.IsFalse(ClearanceValidator.IsValid(right, accepted, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void ObstacleJustOutsideWindowIsIgnored()
        {
            Footprint left = LeftFlush(100f, 2.4f, 4f); // occupies s 98 to 102
            // Window of the right obstacle reaches back to its SMin - 15. Place it so that is 102.01.
            Footprint right = new Footprint(102.01f + Margin + 2f, HalfWidth - 1.2f, 2.4f, 4f);
            Assert.IsTrue(ClearanceValidator.IsValid(right, new List<Footprint> { left }, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void ObstacleJustInsideWindowCounts()
        {
            Footprint left = LeftFlush(100f, 2.4f, 4f);
            Footprint right = new Footprint(101.99f + Margin + 2f, HalfWidth - 1.2f, 2.4f, 4f);
            Assert.IsFalse(ClearanceValidator.IsValid(right, new List<Footprint> { left }, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void CentreObstacleLeavesTooLittleEitherSide()
        {
            // 2.2 m wide in the middle: gaps are 2.4 m each side, neither reaches the 2.5 m corridor.
            Footprint candidate = new Footprint(100f, 0f, 2.2f, 2f);
            Assert.IsFalse(ClearanceValidator.IsValid(candidate, None, RoadWidth, Corridor, Margin));
        }

        [Test]
        public void FootprintOnFootpathIsIgnoredForCorridor()
        {
            Footprint candidate = new Footprint(100f, -HalfWidth - 1f, 1.5f, 2f);
            Assert.AreEqual(RoadWidth, ClearanceValidator.LargestGap(candidate, None, RoadWidth, Margin, out _), 1e-4f);
        }
    }
}
