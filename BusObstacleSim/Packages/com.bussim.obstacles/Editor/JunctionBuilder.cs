using System.Collections.Generic;
using BusSim.Road;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace BusSim.Editor
{
    /// <summary>
    /// Adds a T-junction on the left of the main road: a side road leaving at right angles, curved kerb
    /// returns, a give-way line, and a Junction zone that obstacles are kept out of.
    /// </summary>
    public static class JunctionBuilder
    {
        private const string SideRoadName = "SideRoad";
        private const string SettingsPath = "Assets/_Project/Data/SideRoadSettings.asset";
        private const string MarkingsName = "JunctionMarkings";

        private const float JunctionS = 520f;
        private const float SideRoadLength = 160f;
        // Whole metres, so the kerb gap lines up with the road mesh sections (meshStep is 1 m).
        private const float GapHalfLength = 12f;
        private const float GiveWayStart = 2.5f;
        private const float GiveWayDepth = 0.4f;
        private const float GiveWayInset = 0.2f;

        [MenuItem("BusSim/Road/Create T-Junction")]
        public static void CreateJunction()
        {
            RoadSampler main = FindMainRoad();
            if (main == null || main.Settings == null)
            {
                Debug.LogError("BusSim: create the road first.");
                return;
            }

            RoadMeshBuilder mainBuilder = main.GetComponent<RoadMeshBuilder>();
            RoadSettings mainSettings = main.Settings;
            RoadMaterialFactory.Set materials = RoadMaterialFactory.LoadOrCreate();

            GameObject existing = GameObject.Find(SideRoadName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            // Side road settings: a copy of the main road's, narrowed to two lanes with no oncoming carriageway.
            RoadSettings sideSettings = EditorAssetUtil.LoadOrCreate<RoadSettings>(SettingsPath);
            EditorUtility.CopySerialized(mainSettings, sideSettings);
            sideSettings.oncomingLaneCount = 0;
            sideSettings.roadLength = SideRoadLength;
            EditorUtility.SetDirty(sideSettings);

            float halfWidth = mainSettings.HalfRoadWidth;
            main.GetFrame(JunctionS, out _, out Vector3 forward, out Vector3 right);
            Vector3 origin = main.GetPoint(JunctionS, -halfWidth);
            Vector3 outward = -right;

            GameObject side = new GameObject(SideRoadName);
            Undo.RegisterCreatedObjectUndo(side, "Create T-Junction");
            RoadSampler sideSampler = side.AddComponent<RoadSampler>();
            RoadMeshBuilder sideBuilder = side.AddComponent<RoadMeshBuilder>();
            SplineContainer container = side.GetComponent<SplineContainer>();
            Spline spline = container.Splines[0];
            spline.Clear();
            spline.Add(new BezierKnot((float3)origin), TangentMode.Linear);
            spline.Add(new BezierKnot((float3)(origin + outward * SideRoadLength)), TangentMode.Linear);

            sideSampler.Settings = sideSettings;
            sideBuilder.SetMaterials(materials.Asphalt, materials.Marking, materials.Footpath, materials.Ground);

            JunctionSpec spec = new JunctionSpec(JunctionS, sideSettings.HalfRoadWidth, GapHalfLength);
            sideBuilder.Configure(false, true, spec.Radius, null);
            mainBuilder.Configure(true, true, 0f, new[] { spec });
            mainBuilder.Rebuild();
            sideBuilder.Rebuild();

            AddGiveWayLine(side.transform, sideSampler, sideSettings, materials.Marking);
            AddZone(main, spec);

            EditorSceneManager.MarkSceneDirty(side.scene);
            Debug.Log($"BusSim: T-junction at s = {JunctionS} m, side road {SideRoadLength} m long, kerb gap {GapHalfLength * 2f} m, return radius {spec.Radius:F1} m.");
        }

        private static RoadSampler FindMainRoad()
        {
            foreach (RoadSampler sampler in Object.FindObjectsByType<RoadSampler>())
            {
                if (sampler.gameObject.name != SideRoadName)
                {
                    return sampler;
                }
            }
            return null;
        }

        /// <summary>A solid line across the inbound lane of the side road, where drivers give way.</summary>
        private static void AddGiveWayLine(Transform parent, RoadSampler side, RoadSettings settings, Material marking)
        {
            Transform old = parent.Find(MarkingsName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            Transform markings = new GameObject(MarkingsName).transform;
            markings.SetParent(parent, false);

            // Inbound traffic keeps left of its own travel, which is the +t side of the side road's frame.
            float laneWidth = settings.laneWidth;
            float width = laneWidth - 2f * GiveWayInset;
            ZoneBuilder.Ribbon(side, markings, "GiveWayLine", marking, GiveWayStart, GiveWayStart + GiveWayDepth, laneWidth * 0.5f, width, settings.markingLift);
        }

        private static void AddZone(RoadSampler main, JunctionSpec spec)
        {
            RoadZones zones = main.GetComponent<RoadZones>();
            if (zones == null)
            {
                zones = Undo.AddComponent<RoadZones>(main.gameObject);
            }

            List<RoadZone> list = new List<RoadZone>();
            foreach (RoadZone zone in zones.Zones)
            {
                if (zone.type != ZoneType.Junction)
                {
                    list.Add(zone);
                }
            }
            list.Add(new RoadZone(ZoneType.Junction, spec.s - spec.gapHalfLength, spec.s + spec.gapHalfLength));
            zones.SetZones(list);
            EditorUtility.SetDirty(zones);
        }
    }
}
