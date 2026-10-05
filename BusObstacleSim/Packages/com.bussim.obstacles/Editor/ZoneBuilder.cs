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
        private const float BusStopStart = 380f;
        private const float BusStopEnd = 440f;
        private const float SchoolStart = 600f;
        private const float SchoolEnd = 800f;
        private const float PatchLength = 12f;
        private const float MarkingWidth = 0.15f;
        private const float BayWidth = 2.6f;
        private const float PatchLift = 0.012f;
        private const float MarkingLift = 0.014f;

        [MenuItem("BusSim/Road/Create Default Zones")]
        public static void CreateZones()
        {
            RoadSampler road = Object.FindAnyObjectByType<RoadSampler>();
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
            List<RoadZone> zoneList = new List<RoadZone>
            {
                new RoadZone(ZoneType.BusStop, BusStopStart, BusStopEnd),
                new RoadZone(ZoneType.School, SchoolStart, SchoolEnd)
            };
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
            BuildBusStop(road, root, half);
            BuildSchoolZone(road, root, half);

            EditorUtility.SetDirty(zones);
            EditorSceneManager.MarkSceneDirty(road.gameObject.scene);
            Debug.Log($"BusSim: zones created. Bus stop {BusStopStart}-{BusStopEnd} m, school {SchoolStart}-{SchoolEnd} m.");
        }

        private static void BuildBusStop(RoadSampler road, Transform root, float half)
        {
            Material yellow = ObstacleContentBuilder.Mat("ZoneYellow", new Color(0.98f, 0.8f, 0.05f), 0.3f);
            float inner = -half + BayWidth;
            Ribbon(road, root, "BayEdge", yellow, BusStopStart, BusStopEnd, inner, MarkingWidth, MarkingLift);
            Ribbon(road, root, "BayStart", yellow, BusStopStart, BusStopStart + MarkingWidth, -half + BayWidth * 0.5f, BayWidth, MarkingLift);
            Ribbon(road, root, "BayEnd", yellow, BusStopEnd - MarkingWidth, BusStopEnd, -half + BayWidth * 0.5f, BayWidth, MarkingLift);
            Ribbon(road, root, "BusText", yellow, (BusStopStart + BusStopEnd) * 0.5f - 1.5f, (BusStopStart + BusStopEnd) * 0.5f + 1.5f,
                -half + BayWidth * 0.5f, 0.9f, MarkingLift);

            float shelterS = (BusStopStart + BusStopEnd) * 0.5f;
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

        private static void BuildSchoolZone(RoadSampler road, Transform root, float half)
        {
            // Red road surface patches mark the start and end of the school zone. [Likely Singapore practice]
            Material red = ObstacleContentBuilder.Mat("SchoolRed", new Color(0.62f, 0.12f, 0.1f), 0.2f);
            Ribbon(road, root, "SchoolStartPatch", red, SchoolStart, SchoolStart + PatchLength, 0f, half * 2f, PatchLift);
            Ribbon(road, root, "SchoolEndPatch", red, SchoolEnd - PatchLength, SchoolEnd, 0f, half * 2f, PatchLift);

            Material post = ObstacleContentBuilder.Mat("DarkMetal", new Color(0.15f, 0.15f, 0.16f), 0.5f);
            Material board = ObstacleContentBuilder.Mat("SchoolSign", new Color(0.95f, 0.85f, 0.1f), 0.4f);
            float signT = half + road.Settings.footpathWidth - 0.3f;
            foreach (float side in new[] { -1f, 1f })
            {
                Transform sign = Frame(road, root, "SchoolSign" + side, SchoolStart - 5f, side * signT, road.Settings.kerbHeight);
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
