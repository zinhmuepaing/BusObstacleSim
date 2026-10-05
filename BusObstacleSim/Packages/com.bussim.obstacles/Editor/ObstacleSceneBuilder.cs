using BusSim.Road;
using BusSim.Spawning;
using BusSim.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>Adds the obstacle spawner to the open scene and wires it to the road and bus.</summary>
    public static class ObstacleSceneBuilder
    {
        private const string SpawnerName = "ObstacleSpawner";
        private const string DefaultProfilePath = ObstacleContentBuilder.DifficultyFolder + "/Normal.asset";

        [MenuItem("BusSim/Obstacles/Create Spawner In Scene")]
        public static void CreateSpawner()
        {
            RoadSampler road = Object.FindAnyObjectByType<RoadSampler>();
            if (road == null)
            {
                Debug.LogError("BusSim: create the road first.");
                return;
            }

            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            if (spawner == null)
            {
                GameObject go = new GameObject(SpawnerName);
                Undo.RegisterCreatedObjectUndo(go, "Create Obstacle Spawner");
                spawner = go.AddComponent<ObstacleSpawner>();
                go.AddComponent<DebugGizmos>();
            }
            if (spawner.GetComponent<RunLogger>() == null)
            {
                spawner.gameObject.AddComponent<RunLogger>();
            }

            DifficultyProfile profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(DefaultProfilePath);
            if (profile == null)
            {
                ObstacleContentBuilder.BuildAll();
                profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(DefaultProfilePath);
            }

            CarController bus = Object.FindAnyObjectByType<CarController>();
            RoadZones zones = road.GetComponent<RoadZones>();
            spawner.Configure(road, bus != null ? bus.transform : null, profile, zones);
            if (bus != null && bus.Settings != null)
            {
                spawner.SetVehicleGeometry(bus.Settings.FrontOffset, bus.Settings.RequiredCorridor);
            }
            spawner.PreviewPlan();

            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            Selection.activeObject = spawner.gameObject;
        }

        [MenuItem("BusSim/Obstacles/Preview Plan")]
        public static void PreviewPlan()
        {
            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            if (spawner != null)
            {
                spawner.PreviewPlan();
                SceneView.RepaintAll();
            }
        }
    }
}
