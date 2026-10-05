using UnityEditor;
using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>Creates the road materials and their procedural textures once, then reuses them.</summary>
    internal static class RoadMaterialFactory
    {
        public const string MaterialFolder = "Assets/_Project/Materials";

        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string FallbackShaderName = "Standard";
        private const string BaseMapProperty = "_BaseMap";
        private const string BaseColorProperty = "_BaseColor";
        private const string SmoothnessProperty = "_Smoothness";
        private const string MetallicProperty = "_Metallic";

        private const int TextureSize = 512;
        private const float AsphaltSmoothness = 0.15f;
        private const float MarkingSmoothness = 0.25f;
        private const float PavementSmoothness = 0.1f;
        private const float GroundSmoothness = 0.0f;

        public struct Set
        {
            public Material Asphalt;
            public Material Marking;
            public Material Footpath;
            public Material Ground;
            public Material Grass;
        }

        public static Set LoadOrCreate()
        {
            EditorAssetUtil.EnsureFolder(MaterialFolder);

            Set set = new Set
            {
                Asphalt = LoadOrCreateMaterial("Road_Asphalt", AsphaltSmoothness, Color.white, () =>
                    ProceduralTextures.CreateNoise("Road_Asphalt_Albedo", TextureSize, 8, 4, 11,
                        new Color(0.13f, 0.13f, 0.14f), new Color(0.24f, 0.24f, 0.25f), 0.10f)),
                Marking = LoadOrCreateMaterial("Road_Marking", MarkingSmoothness, new Color(0.92f, 0.92f, 0.9f), null),
                Footpath = LoadOrCreateMaterial("Road_Footpath", PavementSmoothness, Color.white, () =>
                    ProceduralTextures.CreatePavers("Road_Footpath_Albedo", TextureSize, 8, 2, 23,
                        new Color(0.50f, 0.49f, 0.47f), new Color(0.66f, 0.64f, 0.61f),
                        new Color(0.32f, 0.31f, 0.30f), 0.06f)),
                // The land around the road is paved city ground, not lawn; grass only grows in the median and planters.
                Ground = LoadOrCreateMaterial("Road_City", GroundSmoothness, Color.white, () =>
                    ProceduralTextures.CreatePavers("Road_City_Albedo", TextureSize, 6, 2, 41,
                        new Color(0.43f, 0.43f, 0.42f), new Color(0.56f, 0.55f, 0.53f),
                        new Color(0.28f, 0.28f, 0.28f), 0.05f)),
                Grass = LoadOrCreateMaterial("Road_Grass", GroundSmoothness, Color.white, () =>
                    ProceduralTextures.CreateNoise("Road_Grass_Albedo", TextureSize, 6, 5, 37,
                        new Color(0.16f, 0.26f, 0.09f), new Color(0.28f, 0.38f, 0.14f), 0.08f))
            };

            AssetDatabase.SaveAssets();
            return set;
        }

        internal static Material LoadOrCreateMaterial(
            string materialName, float smoothness, Color colour, System.Func<Texture2D> makeTexture)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = new Material(Shader.Find(LitShaderName) ?? Shader.Find(FallbackShaderName)) { name = materialName };
            material.color = colour;
            material.SetFloat(SmoothnessProperty, smoothness);
            material.SetFloat(MetallicProperty, 0f);
            AssetDatabase.CreateAsset(material, path);

            if (makeTexture != null)
            {
                Texture2D texture = makeTexture();
                AssetDatabase.AddObjectToAsset(texture, material);
                material.mainTexture = texture;
            }

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
