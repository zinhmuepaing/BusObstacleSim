using System.Collections.Generic;
using BusSim.Obstacles;
using BusSim.Road;
using BusSim.Spawning;
using BusSim.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Builds the ambient traffic vehicle prefabs from the car models and adds the traffic manager to the scene.
    /// </summary>
    public static class TrafficContentBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Traffic";
        private const string SettingsPath = "Assets/_Project/Data/TrafficSettings.asset";
        private const string ManagerName = "TrafficManager";
        private const string BodyMaterialPath = "Assets/_Project/Materials/TrafficBody.asset";
        private const float VehicleMass = 1400f;
        private const float BodyFriction = 0.2f;

        // Kenney models used as ambient traffic and the share of a normal city mix they stand for.
        private static readonly string[] CarModels =
        {
            "sedan", "sedan-sports", "hatchback-sports", "suv", "taxi", "van", "delivery", "suv-luxury"
        };

        [MenuItem("BusSim/Traffic/Build Traffic Content")]
        public static void BuildContent()
        {
            EditorAssetUtil.EnsureFolder(PrefabFolder);
            DemoSceneBuilder.EnsureLayer(PhysicsSetup.TrafficLayer);
            DemoSceneBuilder.EnsureLayer(PhysicsSetup.VehicleLayer);

            TrafficSettings settings = EditorAssetUtil.LoadOrCreate<TrafficSettings>(SettingsPath);
            settings.vehiclePrefabs = new List<GameObject>();
            PhysicsMaterial material = LoadOrCreateMaterial();
            foreach (string model in CarModels)
            {
                GameObject prefab = BuildVehicle(model, material);
                if (prefab != null)
                {
                    settings.vehiclePrefabs.Add(prefab);
                }
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"BusSim: built {settings.vehiclePrefabs.Count} traffic vehicle prefabs.");
        }

        [MenuItem("BusSim/Traffic/Create Traffic In Scene")]
        public static void CreateTrafficInScene()
        {
            RoadSampler main = null;
            List<RoadSampler> sides = new List<RoadSampler>();
            foreach (RoadSampler sampler in Object.FindObjectsByType<RoadSampler>())
            {
                if (JunctionBuilder.IsSideRoad(sampler.gameObject))
                {
                    sides.Add(sampler);
                }
                else
                {
                    main = sampler;
                }
            }
            sides.Sort((x, y) => string.CompareOrdinal(x.gameObject.name, y.gameObject.name));
            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            if (main == null || spawner == null)
            {
                Debug.LogError("BusSim: create the road and the obstacle spawner first.");
                return;
            }

            TrafficSettings settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(SettingsPath);
            if (settings == null || settings.vehiclePrefabs.Count == 0)
            {
                BuildContent();
                settings = AssetDatabase.LoadAssetAtPath<TrafficSettings>(SettingsPath);
            }

            TrafficManager manager = Object.FindAnyObjectByType<TrafficManager>();
            if (manager == null)
            {
                GameObject go = new GameObject(ManagerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Traffic");
                manager = go.AddComponent<TrafficManager>();
            }
            manager.Configure(main, sides, spawner, main.GetComponent<RoadZones>(), settings);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            if (manager.GetComponent<TrafficWarningHud>() == null)
            {
                Undo.AddComponent<TrafficWarningHud>(manager.gameObject);
            }
            Selection.activeObject = manager.gameObject;
        }

        private static GameObject BuildVehicle(string modelName, PhysicsMaterial material)
        {
            string path = ModelLibrary.Car(modelName);
            if (!ModelLibrary.Exists(path))
            {
                Debug.LogWarning($"BusSim: traffic model missing: {path}");
                return null;
            }

            GameObject root = new GameObject("TRAFFIC_" + modelName);
            GameObject visual = ModelLibrary.Spawn(root.transform, path, ModelLibrary.CarScale, 0f, "Model");
            Bounds bounds = ModelLibrary.LocalBounds(visual.transform, root.transform, null);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = VehicleMass;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            box.sharedMaterial = material;

            root.AddComponent<PooledPhysicsReset>();
            TrafficVehicle vehicle = root.AddComponent<TrafficVehicle>();
            vehicle.Configure(bounds.size.z, bounds.size.x);

            int layer = LayerMask.NameToLayer(PhysicsSetup.TrafficLayer);
            if (layer >= 0)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = layer;
                }
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{root.name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static PhysicsMaterial LoadOrCreateMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BodyMaterialPath);
            if (material != null)
            {
                return material;
            }
            material = new PhysicsMaterial("TrafficBody")
            {
                dynamicFriction = BodyFriction,
                staticFriction = BodyFriction,
                bounciness = 0f
            };
            AssetDatabase.CreateAsset(material, BodyMaterialPath);
            return material;
        }
    }
}
