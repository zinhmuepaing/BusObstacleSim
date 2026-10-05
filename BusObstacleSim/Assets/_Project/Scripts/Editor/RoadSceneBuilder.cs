using BusSim.Road;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

namespace BusSim.Editor
{
    /// <summary>Menu commands that create the road in the open scene and set up the sky.</summary>
    public static class RoadSceneBuilder
    {
        private const string RoadObjectName = "Road";
        private const string SettingsPath = "Assets/_Project/Data/RoadSettings.asset";
        private const string SkyMaterialPath = RoadMaterialFactory.MaterialFolder + "/Sky_Procedural.mat";
        private const string SkyShaderName = "Skybox/Procedural";

        // Gentle S-curve heading +Z from the origin. Scaled at creation so the length matches
        // RoadSettings.roadLength exactly. Shape data, not a tunable.
        private static readonly float3[] DefaultKnots =
        {
            new float3(0f, 0f, 0f),
            new float3(0f, 0f, 150f),
            new float3(40f, 0f, 330f),
            new float3(40f, 0f, 520f),
            new float3(-10f, 0f, 720f),
            new float3(-10f, 0f, 880f),
            new float3(-10f, 0f, 1000f)
        };

        [MenuItem("BusSim/Road/Create Road In Scene")]
        public static void CreateRoad()
        {
            RoadSampler existing = Object.FindAnyObjectByType<RoadSampler>();
            if (existing != null)
            {
                Debug.LogWarning("BusSim: a road already exists in this scene. Delete it first to recreate.", existing);
                Selection.activeObject = existing.gameObject;
                return;
            }

            RoadSettings settings = EditorAssetUtil.LoadOrCreate<RoadSettings>(SettingsPath);
            RoadMaterialFactory.Set materials = RoadMaterialFactory.LoadOrCreate();

            GameObject road = new GameObject(RoadObjectName);
            Undo.RegisterCreatedObjectUndo(road, "Create Road");
            RoadSampler sampler = road.AddComponent<RoadSampler>();
            RoadMeshBuilder builder = road.AddComponent<RoadMeshBuilder>();
            SplineContainer container = road.GetComponent<SplineContainer>();

            sampler.Settings = settings;
            builder.SetMaterials(materials.Asphalt, materials.Marking, materials.Footpath, materials.Ground);
            BuildSpline(container, settings.roadLength);
            builder.Rebuild();

            EditorUtility.SetDirty(road);
            EditorSceneManager.MarkSceneDirty(road.scene);
            Selection.activeObject = road;
            Debug.Log($"BusSim: road created. Spline length {sampler.SplineLength:F2} m, usable length {sampler.Length:F2} m.");
        }

        [MenuItem("BusSim/Road/Rebuild Road Meshes")]
        public static void RebuildRoad()
        {
            RoadMeshBuilder builder = Object.FindAnyObjectByType<RoadMeshBuilder>();
            if (builder == null)
            {
                Debug.LogWarning("BusSim: no RoadMeshBuilder in the scene.");
                return;
            }
            builder.Rebuild();
            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
        }

        [MenuItem("BusSim/Road/Setup Procedural Sky")]
        public static void SetupSky()
        {
            EditorAssetUtil.EnsureFolder(RoadMaterialFactory.MaterialFolder);
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find(SkyShaderName)) { name = "Sky_Procedural" };
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }

            Light sun = FindDirectionalLight();
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        private static Light FindDirectionalLight()
        {
            foreach (Light light in Object.FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }
            return null;
        }

        private static void BuildSpline(SplineContainer container, float targetLength)
        {
            Spline spline = container.Splines.Count > 0 ? container.Splines[0] : container.AddSpline();

            FillSpline(spline, 1f);
            float scale = targetLength / spline.GetLength();
            FillSpline(spline, scale);
        }

        private static void FillSpline(Spline spline, float scale)
        {
            spline.Clear();
            foreach (float3 knot in DefaultKnots)
            {
                spline.Add(new BezierKnot(knot * scale), TangentMode.AutoSmooth);
            }
        }
    }
}
