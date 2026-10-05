using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>Procedural placeholder meshes, saved as assets so prefabs can reference them.</summary>
    internal static class ProceduralMeshes
    {
        public const string MeshFolder = "Assets/_Project/Meshes";

        /// <summary>
        /// Traffic cone as a frustum with a reflective band. Submesh 0 is the cone body,
        /// submesh 1 is the band between bandLow and bandHigh (fractions of the height).
        /// </summary>
        public static Mesh TrafficCone(float height, float baseRadius, float topRadius, float bandLow, float bandHigh, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> body = new List<int>();
            List<int> band = new List<int>();

            float[] heights = { 0f, bandLow * height, bandHigh * height, height };
            for (int part = 0; part < 3; part++)
            {
                AddFrustumSide(vertices, normals, part == 1 ? band : body,
                    heights[part], heights[part + 1], height, baseRadius, topRadius, segments);
            }

            int centre = vertices.Count;
            vertices.Add(new Vector3(0f, height, 0f));
            normals.Add(Vector3.up);
            int ringStart = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                vertices.Add(new Vector3(Mathf.Cos(angle) * topRadius, height, Mathf.Sin(angle) * topRadius));
                normals.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++)
            {
                body.Add(centre);
                body.Add(ringStart + i + 1);
                body.Add(ringStart + i);
            }

            Mesh mesh = new Mesh { name = "TrafficCone" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(body, 0);
            mesh.SetTriangles(band, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFrustumSide(
            List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
            float y0, float y1, float height, float baseRadius, float topRadius, int segments)
        {
            float r0 = Mathf.Lerp(baseRadius, topRadius, y0 / height);
            float r1 = Mathf.Lerp(baseRadius, topRadius, y1 / height);
            float slope = (baseRadius - topRadius) / height;
            int start = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector3 normal = new Vector3(cos, slope, sin).normalized;
                vertices.Add(new Vector3(cos * r0, y0, sin * r0));
                vertices.Add(new Vector3(cos * r1, y1, sin * r1));
                normals.Add(normal);
                normals.Add(normal);
            }
            for (int i = 0; i < segments; i++)
            {
                int bottom = start + i * 2;
                int top = bottom + 1;
                int nextBottom = bottom + 2;
                int nextTop = bottom + 3;
                triangles.Add(bottom);
                triangles.Add(top);
                triangles.Add(nextTop);
                triangles.Add(bottom);
                triangles.Add(nextTop);
                triangles.Add(nextBottom);
            }
        }

        /// <summary>Saves the mesh at Meshes/name.asset, replacing data in place so the GUID stays.</summary>
        public static Mesh SaveMesh(Mesh mesh, string assetName)
        {
            EditorAssetUtil.EnsureFolder(MeshFolder);
            string path = $"{MeshFolder}/{assetName}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                mesh.name = assetName;
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = assetName;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
