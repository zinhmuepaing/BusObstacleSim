using BusSim.Road;
using BusSim.TestRig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>Creates the test bus prefab, places it on the road and hooks up the follow camera.</summary>
    public static class TestRigBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/TestRig";
        private const string PrefabPath = PrefabFolder + "/TestBus.prefab";
        private const string PhysicsMaterialPath = "Assets/_Project/Materials/BusSlide.asset";
        private const string BusObjectName = "TestBus";

        // Bus size from docs/PROJECT_SPEC.md (assumption, supervisor to confirm).
        private const float BusWidth = 2.5f;
        private const float BusLength = 12f;
        private const float BusHeight = 3.2f;
        private const float BusMassKg = 12000f;

        // Visual detail, placeholder until a real bus model is imported.
        private const float WindscreenWidthShare = 0.92f;
        private const float WindowBandHeightShare = 0.28f;
        private const float WindowBandCentreShare = 0.62f;
        private const float WindowBandLengthShare = 0.85f;
        private const float GlassThickness = 0.05f;
        private const float WheelRadius = 0.55f;
        private const float WheelThickness = 0.3f;
        private const float WheelInsetZ = 3.6f;

        private const float StartMarginMetres = 2f;
        private const float SpawnClearance = 0.05f;

        private static readonly Color BodyColour = new Color(0.88f, 0.88f, 0.9f);
        private static readonly Color StripeColour = new Color(0.75f, 0.1f, 0.12f);
        private static readonly Color GlassColour = new Color(0.08f, 0.1f, 0.14f);
        private static readonly Color WheelColour = new Color(0.05f, 0.05f, 0.05f);

        /// <summary>Removes the bus and chase camera, then builds them again with current defaults.</summary>
        [MenuItem("BusSim/Test Rig/Recreate Test Bus")]
        public static void RecreateTestBus()
        {
            TestBusController existing = Object.FindAnyObjectByType<TestBusController>();
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Camera mainCamera = Camera.main;
            FollowCamera follow = mainCamera != null ? mainCamera.GetComponent<FollowCamera>() : null;
            if (follow != null)
            {
                Undo.DestroyObjectImmediate(follow);
            }

            CreateTestBus();
        }

        [MenuItem("BusSim/Test Rig/Create Test Bus")]
        public static void CreateTestBus()
        {
            RoadSampler road = Object.FindAnyObjectByType<RoadSampler>();
            if (road == null || road.Settings == null)
            {
                Debug.LogError("BusSim: create the road first (BusSim/Road/Create Road In Scene).");
                return;
            }
            if (Object.FindAnyObjectByType<TestBusController>() != null)
            {
                Debug.LogWarning("BusSim: a test bus already exists in this scene.");
                return;
            }

            EditorAssetUtil.EnsureFolder(PrefabFolder);
            GameObject bus = BuildBus();
            PlaceOnRoad(bus.transform, road);

            // The return value is the prefab asset. The scene object stays as `bus`, now connected.
            PrefabUtility.SaveAsPrefabAssetAndConnect(bus, PrefabPath, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(bus, "Create Test Bus");
            WireCamera(bus.transform);

            EditorSceneManager.MarkSceneDirty(bus.scene);
            Selection.activeObject = bus;
            Debug.Log($"BusSim: test bus created ({BusWidth} x {BusLength} m) in the left lane. Prefab: {PrefabPath}");
        }

        private static GameObject BuildBus()
        {
            Material body = RoadMaterialFactory.LoadOrCreateMaterial("Bus_Body", 0.4f, BodyColour, null);
            Material stripe = RoadMaterialFactory.LoadOrCreateMaterial("Bus_Stripe", 0.4f, StripeColour, null);
            Material glass = RoadMaterialFactory.LoadOrCreateMaterial("Bus_Glass", 0.8f, GlassColour, null);
            Material wheel = RoadMaterialFactory.LoadOrCreateMaterial("Bus_Wheel", 0.1f, WheelColour, null);

            GameObject root = new GameObject(BusObjectName);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = BusMassKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(BusWidth, BusHeight, BusLength);
            box.center = new Vector3(0f, BusHeight * 0.5f, 0f);
            box.sharedMaterial = LoadOrCreatePhysicsMaterial();

            root.AddComponent<TestBusController>();
            TestBusAutopilot autopilot = root.AddComponent<TestBusAutopilot>();
            autopilot.enabled = false;

            AddBox(root.transform, "Body", body,
                new Vector3(0f, BusHeight * 0.5f, 0f),
                new Vector3(BusWidth, BusHeight, BusLength));

            float windowY = BusHeight * WindowBandCentreShare;
            float windowHeight = BusHeight * WindowBandHeightShare;
            AddBox(root.transform, "Windscreen", glass,
                new Vector3(0f, windowY, BusLength * 0.5f + GlassThickness * 0.5f),
                new Vector3(BusWidth * WindscreenWidthShare, windowHeight, GlassThickness));
            float sideX = BusWidth * 0.5f + GlassThickness * 0.5f;
            Vector3 sideSize = new Vector3(GlassThickness, windowHeight, BusLength * WindowBandLengthShare);
            AddBox(root.transform, "WindowsRight", glass, new Vector3(sideX, windowY, 0f), sideSize);
            AddBox(root.transform, "WindowsLeft", glass, new Vector3(-sideX, windowY, 0f), sideSize);

            float stripeHeight = BusHeight * 0.06f;
            float stripeX = BusWidth * 0.5f + GlassThickness * 0.5f;
            Vector3 stripeSize = new Vector3(GlassThickness, stripeHeight, BusLength);
            AddBox(root.transform, "StripeRight", stripe, new Vector3(stripeX, BusHeight * 0.3f, 0f), stripeSize);
            AddBox(root.transform, "StripeLeft", stripe, new Vector3(-stripeX, BusHeight * 0.3f, 0f), stripeSize);

            float wheelX = BusWidth * 0.5f - WheelThickness * 0.5f;
            foreach (float z in new[] { WheelInsetZ, -WheelInsetZ })
            {
                AddWheel(root.transform, "WheelRight" + z, wheel, new Vector3(wheelX, WheelRadius, z));
                AddWheel(root.transform, "WheelLeft" + z, wheel, new Vector3(-wheelX, WheelRadius, z));
            }

            return root;
        }

        private static void AddBox(Transform parent, string objectName, Material material, Vector3 localPosition, Vector3 size)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            FinishPart(part, parent, objectName, material, localPosition);
            part.transform.localScale = size;
        }

        private static void AddWheel(Transform parent, string objectName, Material material, Vector3 localPosition)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            FinishPart(part, parent, objectName, material, localPosition);
            part.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // Unity's cylinder is 2 units tall and 1 wide at scale 1: Y is the axle length.
            part.transform.localScale = new Vector3(WheelRadius * 2f, WheelThickness * 0.5f, WheelRadius * 2f);
        }

        private static void FinishPart(GameObject part, Transform parent, string objectName, Material material, Vector3 localPosition)
        {
            part.name = objectName;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static PhysicsMaterial LoadOrCreatePhysicsMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
            if (material != null)
            {
                return material;
            }

            material = new PhysicsMaterial("BusSlide")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            return material;
        }

        private static void PlaceOnRoad(Transform bus, RoadSampler road)
        {
            float startS = BusLength * 0.5f + StartMarginMetres;
            float laneT = road.Settings.GetLaneCentreT(0);
            road.GetFrame(startS, out Vector3 centre, out Vector3 forward, out Vector3 right);
            bus.position = centre + right * laneT + Vector3.up * SpawnClearance;
            bus.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void WireCamera(Transform bus)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("BusSim: no Main Camera found, follow camera not set up.");
                return;
            }

            FollowCamera follow = mainCamera.GetComponent<FollowCamera>();
            if (follow == null)
            {
                follow = Undo.AddComponent<FollowCamera>(mainCamera.gameObject);
            }
            follow.Target = bus;
            follow.Snap();
            EditorUtility.SetDirty(follow);
        }
    }
}
