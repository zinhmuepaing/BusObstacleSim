using BusSim.Road;
using BusSim.TestRig;
using BusSim.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>Creates the test car prefab, places it on the road and hooks up the chase camera.</summary>
    public static class CarBuilder
    {
        public const string VehicleLayer = "Vehicle";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Vehicle";
        private const string PrefabPath = PrefabFolder + "/Car.prefab";
        private const string SettingsPath = "Assets/_Project/Data/VehicleSettings.asset";
        private const string PhysicsMaterialPath = "Assets/_Project/Materials/CarBody.asset";
        private const string CarObjectName = "Car";

        private const float StartDistance = 10f;
        private const float SpawnClearance = 0.1f;
        private const float BodyFriction = 0.15f;

        // Placeholder look, used until a real car model is imported (M9).
        private const float LowerBodyHeight = 0.55f;
        private const float CabinHeight = 0.5f;
        private const float CabinWidthShare = 0.86f;
        private const float CabinLengthShare = 0.5f;
        private const float CabinSetBack = 0.1f;
        private const float GlassGrow = 0.02f;
        private const float WheelThickness = 0.22f;
        private const float LightWidth = 0.32f;
        private const float LightHeight = 0.12f;
        private const float LightDepth = 0.04f;
        private const float LightInset = 0.3f;
        private const float LightHeightShare = 0.62f;

        private static readonly Color PaintColour = new Color(0.72f, 0.1f, 0.12f);
        private static readonly Color GlassColour = new Color(0.08f, 0.1f, 0.14f);
        private static readonly Color TyreColour = new Color(0.05f, 0.05f, 0.05f);
        private static readonly Color HeadColour = new Color(1f, 1f, 0.9f);
        private static readonly Color TailColour = new Color(0.6f, 0.02f, 0.02f);

        /// <summary>Removes the car and chase camera, then builds them again with current defaults.</summary>
        [MenuItem("BusSim/Car/Recreate Car")]
        public static void RecreateCar()
        {
            CarController existing = Object.FindAnyObjectByType<CarController>();
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Camera mainCamera = Camera.main;
            FollowCamera follow = mainCamera != null ? mainCamera.GetComponent<FollowCamera>() : null;
            if (follow != null)
            {
                Undo.DestroyObjectImmediate(follow);
            }

            CreateCar();
        }

        [MenuItem("BusSim/Car/Create Car")]
        public static void CreateCar()
        {
            RoadSampler road = Object.FindAnyObjectByType<RoadSampler>();
            if (road == null || road.Settings == null)
            {
                Debug.LogError("BusSim: create the road first (BusSim/Road/Create Road In Scene).");
                return;
            }
            if (Object.FindAnyObjectByType<CarController>() != null)
            {
                Debug.LogWarning("BusSim: a car already exists in this scene.");
                return;
            }

            DemoSceneBuilder.EnsureLayer(VehicleLayer);
            EditorAssetUtil.EnsureFolder(PrefabFolder);
            VehicleSettings settings = EditorAssetUtil.LoadOrCreate<VehicleSettings>(SettingsPath);
            GameObject car = BuildCar(settings);
            PlaceOnRoad(car.transform, road, settings);

            // The return value is the prefab asset. The scene object stays as `car`, now connected.
            PrefabUtility.SaveAsPrefabAssetAndConnect(car, PrefabPath, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(car, "Create Car");
            WireCamera(car.transform, settings);

            EditorSceneManager.MarkSceneDirty(car.scene);
            Selection.activeObject = car;
            Debug.Log($"BusSim: car created ({settings.chassisWidth} x {settings.chassisLength} m, {settings.mass} kg) in the left lane. Prefab: {PrefabPath}");
        }

        private static GameObject BuildCar(VehicleSettings settings)
        {
            Material paint = RoadMaterialFactory.LoadOrCreateMaterial("Car_Paint", 0.75f, PaintColour, null);
            Material glass = RoadMaterialFactory.LoadOrCreateMaterial("Car_Glass", 0.85f, GlassColour, null);
            Material tyre = RoadMaterialFactory.LoadOrCreateMaterial("Car_Tyre", 0.1f, TyreColour, null);
            Material head = RoadMaterialFactory.LoadOrCreateMaterial("Car_Head", 0.9f, HeadColour, null);
            Material tail = RoadMaterialFactory.LoadOrCreateMaterial("Car_Tail", 0.8f, TailColour, null);

            GameObject root = new GameObject(CarObjectName);
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = settings.mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            float colliderHeight = settings.chassisHeight - settings.groundClearance;
            BoxCollider chassis = root.AddComponent<BoxCollider>();
            chassis.size = new Vector3(settings.chassisWidth, colliderHeight, settings.chassisLength);
            chassis.center = new Vector3(0f, settings.groundClearance + colliderHeight * 0.5f, 0f);
            chassis.sharedMaterial = LoadOrCreatePhysicsMaterial();

            float halfLength = settings.chassisLength * 0.5f;
            float lowerY = settings.groundClearance + LowerBodyHeight * 0.5f;
            AddBox(root.transform, "Body", paint, new Vector3(0f, lowerY, 0f),
                new Vector3(settings.chassisWidth, LowerBodyHeight, settings.chassisLength));
            float cabinY = settings.groundClearance + LowerBodyHeight + CabinHeight * 0.5f;
            Vector3 cabinSize = new Vector3(settings.chassisWidth * CabinWidthShare, CabinHeight, settings.chassisLength * CabinLengthShare);
            AddBox(root.transform, "CabinGlass", glass, new Vector3(0f, cabinY, -CabinSetBack), cabinSize + Vector3.one * GlassGrow);
            AddBox(root.transform, "Roof", paint, new Vector3(0f, cabinY + CabinHeight * 0.5f, -CabinSetBack),
                new Vector3(cabinSize.x, GlassGrow * 2f, cabinSize.z));

            float lightX = settings.chassisWidth * 0.5f - LightInset;
            float lightY = settings.groundClearance + LowerBodyHeight * LightHeightShare;
            foreach (float side in new[] { -1f, 1f })
            {
                AddBox(root.transform, "Head" + side, head, new Vector3(side * lightX, lightY, halfLength + LightDepth * 0.5f),
                    new Vector3(LightWidth, LightHeight, LightDepth));
                AddBox(root.transform, "Tail" + side, tail, new Vector3(side * lightX, lightY, -halfLength - LightDepth * 0.5f),
                    new Vector3(LightWidth, LightHeight, LightDepth));
            }

            float wheelY = settings.wheelRadius + settings.suspensionDistance * settings.suspensionTarget;
            float wheelX = settings.trackWidth * 0.5f;
            float wheelZ = settings.wheelbase * 0.5f;
            CarController.Wheel[] wheels =
            {
                BuildWheel(root.transform, "FL", new Vector3(-wheelX, wheelY, wheelZ), settings, tyre, true, false),
                BuildWheel(root.transform, "FR", new Vector3(wheelX, wheelY, wheelZ), settings, tyre, true, false),
                BuildWheel(root.transform, "RL", new Vector3(-wheelX, wheelY, -wheelZ), settings, tyre, false, true),
                BuildWheel(root.transform, "RR", new Vector3(wheelX, wheelY, -wheelZ), settings, tyre, false, true)
            };

            CarController controller = root.AddComponent<CarController>();
            controller.Configure(settings, wheels);
            CarAutopilot autopilot = root.AddComponent<CarAutopilot>();
            autopilot.enabled = false;

            int layer = LayerMask.NameToLayer(VehicleLayer);
            if (layer >= 0)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = layer;
                }
            }
            return root;
        }

        private static CarController.Wheel BuildWheel(
            Transform parent, string wheelName, Vector3 position, VehicleSettings settings, Material tyre, bool steers, bool drives)
        {
            GameObject colliderObject = new GameObject("WheelCollider_" + wheelName);
            colliderObject.transform.SetParent(parent, false);
            colliderObject.transform.localPosition = position;
            WheelCollider collider = colliderObject.AddComponent<WheelCollider>();
            collider.radius = settings.wheelRadius;
            collider.suspensionDistance = settings.suspensionDistance;

            Transform pivot = new GameObject("Wheel_" + wheelName).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            ObstacleContentBuilder.AddPrimitive(PrimitiveType.Cylinder, pivot, "Mesh", tyre, Vector3.zero,
                new Vector3(settings.wheelRadius * 2f, WheelThickness * 0.5f, settings.wheelRadius * 2f), Quaternion.Euler(0f, 0f, 90f));

            return new CarController.Wheel { collider = collider, visual = pivot, steers = steers, drives = drives };
        }

        private static GameObject AddBox(Transform parent, string objectName, Material material, Vector3 position, Vector3 size)
        {
            return ObstacleContentBuilder.AddBox(parent, objectName, material, position, size);
        }

        private static PhysicsMaterial LoadOrCreatePhysicsMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
            if (material != null)
            {
                return material;
            }

            material = new PhysicsMaterial("CarBody")
            {
                dynamicFriction = BodyFriction,
                staticFriction = BodyFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            return material;
        }

        private static void PlaceOnRoad(Transform car, RoadSampler road, VehicleSettings settings)
        {
            float laneT = road.Settings.GetLaneCentreT(0);
            road.GetFrame(StartDistance, out Vector3 centre, out Vector3 forward, out Vector3 right);
            car.position = centre + right * laneT + Vector3.up * SpawnClearance;
            car.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void WireCamera(Transform car, VehicleSettings settings)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("BusSim: no Main Camera found, chase camera not set up.");
                return;
            }

            FollowCamera follow = mainCamera.GetComponent<FollowCamera>();
            if (follow == null)
            {
                follow = Undo.AddComponent<FollowCamera>(mainCamera.gameObject);
            }
            follow.SetFraming(settings.cameraDistance, settings.cameraHeight, settings.cameraLookAhead, settings.cameraLookHeight);
            follow.Target = car;
            follow.Snap();
            EditorUtility.SetDirty(follow);
        }
    }
}
