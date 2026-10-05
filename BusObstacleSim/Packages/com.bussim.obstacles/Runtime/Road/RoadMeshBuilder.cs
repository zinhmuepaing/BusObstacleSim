using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusSim.Road
{
    /// <summary>
    /// Builds the road meshes from the spline: asphalt (driven and oncoming carriageways), kerbs, footpaths,
    /// the grassed median, lane markings, ground, invisible edge guards, and the curved kerb returns where
    /// a side road joins on the left. Parts are child objects. Rebuild is safe to call repeatedly.
    /// The spline is the centreline of the DRIVEN carriageway; the oncoming carriageway and median lie to
    /// its right (positive t), footpaths outside both.
    /// </summary>
    [RequireComponent(typeof(RoadSampler))]
    public class RoadMeshBuilder : MonoBehaviour
    {
        private const string AsphaltName = "Asphalt";
        private const string FootpathName = "Footpaths";
        private const string MarkingName = "Markings";
        private const string GroundName = "Ground";
        private const string GuardName = "EdgeGuard";
        private const string MedianName = "Median";
        private const string MedianGrassName = "MedianGrass";
        private const int ArcSegments = 10;
        private const float MinRibbonLength = 0.1f;

        [SerializeField] private Material asphaltMaterial;
        [SerializeField] private Material markingMaterial;
        [SerializeField] private Material footpathMaterial;
        [SerializeField] private Material groundMaterial;
        [Tooltip("Adds mesh colliders to asphalt, footpaths and median. Markings never collide.")]
        [SerializeField] private bool addColliders = true;
        [SerializeField] private bool buildGround = true;
        [SerializeField] private bool buildGuards = true;
        [Tooltip("Footpaths, guard walls and edge lines start at this distance along the road. Side roads use this to leave room for the kerb returns.")]
        [SerializeField, Min(0f)] private float edgeStartS;
        [SerializeField] private List<JunctionSpec> junctions = new List<JunctionSpec>();

        private enum StripKind
        {
            Surface,
            LeftEdge,
            RightEdge
        }

        private RoadSampler sampler;
        private RoadSettings settings;
        private Vector3[] sectionCentres;
        private Vector3[] sectionRights;
        private float[] sectionS;

        private sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public Mesh ToMesh(string meshName)
            {
                Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public void SetMaterials(Material asphalt, Material marking, Material footpath, Material ground)
        {
            asphaltMaterial = asphalt;
            markingMaterial = marking;
            footpathMaterial = footpath;
            groundMaterial = ground;
        }

        /// <summary>Options for roads that are not the main road (side roads) and the junctions joining this road.</summary>
        public void Configure(bool ground, bool guards, float startS, IEnumerable<JunctionSpec> junctionSpecs)
        {
            buildGround = ground;
            buildGuards = guards;
            edgeStartS = startS;
            junctions.Clear();
            if (junctionSpecs != null)
            {
                junctions.AddRange(junctionSpecs);
            }
        }

        [ContextMenu("Rebuild Road")]
        public void Rebuild()
        {
            sampler = GetComponent<RoadSampler>();
            settings = sampler.Settings;
            if (settings == null)
            {
                Debug.LogError("RoadMeshBuilder: RoadSampler has no RoadSettings assigned.", this);
                return;
            }

            ClearParts();
            SampleSections();

            float halfWidth = settings.HalfRoadWidth;
            float kerbTop = settings.kerbHeight;
            float groundY = -settings.groundDrop;
            float leftOuter = -settings.LeftFootpathOuterT;
            float rightInner = settings.RightFootpathInnerT;
            float rightOuter = settings.RightFootpathOuterT;

            MeshData asphalt = new MeshData();
            AddStrip(asphalt, -halfWidth, 0f, halfWidth, 0f, false, StripKind.Surface);
            if (settings.HasOncoming)
            {
                AddStrip(asphalt, settings.OncomingInnerT, 0f, settings.OncomingOuterT, 0f, false, StripKind.Surface);
            }

            MeshData footpaths = new MeshData();
            // Left footpath (gapped at junctions): kerb face, top, outer face.
            AddStrip(footpaths, -halfWidth, kerbTop, -halfWidth, 0f, true, StripKind.LeftEdge);
            AddStrip(footpaths, -leftOuter, kerbTop, -halfWidth, kerbTop, false, StripKind.LeftEdge);
            AddStrip(footpaths, -leftOuter, groundY, -leftOuter, kerbTop, true, StripKind.LeftEdge);
            // Right footpath beyond the oncoming carriageway (or beside the driven one if there is none).
            AddStrip(footpaths, rightInner, 0f, rightInner, kerbTop, true, StripKind.RightEdge);
            AddStrip(footpaths, rightInner, kerbTop, rightOuter, kerbTop, false, StripKind.RightEdge);
            AddStrip(footpaths, rightOuter, kerbTop, rightOuter, groundY, true, StripKind.RightEdge);

            foreach (JunctionSpec junction in junctions)
            {
                AddJunction(asphalt, footpaths, junction);
            }

            CreatePart(AsphaltName, asphalt.ToMesh(AsphaltName), asphaltMaterial, true, addColliders);
            CreatePart(FootpathName, footpaths.ToMesh(FootpathName), footpathMaterial, true, addColliders);

            if (settings.HasOncoming)
            {
                BuildMedian(halfWidth, kerbTop);
            }

            if (buildGuards && addColliders)
            {
                // Invisible walls facing the road at the outer footpath edges. Vehicles may mount
                // the kerb onto the footpath but cannot drive off into the surroundings.
                MeshData guard = new MeshData();
                AddStrip(guard, rightOuter, groundY, rightOuter, settings.edgeGuardHeight, true, StripKind.RightEdge);
                AddStrip(guard, -leftOuter, settings.edgeGuardHeight, -leftOuter, groundY, true, StripKind.LeftEdge);
                GameObject guardPart = new GameObject(GuardName);
                guardPart.transform.SetParent(transform, false);
                guardPart.isStatic = true;
                Mesh guardMesh = guard.ToMesh(GuardName);
                guardPart.AddComponent<MeshFilter>().sharedMesh = guardMesh;
                guardPart.AddComponent<MeshCollider>().sharedMesh = guardMesh;
            }

            CreatePart(MarkingName, BuildMarkings().ToMesh(MarkingName), markingMaterial, false, false);

            if (buildGround)
            {
                Mesh groundMesh = BuildGround().ToMesh(GroundName);
                GameObject ground = CreatePart(GroundName, groundMesh, groundMaterial, true, false);
                if (addColliders)
                {
                    // One huge quad makes a poor mesh collider, so the ground uses a flat box.
                    Bounds bounds = groundMesh.bounds;
                    BoxCollider box = ground.AddComponent<BoxCollider>();
                    box.size = new Vector3(bounds.size.x, settings.groundColliderThickness, bounds.size.z);
                    box.center = new Vector3(bounds.center.x, bounds.center.y - settings.groundColliderThickness * 0.5f, bounds.center.z);
                }
            }
        }

        private void BuildMedian(float halfWidth, float kerbTop)
        {
            float inner = settings.OncomingInnerT;
            float cap = settings.medianCapWidth;

            MeshData paved = new MeshData();
            AddStrip(paved, halfWidth, 0f, halfWidth, kerbTop, true, StripKind.Surface);
            AddStrip(paved, halfWidth, kerbTop, halfWidth + cap, kerbTop, false, StripKind.Surface);
            AddStrip(paved, inner - cap, kerbTop, inner, kerbTop, false, StripKind.Surface);
            AddStrip(paved, inner, kerbTop, inner, 0f, true, StripKind.Surface);
            CreatePart(MedianName, paved.ToMesh(MedianName), footpathMaterial, true, addColliders);

            MeshData grass = new MeshData();
            float top = kerbTop - settings.medianGrassSink;
            AddStrip(grass, halfWidth + cap, top, inner - cap, top, false, StripKind.Surface);
            CreatePart(MedianGrassName, grass.ToMesh(MedianGrassName), groundMaterial, true, addColliders);
        }

        private void SampleSections()
        {
            float length = sampler.Length;
            int count = Mathf.CeilToInt(length / settings.meshStep) + 1;
            sectionCentres = new Vector3[count];
            sectionRights = new Vector3[count];
            sectionS = new float[count];
            for (int i = 0; i < count; i++)
            {
                float s = Mathf.Min(i * settings.meshStep, length);
                sampler.GetFrame(s, out sectionCentres[i], out _, out sectionRights[i]);
                sectionS[i] = s;
            }
        }

        /// <summary>
        /// Rails A and B are (t, dy) pairs, dy is height relative to the road surface. Winding is
        /// chosen so the face looks toward: up for horizontal strips with tA less than tB, toward the
        /// road for kerb faces listed bottom to top on the right side. Edge strips skip the start
        /// offset and, on the left, the junction gaps.
        /// </summary>
        private void AddStrip(MeshData mesh, float tA, float dyA, float tB, float dyB, bool vertical, StripKind kind)
        {
            int baseIndex = mesh.Vertices.Count;
            int count = sectionCentres.Length;
            float tile = settings.uvMetresPerTile;
            for (int i = 0; i < count; i++)
            {
                mesh.Vertices.Add(SectionPoint(i, tA, dyA));
                mesh.Vertices.Add(SectionPoint(i, tB, dyB));
                if (vertical)
                {
                    mesh.Uvs.Add(new Vector2(sectionS[i] / tile, dyA / tile));
                    mesh.Uvs.Add(new Vector2(sectionS[i] / tile, dyB / tile));
                }
                else
                {
                    mesh.Uvs.Add(new Vector2(tA / tile, sectionS[i] / tile));
                    mesh.Uvs.Add(new Vector2(tB / tile, sectionS[i] / tile));
                }
            }

            for (int segment = 0; segment < count - 1; segment++)
            {
                if (SkipSegment(kind, segment))
                {
                    continue;
                }
                AddQuad(mesh, baseIndex + segment * 2);
            }
        }

        private bool SkipSegment(StripKind kind, int segment)
        {
            if (kind == StripKind.Surface)
            {
                return false;
            }

            float mid = (sectionS[segment] + sectionS[segment + 1]) * 0.5f;
            if (mid < edgeStartS)
            {
                return true;
            }
            if (kind == StripKind.LeftEdge)
            {
                foreach (JunctionSpec junction in junctions)
                {
                    if (Mathf.Abs(mid - junction.s) < junction.gapHalfLength)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private Vector3 SectionPoint(int index, float t, float dy)
        {
            Vector3 point = sectionCentres[index] + sectionRights[index] * t;
            point.y += dy;
            return point;
        }

        private static void AddQuad(MeshData mesh, int a0)
        {
            int b0 = a0 + 1;
            int a1 = a0 + 2;
            int b1 = a0 + 3;
            mesh.Triangles.Add(a0);
            mesh.Triangles.Add(a1);
            mesh.Triangles.Add(b1);
            mesh.Triangles.Add(a0);
            mesh.Triangles.Add(b1);
            mesh.Triangles.Add(b0);
        }

        // ---------------- junctions ----------------

        /// <summary>
        /// Curved kerb returns at both corners where the side road meets this road's left edge: an asphalt
        /// patch filling the corner, the kerb face along the curve, and a footpath ring around it.
        /// Curves are quadratic Beziers from the main road edge to the side road edge, tangent to both.
        /// </summary>
        private void AddJunction(MeshData asphalt, MeshData pavement, JunctionSpec junction)
        {
            float halfWidth = settings.HalfRoadWidth;
            sampler.GetFrame(junction.s, out _, out _, out Vector3 rightAtJunction);
            Vector3 outward = -rightAtJunction;
            float radius = junction.Radius;

            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 corner = sampler.GetPoint(junction.s + sign * junction.sideHalfWidth, -halfWidth);
                Vector3 start = sampler.GetPoint(junction.s + sign * junction.gapHalfLength, -halfWidth);
                Vector3 end = corner + outward * radius;

                List<Vector3> arc = new List<Vector3>(ArcSegments + 1);
                for (int k = 0; k <= ArcSegments; k++)
                {
                    float u = (float)k / ArcSegments;
                    arc.Add((1f - u) * (1f - u) * start + 2f * (1f - u) * u * corner + u * u * end);
                }

                AddFan(asphalt, corner, arc);
                AddKerbReturn(pavement, corner, arc);
            }
        }

        private void AddKerbReturn(MeshData pavement, Vector3 corner, List<Vector3> arc)
        {
            int count = arc.Count;
            Vector3 mid = arc[count / 2];
            Vector3 awayFromCorner = Vector3.ProjectOnPlane(mid - corner, Vector3.up).normalized;

            List<Vector3> top = new List<Vector3>(count);
            List<Vector3> outerTop = new List<Vector3>(count);
            List<Vector3> outerBottom = new List<Vector3>(count);
            Vector3 kerbRise = Vector3.up * settings.kerbHeight;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = arc[Mathf.Min(i + 1, count - 1)] - arc[Mathf.Max(i - 1, 0)];
                Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
                if (Vector3.Dot(normal, awayFromCorner) < 0f)
                {
                    normal = -normal;
                }
                Vector3 outer = arc[i] + normal * settings.footpathWidth;
                top.Add(arc[i] + kerbRise);
                outerTop.Add(outer + kerbRise);
                outerBottom.Add(outer - Vector3.up * settings.groundDrop);
            }

            AddRailStrip(pavement, arc, top, -awayFromCorner);
            AddRailStrip(pavement, top, outerTop, Vector3.up);
            AddRailStrip(pavement, outerTop, outerBottom, awayFromCorner);
        }

        private Vector2 PlanarUv(Vector3 point)
        {
            return new Vector2(point.x, point.z) / settings.uvMetresPerTile;
        }

        /// <summary>Triangle fan from a centre point over a rim, facing up.</summary>
        private void AddFan(MeshData mesh, Vector3 centre, List<Vector3> rim)
        {
            int baseIndex = mesh.Vertices.Count;
            mesh.Vertices.Add(centre);
            mesh.Uvs.Add(PlanarUv(centre));
            foreach (Vector3 point in rim)
            {
                mesh.Vertices.Add(point);
                mesh.Uvs.Add(PlanarUv(point));
            }

            for (int i = 0; i < rim.Count - 1; i++)
            {
                bool facesUp = Vector3.Cross(rim[i] - centre, rim[i + 1] - centre).y > 0f;
                mesh.Triangles.Add(baseIndex);
                mesh.Triangles.Add(baseIndex + (facesUp ? 1 + i : 2 + i));
                mesh.Triangles.Add(baseIndex + (facesUp ? 2 + i : 1 + i));
            }
        }

        /// <summary>Strip between two equal-length rails, wound so the face looks along `facing`.</summary>
        private void AddRailStrip(MeshData mesh, List<Vector3> railA, List<Vector3> railB, Vector3 facing)
        {
            int baseIndex = mesh.Vertices.Count;
            for (int i = 0; i < railA.Count; i++)
            {
                mesh.Vertices.Add(railA[i]);
                mesh.Vertices.Add(railB[i]);
                mesh.Uvs.Add(PlanarUv(railA[i]));
                mesh.Uvs.Add(PlanarUv(railB[i]));
            }

            for (int i = 0; i < railA.Count - 1; i++)
            {
                int a0 = baseIndex + i * 2;
                int b0 = a0 + 1;
                int a1 = a0 + 2;
                int b1 = a0 + 3;
                Vector3 normal = Vector3.Cross(railA[i + 1] - railA[i], railB[i + 1] - railA[i + 1]);
                if (Vector3.Dot(normal, facing) >= 0f)
                {
                    mesh.Triangles.Add(a0);
                    mesh.Triangles.Add(a1);
                    mesh.Triangles.Add(b1);
                    mesh.Triangles.Add(a0);
                    mesh.Triangles.Add(b1);
                    mesh.Triangles.Add(b0);
                }
                else
                {
                    mesh.Triangles.Add(a0);
                    mesh.Triangles.Add(b1);
                    mesh.Triangles.Add(a1);
                    mesh.Triangles.Add(a0);
                    mesh.Triangles.Add(b0);
                    mesh.Triangles.Add(b1);
                }
            }
        }

        // ---------------- markings ----------------

        private MeshData BuildMarkings()
        {
            MeshData mesh = new MeshData();
            AddCarriagewayMarkings(mesh, -settings.HalfRoadWidth, settings.HalfRoadWidth, settings.laneCount, true);
            if (settings.HasOncoming)
            {
                AddCarriagewayMarkings(mesh, settings.OncomingInnerT, settings.OncomingOuterT, settings.oncomingLaneCount, false);
            }
            return mesh;
        }

        /// <summary>Solid edge lines and dashed lane dividers across one carriageway.</summary>
        private void AddCarriagewayMarkings(MeshData mesh, float tLeft, float tRight, int lanes, bool leftEdgeGapped)
        {
            float length = sampler.Length;
            float inset = settings.edgeLineInset + settings.markingWidth * 0.5f;
            AddEdgeLine(mesh, tLeft + inset, leftEdgeGapped);
            AddEdgeLine(mesh, tRight - inset, false);

            float period = settings.dashLength + settings.dashGap;
            for (int boundary = 1; boundary < lanes; boundary++)
            {
                float t = tLeft + boundary * settings.laneWidth;
                for (float s = 0f; s < length; s += period)
                {
                    AddRibbon(mesh, s, Mathf.Min(s + settings.dashLength, length), t);
                }
            }
        }

        /// <summary>A solid line from the start offset to the end, with the junction gaps cut out if gapped.</summary>
        private void AddEdgeLine(MeshData mesh, float t, bool gapped)
        {
            List<Vector2> pieces = new List<Vector2> { new Vector2(edgeStartS, sampler.Length) };
            if (gapped)
            {
                foreach (JunctionSpec junction in junctions)
                {
                    float gapStart = junction.s - junction.gapHalfLength;
                    float gapEnd = junction.s + junction.gapHalfLength;
                    List<Vector2> next = new List<Vector2>();
                    foreach (Vector2 piece in pieces)
                    {
                        if (gapEnd <= piece.x || gapStart >= piece.y)
                        {
                            next.Add(piece);
                            continue;
                        }
                        if (gapStart > piece.x)
                        {
                            next.Add(new Vector2(piece.x, gapStart));
                        }
                        if (gapEnd < piece.y)
                        {
                            next.Add(new Vector2(gapEnd, piece.y));
                        }
                    }
                    pieces = next;
                }
            }

            foreach (Vector2 piece in pieces)
            {
                if (piece.y - piece.x > MinRibbonLength)
                {
                    AddRibbon(mesh, piece.x, piece.y, t);
                }
            }
        }

        private void AddRibbon(MeshData mesh, float sStart, float sEnd, float tCentre)
        {
            float length = sEnd - sStart;
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / settings.meshStep));
            float halfMarking = settings.markingWidth * 0.5f;
            Vector3 lift = Vector3.up * settings.markingLift;
            int baseIndex = mesh.Vertices.Count;

            for (int i = 0; i <= segments; i++)
            {
                float s = sStart + length * i / segments;
                sampler.GetFrame(s, out Vector3 centre, out _, out Vector3 right);
                mesh.Vertices.Add(centre + right * (tCentre - halfMarking) + lift);
                mesh.Vertices.Add(centre + right * (tCentre + halfMarking) + lift);
                mesh.Uvs.Add(new Vector2(0f, s));
                mesh.Uvs.Add(new Vector2(1f, s));
            }
            for (int i = 0; i < segments; i++)
            {
                AddQuad(mesh, baseIndex + i * 2);
            }
        }

        // ---------------- ground and parts ----------------

        private MeshData BuildGround()
        {
            Vector3 min = sectionCentres[0];
            Vector3 max = sectionCentres[0];
            for (int i = 1; i < sectionCentres.Length; i++)
            {
                min = Vector3.Min(min, sectionCentres[i]);
                max = Vector3.Max(max, sectionCentres[i]);
            }

            float extent = Mathf.Max(settings.RightFootpathOuterT, -settings.LeftFootpathOuterT);
            float pad = settings.groundPadding + extent;
            float y = sectionCentres[0].y - settings.groundDrop;
            float minX = min.x - pad;
            float maxX = max.x + pad;
            float minZ = min.z - pad;
            float maxZ = max.z + pad;
            float tile = settings.groundMetresPerTile;

            MeshData mesh = new MeshData();
            mesh.Vertices.Add(new Vector3(minX, y, minZ));
            mesh.Vertices.Add(new Vector3(minX, y, maxZ));
            mesh.Vertices.Add(new Vector3(maxX, y, maxZ));
            mesh.Vertices.Add(new Vector3(maxX, y, minZ));
            mesh.Uvs.Add(new Vector2(minX / tile, minZ / tile));
            mesh.Uvs.Add(new Vector2(minX / tile, maxZ / tile));
            mesh.Uvs.Add(new Vector2(maxX / tile, maxZ / tile));
            mesh.Uvs.Add(new Vector2(maxX / tile, minZ / tile));
            mesh.Triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            return mesh;
        }

        private GameObject CreatePart(string partName, Mesh mesh, Material material, bool castShadows, bool collide)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(transform, false);
            part.isStatic = true;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (collide)
            {
                part.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return part;
        }

        private void ClearParts()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != AsphaltName && child.name != FootpathName && child.name != MarkingName
                    && child.name != GroundName && child.name != GuardName
                    && child.name != MedianName && child.name != MedianGrassName)
                {
                    continue;
                }

                MeshFilter meshFilter = child.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    DestroyPart(meshFilter.sharedMesh);
                }
                DestroyPart(child.gameObject);
            }
        }

        private static void DestroyPart(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
