using System.Collections.Generic;
using BusSim.Road;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusSim.Editor
{
    /// <summary>
    /// Marks a bus stop and a school zone on the road (RoadZones data plus visuals: bus bay
    /// markings and shelter, red school zone surface patches and signs).
    /// </summary>
    public static class ZoneBuilder
    {
        private const string ZonesObjectName = "Zones";
        private const float BusStopLength = 60f;
        private const float SchoolLength = 200f;
        private const float PatchLength = 12f;

        // Zone start positions along the road (metres). Stops and school zones repeat on a longer map; anything
        // past the end of the road is dropped.
        private static readonly float[] BusStopStarts = { 380f, 1480f, 2420f };
        private static readonly float[] SchoolStarts = { 600f, 1800f };
        private const float MarkingWidth = 0.15f;
        private const float BayWidth = 2.6f;
        private const float PatchLift = 0.012f;
        private const float MarkingLift = 0.014f;

        [MenuItem("BusSim/Road/Create Default Zones")]
        public static void CreateZones()
        {
            RoadSampler road = null;
            foreach (RoadSampler sampler in Object.FindObjectsByType<RoadSampler>())
            {
                if (!JunctionBuilder.IsSideRoad(sampler.gameObject))
                {
                    road = sampler;
                }
            }
            if (road == null || road.Settings == null)
            {
                Debug.LogError("BusSim: create the road first.");
                return;
            }

            RoadZones zones = road.GetComponent<RoadZones>();
            if (zones == null)
            {
                zones = Undo.AddComponent<RoadZones>(road.gameObject);
            }
            List<RoadZone> zoneList = new List<RoadZone>();
            foreach (float start in BusStopStarts)
            {
                if (start + BusStopLength < road.Length)
                {
                    zoneList.Add(new RoadZone(ZoneType.BusStop, start, start + BusStopLength));
                }
            }
            foreach (float start in SchoolStarts)
            {
                if (start + SchoolLength < road.Length)
                {
                    zoneList.Add(new RoadZone(ZoneType.School, start, start + SchoolLength));
                }
            }
            foreach (RoadZone existing in zones.Zones)
            {
                if (existing.type == ZoneType.Junction)
                {
                    zoneList.Add(existing);
                }
            }
            zones.SetZones(zoneList);

            Transform old = road.transform.Find(ZonesObjectName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
            Transform root = new GameObject(ZonesObjectName).transform;
            root.SetParent(road.transform, false);

            float half = road.Settings.HalfRoadWidth;
            int stops = 0;
            int schools = 0;
            foreach (RoadZone zone in zoneList)
            {
                if (zone.type == ZoneType.BusStop)
                {
                    BuildBusStop(road, root, half, zone.sStart, zone.sEnd, stops++);
                }
                else if (zone.type == ZoneType.School)
                {
                    BuildSchoolZone(road, root, half, zone.sStart, zone.sEnd, schools++);
                }
            }

            EditorUtility.SetDirty(zones);
            EditorSceneManager.MarkSceneDirty(road.gameObject.scene);
            Debug.Log($"BusSim: zones created. {stops} bus stops, {schools} school zones.");
        }

        private static void BuildBusStop(RoadSampler road, Transform root, float half, float busStopStart, float busStopEnd, int index)
        {
            Material yellow = ObstacleContentBuilder.Mat("ZoneYellow", new Color(0.98f, 0.8f, 0.05f), 0.3f);
            float inner = -half + BayWidth;
            Ribbon(road, root, "BayEdge", yellow, busStopStart, busStopEnd, inner, MarkingWidth, MarkingLift);
            Ribbon(road, root, "BayStart", yellow, busStopStart, busStopStart + MarkingWidth, -half + BayWidth * 0.5f, BayWidth, MarkingLift);
            Ribbon(road, root, "BayEnd", yellow, busStopEnd - MarkingWidth, busStopEnd, -half + BayWidth * 0.5f, BayWidth, MarkingLift);
            Ribbon(road, root, "BusText", yellow, (busStopStart + busStopEnd) * 0.5f - 1.5f, (busStopStart + busStopEnd) * 0.5f + 1.5f,
                -half + BayWidth * 0.5f, 0.9f, MarkingLift);

            float shelterS = (busStopStart + busStopEnd) * 0.5f;
            float footpathMiddle = half + road.Settings.footpathWidth * 0.5f;
            Transform shelter = Frame(road, root, "Shelter", shelterS, -footpathMiddle, road.Settings.kerbHeight);
            Material metal = ObstacleContentBuilder.Mat("ShelterMetal", new Color(0.4f, 0.42f, 0.45f), 0.6f);
            Material glass = ObstacleContentBuilder.Mat("ShelterGlass", new Color(0.55f, 0.65f, 0.7f), 0.9f);
            Material bench = ObstacleContentBuilder.Mat("BenchWood", new Color(0.45f, 0.3f, 0.18f), 0.2f);
            ObstacleContentBuilder.AddBox(shelter, "Roof", metal, new Vector3(0.1f, 2.6f, 0f), new Vector3(1.8f, 0.1f, 6f));
            ObstacleContentBuilder.AddBox(shelter, "BackPanel", glass, new Vector3(-0.75f, 1.3f, 0f), new Vector3(0.05f, 2.4f, 5.6f));
            foreach (float z in new[] { -2.8f, 2.8f })
            {
                ObstacleContentBuilder.AddBox(shelter, "Post" + z, metal, new Vector3(-0.75f, 1.3f, z), new Vector3(0.1f, 2.6f, 0.1f));
            }
            ObstacleContentBuilder.AddBox(shelter, "Bench", bench, new Vector3(-0.45f, 0.45f, 0f), new Vector3(0.45f, 0.06f, 3f));
            ObstacleContentBuilder.AddBox(shelter, "PolePost", metal, new Vector3(0.75f, 1.3f, 3.6f), new Vector3(0.08f, 2.6f, 0.08f));
            ObstacleContentBuilder.AddBox(shelter, "PoleSign", ObstacleContentBuilder.Mat("BusSignRed", new Color(0.8f, 0.1f, 0.1f), 0.5f),
                new Vector3(0.75f, 2.4f, 3.6f), new Vector3(0.05f, 0.45f, 0.6f));
        }

        private static void BuildSchoolZone(RoadSampler road, Transform root, float half, float schoolStart, float schoolEnd, int index)
        {
            // Red road surface patches mark the start and end of the school zone. [Likely Singapore practice]
            Material red = ObstacleContentBuilder.Mat("SchoolRed", new Color(0.62f, 0.12f, 0.1f), 0.2f);
            Ribbon(road, root, "schoolStartPatch", red, schoolStart, schoolStart + PatchLength, 0f, half * 2f, PatchLift);
            Ribbon(road, root, "schoolEndPatch", red, schoolEnd - PatchLength, schoolEnd, 0f, half * 2f, PatchLift);

            Material post = ObstacleContentBuilder.Mat("DarkMetal", new Color(0.15f, 0.15f, 0.16f), 0.5f);
            Material board = ObstacleContentBuilder.Mat("SchoolSign", new Color(0.95f, 0.85f, 0.1f), 0.4f);
            float signT = half + road.Settings.footpathWidth - 0.3f;
            foreach (float side in new[] { -1f, 1f })
            {
                Transform sign = Frame(road, root, "SchoolSign" + side, schoolStart - 5f, side * signT, road.Settings.kerbHeight);
                ObstacleContentBuilder.AddBox(sign, "Post", post, new Vector3(0f, 1.1f, 0f), new Vector3(0.07f, 2.2f, 0.07f));
                ObstacleContentBuilder.AddBox(sign, "Board", board, new Vector3(0f, 2.1f, -0.05f), new Vector3(0.9f, 0.7f, 0.04f));
            }
        }

        /// <summary>Empty transform at (s, t) aligned with the road (forward along it).</summary>
        private static Transform Frame(RoadSampler road, Transform parent, string objectName, float s, float t, float height)
        {
            road.GetFrame(s, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Transform frame = new GameObject(objectName).transform;
            frame.SetParent(parent, true);
            frame.SetPositionAndRotation(centre + right * t + Vector3.up * height, Quaternion.LookRotation(forward, Vector3.up));
            return frame;
        }

        internal static void Ribbon(RoadSampler road, Transform parent, string objectName, Material material,
            float sStart, float sEnd, float tCentre, float width, float lift)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            int segments = Mathf.Max(1, Mathf.CeilToInt(sEnd - sStart));
            for (int i = 0; i <= segments; i++)
            {
                float s = Mathf.Lerp(sStart, sEnd, (float)i / segments);
                road.GetFrame(s, out Vector3 centre, out _, out Vector3 right);
                vertices.Add(centre + right * (tCentre - width * 0.5f) + Vector3.up * lift);
                vertices.Add(centre + right * (tCentre + width * 0.5f) + Vector3.up * lift);
                if (i > 0)
                {
                    int a0 = (i - 1) * 2;
                    triangles.AddRange(new[] { a0, a0 + 2, a0 + 3, a0, a0 + 3, a0 + 1 });
                }
            }

            Mesh mesh = new Mesh { name = objectName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            GameObject part = new GameObject(objectName);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
