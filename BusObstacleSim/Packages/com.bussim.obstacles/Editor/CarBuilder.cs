using System;
using BusSim.Road;
using BusSim.TestRig;
using BusSim.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Creates the test car prefab from the imported sedan model, places it on the road and hooks up the
    /// chase camera. The physics geometry (body size, wheelbase, track, wheel radius) is measured from the model.
    /// </summary>
    public static class CarBuilder
    {
        public const string VehicleLayer = "Vehicle";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Vehicle";
        private const string PrefabPath = PrefabFolder + "/Car.prefab";
        private const string SettingsPath = "Assets/_Project/Data/VehicleSettings.asset";
        private const string PhysicsMaterialPath = "Assets/_Project/Materials/CarBody.asset";
        private const string CarObjectName = "Car";
        private const string CarModelName = "sedan";

        private const float StartDistance = 10f;
        private const float SpawnClearance = 0.1f;
        private const float BodyFriction = 0.15f;
        private const float ChassisClearanceShare = 0.7f;
        private const float ReferenceWheelRadius = 0.32f;
        private const float ReferenceMotorTorque = 900f;

        private static readonly string[] WheelNames =
        {
            "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right"
        };

        /// <summary>Removes the car and chase camera, then builds them again with current defaults.</summary>
        [MenuItem("BusSim/Car/Recreate Car")]
        public static void RecreateCar()
        {
            CarController existing = UnityEngine.Object.FindAnyObjectByType<CarController>();
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
            RoadSampler road = UnityEngine.Object.FindAnyObjectByType<RoadSampler>();
            if (road == null || road.Settings == null)
            {
                Debug.LogError("BusSim: create the road first (BusSim/Road/Create Road In Scene).");
                return;
            }
            if (UnityEngine.Object.FindAnyObjectByType<CarController>() != null)
            {
                Debug.LogWarning("BusSim: a car already exists in this scene.");
                return;
            }
            if (!ModelLibrary.Exists(ModelLibrary.Car(CarModelName)))
            {
                Debug.LogError($"BusSim: car model missing: {ModelLibrary.Car(CarModelName)}");
                return;
            }

            DemoSceneBuilder.EnsureLayer(VehicleLayer);
            EditorAssetUtil.EnsureFolder(PrefabFolder);
            VehicleSettings settings = EditorAssetUtil.LoadOrCreate<VehicleSettings>(SettingsPath);
            GameObject car = BuildCar(settings);
            PlaceOnRoad(car.transform, road);

            // The return value is the prefab asset. The scene object stays as `car`, now connected.
            PrefabUtility.SaveAsPrefabAssetAndConnect(car, PrefabPath, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(car, "Create Car");
            WireCamera(car.transform, settings);

            EditorSceneManager.MarkSceneDirty(car.scene);
            Selection.activeObject = car;
            Debug.Log($"BusSim: car created from '{CarModelName}' ({settings.chassisWidth:F2} x {settings.chassisLength:F2} m, wheel radius {settings.wheelRadius:F2} m, wheelbase {settings.wheelbase:F2} m) in the left lane.");
        }

        private static GameObject BuildCar(VehicleSettings settings)
        {
            GameObject root = new GameObject(CarObjectName);
            GameObject model = ModelLibrary.Spawn(root.transform, ModelLibrary.Car(CarModelName), ModelLibrary.CarScale, 0f, "Model");

            // Measure the model: body without wheels, then each wheel.
            Func<Renderer, bool> notWheel = renderer => !renderer.name.StartsWith("wheel");
            Bounds body = ModelLibrary.LocalBounds(model.transform, root.transform, notWheel);
            Transform[] meshes = new Transform[WheelNames.Length];
            Vector3[] centres = new Vector3[WheelNames.Length];
            float radius = 0f;
            for (int i = 0; i < WheelNames.Length; i++)
            {
                meshes[i] = ModelLibrary.FindDeep(model.transform, WheelNames[i]);
                Bounds wheelBounds = ModelLibrary.LocalBounds(meshes[i], root.transform, null);
                centres[i] = wheelBounds.center;
                radius = Mathf.Max(radius, wheelBounds.size.y * 0.5f);
            }

            FitSettings(settings, body, centres, radius);

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = settings.mass;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            float colliderHeight = settings.chassisHeight - settings.groundClearance;
            BoxCollider chassis = root.AddComponent<BoxCollider>();
            chassis.size = new Vector3(settings.chassisWidth, colliderHeight, settings.chassisLength);
            chassis.center = new Vector3(body.center.x, settings.groundClearance + colliderHeight * 0.5f, body.center.z);
            chassis.sharedMaterial = LoadOrCreatePhysicsMaterial();

            CarController.Wheel[] wheels = new CarController.Wheel[WheelNames.Length];
            for (int i = 0; i < WheelNames.Length; i++)
            {
                bool front = i < 2;
                wheels[i] = BuildWheel(root.transform, meshes[i], centres[i], settings, steers: front, drives: !front);
            }

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

        /// <summary>Copies the measured model geometry into the settings so physics matches what is on screen.</summary>
        private static void FitSettings(VehicleSettings settings, Bounds body, Vector3[] wheelCentres, float wheelRadius)
        {
            settings.chassisWidth = body.size.x;
            settings.chassisLength = body.size.z;
            settings.chassisHeight = body.max.y;
            settings.wheelRadius = wheelRadius;
            settings.wheelbase = Mathf.Abs(wheelCentres[0].z - wheelCentres[2].z);
            settings.trackWidth = Mathf.Abs(wheelCentres[0].x - wheelCentres[1].x);
            settings.groundClearance = Mathf.Max(body.min.y, wheelRadius * ChassisClearanceShare);
            // Bigger wheels turn the same torque into less force, so scale the motor to keep the same push.
            settings.motorTorque = ReferenceMotorTorque * wheelRadius / ReferenceWheelRadius;
            EditorUtility.SetDirty(settings);
        }

        private static CarController.Wheel BuildWheel(
            Transform parent, Transform mesh, Vector3 centre, VehicleSettings settings, bool steers, bool drives)
        {
            GameObject colliderObject = new GameObject("WheelCollider_" + mesh.name);
            colliderObject.transform.SetParent(parent, false);
            // The collider origin sits above the wheel centre by the suspension's resting extension.
            colliderObject.transform.localPosition = centre + Vector3.up * (settings.suspensionDistance * settings.suspensionTarget);
            WheelCollider collider = colliderObject.AddComponent<WheelCollider>();
            collider.radius = settings.wheelRadius;
            collider.suspensionDistance = settings.suspensionDistance;

            // The wheel mesh from the model follows the physics wheel, so it spins and steers.
            Transform pivot = new GameObject("Wheel_" + mesh.name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = centre;
            mesh.SetParent(pivot, true);

            return new CarController.Wheel { collider = collider, visual = pivot, steers = steers, drives = drives };
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

        private static void PlaceOnRoad(Transform car, RoadSampler road)
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
