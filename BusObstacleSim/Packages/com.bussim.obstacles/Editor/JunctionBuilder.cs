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
    /// Adds T-junctions on the left of the main road: a side road leaving at right angles, curved kerb
    /// returns, a give-way line, and a Junction zone that obstacles are kept out of.
    /// </summary>
    public static class JunctionBuilder
    {
        public const string SideRoadPrefix = "SideRoad";
        private const string SettingsPath = "Assets/_Project/Data/SideRoadSettings.asset";
        private const string MarkingsName = "JunctionMarkings";

        // Where each side road leaves the main road (metres along it). Kept clear of the bus stops and school zones.
        private static readonly float[] JunctionPositions = { 520f, 1250f };
        private const float SideRoadLength = 160f;
        // Whole metres, so the kerb gap lines up with the road mesh sections (meshStep is 1 m).
        private const float GapHalfLength = 12f;
        private const float GiveWayStart = 2.5f;
        private const float GiveWayDepth = 0.4f;
        private const float GiveWayInset = 0.2f;

        /// <summary>True for the side roads, false for the main road.</summary>
        public static bool IsSideRoad(GameObject road)
        {
            return road.name.StartsWith(SideRoadPrefix);
        }

        [MenuItem("BusSim/Road/Create T-Junctions")]
        public static void CreateJunctions()
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

            foreach (RoadSampler existing in Object.FindObjectsByType<RoadSampler>())
            {
                if (IsSideRoad(existing.gameObject))
                {
                    Undo.DestroyObjectImmediate(existing.gameObject);
                }
            }

            // Side road settings: a copy of the main road's, narrowed to two lanes with no oncoming carriageway.
            RoadSettings sideSettings = EditorAssetUtil.LoadOrCreate<RoadSettings>(SettingsPath);
            EditorUtility.CopySerialized(mainSettings, sideSettings);
            sideSettings.oncomingLaneCount = 0;
            sideSettings.roadLength = SideRoadLength;
            EditorUtility.SetDirty(sideSettings);

            List<JunctionSpec> specs = new List<JunctionSpec>();
            List<RoadSampler> sides = new List<RoadSampler>();
            float halfWidth = mainSettings.HalfRoadWidth;
            for (int i = 0; i < JunctionPositions.Length; i++)
            {
                float junctionS = JunctionPositions[i];
                if (junctionS + GapHalfLength + SideRoadLength * 0.1f > main.Length)
                {
                    continue;
                }

                main.GetFrame(junctionS, out _, out _, out Vector3 right);
                Vector3 origin = main.GetPoint(junctionS, -halfWidth);
                Vector3 outward = -right;

                GameObject side = new GameObject(i == 0 ? SideRoadPrefix : SideRoadPrefix + (i + 1));
                Undo.RegisterCreatedObjectUndo(side, "Create T-Junction");
                RoadSampler sideSampler = side.AddComponent<RoadSampler>();
                RoadMeshBuilder sideBuilder = side.AddComponent<RoadMeshBuilder>();
                SplineContainer container = side.GetComponent<SplineContainer>();
                Spline spline = container.Splines[0];
                spline.Clear();
                spline.Add(new BezierKnot((float3)origin), TangentMode.Linear);
                spline.Add(new BezierKnot((float3)(origin + outward * SideRoadLength)), TangentMode.Linear);

                sideSampler.Settings = sideSettings;
                sideBuilder.SetMaterials(materials.Asphalt, materials.Marking, materials.Footpath, materials.Ground, materials.Grass);

                JunctionSpec spec = new JunctionSpec(junctionS, sideSettings.HalfRoadWidth, GapHalfLength);
                sideBuilder.Configure(false, true, spec.Radius, null);
                sideBuilder.Rebuild();
                AddGiveWayLine(side.transform, sideSampler, sideSettings, materials.Marking);

                specs.Add(spec);
                sides.Add(sideSampler);
            }

            mainBuilder.Configure(true, true, 0f, specs);
            mainBuilder.Rebuild();
            AddZones(main, specs);

            EditorSceneManager.MarkSceneDirty(main.gameObject.scene);
            Debug.Log($"BusSim: {specs.Count} T-junctions, side roads {SideRoadLength} m long, kerb gap {GapHalfLength * 2f} m.");
        }

        private static RoadSampler FindMainRoad()
        {
            foreach (RoadSampler sampler in Object.FindObjectsByType<RoadSampler>())
            {
                if (!IsSideRoad(sampler.gameObject))
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

        private static void AddZones(RoadSampler main, List<JunctionSpec> specs)
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
            foreach (JunctionSpec spec in specs)
            {
                list.Add(new RoadZone(ZoneType.Junction, spec.s - spec.gapHalfLength, spec.s + spec.gapHalfLength));
            }
            zones.SetZones(list);
            EditorUtility.SetDirty(zones);
        }
    }
}
