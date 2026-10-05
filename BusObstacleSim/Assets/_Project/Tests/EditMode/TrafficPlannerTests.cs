using System.Collections.Generic;
using BusSim.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace BusSim.Tests
{
    public class TrafficPlannerTests
    {
        private const float RoadLength = 1000f;
        private const float JunctionS = 520f;
        private const int Models = 6;
        private const int Seeds = 200;

        private TrafficSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<TrafficSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        private List<TrafficSpawn> Plan(int seed, bool junction = true)
        {
            return TrafficPlanner.Plan(seed, settings, RoadLength, JunctionS, junction, Models);
        }

        [Test]
        public void SameSeedGivesTheSameTraffic()
        {
            List<TrafficSpawn> a = Plan(7);
            List<TrafficSpawn> b = Plan(7);
            Assert.Greater(a.Count, 0);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].S, b[i].S);
                Assert.AreEqual(a[i].Kind, b[i].Kind);
                Assert.AreEqual(a[i].Lane, b[i].Lane);
                Assert.AreEqual(a[i].SpeedMetresPerSecond, b[i].SpeedMetresPerSecond);
                Assert.AreEqual(a[i].ModelIndex, b[i].ModelIndex);
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentTraffic()
        {
            List<TrafficSpawn> a = Plan(1);
            List<TrafficSpawn> b = Plan(2);
            bool same = a.Count == b.Count;
            for (int i = 0; same && i < a.Count; i++)
            {
                same = a[i].S == b[i].S && a[i].Kind == b[i].Kind;
            }
            Assert.IsFalse(same);
        }

        [Test]
        public void DensityMatchesTheSettingWithin20Percent()
        {
            float total = 0f;
            float wantedPerKm = settings.sameDirectionPerKm + settings.oncomingPerKm;
            for (int seed = 0; seed < Seeds; seed++)
            {
                foreach (TrafficSpawn spawn in Plan(seed))
                {
                    total += spawn.Kind == TrafficKind.SideRoadEntry ? 0f : 1f;
                }
            }
            float span = RoadLength - settings.startBuffer - settings.endBuffer;
            float perKm = total / Seeds * 1000f / span;
            Assert.AreEqual(wantedPerKm, perKm, wantedPerKm * 0.2f);
        }

        [Test]
        public void LanesKeepTheirMinimumSpacing()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                List<TrafficSpawn> plan = Plan(seed);
                for (int i = 0; i < plan.Count; i++)
                {
                    for (int j = i + 1; j < plan.Count; j++)
                    {
                        bool sameLane = plan[i].Kind == plan[j].Kind && plan[i].Lane == plan[j].Lane
                            && plan[i].Kind != TrafficKind.SideRoadEntry;
                        if (sameLane)
                        {
                            Assert.GreaterOrEqual(Mathf.Abs(plan[i].S - plan[j].S), settings.minSpacing - 0.01f, $"seed {seed}");
                        }
                    }
                }
            }
        }

        [Test]
        public void SideRoadEntriesAreReleasedBeforeTheJunctionWithAGapDrawnInRange()
        {
            int entries = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                foreach (TrafficSpawn spawn in Plan(seed))
                {
                    if (spawn.Kind != TrafficKind.SideRoadEntry)
                    {
                        continue;
                    }
                    entries++;
                    Assert.Less(spawn.S, JunctionS);
                    Assert.GreaterOrEqual(spawn.AcceptedGapSeconds, settings.acceptedGapMin - 0.001f);
                    Assert.LessOrEqual(spawn.AcceptedGapSeconds, settings.acceptedGapMax + 0.001f);
                }
            }
            Assert.AreEqual(Seeds * settings.sideRoadEntries, entries);
        }

        [Test]
        public void NoJunctionMeansNoSideRoadEntries()
        {
            foreach (TrafficSpawn spawn in Plan(3, junction: false))
            {
                Assert.AreNotEqual(TrafficKind.SideRoadEntry, spawn.Kind);
            }
        }

        [Test]
        public void TrafficNeverChangesTheObstaclePlanSeedStream()
        {
            // The traffic stream is mixed from the run seed, so equal seeds still differ from the raw seed.
            List<TrafficSpawn> traffic = Plan(5);
            System.Random obstacleStream = new System.Random(5);
            double first = obstacleStream.NextDouble();
            float expectedIfShared = settings.startBuffer + (RoadLength - settings.startBuffer - settings.endBuffer) * (float)first;
            Assert.That(traffic[0].S, Is.Not.EqualTo(expectedIfShared).Within(0.001f));
        }
    }
}
