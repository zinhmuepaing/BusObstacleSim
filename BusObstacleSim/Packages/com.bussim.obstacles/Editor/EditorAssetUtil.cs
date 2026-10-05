using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    internal static class EditorAssetUtil
    {
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            int split = folderPath.LastIndexOf('/');
            string parent = folderPath.Substring(0, split);
            string leaf = folderPath.Substring(split + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            EnsureFolder(assetPath.Substring(0, assetPath.LastIndexOf('/')));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }
    }
}
