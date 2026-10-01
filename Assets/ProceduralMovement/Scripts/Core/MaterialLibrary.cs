using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Runtime URP materials and procedurally painted textures (no texture assets are used).</summary>
    public static class MaterialLibrary
    {
        static Shader s_Lit;
        static readonly Dictionary<string, Material> s_Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> s_Textures = new Dictionary<string, Texture2D>();

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public static Shader Lit
        {
            get
            {
                if (s_Lit == null) s_Lit = Shader.Find("Universal Render Pipeline/Lit");
                if (s_Lit == null) s_Lit = Shader.Find("Standard");
                return s_Lit;
            }
        }

        public static Material Create(Color color, float smoothness = 0.3f, float metallic = 0f, Texture2D texture = null, Color? emission = null)
        {
            var m = new Material(Lit);
            m.SetColor(BaseColor, color);
            m.color = color;
            m.SetFloat(Smoothness, smoothness);
            m.SetFloat(Metallic, metallic);
            if (texture != null) m.SetTexture(BaseMap, texture);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionColor, emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Cached material keyed by name so large scenes reuse a handful of materials (keeps batching effective).</summary>
        public static Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f, Texture2D texture = null, Color? emission = null)
        {
            if (s_Cache.TryGetValue(key, out var m) && m != null) return m;
            m = Create(color, smoothness, metallic, texture, emission);
            m.name = key;
            s_Cache[key] = m;
            return m;
        }

        static Texture2D NewTex(int size, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            return t;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float ValueNoise(float x, float y, int seed, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fractal noise in [0,1].</summary>
        public static float Fbm(float u, float v, int seed, int baseFreq = 4, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * f, v * f, seed + o * 17, f) * amp;
                norm += amp; amp *= 0.5f; f *= 2;
            }
            return sum / norm;
        }

        static Texture2D Paint(string key, int size, System.Func<float, float, Color> painter)
        {
            if (s_Textures.TryGetValue(key, out var existing) && existing != null) return existing;
            var t = NewTex(size, key);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = painter((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply(true, true);
            s_Textures[key] = t;
            return t;
        }

        /// <summary>Building facade: one texture tile = 2 x 2 windows (the city maps 1 tile to 7 m).</summary>
        public static Texture2D Facade(int variant)
        {
            return Paint("facade" + variant, 256, (u, v) =>
            {
                float cu = (u * 2f) % 1f, cv = (v * 2f) % 1f;
                int cellX = Mathf.FloorToInt(u * 2f), cellY = Mathf.FloorToInt(v * 2f);
                float wall = 0.82f + 0.12f * Fbm(u, v, 11 + variant, 8, 3);
                Color wallC = new Color(wall, wall, wall);
                bool window = variant == 1
                    ? (cu > 0.08f && cu < 0.92f && cv > 0.28f && cv < 0.9f)
                    : (cu > 0.2f && cu < 0.8f && cv > 0.3f && cv < 0.85f);
                if (window)
                {
                    bool frame = variant != 1 && (Mathf.Abs(cu - 0.5f) < 0.018f || Mathf.Abs(cv - 0.62f) < 0.018f);
                    if (frame) return new Color(0.85f, 0.85f, 0.85f);
                    float lit = Hash(cellX + variant * 7, cellY, 3);
                    float grad = Mathf.Lerp(0.18f, 0.34f, cv);
                    Color glass = new Color(0.16f + grad * 0.3f, 0.22f + grad * 0.4f, 0.3f + grad * 0.55f);
                    if (lit > 0.8f) glass = Color.Lerp(glass, new Color(0.9f, 0.8f, 0.55f), 0.45f);
                    return glass;
                }
                // Floor slab line.
                if (cv < 0.06f) return wallC * 0.78f;
                return wallC;
            });
        }

        public static Texture2D Asphalt() => Paint("asphalt", 256, (u, v) =>
        {
            float n = Fbm(u, v, 5, 16, 4);
            float g = 0.16f + n * 0.09f;
            if (Hash(Mathf.FloorToInt(u * 256), Mathf.FloorToInt(v * 256), 9) > 0.985f) g += 0.08f;
            return new Color(g, g, g * 1.03f);
        });

        public static Texture2D Pavers() => Paint("pavers", 256, (u, v) =>
        {
            float cu = (u * 4f) % 1f, cv = (v * 4f) % 1f;
            float n = Fbm(u, v, 21, 8, 3);
            float tint = 0.66f + 0.1f * Hash(Mathf.FloorToInt(u * 4f), Mathf.FloorToInt(v * 4f), 4) + n * 0.08f;
            bool grout = cu < 0.035f || cv < 0.035f;
            return grout ? new Color(0.42f, 0.41f, 0.4f) : new Color(tint, tint * 0.98f, tint * 0.95f);
        });

        public static Texture2D Grass() => Paint("grass", 256, (u, v) =>
        {
            float n = Fbm(u, v, 31, 8, 5);
            float d = Hash(Mathf.FloorToInt(u * 256), Mathf.FloorToInt(v * 256), 2) * 0.08f;
            return new Color(0.25f + n * 0.12f + d, 0.42f + n * 0.18f + d, 0.16f + n * 0.05f);
        });

        public static Texture2D Concrete() => Paint("concrete", 256, (u, v) =>
        {
            float n = Fbm(u, v, 41, 8, 5);
            float g = 0.62f + n * 0.16f;
            return new Color(g, g * 0.99f, g * 0.96f);
        });

        public static Texture2D Checker() => Paint("checker", 128, (u, v) =>
        {
            bool c = (Mathf.FloorToInt(u * 2f) + Mathf.FloorToInt(v * 2f)) % 2 == 0;
            float g = c ? 0.78f : 0.62f;
            return new Color(g, g, g);
        });

        /// <summary>Soft banded pattern for creature bodies (bands along V).</summary>
        public static Texture2D Bands() => Paint("bands", 128, (u, v) =>
        {
            float band = 0.5f + 0.5f * Mathf.Cos(v * Mathf.PI * 2f * 5f);
            float n = Fbm(u, v, 51, 4, 3);
            float g = Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0.3f, 0.7f, band)) * (0.9f + n * 0.1f);
            return new Color(g, g, g);
        });

        /// <summary>Subtle organic noise for skins and coats.</summary>
        public static Texture2D Skin() => Paint("skin", 128, (u, v) =>
        {
            float n = Fbm(u, v, 61, 8, 4);
            float g = 0.9f + n * 0.1f;
            return new Color(g, g, g);
        });
    }
}
