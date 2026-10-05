using BusSim.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace BusSim.Tests
{
    public class TrafficWarningHudTests
    {
        private GameObject host;
        private TrafficWarningHud hud;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("HudTest");
            hud = host.AddComponent<TrafficWarningHud>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        private static TrafficManager.FollowerInfo Follower(float gap, float closing, int side, bool aggressive = true)
        {
            return new TrafficManager.FollowerInfo { Present = true, Gap = gap, ClosingSpeed = closing, Side = side, Aggressive = aggressive };
        }

        [Test]
        public void NoFollowerGivesNoWarning()
        {
            Assert.AreEqual(TrafficWarningHud.Warning.None, hud.Classify(new TrafficManager.FollowerInfo()));
        }

        [Test]
        public void FastCarFarBehindIsOnlyAnApproachWarning()
        {
            Assert.AreEqual(TrafficWarningHud.Warning.Approaching, hud.Classify(Follower(40f, 10f, 0)));
        }

        [Test]
        public void SlowFollowerFarBehindGivesNoWarning()
        {
            Assert.AreEqual(TrafficWarningHud.Warning.None, hud.Classify(Follower(40f, 0.5f, 0)));
        }

        [Test]
        public void CloseCarInTheSameLaneIsATailgater()
        {
            Assert.AreEqual(TrafficWarningHud.Warning.Tailgating, hud.Classify(Follower(5f, 2f, 0)));
        }

        [Test]
        public void CarInAnotherLaneNearbyIsOvertakingOnItsSide()
        {
            Assert.AreEqual(TrafficWarningHud.Warning.Overtaking, hud.Classify(Follower(4f, 6f, 1)));
            Assert.AreEqual(TrafficWarningHud.Warning.Overtaking, hud.Classify(Follower(-3f, 3f, -1)));
        }

        [Test]
        public void TextNamesTheSideTheDistanceAndTheSpeedingUp()
        {
            string left = TrafficWarningHud.Describe(TrafficWarningHud.Warning.Overtaking, Follower(2f, 5f, -1), 0);
            string right = TrafficWarningHud.Describe(TrafficWarningHud.Warning.Overtaking, Follower(2f, 5f, 1), 0);
            StringAssert.Contains("LEFT", left);
            StringAssert.Contains("RIGHT", right);
            string fast = TrafficWarningHud.Describe(TrafficWarningHud.Warning.Approaching, Follower(40f, 10f, 0), 8);
            StringAssert.Contains("SPEEDING UP", fast);
            StringAssert.Contains("40 m", fast);
        }
    }
}
