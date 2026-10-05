using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Builds every obstacle prefab, ObstacleDefinition and DifficultyProfile from code, so a fresh
    /// project (or the Phase 2 package) can regenerate all content with one menu command.
    /// Re-running updates assets in place and keeps their GUIDs. Models are procedural placeholders
    /// built from primitives; behaviour scripts do not depend on them (docs/PROJECT_SPEC.md 7).
    ///
    /// Every prefab has: a root with an ObstacleBehaviour and PooledPhysicsReset, a "Footprint"
    /// trigger child (layer Obstacles, used for awareness queries), and solid bodies on layer
    /// ObstacleBody. Scripted movers carry their Rigidbody on the root. Static types carry bodies
    /// on child objects: dynamic Rigidbodies for things a vehicle can push, plain colliders for
    /// things that cannot move (barriers, signs).
    /// </summary>
    public static class ObstacleContentBuilder
    {
        public const string ObstaclePrefabFolder = "Assets/_Project/Prefabs/Obstacles";
        public const string ObstacleDataFolder = "Assets/_Project/Data/Obstacles";
        public const string DifficultyFolder = "Assets/_Project/Data/Difficulty";
        public const string ObstacleTag = "Obstacle";
        public const string ObstacleLayer = "Obstacles";
        public const string FootprintName = "Footprint";

        private const string RoadSettingsPath = "Assets/_Project/Data/RoadSettings.asset";
        private const string BodyMaterialPath = "Assets/_Project/Materials/ObstacleBody.asset";
        private const float DefaultHalfRoadWidth = 3.5f;
        private const float DefaultFootpathWidth = 2f;
        private const float DefaultTriggerHeight = 1f;
        private const float BodyFriction = 0.6f;
        private const float BodyBounciness = 0.1f;

        // Traffic cone, 0.7 m. [Likely]
        private const float ConeHeight = 0.7f;
        private const float ConeFootSize = 0.38f;
        private const float ConeFootHeight = 0.03f;
        private const float ConeMass = 1.5f;
        private const float ConeBodySize = 0.32f;
        // Roadworks layout: fixtures must not overlap cone bodies or depenetration flings them.
        private const float BarrierInsetFromCones = 0.6f;
        private const float SignZ = -6.2f;
        private const float ConeLinearDamping = 0.3f;
        private const float ConeAngularDamping = 1.5f;

        // A fallen limb must be taller than a car's ground clearance (0.3 m) to count as an obstacle.
        private const float BranchRadius = 0.22f;
        private const float BranchMass = 30f;
        private const float BranchLength = 2.4f;
        private const float CargoSize = 0.8f;
        private const float CarFootprintPad = 0.1f;

        // The cut-in car ends up crawling in the player's lane ahead of its event position, so keep the next obstacle clear of it.
        private const float CutInClearAhead = 100f;
        private const float CarLightInset = 0.25f;
        private const float CarLightHeightShare = 0.35f;
        private const float CarLightSize = 0.2f;

        // Adult proportions in metres at scale 1 (about 1.72 m tall).
        private const float HipHeight = 0.88f;
        private const float LegLength = 0.86f;
        private const float ShoulderHeight = 1.42f;
        private const float ArmLength = 0.64f;
        private const float HeadCentre = 1.6f;
        private const float HeadSize = 0.22f;
        private const float PersonHeight = 1.75f;
        private const float PersonRadius = 0.28f;
        private const float AdultMass = 80f;

        private static Mesh coneMesh;
        private static PhysicsMaterial bodyMaterial;

        [MenuItem("BusSim/Obstacles/Build Default Content")]
        public static void BuildAll()
        {
            EditorAssetUtil.EnsureFolder(ObstaclePrefabFolder);
            EditorAssetUtil.EnsureFolder(ObstacleDataFolder);
            EditorAssetUtil.EnsureFolder(DifficultyFolder);
            DemoSceneBuilder.EnsureTagAndLayer(ObstacleTag, ObstacleLayer);
            DemoSceneBuilder.EnsureLayer(PhysicsSetup.ObstacleBodyLayer);
            DemoSceneBuilder.EnsureLayer(PhysicsSetup.VehicleLayer);
            DemoSceneBuilder.EnsureLayer(PhysicsSetup.TrafficLayer);
            coneMesh = ProceduralMeshes.SaveMesh(ProceduralMeshes.TrafficCone(ConeHeight, 0.17f, 0.025f, 0.45f, 0.7f, 20), "TrafficCone");
            bodyMaterial = LoadOrCreateBodyMaterial();

            float halfRoad = RoadHalfWidth();
            float footpathMiddle = halfRoad + FootpathWidth() * 0.5f;

            List<ObstacleDefinition> definitions = new List<ObstacleDefinition>
            {
                BuildConeCluster(),
                BuildPedestrian(new PedestrianSpec
                {
                    Id = "PED_JAYWALK_ADULT", Name = "Jaywalking adult", Model = "character-male-a", Speed = 1.4f, Scale = 1f, Mass = AdultMass, T = footpathMiddle,
                    Shirt = new Color(0.16f, 0.35f, 0.62f), Trousers = new Color(0.18f, 0.18f, 0.2f), Hair = new Color(0.08f, 0.06f, 0.05f),
                    Trigger = 3.5f, MaxBlock = 6f, Danger = 4, MinDifficulty = 0, Weight = 1f
                }),
                BuildPedestrian(new PedestrianSpec
                {
                    // 7.6 m at 0.8 m/s is 9.5 s plus a 1 s pause, so 11 s rather than the catalog's 10.
                    Id = "PED_ELDERLY", Name = "Elderly slow crosser", Model = "character-female-f", Speed = 0.8f, Scale = 0.95f, Mass = 65f, T = footpathMiddle,
                    Shirt = new Color(0.55f, 0.5f, 0.42f), Trousers = new Color(0.3f, 0.28f, 0.25f), Hair = new Color(0.82f, 0.82f, 0.8f),
                    PauseChance = 0.6f, PauseSeconds = 1f, Trigger = 4.5f, MaxBlock = 11f, Danger = 5, MinDifficulty = 1, Weight = 0.6f
                }),
                BuildRoadworks(halfRoad),
                BuildBranch(halfRoad),
                BuildCargo(halfRoad),
                BuildStalledCar(halfRoad),
                BuildVan(halfRoad),
                BuildCyclist(halfRoad),
                BuildMotorcycle(),
                BuildCutInCar(halfRoad),
                BuildPedestrian(new PedestrianSpec
                {
                    Id = "PED_CHILD_RUN", Name = "Child running across", Model = "character-male-b", Speed = 2.5f, Scale = 0.72f, Mass = 35f, T = footpathMiddle,
                    Shirt = new Color(0.9f, 0.9f, 0.92f), Trousers = new Color(0.12f, 0.2f, 0.45f), Hair = new Color(0.05f, 0.04f, 0.03f),
                    Trigger = 2.5f, MaxBlock = 4f, Danger = 5, MinDifficulty = 1, Weight = 3f, Zone = ZoneType.School
                }),
                BuildBusStopBlock(halfRoad),
                BuildPassengerRush(footpathMiddle),
                BuildPedestrian(new PedestrianSpec
                {
                    Id = "PED_JOGGER", Name = "Jogger crossing fast", Model = "character-male-f", Speed = 2.8f, Scale = 1f, Mass = AdultMass, T = footpathMiddle,
                    Shirt = new Color(0.9f, 0.3f, 0.1f), Trousers = new Color(0.1f, 0.1f, 0.12f), Hair = new Color(0.1f, 0.07f, 0.05f),
                    Trigger = 3f, MaxBlock = 4f, Danger = 4, MinDifficulty = 1, Weight = 0.8f
                }),
                BuildPedestrian(new PedestrianSpec
                {
                    Id = "PED_PHONE_USER", Name = "Distracted phone user", Model = "character-female-c", Speed = 1.2f, Scale = 0.97f, Mass = 65f, T = footpathMiddle,
                    Shirt = new Color(0.3f, 0.6f, 0.4f), Trousers = new Color(0.2f, 0.2f, 0.3f), Hair = new Color(0.15f, 0.1f, 0.06f),
                    PauseChance = 0.8f, PauseSeconds = 2f, Trigger = 3.5f, MaxBlock = 9f, Danger = 4, MinDifficulty = 1, Weight = 0.8f
                })
            };

            // Types built from models that may be missing in a stripped-down project: skipped when absent.
            AddIfBuilt(definitions, BuildPedestrianGroup(footpathMiddle));
            AddIfBuilt(definitions, BuildWheelchairUser(footpathMiddle));
            AddIfBuilt(definitions, BuildCyclistVariant("CYCLIST_FAST", "Fast cyclist", -halfRoad + 1.1f, 7f, "character-female-b", 3, 1, 0.6f, 0.7f));
            AddIfBuilt(definitions, BuildModelDebris("DEBRIS_TYRE", "Tyre in the road", "debris-tire", TyreDiameter, TyreMass, 1f, 1f, 0f, 0.8f, 2, 0.7f, halfRoad));
            AddIfBuilt(definitions, BuildModelDebris("DEBRIS_BUMPER", "Detached bumper", "debris-bumper", BumperLength, BumperMass, 1.0f, 2.0f, 30f, 0.5f, 2, 0.6f, halfRoad));
            AddIfBuilt(definitions, BuildStoppedModel("DOUBLE_PARKED_GARBAGE_TRUCK", "Garbage truck stopped at the kerb", "garbage-truck",
                new Color(0.2f, 0.45f, 0.3f), 2.4f, 7f, GarbageTruckMass, true, true, 3, 1, 0.6f, halfRoad));
            AddIfBuilt(definitions, BuildStoppedModel("AMBULANCE_STOPPED", "Ambulance stopped with lights on", "ambulance",
                new Color(0.95f, 0.95f, 0.95f), 2f, 5.5f, AmbulanceMass, false, true, 4, 1, 0.4f, halfRoad));
            AddIfBuilt(definitions, BuildStoppedModel("POLICE_STOP", "Police car at the roadside", "police",
                new Color(0.1f, 0.15f, 0.4f), 1.9f, 4.8f, 1500f, true, true, 3, 1, 0.4f, halfRoad));
            AddIfBuilt(definitions, BuildStoppedBus(halfRoad));
            AddIfBuilt(definitions, BuildPullOut("TAXI_PULLOUT", "Taxi pulling out from the kerb", "taxi", false, PullOutTaxiMass, 4, 1, 0.8f, ZoneType.None, halfRoad));
            AddIfBuilt(definitions, BuildPullOut("BUS_PULLOUT", "Bus pulling out of the stop", "", true, BusMass, 5, 2, 2f, ZoneType.BusStop, halfRoad));

            BuildProfile("Easy", 0, EasyEventsPerKm, EasyMinGap, definitions, new List<ObstacleCategory>
            {
                ObstacleCategory.TrafficControl, ObstacleCategory.Debris, ObstacleCategory.StoppedVehicle, ObstacleCategory.Pedestrian
            });
            BuildProfile("Normal", 1, NormalEventsPerKm, NormalMinGap, definitions, new List<ObstacleCategory>());
            BuildProfile("Hard", 2, HardEventsPerKm, HardMinGap, definitions, new List<ObstacleCategory>());
            AssetDatabase.SaveAssets();
            Debug.Log($"BusSim: built {definitions.Count} obstacle types and 3 difficulty profiles.");
        }

        private static void AddIfBuilt(List<ObstacleDefinition> definitions, ObstacleDefinition definition)
        {
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        // ---------------- obstacle types ----------------

        private static ObstacleDefinition BuildConeCluster()
        {
            ObstacleDefinition definition = Define("CONE_CLUSTER", "Traffic cone taper", ObstacleCategory.TrafficControl, false,
                1f, 8f, -3f, 3f, false, true, 0f, 2, 0, 1f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle));
            const int cones = 6;
            float halfWidth = definition.footprintWidth * 0.5f - ConeFootSize * 0.5f;
            float halfLength = definition.footprintLength * 0.5f;
            for (int i = 0; i < cones; i++)
            {
                // Taper: the first cone sits at the kerb side (-x), each next one steps into the lane.
                float f = (float)i / (cones - 1);
                AddCone(root.transform, $"Cone{i}", new Vector3(Mathf.Lerp(-halfWidth, halfWidth, f), 0f, Mathf.Lerp(-halfLength, halfLength, f)));
            }
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildRoadworks(float halfRoad)
        {
            float laneWidth = halfRoad;
            ObstacleDefinition definition = Define("ROADWORK_BARRIER", "Road works lane closure", ObstacleCategory.TrafficControl, false,
                laneWidth, 15f, -laneWidth * 0.5f, laneWidth * 0.5f, true, true, 0f, 3, 0, 0.5f);
            definition.minGapAfter = 10f;
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 1.5f);
            float edge = laneWidth * 0.5f - 0.2f;
            for (int i = 0; i < 5; i++)
            {
                float f = i / 4f;
                AddCone(root.transform, $"TaperCone{i}", new Vector3(Mathf.Lerp(-edge, edge, f), 0f, Mathf.Lerp(-7.3f, -3.3f, f)));
                AddCone(root.transform, $"LineCone{i}", new Vector3(edge, 0f, Mathf.Lerp(-1.5f, 7.3f, f)));
            }

            // Heavy fixtures cannot be pushed: plain colliders, no Rigidbody.
            GameObject fixtures = MakeBody(root.transform, "Fixtures", 0f);
            float acrossLength = laneWidth - 0.6f;
            AddStripedBoard(root.transform, "BarrierAcross", new Vector3(0.1f, 0f, -2.4f), acrossLength, 0f);
            AddBoxCollider(fixtures, new Vector3(0.1f, 0.5f, -2.4f), new Vector3(acrossLength, 1f, 0.4f));
            float alongX = edge - BarrierInsetFromCones;
            AddStripedBoard(root.transform, "BarrierAlong", new Vector3(alongX, 0f, 3f), 8f, 90f);
            AddBoxCollider(fixtures, new Vector3(alongX, 0.5f, 3f), new Vector3(0.4f, 1f, 8f));

            Material sign = Mat("SignOrange", new Color(1f, 0.55f, 0.05f), 0.4f);
            Material dark = Mat("DarkMetal", new Color(0.15f, 0.15f, 0.16f), 0.5f);
            GameObject signModel = ModelLibrary.Spawn(root.transform, ModelLibrary.Road("road-sign-warning"), ModelLibrary.SignScale, 180f, "WarningSign");
            if (signModel != null)
            {
                signModel.transform.localPosition = new Vector3(-edge, 0f, SignZ);
            }
            else
            {
                AddBox(root.transform, "SignPost", dark, new Vector3(-edge, 0.8f, SignZ), new Vector3(0.06f, 1.6f, 0.06f));
                AddBox(root.transform, "SignBoard", sign, new Vector3(-edge, 1.75f, SignZ - 0.03f), new Vector3(0.9f, 0.9f, 0.04f));
                AddBox(root.transform, "SignSymbol", dark, new Vector3(-edge, 1.75f, SignZ - 0.06f), new Vector3(0.35f, 0.35f, 0.02f));
            }
            AddBoxCollider(fixtures, new Vector3(-edge, 1f, SignZ), new Vector3(0.9f, 2.2f, 0.2f));

            GameObject worker = AddPerson(root.transform, "character-male-c", 1f, 140f);
            if (worker != null)
            {
                worker.transform.localPosition = new Vector3(-0.5f, 0f, 3f);
            }
            else
            {
                HumanParts workerParts = BuildHuman(root.transform, new Vector3(-0.5f, 0f, 3f), 1f,
                    Mat("HiVis", new Color(0.95f, 0.85f, 0.05f), 0.3f), Mat("WorkTrousers", new Color(0.12f, 0.14f, 0.2f), 0.2f),
                    Mat("Helmet", new Color(1f, 1f, 1f), 0.6f));
                workerParts.Body.localRotation = Quaternion.Euler(0f, 140f, 0f);
            }
            AddCapsuleCollider(fixtures, new Vector3(-0.5f, PersonHeight * 0.5f, 3f), PersonRadius, PersonHeight);
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildBranch(float halfRoad)
        {
            // Footprint covers the branch at up to 25 degrees of yaw (2.5 m long, about 1.2 m wide).
            ObstacleDefinition definition = Define("DEBRIS_BRANCH", "Fallen branch", ObstacleCategory.Debris, false,
                1.2f, 2.5f, -halfRoad + 0.6f, halfRoad - 0.6f, false, false, 25f, 2, 0, 0.7f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 0.6f);
            GameObject body = MakeBody(root.transform, "Body", BranchMass);
            GameObject log = ModelLibrary.SpawnFitted(body.transform, ModelLibrary.Nature + "log.fbx", BranchLength, 0f, "Log", true);
            if (log != null)
            {
                ModelLibrary.AddFittedBoxCollider(body, log.transform, bodyMaterial);
                return Finish(definition, root);
            }

            Material bark = Mat("Bark", new Color(0.33f, 0.22f, 0.13f), 0.1f);
            Material leaves = Mat("Leaves", new Color(0.18f, 0.38f, 0.12f), 0.1f);
            AddPrimitive(PrimitiveType.Cylinder, body.transform, "Limb", bark, new Vector3(0f, BranchRadius, 0f), new Vector3(BranchRadius * 2f, 1.15f, BranchRadius * 2f), Quaternion.Euler(90f, 0f, 0f));
            AddPrimitive(PrimitiveType.Cylinder, body.transform, "Twig1", bark, new Vector3(0.22f, 0.08f, 0.5f), new Vector3(0.07f, 0.35f, 0.07f), Quaternion.Euler(90f, 40f, 0f));
            AddPrimitive(PrimitiveType.Cylinder, body.transform, "Twig2", bark, new Vector3(-0.2f, 0.08f, -0.3f), new Vector3(0.06f, 0.3f, 0.06f), Quaternion.Euler(90f, -35f, 0f));
            AddPrimitive(PrimitiveType.Sphere, body.transform, "Leaves1", leaves, new Vector3(0.1f, 0.22f, 1.0f), new Vector3(0.7f, 0.4f, 0.6f), Quaternion.identity);
            AddPrimitive(PrimitiveType.Sphere, body.transform, "Leaves2", leaves, new Vector3(0.4f, 0.18f, 0.6f), new Vector3(0.45f, 0.3f, 0.5f), Quaternion.identity);
            AddPrimitive(PrimitiveType.Sphere, body.transform, "Leaves3", leaves, new Vector3(-0.35f, 0.15f, -0.4f), new Vector3(0.4f, 0.28f, 0.45f), Quaternion.identity);
            AddBoxCollider(body, new Vector3(0f, BranchRadius, 0.1f), new Vector3(BranchRadius * 2f, BranchRadius * 2f, 2.4f));
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildCargo(float halfRoad)
        {
            // 0.8 m cargo at up to 45 degrees of yaw needs about 1.15 m square.
            ObstacleDefinition definition = Define("DEBRIS_CARGO", "Fallen cargo", ObstacleCategory.Debris, false,
                1.15f, 1.15f, -halfRoad + 0.6f, halfRoad - 0.6f, false, false, 45f, 2, 0, 0.7f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 0.8f);
            GameObject body = MakeBody(root.transform, "Body", 15f);
            GameObject crate = ModelLibrary.SpawnFitted(body.transform, ModelLibrary.Car("box"), CargoSize, 0f, "Crate", false);
            if (crate != null)
            {
                ModelLibrary.AddFittedBoxCollider(body, crate.transform, bodyMaterial);
                return Finish(definition, root);
            }

            Material wood = Mat("CrateWood", new Color(0.6f, 0.45f, 0.26f), 0.15f);
            Material strap = Mat("Strap", new Color(0.2f, 0.2f, 0.22f), 0.3f);
            AddBox(body.transform, "Crate", wood, new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.6f, 0.6f));
            AddBox(body.transform, "Strap", strap, new Vector3(0f, 0.3f, 0f), new Vector3(0.77f, 0.62f, 0.06f));
            AddBox(body.transform, "Box", Mat("Cardboard", new Color(0.7f, 0.58f, 0.4f), 0.05f), new Vector3(0.2f, 0.12f, 0.38f), new Vector3(0.35f, 0.24f, 0.28f));
            AddBoxCollider(body, new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.6f, 0.6f));
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildStalledCar(float halfRoad)
        {
            float lane = halfRoad * 0.5f;
            Bounds size = CarBounds("suv", 1.8f, 4.5f);
            ObstacleDefinition definition = Define("STALLED_CAR", "Breakdown with hazards", ObstacleCategory.StoppedVehicle, false,
                size.size.x + CarFootprintPad, size.size.z + CarFootprintPad, -lane, lane, true, false, 0f, 3, 0, 0.6f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 1.5f);
            GameObject body = MakeParkedBody(root.transform, "Body", 1300f);
            CarVisual car = AddCarVisual(body.transform, "suv", 1.8f, 4.5f, new Color(0.55f, 0.56f, 0.58f), "Silver");
            AddBoxCollider(body, car.Bounds.center, car.Bounds.size);
            AddBlinker(root, car.Hazards);

            // Warning triangle 30 m behind the car (visual only, outside the footprint).
            Material red = Mat("TriangleRed", new Color(0.85f, 0.05f, 0.05f), 0.6f);
            Transform triangle = new GameObject("WarningTriangle").transform;
            triangle.SetParent(root.transform, false);
            triangle.localPosition = new Vector3(0f, 0f, -30f);
            AddBox(triangle, "Left", red, new Vector3(-0.11f, 0.19f, 0f), new Vector3(0.04f, 0.42f, 0.03f)).transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
            AddBox(triangle, "Right", red, new Vector3(0.11f, 0.19f, 0f), new Vector3(0.04f, 0.42f, 0.03f)).transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            AddBox(triangle, "Base", red, new Vector3(0f, 0.02f, 0f), new Vector3(0.44f, 0.04f, 0.03f));
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildVan(float halfRoad)
        {
            Bounds size = CarBounds("delivery", 2f, 5.5f);
            // Double parked against the left kerb, 0.2 m off it.
            float t = -halfRoad + size.size.x * 0.5f + 0.2f;
            ObstacleDefinition definition = Define("DOUBLE_PARKED_VAN", "Delivery van double parked", ObstacleCategory.StoppedVehicle, false,
                size.size.x + CarFootprintPad, size.size.z + CarFootprintPad, t, t, false, false, 0f, 3, 1, 0.8f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 2.4f);
            GameObject body = MakeParkedBody(root.transform, "Body", 2200f);
            CarVisual van = AddCarVisual(body.transform, "delivery", 2f, 5.5f, new Color(0.93f, 0.93f, 0.92f), "VanWhite");
            AddBoxCollider(body, van.Bounds.center, van.Bounds.size);
            AddBlinker(root, van.Hazards);
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildCyclist(float halfRoad)
        {
            // Rides within 0.8 m of the left kerb.
            float t = -halfRoad + 0.35f + 0.2f;
            ObstacleDefinition definition = Define("CYCLIST_EDGE", "Cyclist or PMD rider", ObstacleCategory.Cyclist, true,
                0.7f, 1.8f, t, t, false, false, 0f, 3, 1, 0.8f);
            definition.triggerTimeToArrival = 8f;
            definition.maxBlockSeconds = 3f;
            GameObject root = CreateRoot(definition, typeof(CyclistRide), 1.8f);

            // Real bicycle (Quaternius, about 1.8 m long) with a seated rider character.
            GameObject bike = ModelLibrary.SpawnFitted(root.transform, ModelLibrary.Transport + "Bicycle.fbx", BikeLength, 0f, "Bicycle", true);
            if (bike != null)
            {
                GameObject rider = AddPerson(root.transform, "character-male-e", RiderScale, 0f, CharacterAnimation.RiderController());
                if (rider != null)
                {
                    rider.transform.localPosition = new Vector3(0f, RiderHeightAboveGround, RiderOffsetZ);
                }
            }
            else
            {
                BuildProceduralCyclist(root);
            }

            MakeMoverBody(root, 95f);
            AddBoxCollider(root, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 1.7f, 1.7f));
            return Finish(definition, root);
        }

        private static void BuildProceduralCyclist(GameObject root)
        {
            Material frame = Mat("BikeFrame", new Color(0.1f, 0.45f, 0.25f), 0.5f);
            Material tyre = Mat("Rubber", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            Transform front = AddWheel(root.transform, "FrontWheel", tyre, new Vector3(0f, 0.34f, 0.52f), 0.34f, 0.05f);
            Transform rear = AddWheel(root.transform, "RearWheel", tyre, new Vector3(0f, 0.34f, -0.52f), 0.34f, 0.05f);
            AddBox(root.transform, "TopTube", frame, new Vector3(0f, 0.72f, 0f), new Vector3(0.04f, 0.04f, 0.8f));
            AddBox(root.transform, "DownTube", frame, new Vector3(0f, 0.52f, 0.1f), new Vector3(0.04f, 0.04f, 0.9f)).transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            AddBox(root.transform, "Handlebar", frame, new Vector3(0f, 0.98f, 0.45f), new Vector3(0.5f, 0.03f, 0.03f));
            HumanParts rider = BuildHuman(root.transform, new Vector3(0f, 0.12f, -0.05f), 0.95f,
                Mat("RiderShirt", new Color(0.85f, 0.25f, 0.1f), 0.3f), Mat("RiderShorts", new Color(0.1f, 0.1f, 0.12f), 0.2f),
                Mat("BikeHelmet", new Color(0.95f, 0.95f, 0.95f), 0.6f));
            rider.Body.localRotation = Quaternion.Euler(18f, 0f, 0f);
            rider.LegLeft.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            rider.LegRight.localRotation = Quaternion.Euler(10f, 0f, 0f);
            rider.ArmLeft.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            rider.ArmRight.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            SetRef(root.GetComponent<CyclistRide>(), "frontWheel", front);
            SetRef(root.GetComponent<CyclistRide>(), "rearWheel", rear);
        }

        private static ObstacleDefinition BuildMotorcycle()
        {
            ObstacleDefinition definition = Define("MOTORCYCLE_FILTER", "Motorcycle lane filtering", ObstacleCategory.MovingVehicle, true,
                0.8f, 2.1f, 0.3f, 0.3f, false, false, 0f, 3, 2, 0.6f);
            definition.triggerTimeToArrival = 3f;
            definition.maxBlockSeconds = 0f;
            GameObject root = CreateRoot(definition, typeof(MotorcycleFilter), 1.6f);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Material paint = Mat("MotoPaint", new Color(0.1f, 0.15f, 0.45f), 0.6f);
            Material tyre = Mat("Rubber", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            AddWheel(visual, "FrontWheel", tyre, new Vector3(0f, 0.3f, 0.72f), 0.3f, 0.12f);
            AddWheel(visual, "RearWheel", tyre, new Vector3(0f, 0.3f, -0.72f), 0.3f, 0.14f);
            AddBox(visual, "Body", paint, new Vector3(0f, 0.6f, 0f), new Vector3(0.36f, 0.4f, 1.3f));
            AddBox(visual, "Tank", paint, new Vector3(0f, 0.85f, 0.25f), new Vector3(0.3f, 0.2f, 0.5f));
            AddBox(visual, "Seat", Mat("Seat", new Color(0.05f, 0.05f, 0.05f), 0.2f), new Vector3(0f, 0.86f, -0.3f), new Vector3(0.3f, 0.1f, 0.6f));
            AddBox(visual, "Bars", Mat("Chrome", new Color(0.7f, 0.7f, 0.72f), 0.9f), new Vector3(0f, 1.05f, 0.55f), new Vector3(0.7f, 0.04f, 0.04f));
            HumanParts rider = BuildHuman(visual, new Vector3(0f, 0.15f, -0.2f), 1f,
                Mat("RiderJacket", new Color(0.15f, 0.15f, 0.15f), 0.3f), Mat("RiderJeans", new Color(0.15f, 0.2f, 0.35f), 0.2f),
                Mat("MotoHelmet", new Color(0.9f, 0.1f, 0.1f), 0.8f));
            rider.Body.localRotation = Quaternion.Euler(20f, 0f, 0f);
            rider.LegLeft.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            rider.LegRight.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            rider.ArmLeft.localRotation = Quaternion.Euler(-65f, 0f, 0f);
            rider.ArmRight.localRotation = Quaternion.Euler(-65f, 0f, 0f);
            SetRef(root.GetComponent<MotorcycleFilter>(), "visual", visual.gameObject);
            MakeMoverBody(root, 220f);
            AddBoxCollider(root, new Vector3(0f, 0.7f, 0f), new Vector3(0.6f, 1.4f, 2f));
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildCutInCar(float halfRoad)
        {
            float lane = halfRoad * 0.5f;
            Bounds size = CarBounds("hatchback-sports", 1.8f, 4.5f);
            ObstacleDefinition definition = Define("CAR_CUTIN", "Car cutting in and braking", ObstacleCategory.MovingVehicle, true,
                size.size.x + CarFootprintPad, size.size.z + CarFootprintPad, lane, lane, false, false, 0f, 4, 2, 0.6f);
            definition.triggerTimeToArrival = 4f;
            definition.maxBlockSeconds = 8f;
            definition.minGapAfter = CutInClearAhead;
            GameObject root = CreateRoot(definition, typeof(CarCutIn), 1.5f);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            CarVisual car = AddCarVisual(visual, "hatchback-sports", 1.8f, 4.5f, new Color(0.1f, 0.25f, 0.55f), "Blue");
            CarCutIn behaviour = root.GetComponent<CarCutIn>();
            SetRef(behaviour, "visual", visual.gameObject);
            SetRefs(behaviour, "brakeLights", car.BrakeLights.ToArray());
            MakeMoverBody(root, 1400f);
            AddBoxCollider(root, car.Bounds.center, car.Bounds.size);
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildBusStopBlock(float halfRoad)
        {
            Bounds size = CarBounds("taxi", 1.8f, 4.6f);
            float t = -halfRoad + size.size.x * 0.5f + 0.2f;
            ObstacleDefinition definition = Define("BUSSTOP_BLOCK", "Vehicle blocking bus stop bay", ObstacleCategory.StoppedVehicle, false,
                size.size.x + CarFootprintPad, size.size.z + CarFootprintPad, t, t, false, false, 0f, 2, 0, 3f);
            definition.requiredZone = ZoneType.BusStop;
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 1.5f);
            GameObject body = MakeParkedBody(root.transform, "Body", 1300f);
            CarVisual car = AddCarVisual(body.transform, "taxi", 1.8f, 4.6f, new Color(0.9f, 0.75f, 0.1f), "Taxi");
            AddBoxCollider(body, car.Bounds.center, car.Bounds.size);
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildPassengerRush(float footpathMiddle)
        {
            ObstacleDefinition definition = Define("BUSSTOP_RUSH", "Passenger rushing to the bus", ObstacleCategory.Pedestrian, true,
                0.6f, 0.6f, -footpathMiddle, -footpathMiddle, false, false, 0f, 4, 1, 3f);
            definition.requiredZone = ZoneType.BusStop;
            definition.triggerTimeToArrival = 4f;
            definition.maxBlockSeconds = 4f;
            GameObject root = CreateRoot(definition, typeof(PassengerRush), PersonHeight);
            AttachPerson(root, "character-female-d", 1f, new Color(0.8f, 0.15f, 0.35f), new Color(0.12f, 0.12f, 0.14f), new Color(0.1f, 0.07f, 0.05f), "Rush");
            MakeMoverBody(root, AdultMass);
            AddCapsuleCollider(root, new Vector3(0f, PersonHeight * 0.5f, 0f), PersonRadius, PersonHeight);
            return Finish(definition, root);
        }

        // ---------------- extra obstacle types ----------------

        private const float TyreDiameter = 0.75f;
        private const float BumperLength = 1.7f;
        private const float TyreMass = 12f;
        private const float BumperMass = 10f;
        private const float GarbageTruckMass = 7000f;
        private const float AmbulanceMass = 2800f;
        private const float BusMass = 9000f;
        private const float BusLength = 11f;
        private const float BusWidth = 2.55f;
        private const float BusHeight = 3.1f;
        // The model's nose points along +X or -X; turn it so the front faces +Z (direction of travel).
        private const float BusYaw = -90f;
        private const float PullOutTaxiMass = 1400f;
        private const float GroupSpacing = 0.85f;
        private const float WheelchairSpeed = 0.9f;
        private const float WheelchairHeight = 1.3f;
        private const float PullOutClearAhead = 90f;
        private const float WaitingTime = 4f;

        /// <summary>A loose car part lying in the road, taken from the car kit. Skipped if the model is missing.</summary>
        private static ObstacleDefinition BuildModelDebris(string id, string displayName, string model, float longestSide, float mass,
            float footprintWidth, float footprintLength, float yawJitter, float height, int danger, float weight, float halfRoad)
        {
            string path = ModelLibrary.Car(model);
            if (!ModelLibrary.Exists(path))
            {
                return null;
            }
            ObstacleDefinition definition = Define(id, displayName, ObstacleCategory.Debris, false,
                footprintWidth, footprintLength, -halfRoad + 0.6f, halfRoad - 0.6f, false, false, yawJitter, danger, 0, weight);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), height);
            GameObject body = MakeBody(root.transform, "Body", mass);
            GameObject part = ModelLibrary.SpawnFitted(body.transform, path, longestSide, 0f, "Part", true);
            ModelLibrary.AddFittedBoxCollider(body, part.transform, bodyMaterial);
            return Finish(definition, root);
        }

        /// <summary>A stopped or parked vehicle from the car kit. Kerbside ones sit against the left kerb, the others fill the lane edge.</summary>
        private static ObstacleDefinition BuildStoppedModel(string id, string displayName, string model, Color fallback, float fallbackWidth, float fallbackLength,
            float mass, bool kerbside, bool blinking, int danger, int minDifficulty, float weight, float halfRoad)
        {
            string path = ModelLibrary.Car(model);
            if (!ModelLibrary.Exists(path))
            {
                return null;
            }
            Bounds size = CarBounds(model, fallbackWidth, fallbackLength);
            float lane = halfRoad * 0.5f;
            float kerbT = -halfRoad + size.size.x * 0.5f + 0.2f;
            ObstacleDefinition definition = kerbside
                ? Define(id, displayName, ObstacleCategory.StoppedVehicle, false, size.size.x + CarFootprintPad, size.size.z + CarFootprintPad,
                    kerbT, kerbT, false, false, 0f, danger, minDifficulty, weight)
                : Define(id, displayName, ObstacleCategory.StoppedVehicle, false, size.size.x + CarFootprintPad, size.size.z + CarFootprintPad,
                    -lane, lane, true, false, 0f, danger, minDifficulty, weight);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), size.size.y + 0.2f);
            GameObject body = MakeParkedBody(root.transform, "Body", mass);
            CarVisual car = AddCarVisual(body.transform, model, fallbackWidth, fallbackLength, fallback, id);
            AddBoxCollider(body, car.Bounds.center, car.Bounds.size);
            if (blinking)
            {
                AddBlinker(root, car.Hazards);
            }
            return Finish(definition, root);
        }

        /// <summary>
        /// The bus model stretched to a real bus: the Quaternius model is a stubby 4 x 1.7 x 1.6 m toy, so it is scaled
        /// unevenly to BusLength x BusWidth x BusHeight, turned so its long side runs along Z, and painted (the
        /// FBX materials import as flat grey).
        /// </summary>
        private static GameObject SpawnBus(Transform parent, string path, string objectName)
        {
            Bounds natural = ModelLibrary.Measure(path, Vector3.one, 0f);
            bool longAlongX = natural.size.x >= natural.size.z;
            float longSide = longAlongX ? natural.size.x : natural.size.z;
            float shortSide = longAlongX ? natural.size.z : natural.size.x;
            Vector3 scale = longAlongX
                ? new Vector3(BusLength / longSide, BusHeight / natural.size.y, BusWidth / shortSide)
                : new Vector3(BusWidth / shortSide, BusHeight / natural.size.y, BusLength / longSide);
            GameObject bus = ModelLibrary.Spawn(parent, path, scale, longAlongX ? BusYaw : 0f, objectName);
            if (bus != null)
            {
                PaintBus(bus);
            }
            return bus;
        }

        private static void PaintBus(GameObject bus)
        {
            Material body = Mat("BusWhite", new Color(0.93f, 0.93f, 0.94f), 0.5f);
            Material lower = Mat("BusRed", new Color(0.78f, 0.1f, 0.14f), 0.45f);
            Material glass = Mat("BusGlass", new Color(0.18f, 0.28f, 0.36f), 0.85f);
            Material dark = Mat("BusDark", new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Material lamp = Mat("BusLamp", new Color(1f, 0.85f, 0.5f), 0.8f);
            foreach (Renderer renderer in bus.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                    materials[i] = materialName switch
                    {
                        "top" => body,
                        "bottom" => lower,
                        "windows" => glass,
                        "lights" => lamp,
                        _ => dark
                    };
                }
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>Size of the painted, stretched bus (width in x, length in z).</summary>
        private static Bounds BusBounds(string path)
        {
            GameObject probe = new GameObject("BusProbe");
            try
            {
                GameObject bus = SpawnBus(probe.transform, path, "Bus");
                return ModelLibrary.LocalBounds(bus.transform, bus.transform, null);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>A bus standing at the kerb in a bus stop zone (Quaternius model).</summary>
        private static ObstacleDefinition BuildStoppedBus(float halfRoad)
        {
            string path = ModelLibrary.Transport + "Bus.fbx";
            if (!ModelLibrary.Exists(path))
            {
                return null;
            }
            float width = BusBounds(path).size.x;
            float kerbT = -halfRoad + width * 0.5f + 0.2f;
            ObstacleDefinition definition = Define("BUSSTOP_BUS", "Bus standing at the stop", ObstacleCategory.StoppedVehicle, false,
                width + CarFootprintPad, BusLength + CarFootprintPad, kerbT, kerbT, false, false, 0f, 2, 0, 2.5f);
            definition.requiredZone = ZoneType.BusStop;
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 3.2f);
            GameObject body = MakeParkedBody(root.transform, "Body", BusMass);
            GameObject bus = SpawnBus(body.transform, path, "Bus");
            ModelLibrary.AddFittedBoxCollider(body, bus.transform, bodyMaterial);
            return Finish(definition, root);
        }

        /// <summary>A vehicle parked at the kerb that signals and pulls out ahead of the approaching car.</summary>
        private static ObstacleDefinition BuildPullOut(string id, string displayName, string carModel, bool bus, float mass,
            int danger, int minDifficulty, float weight, ZoneType zone, float halfRoad)
        {
            Bounds size;
            string busPath = ModelLibrary.Transport + "Bus.fbx";
            if (bus)
            {
                if (!ModelLibrary.Exists(busPath))
                {
                    return null;
                }
                size = BusBounds(busPath);
            }
            else
            {
                if (!ModelLibrary.Exists(ModelLibrary.Car(carModel)))
                {
                    return null;
                }
                size = CarBounds(carModel, 1.8f, 4.6f);
            }

            float kerbT = -halfRoad + size.size.x * 0.5f + 0.2f;
            ObstacleDefinition definition = Define(id, displayName, ObstacleCategory.MovingVehicle, true,
                size.size.x + CarFootprintPad, size.size.z + CarFootprintPad, kerbT, kerbT, false, false, 0f, danger, minDifficulty, weight);
            definition.requiredZone = zone;
            definition.triggerTimeToArrival = WaitingTime;
            definition.maxBlockSeconds = 10f;
            definition.minGapAfter = PullOutClearAhead;
            GameObject root = CreateRoot(definition, typeof(VehiclePullOut), size.size.y + 0.2f);

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            CarVisual car;
            if (bus)
            {
                GameObject model = SpawnBus(visual, busPath, "Bus");
                car = AddLights(visual, ModelLibrary.LocalBounds(model.transform, visual, null));
            }
            else
            {
                car = AddCarVisual(visual, carModel, 1.8f, 4.6f, new Color(0.9f, 0.75f, 0.1f), id);
            }
            SetRefs(root.GetComponent<VehiclePullOut>(), "indicators", car.Hazards.ToArray());
            MakeMoverBody(root, mass);
            AddBoxCollider(root, car.Bounds.center, car.Bounds.size);
            return Finish(definition, root);
        }

        /// <summary>A small group crossing together, spread along the road.</summary>
        private static ObstacleDefinition BuildPedestrianGroup(float footpathMiddle)
        {
            string[] models = { "character-male-a", "character-female-c", "character-male-d" };
            float[] sizes = { 1f, 0.95f, 0.88f };
            float groupLength = GroupSpacing * (models.Length - 1) + 0.6f;
            ObstacleDefinition definition = Define("PED_GROUP", "Group crossing together", ObstacleCategory.Pedestrian, true,
                groupLength, 0.6f, -footpathMiddle, footpathMiddle, true, false, 0f, 5, 1, 0.7f);
            definition.triggerTimeToArrival = 4f;
            definition.maxBlockSeconds = 7f;
            GameObject root = CreateRoot(definition, typeof(PedestrianCrossing), PersonHeight);
            List<Animator> animators = new List<Animator>();
            for (int i = 0; i < models.Length; i++)
            {
                GameObject person = AddPerson(root.transform, models[i], sizes[i], 0f);
                if (person == null)
                {
                    continue;
                }
                float x = (i - (models.Length - 1) * 0.5f) * GroupSpacing;
                person.transform.localPosition = new Vector3(x, 0f, 0f);
                animators.Add(person.GetComponentInChildren<Animator>());
            }
            if (animators.Count == 0)
            {
                Object.DestroyImmediate(root);
                return null;
            }
            AnimatorWalkRig rig = root.AddComponent<AnimatorWalkRig>();
            rig.SetAnimators(animators.ToArray());
            root.GetComponent<PedestrianCrossing>().SetMovement(1.3f, 0f, 0f);
            MakeMoverBody(root, AdultMass * models.Length);
            for (int i = 0; i < models.Length; i++)
            {
                float x = (i - (models.Length - 1) * 0.5f) * GroupSpacing;
                float height = PersonHeight * sizes[i];
                AddCapsuleCollider(root, new Vector3(x, height * 0.5f, 0f), PersonRadius * sizes[i], height);
            }
            return Finish(definition, root);
        }

        /// <summary>A wheelchair user crossing slowly: seated character on a wheelchair model.</summary>
        private static ObstacleDefinition BuildWheelchairUser(float footpathMiddle)
        {
            string chairPath = ModelLibrary.Person("wheelchair");
            if (!ModelLibrary.Exists(chairPath) || !ModelLibrary.Exists(ModelLibrary.Person("character-female-a")))
            {
                return null;
            }
            ObstacleDefinition definition = Define("PED_WHEELCHAIR", "Wheelchair user crossing", ObstacleCategory.Pedestrian, true,
                0.7f, 0.7f, -footpathMiddle, footpathMiddle, true, false, 0f, 5, 1, 0.4f);
            definition.triggerTimeToArrival = 5f;
            definition.maxBlockSeconds = 12f;
            GameObject root = CreateRoot(definition, typeof(PedestrianCrossing), WheelchairHeight);
            GameObject person = AddPerson(root.transform, "character-female-a", 1f, 0f, CharacterAnimation.SeatedController());
            GameObject chair = ModelLibrary.Spawn(root.transform, chairPath, ModelLibrary.CharacterScale, 0f, "Wheelchair");
            if (person == null || chair == null)
            {
                Object.DestroyImmediate(root);
                return null;
            }
            AnimatorWalkRig rig = root.AddComponent<AnimatorWalkRig>();
            rig.SetAnimator(person.GetComponentInChildren<Animator>());
            root.GetComponent<PedestrianCrossing>().SetMovement(WheelchairSpeed, 0f, 0f);
            MakeMoverBody(root, AdultMass + 25f);
            AddCapsuleCollider(root, new Vector3(0f, WheelchairHeight * 0.5f, 0f), PersonRadius, WheelchairHeight);
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildCyclistVariant(string id, string displayName, float t, float rideSpeed, string riderModel,
            int danger, int minDifficulty, float weight, float swerveChance)
        {
            ObstacleDefinition definition = Define(id, displayName, ObstacleCategory.Cyclist, true,
                0.7f, 1.8f, t, t, false, false, 0f, danger, minDifficulty, weight);
            definition.triggerTimeToArrival = 8f;
            definition.maxBlockSeconds = 3f;
            GameObject root = CreateRoot(definition, typeof(CyclistRide), 1.8f);
            GameObject bike = ModelLibrary.SpawnFitted(root.transform, ModelLibrary.Transport + "Bicycle.fbx", BikeLength, 0f, "Bicycle", true);
            if (bike != null)
            {
                GameObject rider = AddPerson(root.transform, riderModel, RiderScale, 0f, CharacterAnimation.RiderController());
                if (rider != null)
                {
                    rider.transform.localPosition = new Vector3(0f, RiderHeightAboveGround, RiderOffsetZ);
                }
            }
            else
            {
                BuildProceduralCyclist(root);
            }
            CyclistRide ride = root.GetComponent<CyclistRide>();
            SetFloat(ride, "rideSpeed", rideSpeed);
            SetFloat(ride, "swerveChance", swerveChance);
            MakeMoverBody(root, 95f);
            AddBoxCollider(root, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 1.7f, 1.7f));
            return Finish(definition, root);
        }

        private static void SetFloat(Object target, string field, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal struct PedestrianSpec
        {
            public string Id;
            public string Name;
            public string Model;
            public float Speed;
            public float Scale;
            public float Mass;
            public float T;
            public Color Shirt;
            public Color Trousers;
            public Color Hair;
            public float PauseChance;
            public float PauseSeconds;
            public float Trigger;
            public float MaxBlock;
            public int Danger;
            public int MinDifficulty;
            public float Weight;
            public ZoneType Zone;
        }

        /// <summary>Pedestrian waiting mid-footpath on either side; crosses the whole carriageway.</summary>
        private static ObstacleDefinition BuildPedestrian(PedestrianSpec spec)
        {
            ObstacleDefinition definition = Define(spec.Id, spec.Name, ObstacleCategory.Pedestrian, true,
                0.6f, 0.6f, -spec.T, spec.T, true, false, 0f, spec.Danger, spec.MinDifficulty, spec.Weight);
            definition.requiredZone = spec.Zone;
            definition.triggerTimeToArrival = spec.Trigger;
            definition.maxBlockSeconds = spec.MaxBlock;
            GameObject root = CreateRoot(definition, typeof(PedestrianCrossing), PersonHeight * spec.Scale);
            AttachPerson(root, spec.Model, spec.Scale, spec.Shirt, spec.Trousers, spec.Hair, spec.Id);
            root.GetComponent<PedestrianCrossing>().SetMovement(spec.Speed, spec.PauseChance, spec.PauseSeconds);
            MakeMoverBody(root, spec.Mass);
            float height = PersonHeight * spec.Scale;
            AddCapsuleCollider(root, new Vector3(0f, height * 0.5f, 0f), PersonRadius * spec.Scale, height);
            return Finish(definition, root);
        }

        // Events per kilometre and the closest two events may be, per level. Hard also favours the moving hazards.
        private const float EasyEventsPerKm = 6f;
        private const float NormalEventsPerKm = 12f;
        private const float HardEventsPerKm = 22f;
        private const float EasyMinGap = 40f;
        private const float NormalMinGap = 28f;
        private const float HardMinGap = 18f;
        private const float HardMovingWeightBoost = 1.6f;

        private static void BuildProfile(string profileName, int level, float eventsPerKm, float minGap, List<ObstacleDefinition> definitions, List<ObstacleCategory> categories)
        {
            DifficultyProfile profile = EditorAssetUtil.LoadOrCreate<DifficultyProfile>($"{DifficultyFolder}/{profileName}.asset");
            profile.profileName = profileName;
            profile.difficultyLevel = level;
            profile.eventsPerKm = eventsPerKm;
            profile.minGap = minGap;
            profile.definitions = new List<ObstacleDefinition>(definitions);
            profile.allowedCategories = categories;
            profile.weightOverrides = new List<DifficultyProfile.WeightOverride>();
            if (level >= 2)
            {
                foreach (ObstacleDefinition definition in definitions)
                {
                    if (definition.isDynamic)
                    {
                        profile.weightOverrides.Add(new DifficultyProfile.WeightOverride
                        {
                            definition = definition,
                            weight = definition.weight * HardMovingWeightBoost
                        });
                    }
                }
            }
            EditorUtility.SetDirty(profile);
        }

        // ---------------- model pieces ----------------

        internal struct HumanParts
        {
            public Transform Body;
            public Transform LegLeft;
            public Transform LegRight;
            public Transform ArmLeft;
            public Transform ArmRight;
        }

        internal struct CarParts
        {
            public List<Renderer> Hazards;
            public List<Renderer> BrakeLights;
        }

        internal static HumanParts BuildHuman(Transform parent, Vector3 offset, float scale, Material shirt, Material trousers, Material hair)
        {
            Material skin = Mat("Skin", new Color(0.78f, 0.6f, 0.47f), 0.3f);
            Material shoes = Mat("Shoes", new Color(0.1f, 0.1f, 0.1f), 0.4f);
            Transform body = new GameObject("Body").transform;
            body.SetParent(parent, false);
            body.localPosition = offset;
            body.localScale = Vector3.one * scale;

            AddPrimitive(PrimitiveType.Capsule, body, "Torso", shirt, new Vector3(0f, 1.17f, 0f), new Vector3(0.38f, 0.32f, 0.24f), Quaternion.identity);
            AddPrimitive(PrimitiveType.Sphere, body, "Head", skin, new Vector3(0f, HeadCentre, 0f), Vector3.one * HeadSize, Quaternion.identity);
            AddPrimitive(PrimitiveType.Sphere, body, "Hair", hair, new Vector3(0f, HeadCentre + 0.035f, -0.015f), Vector3.one * (HeadSize + 0.012f), Quaternion.identity);

            HumanParts parts = new HumanParts
            {
                Body = body,
                LegLeft = AddLimb(body, "LegLeft", new Vector3(-0.1f, HipHeight, 0f), LegLength, 0.15f, trousers),
                LegRight = AddLimb(body, "LegRight", new Vector3(0.1f, HipHeight, 0f), LegLength, 0.15f, trousers),
                ArmLeft = AddLimb(body, "ArmLeft", new Vector3(-0.25f, ShoulderHeight, 0f), ArmLength, 0.1f, shirt),
                ArmRight = AddLimb(body, "ArmRight", new Vector3(0.25f, ShoulderHeight, 0f), ArmLength, 0.1f, shirt)
            };
            AddBox(parts.LegLeft, "ShoeLeft", shoes, new Vector3(0f, -LegLength, 0.04f), new Vector3(0.11f, 0.07f, 0.26f));
            AddBox(parts.LegRight, "ShoeRight", shoes, new Vector3(0f, -LegLength, 0.04f), new Vector3(0.11f, 0.07f, 0.26f));
            AddPrimitive(PrimitiveType.Sphere, parts.ArmLeft, "HandLeft", skin, new Vector3(0f, -ArmLength, 0f), Vector3.one * 0.09f, Quaternion.identity);
            AddPrimitive(PrimitiveType.Sphere, parts.ArmRight, "HandRight", skin, new Vector3(0f, -ArmLength, 0f), Vector3.one * 0.09f, Quaternion.identity);
            return parts;
        }

        private static Transform AddLimb(Transform parent, string objectName, Vector3 joint, float length, float thickness, Material material)
        {
            Transform pivot = new GameObject(objectName).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = joint;
            AddPrimitive(PrimitiveType.Capsule, pivot, objectName + "Mesh", material,
                new Vector3(0f, -length * 0.5f, 0f), new Vector3(thickness, length * 0.5f, thickness), Quaternion.identity);
            return pivot;
        }

        internal static CarParts BuildCar(Transform parent, float width, float length, Color paintColour, string paintName)
        {
            Material paint = Mat($"CarPaint{paintName}", paintColour, 0.75f);
            Material glass = Mat("Glass", new Color(0.08f, 0.1f, 0.13f), 0.85f);
            AddBox(parent, "Body", paint, new Vector3(0f, 0.62f, 0f), new Vector3(width, 0.66f, length));
            AddBox(parent, "Cabin", glass, new Vector3(0f, 1.2f, -0.15f * length / 4.5f), new Vector3(width * 0.88f, 0.52f, length * 0.5f));
            AddBox(parent, "Roof", paint, new Vector3(0f, 1.48f, -0.15f * length / 4.5f), new Vector3(width * 0.86f, 0.06f, length * 0.46f));
            CarParts parts = new CarParts { Hazards = AddWheelsAndLights(parent, width, length, 0.33f, out List<Renderer> brakes) };
            parts.BrakeLights = brakes;
            return parts;
        }

        /// <summary>Four wheels plus head, tail, brake and indicator lights. Returns the indicators.</summary>
        private static List<Renderer> AddWheelsAndLights(Transform parent, float width, float length, float wheelRadius, out List<Renderer> brakeLights)
        {
            Material tyre = Mat("Rubber", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            Material head = Mat("HeadLight", new Color(1f, 1f, 0.92f), 0.9f);
            Material tail = Mat("TailLight", new Color(0.45f, 0.02f, 0.02f), 0.8f);
            Material brake = Mat("BrakeLight", new Color(1f, 0.05f, 0.05f), 0.9f);
            Material amber = Mat("Indicator", new Color(1f, 0.55f, 0f), 0.9f);
            float wheelX = width * 0.5f - 0.11f;
            float wheelZ = length * 0.5f - 0.85f;
            foreach (float z in new[] { wheelZ, -wheelZ })
            {
                AddWheel(parent, "WheelL" + z, tyre, new Vector3(-wheelX, wheelRadius, z), wheelRadius, 0.22f);
                AddWheel(parent, "WheelR" + z, tyre, new Vector3(wheelX, wheelRadius, z), wheelRadius, 0.22f);
            }

            List<Renderer> indicators = new List<Renderer>();
            brakeLights = new List<Renderer>();
            float lightX = width * 0.5f - 0.25f;
            float frontZ = length * 0.5f + 0.01f;
            foreach (float side in new[] { -1f, 1f })
            {
                AddBox(parent, "Head" + side, head, new Vector3(side * lightX, 0.75f, frontZ), new Vector3(0.3f, 0.12f, 0.04f));
                AddBox(parent, "Tail" + side, tail, new Vector3(side * lightX, 0.8f, -frontZ), new Vector3(0.3f, 0.14f, 0.04f));
                brakeLights.Add(AddBox(parent, "Brake" + side, brake, new Vector3(side * lightX, 0.8f, -frontZ - 0.01f), new Vector3(0.28f, 0.12f, 0.03f)).GetComponent<Renderer>());
                indicators.Add(AddBox(parent, "IndFront" + side, amber, new Vector3(side * (width * 0.5f - 0.08f), 0.72f, frontZ), new Vector3(0.12f, 0.08f, 0.05f)).GetComponent<Renderer>());
                indicators.Add(AddBox(parent, "IndRear" + side, amber, new Vector3(side * (width * 0.5f - 0.08f), 0.8f, -frontZ - 0.01f), new Vector3(0.12f, 0.08f, 0.05f)).GetComponent<Renderer>());
            }
            foreach (Renderer light in brakeLights)
            {
                light.enabled = false;
            }
            return indicators;
        }

        private static Transform AddWheel(Transform parent, string objectName, Material material, Vector3 position, float radius, float thickness)
        {
            // Unity's cylinder is 2 tall and 1 wide at scale 1; rotated 90 degrees about Z its axis lies along X.
            return AddPrimitive(PrimitiveType.Cylinder, parent, objectName, material, position,
                new Vector3(radius * 2f, thickness * 0.5f, radius * 2f), Quaternion.Euler(0f, 0f, 90f)).transform;
        }

        /// <summary>A traffic cone: its own dynamic body (it can be knocked over) with the cone mesh as visual.</summary>
        private static void AddCone(Transform parent, string objectName, Vector3 groundPosition)
        {
            GameObject body = MakeBody(parent, objectName, ConeMass);
            Rigidbody coneBody = body.GetComponent<Rigidbody>();
            coneBody.linearDamping = ConeLinearDamping;
            coneBody.angularDamping = ConeAngularDamping;
            body.transform.localPosition = groundPosition;
            AddBoxCollider(body, new Vector3(0f, ConeHeight * 0.5f, 0f), new Vector3(ConeBodySize, ConeHeight, ConeBodySize));

            GameObject coneModel = ModelLibrary.Spawn(body.transform, ModelLibrary.Road("construction-cone"), ModelLibrary.ConeScale, 0f, "Cone");
            if (coneModel != null)
            {
                return;
            }

            GameObject cone = new GameObject("Mesh");
            cone.transform.SetParent(body.transform, false);
            cone.transform.localPosition = Vector3.up * ConeFootHeight;
            cone.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            cone.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Mat("ConeOrange", new Color(1f, 0.36f, 0.02f), 0.35f),
                Mat("Reflective", new Color(0.95f, 0.95f, 0.95f), 0.7f)
            };
            AddBox(body.transform, "Foot", Mat("Rubber", new Color(0.06f, 0.06f, 0.06f), 0.1f),
                Vector3.up * (ConeFootHeight * 0.5f), new Vector3(ConeFootSize, ConeFootHeight, ConeFootSize));
        }

        /// <summary>Red and white striped barrier board on two legs, length along local X before yaw.</summary>
        private static void AddStripedBoard(Transform parent, string objectName, Vector3 position, float length, float yawDegrees)
        {
            Transform board = new GameObject(objectName).transform;
            board.SetParent(parent, false);
            board.localPosition = position;
            board.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            string barrierPath = ModelLibrary.Road("construction-barrier");
            if (ModelLibrary.Exists(barrierPath))
            {
                // Kenney barriers are 0.22 wide along Z as modelled, so turn them 90 degrees to run along X.
                float pieceLength = ModelLibrary.Measure(barrierPath, Vector3.one * ModelLibrary.BarrierScale, 90f).size.x;
                int pieces = Mathf.Max(1, Mathf.RoundToInt(length / pieceLength));
                float spacing = length / pieces;
                for (int i = 0; i < pieces; i++)
                {
                    GameObject piece = ModelLibrary.Spawn(board, barrierPath, ModelLibrary.BarrierScale, 90f, $"Barrier{i}");
                    piece.transform.localPosition = new Vector3(-length * 0.5f + spacing * (i + 0.5f), 0f, 0f);
                }
                return;
            }

            Material red = Mat("BarrierRed", new Color(0.85f, 0.08f, 0.08f), 0.4f);
            Material white = Mat("BarrierWhite", new Color(0.95f, 0.95f, 0.95f), 0.4f);
            Material leg = Mat("DarkMetal", new Color(0.15f, 0.15f, 0.16f), 0.5f);
            const float stripe = 0.4f;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / stripe));
            for (int i = 0; i < count; i++)
            {
                float x = -length * 0.5f + stripe * (i + 0.5f);
                AddBox(board, $"Stripe{i}", i % 2 == 0 ? red : white, new Vector3(x, 0.95f, 0f), new Vector3(stripe, 0.25f, 0.04f));
            }
            AddBox(board, "LegA", leg, new Vector3(-length * 0.5f + 0.1f, 0.5f, 0f), new Vector3(0.05f, 1f, 0.4f));
            AddBox(board, "LegB", leg, new Vector3(length * 0.5f - 0.1f, 0.5f, 0f), new Vector3(0.05f, 1f, 0.4f));
        }

        private static void AddBlinker(GameObject root, List<Renderer> lights)
        {
            BlinkingLights blinker = root.AddComponent<BlinkingLights>();
            SetRefs(blinker, "lights", lights.ToArray());
        }

        // ---------------- model helpers ----------------

        private const float BikeLength = 1.8f;
        private const float RiderScale = 0.9f;
        private const float RiderHeightAboveGround = 0.3f;
        private const float RiderOffsetZ = -0.1f;

        internal struct CarVisual
        {
            public Bounds Bounds;
            public List<Renderer> Hazards;
            public List<Renderer> BrakeLights;
        }

        /// <summary>Size a car model will have (or the fallback size if the model is missing).</summary>
        private static Bounds CarBounds(string modelName, float fallbackWidth, float fallbackLength)
        {
            string path = ModelLibrary.Car(modelName);
            return ModelLibrary.Exists(path)
                ? ModelLibrary.Measure(path, ModelLibrary.CarScale, 0f)
                : new Bounds(Vector3.zero, new Vector3(fallbackWidth, 1.4f, fallbackLength));
        }

        /// <summary>
        /// A car model with amber hazard and red brake light boxes on its corners. Bounds are in the parent's
        /// local space. Falls back to the procedural car if the model is missing.
        /// </summary>
        private static CarVisual AddCarVisual(Transform parent, string modelName, float fallbackWidth, float fallbackLength, Color fallbackColour, string fallbackName)
        {
            string path = ModelLibrary.Car(modelName);
            if (!ModelLibrary.Exists(path))
            {
                CarParts parts = BuildCar(parent, fallbackWidth, fallbackLength, fallbackColour, fallbackName);
                return new CarVisual
                {
                    Bounds = new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(fallbackWidth, 1.2f, fallbackLength)),
                    Hazards = parts.Hazards,
                    BrakeLights = parts.BrakeLights
                };
            }

            GameObject visual = ModelLibrary.Spawn(parent, path, ModelLibrary.CarScale, 0f, "Model");
            Bounds bounds = ModelLibrary.LocalBounds(visual.transform, parent, null);
            return AddLights(parent, bounds);
        }

        /// <summary>Amber hazard boxes on the four corners and red brake boxes (off) on the rear of a vehicle with these bounds.</summary>
        private static CarVisual AddLights(Transform parent, Bounds bounds)
        {
            Material amber = Mat("Indicator", new Color(1f, 0.55f, 0f), 0.9f);
            Material brake = Mat("BrakeLight", new Color(1f, 0.05f, 0.05f), 0.9f);
            float y = bounds.min.y + bounds.size.y * CarLightHeightShare;
            float x = bounds.extents.x - CarLightInset;
            Vector3 lightSize = new Vector3(CarLightSize, CarLightSize * 0.6f, CarLightSize * 0.4f);

            List<Renderer> hazards = new List<Renderer>();
            List<Renderer> brakes = new List<Renderer>();
            foreach (float side in new[] { -1f, 1f })
            {
                hazards.Add(AddBox(parent, "HazardFront" + side, amber, new Vector3(bounds.center.x + side * x, y, bounds.max.z + 0.02f), lightSize).GetComponent<Renderer>());
                hazards.Add(AddBox(parent, "HazardRear" + side, amber, new Vector3(bounds.center.x + side * x, y, bounds.min.z - 0.02f), lightSize).GetComponent<Renderer>());
                Renderer brakeLight = AddBox(parent, "Brake" + side, brake, new Vector3(bounds.center.x + side * x * 0.6f, y + CarLightSize, bounds.min.z - 0.02f), lightSize).GetComponent<Renderer>();
                brakeLight.enabled = false;
                brakes.Add(brakeLight);
            }
            return new CarVisual { Bounds = bounds, Hazards = hazards, BrakeLights = brakes };
        }

        /// <summary>An animated character model (idle / walk / sprint) scaled to a person of the given size factor.</summary>
        private static GameObject AddPerson(Transform parent, string modelName, float sizeFactor, float yawDegrees, RuntimeAnimatorController controller = null)
        {
            GameObject person = ModelLibrary.Spawn(parent, ModelLibrary.Person(modelName), ModelLibrary.CharacterScale * sizeFactor, yawDegrees, "Person");
            if (person == null)
            {
                return null;
            }

            Animator animator = person.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                // The character FBX files carry clips but no Animator, so the model root gets one here.
                Transform model = person.transform.childCount > 0 ? person.transform.GetChild(0) : person.transform;
                animator = model.gameObject.AddComponent<Animator>();
                animator.avatar = LoadAvatar(ModelLibrary.Person(modelName));
            }
            animator.runtimeAnimatorController = controller != null ? controller : CharacterAnimation.PersonController();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return person;
        }

        private static Avatar LoadAvatar(string modelPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is Avatar avatar)
                {
                    return avatar;
                }
            }
            return null;
        }

        /// <summary>Gives a pedestrian root a person model and the matching walk rig, or the procedural person if the model is missing.</summary>
        private static void AttachPerson(GameObject root, string modelName, float sizeFactor, Color shirt, Color trousers, Color hair, string materialKey)
        {
            GameObject person = AddPerson(root.transform, modelName, sizeFactor, 0f);
            if (person != null)
            {
                AnimatorWalkRig rig = root.AddComponent<AnimatorWalkRig>();
                rig.SetAnimator(person.GetComponentInChildren<Animator>());
                return;
            }

            HumanParts parts = BuildHuman(root.transform, Vector3.zero, sizeFactor,
                Mat($"{materialKey}_Shirt", shirt, 0.2f), Mat($"{materialKey}_Trousers", trousers, 0.2f), Mat($"{materialKey}_Hair", hair, 0.3f));
            root.AddComponent<WalkRig>().SetRig(parts.Body, parts.LegLeft, parts.LegRight, parts.ArmLeft, parts.ArmRight);
        }

        // ---------------- physics helpers ----------------

        /// <summary>
        /// Child object that carries a solid body. Mass above zero adds a dynamic Rigidbody the vehicle can push,
        /// zero means a fixed collider. Every body reports collisions.
        /// </summary>
        private static GameObject MakeBody(Transform parent, string objectName, float mass)
        {
            GameObject body = new GameObject(objectName);
            body.transform.SetParent(parent, false);
            if (mass > 0f)
            {
                ConfigureRigidbody(body.AddComponent<Rigidbody>(), mass, 0.05f, 0.5f);
            }
            body.AddComponent<ObstacleCollisionReporter>();
            return body;
        }

        /// <summary>A parked car: heavy and well braked, so a hit shoves it a little instead of launching it.</summary>
        private static GameObject MakeParkedBody(Transform parent, string objectName, float mass)
        {
            GameObject body = new GameObject(objectName);
            body.transform.SetParent(parent, false);
            ConfigureRigidbody(body.AddComponent<Rigidbody>(), mass, 0.4f, 1f);
            body.AddComponent<ObstacleCollisionReporter>();
            return body;
        }

        /// <summary>
        /// Makes the prefab root a dynamic body for a scripted mover. It is steered to its scripted pose by
        /// velocity (gravity off, rotation frozen) until hit, then falls and tumbles.
        /// </summary>
        private static void MakeMoverBody(GameObject root, float mass)
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            ConfigureRigidbody(body, mass, 0f, 0.5f);
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<ObstacleCollisionReporter>();
        }

        private static void ConfigureRigidbody(Rigidbody body, float mass, float linearDamping, float angularDamping)
        {
            body.mass = mass;
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private static void AddBoxCollider(GameObject host, Vector3 centre, Vector3 size)
        {
            BoxCollider box = host.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.sharedMaterial = bodyMaterial;
        }

        private static void AddCapsuleCollider(GameObject host, Vector3 centre, float radius, float height)
        {
            CapsuleCollider capsule = host.AddComponent<CapsuleCollider>();
            capsule.center = centre;
            capsule.radius = radius;
            capsule.height = height;
            capsule.sharedMaterial = bodyMaterial;
        }

        private static PhysicsMaterial LoadOrCreateBodyMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BodyMaterialPath);
            if (material != null)
            {
                return material;
            }

            material = new PhysicsMaterial("ObstacleBody")
            {
                dynamicFriction = BodyFriction,
                staticFriction = BodyFriction,
                bounciness = BodyBounciness
            };
            AssetDatabase.CreateAsset(material, BodyMaterialPath);
            return material;
        }

        // ---------------- shared helpers ----------------

        private static ObstacleDefinition Define(string id, string displayName, ObstacleCategory category, bool dynamic,
            float width, float length, float minT, float maxT, bool endsOnly, bool mirror, float yawJitter, int danger, int minDifficulty, float weight)
        {
            ObstacleDefinition definition = EditorAssetUtil.LoadOrCreate<ObstacleDefinition>($"{ObstacleDataFolder}/{id}.asset");
            definition.id = id;
            definition.displayName = displayName;
            definition.category = category;
            definition.isDynamic = dynamic;
            definition.footprintWidth = width;
            definition.footprintLength = length;
            definition.minT = minT;
            definition.maxT = maxT;
            definition.placeAtRangeEndsOnly = endsOnly;
            definition.mirrorOnRightSide = mirror;
            definition.yawJitterDegrees = yawJitter;
            definition.dangerLevel = danger;
            definition.minDifficulty = minDifficulty;
            definition.weight = weight;
            definition.requiredZone = ZoneType.None;
            definition.minGapAfter = 0f;
            definition.maxBlockSeconds = 0f;
            definition.triggerTimeToArrival = 0f;
            return definition;
        }

        /// <summary>
        /// Prefab root: pivot at the footprint's ground centre, forward along the road. Holds the behaviour,
        /// the pooled-physics reset and a "Footprint" trigger child that matches the definition.
        /// </summary>
        internal static GameObject CreateRoot(ObstacleDefinition definition, System.Type behaviourType, float height = DefaultTriggerHeight)
        {
            GameObject root = new GameObject(definition.id);
            root.AddComponent(behaviourType);
            root.AddComponent<PooledPhysicsReset>();

            GameObject footprint = new GameObject(FootprintName);
            footprint.transform.SetParent(root.transform, false);
            BoxCollider trigger = footprint.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(definition.footprintWidth, height, definition.footprintLength);
            trigger.center = new Vector3(0f, height * 0.5f, 0f);
            return root;
        }

        private static ObstacleDefinition Finish(ObstacleDefinition definition, GameObject root)
        {
            ApplyTagAndLayer(root);
            definition.prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ObstaclePrefabFolder}/{definition.id}.prefab");
            Object.DestroyImmediate(root);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        internal static Material Mat(string materialName, Color colour, float smoothness)
        {
            return RoadMaterialFactory.LoadOrCreateMaterial("Obstacle_" + materialName, smoothness, colour, null);
        }

        internal static GameObject AddBox(Transform parent, string objectName, Material material, Vector3 localPosition, Vector3 size)
        {
            return AddPrimitive(PrimitiveType.Cube, parent, objectName, material, localPosition, size, Quaternion.identity);
        }

        internal static GameObject AddPrimitive(PrimitiveType type, Transform parent, string objectName, Material material,
            Vector3 localPosition, Vector3 scale, Quaternion rotation)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = objectName;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRefs(Object target, string field, Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Footprint trigger goes on layer Obstacles, everything else on ObstacleBody. Root is tagged.</summary>
        private static void ApplyTagAndLayer(GameObject root)
        {
            int footprintLayer = LayerMask.NameToLayer(ObstacleLayer);
            int bodyLayer = LayerMask.NameToLayer(PhysicsSetup.ObstacleBodyLayer);
            if (footprintLayer < 0 || bodyLayer < 0)
            {
                Debug.LogWarning($"BusSim: layers '{ObstacleLayer}' or '{PhysicsSetup.ObstacleBodyLayer}' are missing. Add them in Tags and Layers.");
            }
            else
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = child.name == FootprintName ? footprintLayer : bodyLayer;
                }
            }

            try
            {
                root.tag = ObstacleTag;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"BusSim: tag '{ObstacleTag}' is missing. Add it in Tags and Layers.");
            }
        }

        private static float RoadHalfWidth()
        {
            RoadSettings settings = AssetDatabase.LoadAssetAtPath<RoadSettings>(RoadSettingsPath);
            return settings != null ? settings.HalfRoadWidth : DefaultHalfRoadWidth;
        }

        private static float FootpathWidth()
        {
            RoadSettings settings = AssetDatabase.LoadAssetAtPath<RoadSettings>(RoadSettingsPath);
            return settings != null ? settings.footpathWidth : DefaultFootpathWidth;
        }
    }
}
