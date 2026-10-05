using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>One command that builds a complete, playable demo in the open scene.</summary>
    public static class DemoSceneBuilder
    {
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";
        private const int FirstUserLayer = 8;
        private const int LayerCount = 32;

        [MenuItem("BusSim/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            EnsureTagAndLayer(ObstacleContentBuilder.ObstacleTag, ObstacleContentBuilder.ObstacleLayer);
            RoadSceneBuilder.CreateRoad();
            RoadSceneBuilder.SetupSky();
            ZoneBuilder.CreateZones();
            ObstacleContentBuilder.BuildAll();
            TestRigBuilder.CreateTestBus();
            ObstacleSceneBuilder.CreateSpawner();
            Debug.Log("BusSim: demo scene ready. Press Play and drive with W A S D or the arrow keys.");
        }

        /// <summary>Adds the tag and a user layer of these names to the project if they are missing.</summary>
        public static void EnsureTagAndLayer(string tag, string layer)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("BusSim: could not open the tag manager.");
                return;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tags = tagManager.FindProperty("tags");
            bool hasTag = false;
            for (int i = 0; i < tags.arraySize; i++)
            {
                hasTag |= tags.GetArrayElementAtIndex(i).stringValue == tag;
            }
            if (!hasTag)
            {
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }

            if (LayerMask.NameToLayer(layer) < 0)
            {
                SerializedProperty layers = tagManager.FindProperty("layers");
                for (int i = FirstUserLayer; i < LayerCount; i++)
                {
                    SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(slot.stringValue))
                    {
                        slot.stringValue = layer;
                        break;
                    }
                }
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
