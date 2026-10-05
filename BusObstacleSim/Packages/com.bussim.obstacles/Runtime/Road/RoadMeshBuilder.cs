using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusSim.Road
{
    /// <summary>
    /// Builds the road meshes (asphalt, kerbs, footpaths, lane markings, ground) from the spline.
    /// Parts are child objects. Rebuild is safe to call repeatedly.
    /// </summary>
    [RequireComponent(typeof(RoadSampler))]
    public class RoadMeshBuilder : MonoBehaviour
    {
        private const string AsphaltName = "Asphalt";
        private const string FootpathName = "Footpaths";
        private const string MarkingName = "Markings";
        private const string GroundName = "Ground";
        private const string GuardName = "EdgeGuard";

        [SerializeField] private Material asphaltMaterial;
        [SerializeField] private Material markingMaterial;
        [SerializeField] private Material footpathMaterial;
        [SerializeField] private Material groundMaterial;
        [Tooltip("Adds mesh colliders to asphalt, footpaths and ground. Markings never collide.")]
        [SerializeField] private bool addColliders = true;

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
            float footpathOuter = halfWidth + settings.footpathWidth;

            MeshData asphalt = new MeshData();
            AddStrip(asphalt, -halfWidth, 0f, halfWidth, 0f, false);
            CreatePart(AsphaltName, asphalt.ToMesh(AsphaltName), asphaltMaterial, true, addColliders);

            MeshData footpaths = new MeshData();
            AddStrip(footpaths, halfWidth, 0f, halfWidth, kerbTop, true);
            AddStrip(footpaths, halfWidth, kerbTop, footpathOuter, kerbTop, false);
            AddStrip(footpaths, footpathOuter, kerbTop, footpathOuter, groundY, true);
            AddStrip(footpaths, -halfWidth, kerbTop, -halfWidth, 0f, true);
            AddStrip(footpaths, -footpathOuter, kerbTop, -halfWidth, kerbTop, false);
            AddStrip(footpaths, -footpathOuter, groundY, -footpathOuter, kerbTop, true);
            CreatePart(FootpathName, footpaths.ToMesh(FootpathName), footpathMaterial, true, addColliders);

            if (addColliders)
            {
                // Invisible walls facing the road at the outer footpath edges. Vehicles may mount
                // the kerb onto the footpath but cannot drive off into the surroundings.
                MeshData guard = new MeshData();
                AddStrip(guard, footpathOuter, groundY, footpathOuter, settings.edgeGuardHeight, true);
                AddStrip(guard, -footpathOuter, settings.edgeGuardHeight, -footpathOuter, groundY, true);
                GameObject guardPart = new GameObject(GuardName);
                guardPart.transform.SetParent(transform, false);
                guardPart.isStatic = true;
                Mesh guardMesh = guard.ToMesh(GuardName);
                guardPart.AddComponent<MeshFilter>().sharedMesh = guardMesh;
                guardPart.AddComponent<MeshCollider>().sharedMesh = guardMesh;
            }

            CreatePart(MarkingName, BuildMarkings().ToMesh(MarkingName), markingMaterial, false, false);
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

        // Rails A and B are (t, dy) pairs, dy is height relative to the road surface. Winding is
        // chosen so the face looks toward: up for horizontal strips with tA < tB, toward the
        // road for kerb faces listed bottom to top on the right side.
        private void AddStrip(MeshData mesh, float tA, float dyA, float tB, float dyB, bool vertical)
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
            AddStripTriangles(mesh, baseIndex, count - 1);
        }

        private Vector3 SectionPoint(int index, float t, float dy)
        {
            Vector3 point = sectionCentres[index] + sectionRights[index] * t;
            point.y += dy;
            return point;
        }

        private static void AddStripTriangles(MeshData mesh, int baseIndex, int segmentCount)
        {
            for (int i = 0; i < segmentCount; i++)
            {
                int a0 = baseIndex + i * 2;
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
        }

        private MeshData BuildMarkings()
        {
            MeshData mesh = new MeshData();
            float length = sampler.Length;
            float halfWidth = settings.HalfRoadWidth;
            float edgeT = halfWidth - settings.edgeLineInset - settings.markingWidth * 0.5f;

            AddRibbon(mesh, 0f, length, -edgeT);
            AddRibbon(mesh, 0f, length, edgeT);

            float period = settings.dashLength + settings.dashGap;
            for (int boundary = 1; boundary < settings.laneCount; boundary++)
            {
                float t = -halfWidth + boundary * settings.laneWidth;
                for (float s = 0f; s < length; s += period)
                {
                    AddRibbon(mesh, s, Mathf.Min(s + settings.dashLength, length), t);
                }
            }
            return mesh;
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
            AddStripTriangles(mesh, baseIndex, segments);
        }

        private MeshData BuildGround()
        {
            Vector3 min = sectionCentres[0];
            Vector3 max = sectionCentres[0];
            for (int i = 1; i < sectionCentres.Length; i++)
            {
                min = Vector3.Min(min, sectionCentres[i]);
                max = Vector3.Max(max, sectionCentres[i]);
            }

            float pad = settings.groundPadding + settings.HalfRoadWidth + settings.footpathWidth;
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
                if (child.name != AsphaltName && child.name != FootpathName
                    && child.name != MarkingName && child.name != GroundName && child.name != GuardName)
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
