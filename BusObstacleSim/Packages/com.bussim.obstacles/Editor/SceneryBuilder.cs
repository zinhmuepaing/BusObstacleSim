using System;
using System.Collections.Generic;
using BusSim.Road;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BusSim.Editor
{
    /// <summary>
    /// Dresses the roads: street lights, rows of trees, houses and shops, tall flats (HDB-style blocks) and a few
    /// towers in the distance. Placement is seeded, so rebuilding gives the same scene. Scenery has no colliders;
    /// the edge guards keep vehicles on the road.
    /// </summary>
    public static class SceneryBuilder
    {
        private const string RootName = "Scenery";
        private const string SideRoadName = "SideRoad";
        private const string FacadeMaterialPrefix = "Hdb_";
        private const int FixedSeed = 20261005;

        private const float LampSpacing = 36f;
        private const float TreeSpacing = 11f;
        private const float MedianTreeSpacing = 14f;
        private const float BuildingSetback = 6f;
        private const float BuildingGap = 4f;
        private const float TowerRowOffset = 75f;
        private const float HdbRowOffset = 38f;
        private const float HdbSpacing = 150f;
        private const float EndMargin = 20f;
        private const float LampInset = 0.5f;
        private const float TreeInset = 0.5f;
        private const float SideRoadClearance = 7f;
        private const float JunctionClearance = 6f;
        private const float TreeScaleMin = 0.85f;
        private const float TreeScaleMax = 1.25f;
        private const float BusStopStart = 375f;
        private const float BusStopEnd = 445f;

        // Hdb block: 6 m bays, 3 m floors.
        private const float BayWidth = 6f;
        private const float FloorHeight = 3f;

        private static readonly string[] TreeModels =
        {
            "tree_palm", "tree_oak", "tree_default", "tree_detailed", "tree_palmTall", "tree_fat"
        };

        private static readonly Color[] HdbColours =
        {
            new Color(0.93f, 0.88f, 0.72f), new Color(0.82f, 0.88f, 0.92f), new Color(0.93f, 0.80f, 0.74f)
        };

        [MenuItem("BusSim/Scenery/Create Scenery")]
        public static void CreateScenery()
        {
            RoadSampler main = null;
            RoadSampler side = null;
            foreach (RoadSampler sampler in Object.FindObjectsByType<RoadSampler>())
            {
                if (sampler.gameObject.name == SideRoadName)
                {
                    side = sampler;
                }
                else
                {
                    main = sampler;
                }
            }
            if (main == null || main.Settings == null)
            {
                Debug.LogError("BusSim: create the road first.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Scenery");
            System.Random rng = new System.Random(FixedSeed);
            Material[] facades = BuildFacadeMaterials();
            BuildFoliageMaterials();

            float junctionS = FindJunction(main, out bool hasJunction);
            DressRoad(main, side, root.transform, rng, facades, true, hasJunction, junctionS, 0f);
            if (side != null)
            {
                DressRoad(side, null, root.transform, rng, facades, false, false, 0f, side.Settings.roadLength > 0f ? 22f : 0f);
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = true;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"BusSim: scenery created, {root.transform.childCount} groups, {CountDescendants(root.transform)} objects.");
        }

        private static int CountDescendants(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true).Length - 1;
        }

        private static float FindJunction(RoadSampler main, out bool found)
        {
            found = false;
            RoadZones zones = main.GetComponent<RoadZones>();
            if (zones == null)
            {
                return 0f;
            }
            foreach (RoadZone zone in zones.Zones)
            {
                if (zone.type == ZoneType.Junction)
                {
                    found = true;
                    return (zone.sStart + zone.sEnd) * 0.5f;
                }
            }
            return 0f;
        }

        private static void DressRoad(RoadSampler road, RoadSampler otherRoad, Transform root, System.Random rng, Material[] facades,
            bool isMain, bool hasJunction, float junctionS, float startS)
        {
            RoadSettings settings = road.Settings;
            string tag = isMain ? "Main" : "Side";
            Transform lamps = Group(root, tag + "Lamps");
            Transform trees = Group(root, tag + "Trees");
            Transform buildings = Group(root, tag + "Buildings");
            float length = road.Length;
            float leftOuter = -settings.LeftFootpathOuterT;
            float rightOuter = settings.RightFootpathOuterT;
            float junctionGap = hasJunction ? 12f + JunctionClearance : 0f;
            RoadSampler sideRoad = isMain ? otherRoad : null;

            // Street lights: left and right footpath edges, double-arm lights on the median.
            for (float s = Mathf.Max(EndMargin, startS + LampSpacing * 0.5f); s < length - EndMargin; s += LampSpacing)
            {
                bool nearJunctionLeft = hasJunction && Mathf.Abs(s - junctionS) < junctionGap;
                if (!nearJunctionLeft)
                {
                    PlaceLamp(road, lamps, ModelLibrary.Road("light-square"), s, -(leftOuter - LampInset), true);
                }
                PlaceLamp(road, lamps, ModelLibrary.Road("light-square"), s + LampSpacing * 0.5f, rightOuter - LampInset, false);
                if (settings.HasOncoming)
                {
                    float medianT = settings.HalfRoadWidth + settings.medianWidth * 0.5f;
                    PlaceLamp(road, lamps, ModelLibrary.Road("light-square-double"), s, medianT, true);
                }
            }

            // Trees along both footpaths and the median.
            for (float s = Mathf.Max(EndMargin, startS + TreeSpacing); s < length - EndMargin; s += TreeSpacing * (0.8f + 0.5f * (float)rng.NextDouble()))
            {
                bool nearJunctionLeft = hasJunction && Mathf.Abs(s - junctionS) < junctionGap;
                bool inBusStop = isMain && s > BusStopStart && s < BusStopEnd;
                if (!nearJunctionLeft && !inBusStop)
                {
                    PlaceTree(road, trees, rng, s, -(leftOuter - TreeInset));
                }
                PlaceTree(road, trees, rng, s + TreeSpacing * 0.4f, rightOuter - TreeInset);
            }
            if (settings.HasOncoming)
            {
                float medianT = settings.HalfRoadWidth + settings.medianWidth * 0.5f;
                for (float s = EndMargin; s < length - EndMargin; s += MedianTreeSpacing)
                {
                    PlaceTree(road, trees, rng, s + 5f, medianT, "tree_palm");
                }
            }

            // Houses, shops and flats in rows behind the footpaths, and towers further back.
            string[] houses = ModelNames(ModelLibrary.Suburban, "building-type-");
            string[] shops = ModelNames(ModelLibrary.Commercial, "building-");
            string[] towers = ModelNames(ModelLibrary.Commercial, "building-skyscraper");
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float outerT = left ? -leftOuter : rightOuter;
                float sign = left ? -1f : 1f;
                float s = Mathf.Max(EndMargin, startS) + (float)rng.NextDouble() * 6f;
                while (s < length - EndMargin)
                {
                    bool useShop = rng.NextDouble() < 0.45;
                    string[] pool = useShop && shops.Length > 0 ? shops : houses;
                    string model = pool.Length > 0 ? pool[rng.Next(pool.Length)] : null;
                    float scale = useShop && shops.Length > 0 ? ModelLibrary.ShopScale : ModelLibrary.HouseScale;
                    string folder = useShop && shops.Length > 0 ? ModelLibrary.Commercial : ModelLibrary.Suburban;
                    if (model == null)
                    {
                        break;
                    }

                    GameObject placed = PlaceBuilding(road, buildings, folder + model + ".fbx", scale, s, outerT + sign * BuildingSetback, left, sideRoad, hasJunction && left, junctionS);
                    float footprint = placed != null ? Mathf.Max(8f, MeasureAlongRoad(placed)) : 12f;
                    s += footprint + BuildingGap + (float)rng.NextDouble() * 6f;
                }

                PlaceFlatsAndTowers(road, buildings, rng, facades, towers, outerT, sign, left, startS, sideRoad, hasJunction && left, junctionS);
            }
        }

        private static void PlaceFlatsAndTowers(RoadSampler road, Transform parent, System.Random rng, Material[] facades, string[] towers,
            float outerT, float sign, bool left, float startS, RoadSampler sideRoad, bool avoidJunction, float junctionS)
        {
            float length = road.Length;
            for (float s = Mathf.Max(60f, startS + 40f) + (float)rng.NextDouble() * 40f; s < length - 60f; s += HdbSpacing * (0.8f + 0.5f * (float)rng.NextDouble()))
            {
                float lengthMetres = 50f + (float)rng.NextDouble() * 30f;
                float floors = rng.Next(12, 22);
                Vector3 centre = road.GetPoint(s, outerT + sign * (HdbRowOffset + 9f));
                if (BlockedBySideRoad(centre, sideRoad, 30f) || (avoidJunction && Mathf.Abs(s - junctionS) < 55f))
                {
                    continue;
                }
                CreateFlatBlock(road, parent, facades[rng.Next(facades.Length)], s, outerT + sign * (HdbRowOffset + 9f), lengthMetres, 14f, floors * FloorHeight);
            }

            if (towers.Length == 0)
            {
                return;
            }
            for (float s = 100f + (float)rng.NextDouble() * 80f; s < length - 80f; s += 220f + (float)rng.NextDouble() * 120f)
            {
                float t = outerT + sign * TowerRowOffset;
                Vector3 p = road.GetPoint(s, t);
                if (BlockedBySideRoad(p, sideRoad, 40f) || (avoidJunction && Mathf.Abs(s - junctionS) < 70f))
                {
                    continue;
                }
                string model = towers[rng.Next(towers.Length)];
                PlaceBuilding(road, parent, ModelLibrary.Commercial + model + ".fbx", ModelLibrary.ShopScale * 2.2f, s, t, left, sideRoad, false, 0f);
            }
        }

        // ---------------- placement helpers ----------------

        private static void PlaceLamp(RoadSampler road, Transform parent, string model, float s, float t, bool facingRight)
        {
            road.GetFrame(s, out _, out Vector3 forward, out Vector3 right);
            Vector3 position = road.GetPoint(s, t) + Vector3.up * road.Settings.kerbHeight;
            // The arm reaches toward the road: from the left side that is +right, from the right side -right.
            Vector3 toRoad = t < road.Settings.HalfRoadWidth + road.Settings.medianWidth * 0.5f && t < 0f ? right : -right;
            if (model.EndsWith("double.fbx"))
            {
                toRoad = forward;
            }
            Spawn(parent, model, position, Quaternion.LookRotation(toRoad, Vector3.up), ModelLibrary.StreetLightScale);
        }

        private static void PlaceTree(RoadSampler road, Transform parent, System.Random rng, float s, float t, string forcedModel = null)
        {
            string name = forcedModel ?? TreeModels[rng.Next(TreeModels.Length)];
            float scale = (name.StartsWith("tree_palm") ? ModelLibrary.PalmScale : ModelLibrary.OakScale)
                * Mathf.Lerp(TreeScaleMin, TreeScaleMax, (float)rng.NextDouble());
            Vector3 position = road.GetPoint(s, t) + Vector3.up * road.Settings.kerbHeight;
            Quaternion rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            GameObject tree = Spawn(parent, ModelLibrary.Nature + name + ".fbx", position, rotation, scale);
            if (tree != null)
            {
                ApplyFoliageColours(tree, rng);
            }
        }

        /// <summary>The nature kit's embedded colours import with a wrong tint (cyan leaves), so foliage and bark get their own materials.</summary>
        private static void ApplyFoliageColours(GameObject tree, System.Random rng)
        {
            Material leaf = LeafMaterials[rng.Next(LeafMaterials.Length)];
            foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string materialName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                    if (materialName.Contains("leaf"))
                    {
                        materials[i] = leaf;
                    }
                    else if (materialName.Contains("wood") || materialName.Contains("bark"))
                    {
                        materials[i] = BarkMaterial;
                    }
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material[] LeafMaterials;
        private static Material BarkMaterial;

        private static void BuildFoliageMaterials()
        {
            LeafMaterials = new Material[LeafColours.Length];
            for (int i = 0; i < LeafColours.Length; i++)
            {
                LeafMaterials[i] = RoadMaterialFactory.LoadOrCreateMaterial($"Tree_Leaf_{i}", 0.05f, LeafColours[i], null);
            }
            BarkMaterial = RoadMaterialFactory.LoadOrCreateMaterial("Tree_Bark", 0.05f, new Color(0.36f, 0.25f, 0.16f), null);
        }

        private static readonly Color[] LeafColours =
        {
            new Color(0.20f, 0.42f, 0.14f), new Color(0.27f, 0.50f, 0.16f), new Color(0.16f, 0.36f, 0.16f)
        };

        private static GameObject PlaceBuilding(RoadSampler road, Transform parent, string assetPath, float scale, float s, float t, bool left,
            RoadSampler sideRoad, bool avoidJunction, float junctionS)
        {
            Vector3 position = road.GetPoint(s, t);
            if (BlockedBySideRoad(position, sideRoad, SideRoadClearance + 8f) || (avoidJunction && Mathf.Abs(s - junctionS) < 12f + JunctionClearance + 8f))
            {
                return null;
            }
            road.GetFrame(s, out _, out _, out Vector3 right);
            // The front of a Kenney building faces +Z, so turn that toward the road.
            Vector3 toRoad = left ? right : -right;
            return Spawn(parent, assetPath, position, Quaternion.LookRotation(toRoad, Vector3.up), scale);
        }

        private static bool BlockedBySideRoad(Vector3 point, RoadSampler sideRoad, float clearance)
        {
            if (sideRoad == null || sideRoad.Settings == null)
            {
                return false;
            }
            (float s, float t) = sideRoad.ProjectToRoad(point);
            float corridor = sideRoad.Settings.HalfRoadWidth + sideRoad.Settings.footpathWidth + clearance;
            return Mathf.Abs(t) < corridor && s > -clearance && s < sideRoad.Length + clearance;
        }

        private static GameObject Spawn(Transform parent, string assetPath, Vector3 position, Quaternion rotation, float scale)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                return null;
            }
            GameObject instance = Object.Instantiate(asset, parent);
            instance.name = asset.name;
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * scale;
            return instance;
        }

        private static float MeasureAlongRoad(GameObject placed)
        {
            Bounds bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return Mathf.Max(bounds.size.x, bounds.size.z);
        }

        private static string[] ModelNames(string folder, string prefix)
        {
            List<string> names = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder.TrimEnd('/') }))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                bool isSkyscraper = name.Contains("skyscraper");
                bool wanted = name.StartsWith(prefix) && (prefix.Contains("skyscraper") || !isSkyscraper);
                if (wanted)
                {
                    names.Add(name);
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        private static Transform Group(Transform root, string groupName)
        {
            Transform group = new GameObject(groupName).transform;
            group.SetParent(root, false);
            return group;
        }

        // ---------------- tall flats ----------------

        private static void CreateFlatBlock(RoadSampler road, Transform parent, Material facade, float s, float t, float blockLength, float depth, float height)
        {
            road.GetFrame(s, out Vector3 centre, out Vector3 forward, out Vector3 right);
            Vector3 position = road.GetPoint(s, t);
            position.y = centre.y;

            GameObject block = new GameObject("FlatBlock");
            block.transform.SetParent(parent, false);
            block.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            block.AddComponent<MeshFilter>().sharedMesh = BuildBlockMesh(blockLength, depth, height);
            block.AddComponent<MeshRenderer>().sharedMaterial = facade;
        }

        /// <summary>A box with its pivot at the base centre. Side UVs are in bays and floors so one texture tiles correctly.</summary>
        private static Mesh BuildBlockMesh(float blockLength, float depth, float height)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float hx = blockLength * 0.5f;
            float hz = depth * 0.5f;

            AddWall(vertices, uvs, triangles, new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), height, blockLength / BayWidth, height / FloorHeight);
            AddWall(vertices, uvs, triangles, new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz), height, depth / BayWidth, height / FloorHeight);
            AddWall(vertices, uvs, triangles, new Vector3(hx, 0f, -hz), new Vector3(-hx, 0f, -hz), height, blockLength / BayWidth, height / FloorHeight);
            AddWall(vertices, uvs, triangles, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), height, depth / BayWidth, height / FloorHeight);

            int roof = vertices.Count;
            vertices.Add(new Vector3(-hx, height, -hz));
            vertices.Add(new Vector3(-hx, height, hz));
            vertices.Add(new Vector3(hx, height, hz));
            vertices.Add(new Vector3(hx, height, -hz));
            for (int i = 0; i < 4; i++)
            {
                uvs.Add(new Vector2(0.02f, 0.02f));
            }
            triangles.AddRange(new[] { roof, roof + 1, roof + 2, roof, roof + 2, roof + 3 });

            Mesh mesh = new Mesh { name = "FlatBlock" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddWall(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 a, Vector3 b, float height, float bays, float floors)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(b + Vector3.up * height);
            vertices.Add(a + Vector3.up * height);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(bays, 0f));
            uvs.Add(new Vector2(bays, floors));
            uvs.Add(new Vector2(0f, floors));
            // Wound so the wall faces outward for a box walked clockwise seen from above.
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        private static Material[] BuildFacadeMaterials()
        {
            Material[] materials = new Material[HdbColours.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>($"{RoadMaterialFactory.MaterialFolder}/{FacadeMaterialPrefix}{i}.mat");
                if (material == null)
                {
                    material = RoadMaterialFactory.LoadOrCreateMaterial($"{FacadeMaterialPrefix}{i}", 0.15f, HdbColours[i], BuildFacadeTexture);
                }
                materials[i] = material;
            }
            return materials;
        }

        /// <summary>One bay: a cream wall, a dark window with a frame, a balcony rail strip and a floor slab line.</summary>
        private static Texture2D BuildFacadeTexture()
        {
            const int size = 128;
            Color wall = Color.white;
            Color slab = new Color(0.78f, 0.78f, 0.76f);
            Color glass = new Color(0.22f, 0.3f, 0.38f);
            Color frame = new Color(0.9f, 0.9f, 0.88f);
            Color rail = new Color(0.62f, 0.64f, 0.66f);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color colour = wall;
                    bool slabLine = y < 6;
                    bool inWindow = x > 28 && x < 100 && y > 36 && y < 104;
                    bool inFrame = x > 24 && x < 104 && y > 32 && y < 108 && !inWindow;
                    bool inRail = y >= 6 && y < 26;
                    bool mullion = inWindow && (x > 62 && x < 66);
                    if (slabLine) colour = slab;
                    else if (inRail) colour = Color.Lerp(wall, rail, (x % 12 < 3) ? 0.7f : 0.25f);
                    else if (mullion) colour = frame;
                    else if (inWindow) colour = glass;
                    else if (inFrame) colour = frame;
                    pixels[y * size + x] = colour;
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Hdb_Facade",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
