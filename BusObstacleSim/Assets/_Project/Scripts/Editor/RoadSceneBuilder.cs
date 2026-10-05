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

        // Default shape: a gentle S-curve x = A * sin(2 pi z / L) * sin(pi z / L). It starts and
        // ends straight along +Z with no sideways dip. Scaled at creation so the spline length
        // matches RoadSettings.roadLength exactly. Shape data, not a road tunable.
        private const int DefaultKnotCount = 9;
        private const float DefaultShapeLength = 1000f;
        private const float DefaultSwingMetres = 60f;

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

        [MenuItem("BusSim/Road/Reset Spline To Default Shape")]
        public static void ResetSpline()
        {
            RoadSampler sampler = Object.FindAnyObjectByType<RoadSampler>();
            if (sampler == null || sampler.Settings == null)
            {
                Debug.LogWarning("BusSim: no road in the scene.");
                return;
            }

            Undo.RecordObject(sampler.GetComponent<SplineContainer>(), "Reset Road Spline");
            BuildSpline(sampler.GetComponent<SplineContainer>(), sampler.Settings.roadLength);
            sampler.GetComponent<RoadMeshBuilder>().Rebuild();
            EditorSceneManager.MarkSceneDirty(sampler.gameObject.scene);
            Debug.Log($"BusSim: spline reset. Length {sampler.SplineLength:F2} m.");
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
            for (int i = 0; i < DefaultKnotCount; i++)
            {
                float z = DefaultShapeLength * i / (DefaultKnotCount - 1);
                float phase = Mathf.PI * z / DefaultShapeLength;
                float x = DefaultSwingMetres * Mathf.Sin(2f * phase) * Mathf.Sin(phase);
                spline.Add(new BezierKnot(new float3(x, 0f, z) * scale), TangentMode.AutoSmooth);
            }
        }
    }
}
