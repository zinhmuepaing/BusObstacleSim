using UnityEngine;

namespace BusSim.Editor
{
    /// <summary>
    /// Tileable procedural surface textures (placeholders until real PBR textures are imported).
    /// Noise is a value noise on a lattice that wraps at the texture edge, so it tiles.
    /// </summary>
    internal static class ProceduralTextures
    {
        private const float OctaveAmplitudeFalloff = 0.5f;
        private const int OctavePeriodGrowth = 2;
        private const uint HashMultiplierX = 374761393u;
        private const uint HashMultiplierY = 668265263u;
        private const uint HashMultiplierSeed = 362437u;
        private const uint HashMixMultiplier = 1274126177u;
        private const int HashShiftA = 13;
        private const int HashShiftB = 16;
        private const uint HashMask = 0xFFFFFFu;
        private const float HashDivisor = 16777216f;
        private const float SpeckleSeedOffset = 7919f;

        public static Texture2D CreateNoise(
            string textureName, int size, int basePeriod, int octaves, int seed,
            Color low, Color high, float speckle)
        {
            Color[] pixels = new Color[size * size];
            FillNoise(pixels, size, basePeriod, octaves, seed, low, high, speckle);
            return Finish(textureName, size, pixels);
        }

        /// <summary>Noise with a grid of darker joints, like concrete paving slabs.</summary>
        public static Texture2D CreatePavers(
            string textureName, int size, int cellsPerSide, int jointPixels, int seed,
            Color low, Color high, Color joint, float speckle)
        {
            Color[] pixels = new Color[size * size];
            FillNoise(pixels, size, cellsPerSide, 2, seed, low, high, speckle);

            int cellSize = size / cellsPerSide;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onJoint = x % cellSize < jointPixels || y % cellSize < jointPixels;
                    if (onJoint)
                    {
                        pixels[y * size + x] = joint;
                    }
                }
            }
            return Finish(textureName, size, pixels);
        }

        private static void FillNoise(
            Color[] pixels, int size, int basePeriod, int octaves, int seed,
            Color low, Color high, float speckle)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float value = 0f;
                    float amplitude = 1f;
                    float total = 0f;
                    int period = basePeriod;
                    for (int o = 0; o < octaves; o++)
                    {
                        value += ValueNoise(u, v, period, seed + o) * amplitude;
                        total += amplitude;
                        amplitude *= OctaveAmplitudeFalloff;
                        period *= OctavePeriodGrowth;
                    }
                    value /= total;

                    float grain = (Hash(x, y, seed + (int)SpeckleSeedOffset) - 0.5f) * speckle;
                    Color colour = Color.Lerp(low, high, value);
                    colour.r = Mathf.Clamp01(colour.r + grain);
                    colour.g = Mathf.Clamp01(colour.g + grain);
                    colour.b = Mathf.Clamp01(colour.b + grain);
                    pixels[y * size + x] = colour;
                }
            }
        }

        private static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period;
            float y = v * period;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0);
            float fy = Smooth(y - y0);
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;
            x0 %= period;
            y0 %= period;

            float top = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float bottom = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(top, bottom, fy);
        }

        private static float Smooth(float f)
        {
            return f * f * (3f - 2f * f);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * HashMultiplierX + (uint)y * HashMultiplierY + (uint)seed * HashMultiplierSeed;
                h = (h ^ (h >> HashShiftA)) * HashMixMultiplier;
                h ^= h >> HashShiftB;
                return (h & HashMask) / HashDivisor;
            }
        }

        private static Texture2D Finish(string textureName, int size, Color[] pixels)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = textureName,
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
