using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BusSim.Tests
{
    /// <summary>Checks the real content built by ObstacleContentBuilder (M5 and M6 acceptance).</summary>
    public class CatalogTests
    {
        private const string ObstacleFolder = "Assets/_Project/Data/Obstacles";
        private const string DifficultyFolder = "Assets/_Project/Data/Difficulty";
        private const int ExpectedTypes = 14;
        private const int DensitySeeds = 20;
        private const float DensityTolerance = 0.2f;

        private static readonly List<RoadZone> Zones = new List<RoadZone>
        {
            new RoadZone(ZoneType.BusStop, 380f, 440f),
            new RoadZone(ZoneType.School, 600f, 800f)
        };

        private static SpawnPlanner.Settings Settings => new SpawnPlanner.Settings
        {
            RoadLength = 1000f,
            RoadWidth = 7f,
            MinCorridor = 2.5f,
            WindowMargin = 15f,
            MaxAttempts = 8
        };

        private static List<ObstacleDefinition> Definitions()
        {
            List<ObstacleDefinition> result = new List<ObstacleDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ObstacleDefinition", new[] { ObstacleFolder }))
            {
                result.Add(AssetDatabase.LoadAssetAtPath<ObstacleDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            return result;
        }

        private static DifficultyProfile Profile(string profileName)
        {
            return AssetDatabase.LoadAssetAtPath<DifficultyProfile>($"{DifficultyFolder}/{profileName}.asset");
        }

        [Test]
        public void EveryTypeHasAValidPrefab()
        {
            List<ObstacleDefinition> definitions = Definitions();
            Assert.AreEqual(ExpectedTypes, definitions.Count, "catalog size");
            int footprintLayer = LayerMask.NameToLayer("Obstacles");
            int bodyLayer = LayerMask.NameToLayer("ObstacleBody");
            Assert.GreaterOrEqual(bodyLayer, 0, "layer ObstacleBody missing");
            foreach (ObstacleDefinition definition in definitions)
            {
                GameObject prefab = definition.prefab;
                Assert.IsNotNull(prefab, definition.id);
                Assert.IsNotNull(prefab.GetComponent<ObstacleBehaviour>(), $"{definition.id} behaviour");
                Assert.IsNotNull(prefab.GetComponent<PooledPhysicsReset>(), $"{definition.id} pooled reset");
                Assert.IsTrue(prefab.CompareTag("Obstacle"), $"{definition.id} tag");

                // The footprint trigger matches the definition and sits on its own layer.
                Transform footprint = prefab.transform.Find("Footprint");
                Assert.IsNotNull(footprint, $"{definition.id} footprint child");
                BoxCollider box = footprint.GetComponent<BoxCollider>();
                Assert.IsTrue(box.isTrigger, $"{definition.id} footprint is a trigger");
                Assert.AreEqual(definition.footprintWidth, box.size.x, 0.01f, $"{definition.id} footprint width");
                Assert.AreEqual(definition.footprintLength, box.size.z, 0.01f, $"{definition.id} footprint length");
                Assert.AreEqual(footprintLayer, footprint.gameObject.layer, $"{definition.id} footprint layer");

                // Every obstacle has real solid colliders, each reporting collisions, on the body layer.
                int solids = 0;
                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger)
                    {
                        continue;
                    }
                    solids++;
                    Assert.AreEqual(bodyLayer, collider.gameObject.layer, $"{definition.id} {collider.name} layer");
                    Assert.IsNotNull(collider.GetComponentInParent<ObstacleCollisionReporter>(true), $"{definition.id} {collider.name} reporter");
                }
                Assert.Greater(solids, 0, $"{definition.id} has no solid collider");

                // Scripted movers are dynamic bodies on the root so impacts exchange momentum.
                if (definition.isDynamic)
                {
                    Rigidbody body = prefab.GetComponent<Rigidbody>();
                    Assert.IsNotNull(body, $"{definition.id} mover needs a root Rigidbody");
                    Assert.IsFalse(body.isKinematic, $"{definition.id} mover must be dynamic");
                    Assert.Greater(definition.triggerTimeToArrival, 0f, $"{definition.id} trigger");
                }
            }
        }

        [TestCase("Easy")]
        [TestCase("Normal")]
        [TestCase("Hard")]
        public void DensityMatchesProfileWithin20Percent(string profileName)
        {
            DifficultyProfile profile = Profile(profileName);
            Assert.IsNotNull(profile, profileName);
            float total = 0f;
            for (int seed = 0; seed < DensitySeeds; seed++)
            {
                total += new SpawnPlanner().Plan(seed, profile, Zones, Settings).EventsPerKm;
            }
            float mean = total / DensitySeeds;
            Debug.Log($"{profileName}: mean {mean:F2} events/km over {DensitySeeds} seeds, target {profile.eventsPerKm}");
            Assert.AreEqual(profile.eventsPerKm, mean, profile.eventsPerKm * DensityTolerance, $"{profileName} mean {mean:F2}/km");
        }

        [Test]
        public void ZoneObstaclesOnlyAppearInsideTheirZones()
        {
            DifficultyProfile profile = Profile("Hard");
            Dictionary<ZoneType, int> seen = new Dictionary<ZoneType, int> { { ZoneType.BusStop, 0 }, { ZoneType.School, 0 } };
            for (int seed = 0; seed < 300; seed++)
            {
                foreach (SpawnEvent spawnEvent in new SpawnPlanner().Plan(seed, profile, Zones, Settings).Events)
                {
                    ZoneType zone = spawnEvent.Definition.requiredZone;
                    if (zone == ZoneType.None)
                    {
                        continue;
                    }
                    bool inside = false;
                    foreach (RoadZone roadZone in Zones)
                    {
                        inside |= roadZone.type == zone && roadZone.Contains(spawnEvent.Footprint.SMin, spawnEvent.Footprint.SMax);
                    }
                    Assert.IsTrue(inside, $"seed {seed}: {spawnEvent.Definition.id} at s={spawnEvent.S:F1} outside its zone");
                    seen[zone]++;
                }
            }
            Assert.Greater(seen[ZoneType.BusStop], 0, "bus stop types never appeared");
            Assert.Greater(seen[ZoneType.School], 0, "school types never appeared");
        }

        [Test]
        public void RealCatalogNeverBreaksTheCorridor()
        {
            DifficultyProfile profile = Profile("Hard");
            List<Footprint> statics = new List<Footprint>();
            for (int seed = 0; seed < 1000; seed++)
            {
                statics.Clear();
                foreach (SpawnEvent spawnEvent in new SpawnPlanner().Plan(seed, profile, Zones, Settings).Events)
                {
                    if (!spawnEvent.Definition.isDynamic)
                    {
                        statics.Add(spawnEvent.Footprint);
                    }
                }
                foreach (Footprint footprint in statics)
                {
                    Assert.GreaterOrEqual(ClearanceValidator.LargestGap(footprint, statics, 7f, 15f, out _),
                        2.5f - ClearanceValidator.Tolerance, $"seed {seed} {footprint}");
                }
            }
        }

        [Test]
        public void EasyProfileExcludesHarderTypes()
        {
            DifficultyProfile profile = Profile("Easy");
            for (int seed = 0; seed < 50; seed++)
            {
                foreach (SpawnEvent spawnEvent in new SpawnPlanner().Plan(seed, profile, Zones, Settings).Events)
                {
                    Assert.LessOrEqual(spawnEvent.Definition.minDifficulty, 0, spawnEvent.Definition.id);
                    Assert.IsTrue(profile.allowedCategories.Contains(spawnEvent.Definition.category), spawnEvent.Definition.id);
                }
            }
        }
    }
}
