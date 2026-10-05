using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using NUnit.Framework;
using UnityEngine;

namespace BusSim.Tests
{
    public class SpawnPlannerTests
    {
        private const float RoadLength = 1000f;
        private const float RoadWidth = 7f;
        private const float Corridor = 3.2f;
        private const float Margin = 15f;
        private const int PropertySeeds = 1000;

        private readonly List<Object> created = new List<Object>();
        private GameObject dummyPrefab;

        private static SpawnPlanner.Settings Settings => new SpawnPlanner.Settings
        {
            RoadLength = RoadLength,
            RoadWidth = RoadWidth,
            MinCorridor = Corridor,
            WindowMargin = Margin,
            MaxAttempts = 8
        };

        [SetUp]
        public void SetUp()
        {
            dummyPrefab = new GameObject("DummyPrefab");
            created.Add(dummyPrefab);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
            {
                Object.DestroyImmediate(item);
            }
            created.Clear();
        }

        private ObstacleDefinition Definition(string id, bool dynamic, float width, float length, float minT, float maxT,
            bool endsOnly = false, ZoneType zone = ZoneType.None)
        {
            ObstacleDefinition definition = ScriptableObject.CreateInstance<ObstacleDefinition>();
            definition.id = id;
            definition.prefab = dummyPrefab;
            definition.isDynamic = dynamic;
            definition.footprintWidth = width;
            definition.footprintLength = length;
            definition.minT = minT;
            definition.maxT = maxT;
            definition.placeAtRangeEndsOnly = endsOnly;
            definition.requiredZone = zone;
            created.Add(definition);
            return definition;
        }

        /// <summary>A mix that can genuinely block the road: wide lane closures, vans, cones, debris.</summary>
        private DifficultyProfile HardMix(float eventsPerKm)
        {
            DifficultyProfile profile = ScriptableObject.CreateInstance<DifficultyProfile>();
            profile.difficultyLevel = 2;
            profile.eventsPerKm = eventsPerKm;
            profile.definitions = new List<ObstacleDefinition>
            {
                Definition("CONE", false, 1f, 8f, -3f, 3f),
                Definition("LANE_CLOSURE", false, 3.5f, 15f, -1.75f, 1.75f, true),
                Definition("VAN", false, 2f, 5.5f, -2.5f, 2.5f),
                Definition("DEBRIS", false, 0.8f, 0.8f, -3f, 3f),
                Definition("PEDESTRIAN", true, 0.6f, 0.6f, -4.5f, 4.5f, true)
            };
            created.Add(profile);
            return profile;
        }

        private static bool SamePlan(SpawnPlan a, SpawnPlan b)
        {
            if (a.Events.Count != b.Events.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Events.Count; i++)
            {
                SpawnEvent x = a.Events[i];
                SpawnEvent y = b.Events[i];
                if (x.Definition != y.Definition || x.S != y.S || x.T != y.T || x.YawDegrees != y.YawDegrees || x.Seed != y.Seed)
                {
                    return false;
                }
            }
            return true;
        }

        [Test]
        public void SameSeedGivesIdenticalPlan()
        {
            DifficultyProfile profile = HardMix(14f);
            SpawnPlan first = new SpawnPlanner().Plan(42, profile, null, Settings);
            SpawnPlan second = new SpawnPlanner().Plan(42, profile, null, Settings);
            Assert.Greater(first.Events.Count, 0);
            Assert.IsTrue(SamePlan(first, second));
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            DifficultyProfile profile = HardMix(14f);
            SpawnPlanner planner = new SpawnPlanner();
            Assert.IsFalse(SamePlan(planner.Plan(1, profile, null, Settings), planner.Plan(2, profile, null, Settings)));
        }

        [Test]
        public void MinGapRespectedAndEventsInsideBuffers()
        {
            DifficultyProfile profile = HardMix(14f);
            for (int seed = 0; seed < 100; seed++)
            {
                SpawnPlan plan = new SpawnPlanner().Plan(seed, profile, null, Settings);
                for (int i = 0; i < plan.Events.Count; i++)
                {
                    Assert.GreaterOrEqual(plan.Events[i].S, profile.startBuffer);
                    Assert.LessOrEqual(plan.Events[i].S, RoadLength - profile.endBuffer);
                    if (i > 0)
                    {
                        Assert.GreaterOrEqual(plan.Events[i].S - plan.Events[i - 1].S, profile.minGap - 1e-3f, $"seed {seed} event {i}");
                    }
                }
            }
        }

        [Test]
        public void ThousandSeedsNeverBreakTheCorridor()
        {
            // Dense on purpose (30 per km) so obstacle windows overlap and the rule is really tested.
            DifficultyProfile profile = HardMix(30f);
            profile.minGap = 20f;
            SpawnPlanner planner = new SpawnPlanner();
            List<Footprint> statics = new List<Footprint>();
            int checkedObstacles = 0;

            for (int seed = 0; seed < PropertySeeds; seed++)
            {
                SpawnPlan plan = planner.Plan(seed, profile, null, Settings);
                statics.Clear();
                foreach (SpawnEvent spawnEvent in plan.Events)
                {
                    if (!spawnEvent.Definition.isDynamic)
                    {
                        statics.Add(spawnEvent.Footprint);
                    }
                }
                foreach (Footprint footprint in statics)
                {
                    float gap = ClearanceValidator.LargestGap(footprint, statics, RoadWidth, Margin, out _);
                    Assert.GreaterOrEqual(gap, Corridor - ClearanceValidator.Tolerance, $"seed {seed} at {footprint}");
                    checkedObstacles++;
                }
            }
            Assert.Greater(checkedObstacles, PropertySeeds * 5, "too few obstacles to be a meaningful check");
        }

        [Test]
        public void ZoneTypesOnlyInsideZones()
        {
            DifficultyProfile profile = HardMix(14f);
            ObstacleDefinition zoneOnly = Definition("ZONE_ONLY", false, 0.8f, 0.8f, -3f, -2f, false, ZoneType.BusStop);
            zoneOnly.weight = 5f;
            profile.definitions.Add(zoneOnly);
            List<RoadZone> zones = new List<RoadZone> { new RoadZone(ZoneType.BusStop, 300f, 360f) };

            int inZone = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                foreach (SpawnEvent spawnEvent in new SpawnPlanner().Plan(seed, profile, zones, Settings).Events)
                {
                    if (spawnEvent.Definition == zoneOnly)
                    {
                        Assert.IsTrue(zones[0].Contains(spawnEvent.Footprint.SMin, spawnEvent.Footprint.SMax));
                        inZone++;
                    }
                }
            }
            Assert.Greater(inZone, 0, "zone type never appeared");
        }

        [Test]
        public void EventSeedsDependOnRunSeedAndIndex()
        {
            Assert.AreEqual(SpawnPlanner.DeriveEventSeed(5, 3), SpawnPlanner.DeriveEventSeed(5, 3));
            Assert.AreNotEqual(SpawnPlanner.DeriveEventSeed(5, 3), SpawnPlanner.DeriveEventSeed(6, 3));
            Assert.AreNotEqual(SpawnPlanner.DeriveEventSeed(5, 3), SpawnPlanner.DeriveEventSeed(5, 4));
        }
    }
}
