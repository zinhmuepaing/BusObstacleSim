using System;
using System.Diagnostics;
using BusSim.Spawning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BusSim.Editor
{
    /// <summary>
    /// Batch-mode check that the package works in a fresh project:
    /// Unity -batchmode -projectPath P -executeMethod BusSim.Editor.PackageSmokeTest.Run
    /// Builds the demo scene, plans a run twice from one seed, saves the scene and exits with 0 on success.
    /// </summary>
    public static class PackageSmokeTest
    {
        private const int Seed = 4242;
        private const string ScenePath = "Assets/BusSimDemo.unity";

        public static void Run()
        {
            Stopwatch watch = Stopwatch.StartNew();
            int exitCode = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                DemoSceneBuilder.CreateDemoScene();

                ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
                if (spawner == null)
                {
                    throw new InvalidOperationException("no spawner in the demo scene");
                }

                SpawnPlan first = spawner.BuildPlan(Seed);
                int firstCount = first.Events.Count;
                string firstSummary = first.Summary();
                SpawnPlan second = spawner.BuildPlan(Seed);
                Debug.Log($"BUSSIM_SMOKE plan: {firstSummary}");
                if (firstCount == 0)
                {
                    exitCode = 2;
                }
                if (second.Summary() != firstSummary)
                {
                    exitCode = 3;
                }

                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
                Debug.Log($"BUSSIM_SMOKE {(exitCode == 0 ? "OK" : "FAILED code " + exitCode)} in {watch.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception exception)
            {
                Debug.LogError($"BUSSIM_SMOKE FAILED: {exception}");
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }
    }
}
