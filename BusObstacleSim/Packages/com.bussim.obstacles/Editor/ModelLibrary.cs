using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Imported CC0 models (see Assets/_Project/ASSETS.md): where they are, what scale makes them
    /// real-world size, and helpers that place them with the pivot at the ground centre. The kits are
    /// modelled at different toy scales, so every category has its own factor.
    /// Behaviour scripts never reference these models, so a model can be swapped without code changes.
    /// </summary>
    internal static class ModelLibrary
    {
        public const string Root = "Assets/_Project/ThirdParty/";
        public const string Cars = Root + "KenneyCars/Models/FBX format/";
        public const string Roads = Root + "KenneyRoads/Models/FBX format/";
        public const string People = Root + "KenneyChars/Models/FBX format/";
        public const string Suburban = Root + "KenneySuburban/Models/FBX format/";
        public const string Commercial = Root + "KenneyCommercial/Models/FBX format/";
        public const string Nature = Root + "KenneyNature/Models/FBX format/";
        public const string Transport = Root + "QuatTransport/Public Transport/FBX/";

        // Cars are 1.5 m wide, 2.55 m long and 1.3 m tall as modelled. Width x1.2 gives a 1.8 m car,
        // and length and height x1.55 keep the wheels round (about 4 m long, 2 m tall: chunky on purpose).
        public static readonly Vector3 CarScale = new Vector3(1.2f, 1.55f, 1.55f);
        public const float CharacterScale = 2.5f;
        public const float ConeScale = 7.8f;
        public const float BarrierScale = 7.7f;
        public const float StreetLightScale = 10f;
        public const float SignScale = 5f;
        public const float PalmScale = 5.3f;
        public const float OakScale = 5.7f;
        public const float HouseScale = 10f;
        public const float ShopScale = 9f;

        public static string Car(string name) => $"{Cars}{name}.fbx";
        public static string Person(string name) => $"{People}{name}.fbx";
        public static string Road(string name) => $"{Roads}{name}.fbx";

        public static bool Exists(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null;
        }

        /// <summary>
        /// Instantiates the model under a wrapper, scaled and turned by yaw, shifted so the bottom
        /// centre of its bounds is at the wrapper's origin. Returns null if the asset is missing.
        /// </summary>
        public static GameObject Spawn(Transform parent, string assetPath, Vector3 scale, float yawDegrees, string objectName)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"BusSim: model not found: {assetPath}");
                return null;
            }

            GameObject wrapper = new GameObject(objectName);
            wrapper.transform.SetParent(parent, false);
            GameObject model = UnityEngine.Object.Instantiate(asset, wrapper.transform);
            model.name = asset.name;
            model.transform.localScale = scale;
            model.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            Bounds bounds = LocalBounds(wrapper.transform, wrapper.transform, null);
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            return wrapper;
        }

        public static GameObject Spawn(Transform parent, string assetPath, float uniformScale, float yawDegrees, string objectName)
        {
            return Spawn(parent, assetPath, Vector3.one * uniformScale, yawDegrees, objectName);
        }

        /// <summary>
        /// Spawns a model scaled so its longest horizontal side is `longestSide` metres. With alignLongAxisToZ
        /// the model is turned so that side runs along the local Z axis (the direction of travel).
        /// </summary>
        public static GameObject SpawnFitted(Transform parent, string assetPath, float longestSide, float yawDegrees, string objectName, bool alignLongAxisToZ)
        {
            Bounds natural = Measure(assetPath, Vector3.one, yawDegrees);
            if (alignLongAxisToZ && natural.size.x > natural.size.z)
            {
                yawDegrees += 90f;
                natural = Measure(assetPath, Vector3.one, yawDegrees);
            }
            float longest = Mathf.Max(natural.size.x, natural.size.z, 0.0001f);
            return Spawn(parent, assetPath, Vector3.one * (longestSide / longest), yawDegrees, objectName);
        }

        /// <summary>Size of a model after scale and yaw, without leaving anything in the scene.</summary>
        public static Bounds Measure(string assetPath, Vector3 scale, float yawDegrees)
        {
            GameObject probe = new GameObject("ProbeRoot");
            try
            {
                GameObject wrapper = Spawn(probe.transform, assetPath, scale, yawDegrees, "Probe");
                return wrapper == null ? new Bounds(Vector3.zero, Vector3.one) : LocalBounds(wrapper.transform, wrapper.transform, null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// Bounds of every mesh under root, expressed in the local space of `space`. Renderers whose name
        /// the filter rejects are skipped (for example the wheels when measuring a car body).
        /// </summary>
        public static Bounds LocalBounds(Transform root, Transform space, Func<Renderer, bool> include)
        {
            Bounds result = new Bounds();
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (include != null && !include(renderer))
                {
                    continue;
                }

                Mesh mesh = null;
                if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                else if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                }
                if (mesh == null)
                {
                    continue;
                }

                Bounds local = mesh.bounds;
                foreach (Vector3 corner in Corners(local))
                {
                    Vector3 point = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!any)
                    {
                        result = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }
            return result;
        }

        public static Transform FindDeep(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>Fits a BoxCollider on `host` around the meshes under `visualRoot`, in host space.</summary>
        public static void AddFittedBoxCollider(GameObject host, Transform visualRoot, PhysicsMaterial material, Func<Renderer, bool> include = null)
        {
            Bounds bounds = LocalBounds(visualRoot, host.transform, include);
            BoxCollider box = host.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            box.sharedMaterial = material;
        }

        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                yield return c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            }
        }
    }
}
