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
                    Id = "PED_JAYWALK_ADULT", Name = "Jaywalking adult", Speed = 1.4f, Scale = 1f, Mass = AdultMass, T = footpathMiddle,
                    Shirt = new Color(0.16f, 0.35f, 0.62f), Trousers = new Color(0.18f, 0.18f, 0.2f), Hair = new Color(0.08f, 0.06f, 0.05f),
                    Trigger = 3.5f, MaxBlock = 6f, Danger = 4, MinDifficulty = 0, Weight = 1f
                }),
                BuildPedestrian(new PedestrianSpec
                {
                    // 7.6 m at 0.8 m/s is 9.5 s plus a 1 s pause, so 11 s rather than the catalog's 10.
                    Id = "PED_ELDERLY", Name = "Elderly slow crosser", Speed = 0.8f, Scale = 0.95f, Mass = 65f, T = footpathMiddle,
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
                    Id = "PED_CHILD_RUN", Name = "Child running across", Speed = 2.5f, Scale = 0.72f, Mass = 35f, T = footpathMiddle,
                    Shirt = new Color(0.9f, 0.9f, 0.92f), Trousers = new Color(0.12f, 0.2f, 0.45f), Hair = new Color(0.05f, 0.04f, 0.03f),
                    Trigger = 2.5f, MaxBlock = 4f, Danger = 5, MinDifficulty = 1, Weight = 3f, Zone = ZoneType.School
                }),
                BuildBusStopBlock(halfRoad),
                BuildPassengerRush(footpathMiddle)
            };

            BuildProfile("Easy", 0, 4f, definitions, new List<ObstacleCategory>
            {
                ObstacleCategory.TrafficControl, ObstacleCategory.Debris, ObstacleCategory.StoppedVehicle, ObstacleCategory.Pedestrian
            });
            BuildProfile("Normal", 1, 8f, definitions, new List<ObstacleCategory>());
            BuildProfile("Hard", 2, 14f, definitions, new List<ObstacleCategory>());
            AssetDatabase.SaveAssets();
            Debug.Log($"BusSim: built {definitions.Count} obstacle types and 3 difficulty profiles.");
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
            AddBox(root.transform, "SignPost", dark, new Vector3(-edge, 0.8f, SignZ), new Vector3(0.06f, 1.6f, 0.06f));
            AddBox(root.transform, "SignBoard", sign, new Vector3(-edge, 1.75f, SignZ - 0.03f), new Vector3(0.9f, 0.9f, 0.04f));
            AddBox(root.transform, "SignSymbol", dark, new Vector3(-edge, 1.75f, SignZ - 0.06f), new Vector3(0.35f, 0.35f, 0.02f));
            AddBoxCollider(fixtures, new Vector3(-edge, 1f, SignZ), new Vector3(0.9f, 2.2f, 0.2f));

            HumanParts worker = BuildHuman(root.transform, new Vector3(-0.5f, 0f, 3f), 1f,
                Mat("HiVis", new Color(0.95f, 0.85f, 0.05f), 0.3f), Mat("WorkTrousers", new Color(0.12f, 0.14f, 0.2f), 0.2f),
                Mat("Helmet", new Color(1f, 1f, 1f), 0.6f));
            worker.Body.localRotation = Quaternion.Euler(0f, 140f, 0f);
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
            ObstacleDefinition definition = Define("STALLED_CAR", "Breakdown with hazards", ObstacleCategory.StoppedVehicle, false,
                1.8f, 4.5f, -lane, lane, true, false, 0f, 3, 0, 0.6f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 1.5f);
            GameObject body = MakeParkedBody(root.transform, "Body", 1300f);
            CarParts car = BuildCar(body.transform, 1.8f, 4.5f, new Color(0.55f, 0.56f, 0.58f), "Silver");
            AddBoxCollider(body, new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.2f, 4.5f));
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
            // Double parked against the left kerb, 0.2 m off it.
            float t = -halfRoad + 1f + 0.2f;
            ObstacleDefinition definition = Define("DOUBLE_PARKED_VAN", "Delivery van double parked", ObstacleCategory.StoppedVehicle, false,
                2f, 5.5f, t, t, false, false, 0f, 3, 1, 0.8f);
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 2.4f);
            GameObject body = MakeParkedBody(root.transform, "Body", 2200f);
            Material white = Mat("VanWhite", new Color(0.93f, 0.93f, 0.92f), 0.45f);
            Material glass = Mat("Glass", new Color(0.08f, 0.1f, 0.13f), 0.85f);
            AddBox(body.transform, "Cargo", white, new Vector3(0f, 1.35f, -0.6f), new Vector3(2f, 2.1f, 4.2f));
            AddBox(body.transform, "Cab", white, new Vector3(0f, 1.0f, 2.05f), new Vector3(1.96f, 1.4f, 1.3f));
            AddBox(body.transform, "Windscreen", glass, new Vector3(0f, 1.45f, 2.55f), new Vector3(1.8f, 0.6f, 0.3f));
            AddBox(body.transform, "Livery", Mat("LiveryRed", new Color(0.75f, 0.12f, 0.12f), 0.4f), new Vector3(0f, 1.2f, -0.6f), new Vector3(2.02f, 0.25f, 4f));
            List<Renderer> hazards = AddWheelsAndLights(body.transform, 2f, 5.5f, 0.38f, out _);
            AddBoxCollider(body, new Vector3(0f, 1.3f, -0.4f), new Vector3(2f, 2.1f, 4.6f));
            AddBoxCollider(body, new Vector3(0f, 0.8f, 2.05f), new Vector3(1.96f, 1.1f, 1.3f));
            AddBlinker(root, hazards);
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
            MakeMoverBody(root, 95f);
            AddBoxCollider(root, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 1.7f, 1.7f));
            return Finish(definition, root);
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
            ObstacleDefinition definition = Define("CAR_CUTIN", "Car cutting in and braking", ObstacleCategory.MovingVehicle, true,
                1.8f, 4.5f, lane, lane, false, false, 0f, 4, 2, 0.6f);
            definition.triggerTimeToArrival = 4f;
            definition.maxBlockSeconds = 8f;
            GameObject root = CreateRoot(definition, typeof(CarCutIn), 1.5f);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            CarParts car = BuildCar(visual, 1.8f, 4.5f, new Color(0.1f, 0.25f, 0.55f), "Blue");
            CarCutIn behaviour = root.GetComponent<CarCutIn>();
            SetRef(behaviour, "visual", visual.gameObject);
            SetRefs(behaviour, "brakeLights", car.BrakeLights.ToArray());
            MakeMoverBody(root, 1400f);
            AddBoxCollider(root, new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.2f, 4.5f));
            return Finish(definition, root);
        }

        private static ObstacleDefinition BuildBusStopBlock(float halfRoad)
        {
            float t = -halfRoad + 0.9f + 0.2f;
            ObstacleDefinition definition = Define("BUSSTOP_BLOCK", "Vehicle blocking bus stop bay", ObstacleCategory.StoppedVehicle, false,
                2f, 5.5f, t, t, false, false, 0f, 2, 0, 3f);
            definition.requiredZone = ZoneType.BusStop;
            GameObject root = CreateRoot(definition, typeof(StaticObstacle), 1.5f);
            GameObject body = MakeParkedBody(root.transform, "Body", 1300f);
            BuildCar(body.transform, 1.8f, 4.6f, new Color(0.08f, 0.08f, 0.09f), "Black");
            AddBoxCollider(body, new Vector3(0f, 0.75f, 0f), new Vector3(1.8f, 1.2f, 4.6f));
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
            HumanParts person = BuildHuman(root.transform, Vector3.zero, 1f,
                Mat("RushShirt", new Color(0.8f, 0.15f, 0.35f), 0.2f), Mat("RushSkirt", new Color(0.12f, 0.12f, 0.14f), 0.2f),
                Mat("RushHair", new Color(0.1f, 0.07f, 0.05f), 0.3f));
            root.GetComponent<WalkRig>().SetRig(person.Body, person.LegLeft, person.LegRight, person.ArmLeft, person.ArmRight);
            MakeMoverBody(root, AdultMass);
            AddCapsuleCollider(root, new Vector3(0f, PersonHeight * 0.5f, 0f), PersonRadius, PersonHeight);
            return Finish(definition, root);
        }

        internal struct PedestrianSpec
        {
            public string Id;
            public string Name;
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
            HumanParts person = BuildHuman(root.transform, Vector3.zero, spec.Scale,
                Mat($"{spec.Id}_Shirt", spec.Shirt, 0.2f), Mat($"{spec.Id}_Trousers", spec.Trousers, 0.2f), Mat($"{spec.Id}_Hair", spec.Hair, 0.3f));
            root.GetComponent<WalkRig>().SetRig(person.Body, person.LegLeft, person.LegRight, person.ArmLeft, person.ArmRight);
            root.GetComponent<PedestrianCrossing>().SetMovement(spec.Speed, spec.PauseChance, spec.PauseSeconds);
            MakeMoverBody(root, spec.Mass);
            float height = PersonHeight * spec.Scale;
            AddCapsuleCollider(root, new Vector3(0f, height * 0.5f, 0f), PersonRadius * spec.Scale, height);
            return Finish(definition, root);
        }

        private static void BuildProfile(string profileName, int level, float eventsPerKm, List<ObstacleDefinition> definitions, List<ObstacleCategory> categories)
        {
            DifficultyProfile profile = EditorAssetUtil.LoadOrCreate<DifficultyProfile>($"{DifficultyFolder}/{profileName}.asset");
            profile.profileName = profileName;
            profile.difficultyLevel = level;
            profile.eventsPerKm = eventsPerKm;
            profile.definitions = new List<ObstacleDefinition>(definitions);
            profile.allowedCategories = categories;
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
