using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// Generates a dense city: a 7 x 7 grid of blocks with streets, sidewalks and curbs, buildings with
    /// setbacks and ledges, rooftop clutter, parked cars and street furniture. Special blocks exercise the
    /// locomotion: an obstacle plaza (spawn), a hilly park, a skatepark with curved surfaces, a stair
    /// amphitheatre and an elevated overpass with a ceiling to climb under.
    /// </summary>
    public class CityGenerator
    {
        public const int Blocks = 7;
        public const float BlockSize = 36f;
        public const float Sidewalk = 2.5f;
        public const float Road = 9f;
        public const float Pitch = BlockSize + Sidewalk * 2f + Road; // 50
        public const float Curb = 0.15f;
        public static float Extent => Blocks * 0.5f * Pitch;

        public readonly List<Vector3> spawnPoints = new List<Vector3>();
        public Vector3 PlayerSpawn { get; private set; }
        public Vector3 PlayerForward { get; private set; } = Vector3.forward;

        System.Random m_Rng;
        CityBuilder m_B;

        // Materials.
        Material m_Asphalt, m_Pavers, m_Concrete, m_Grass, m_Roof, m_Storefront, m_Metal, m_Lamp, m_Paint, m_PaintYellow;
        Material m_Wood, m_Trunk, m_Rock, m_Glass, m_Tire;
        Material[] m_Facades, m_Foliage, m_CarPaint, m_Toy;

        float R(float a, float b) => a + (float)m_Rng.NextDouble() * (b - a);
        int RI(int a, int bExclusive) => m_Rng.Next(a, bExclusive);
        bool Chance(float p) => m_Rng.NextDouble() < p;
        T Pick<T>(T[] arr) => arr[m_Rng.Next(arr.Length)];

        public GameObject Generate(int seed)
        {
            m_Rng = new System.Random(seed);
            var root = new GameObject("City");
            m_B = new CityBuilder(root.transform);
            CreateMaterials();

            float ext = Extent + 25f;
            // Ground: asphalt city floor, grass beyond.
            m_B.Box(new Vector3(0, -0.5f, 0), new Vector3(ext * 2f, 1f, ext * 2f), m_Asphalt, 1f / 8f);
            m_B.Box(new Vector3(0, -0.53f, 0), new Vector3(1600f, 1f, 1600f), m_Grass, 1f / 10f, false);
            var groundCollider = new GameObject("OuterGround").AddComponent<BoxCollider>();
            groundCollider.transform.SetParent(root.transform, false);
            groundCollider.center = new Vector3(0, -0.53f, 0);
            groundCollider.size = new Vector3(1600f, 1f, 1600f);

            int half = Blocks / 2;
            for (int bx = -half; bx <= half; bx++)
                for (int bz = -half; bz <= half; bz++)
                {
                    Vector3 c = new Vector3(bx * Pitch, 0, bz * Pitch);
                    // Raised sidewalk slab with a curb.
                    float slab = BlockSize + Sidewalk * 2f;
                    m_B.Box(c + Vector3.up * (Curb * 0.5f), new Vector3(slab, Curb, slab), m_Pavers, 1f / 4f);
                    bool ring = Mathf.Abs(bx) == half || Mathf.Abs(bz) == half;
                    if (bx == 0 && bz == 0) Plaza(c);
                    else if (bx == 1 && bz == 0) Park(c);
                    else if (bx == -1 && bz == 0) Skatepark(c);
                    else if (bx == 0 && bz == -1) Amphitheatre(c);
                    else Buildings(c, ring, bx, bz);
                    StreetFurniture(c, bx, bz);
                }
            Roads();
            Overpass();
            Skyline();
            m_B.Finish();
            return root;
        }

        void CreateMaterials()
        {
            m_Asphalt = MaterialLibrary.Get("Asphalt", Color.white, 0.15f, 0f, MaterialLibrary.Asphalt());
            m_Pavers = MaterialLibrary.Get("Pavers", new Color(0.95f, 0.93f, 0.9f), 0.2f, 0f, MaterialLibrary.Pavers());
            m_Concrete = MaterialLibrary.Get("Concrete", Color.white, 0.2f, 0f, MaterialLibrary.Concrete());
            m_Grass = MaterialLibrary.Get("Grass", Color.white, 0.1f, 0f, MaterialLibrary.Grass());
            m_Roof = MaterialLibrary.Get("Roof", new Color(0.55f, 0.55f, 0.56f), 0.2f, 0f, MaterialLibrary.Concrete());
            m_Storefront = MaterialLibrary.Get("Storefront", new Color(0.35f, 0.4f, 0.46f), 0.85f, 0.2f, MaterialLibrary.Facade(1));
            m_Metal = MaterialLibrary.Get("Metal", new Color(0.22f, 0.24f, 0.26f), 0.6f, 0.6f);
            m_Lamp = MaterialLibrary.Get("Lamp", new Color(1f, 0.95f, 0.8f), 0.8f, 0f, null, new Color(1.2f, 1.0f, 0.7f));
            m_Paint = MaterialLibrary.Get("PaintWhite", new Color(0.92f, 0.92f, 0.9f), 0.3f);
            m_PaintYellow = MaterialLibrary.Get("PaintYellow", new Color(0.95f, 0.75f, 0.15f), 0.3f);
            m_Wood = MaterialLibrary.Get("Wood", new Color(0.55f, 0.36f, 0.2f), 0.25f, 0f, MaterialLibrary.Skin());
            m_Trunk = MaterialLibrary.Get("Trunk", new Color(0.36f, 0.25f, 0.16f), 0.1f, 0f, MaterialLibrary.Skin());
            m_Rock = MaterialLibrary.Get("Rock", new Color(0.55f, 0.53f, 0.5f), 0.15f, 0f, MaterialLibrary.Concrete());
            m_Glass = MaterialLibrary.Get("CarGlass", new Color(0.12f, 0.16f, 0.2f), 0.92f, 0.3f);
            m_Tire = MaterialLibrary.Get("Tire", new Color(0.08f, 0.08f, 0.08f), 0.2f);
            m_Facades = new[]
            {
                MaterialLibrary.Get("FacadeBeige", new Color(0.96f, 0.88f, 0.76f), 0.3f, 0f, MaterialLibrary.Facade(0)),
                MaterialLibrary.Get("FacadeWhite", new Color(0.98f, 0.98f, 0.97f), 0.3f, 0f, MaterialLibrary.Facade(0)),
                MaterialLibrary.Get("FacadeBrick", new Color(0.8f, 0.5f, 0.4f), 0.2f, 0f, MaterialLibrary.Facade(0)),
                MaterialLibrary.Get("FacadeSlate", new Color(0.7f, 0.74f, 0.8f), 0.3f, 0f, MaterialLibrary.Facade(0)),
                MaterialLibrary.Get("GlassTeal", new Color(0.72f, 0.95f, 0.95f), 0.85f, 0.1f, MaterialLibrary.Facade(1)),
                MaterialLibrary.Get("GlassBlue", new Color(0.78f, 0.86f, 1f), 0.85f, 0.1f, MaterialLibrary.Facade(1)),
                MaterialLibrary.Get("FacadeSand", new Color(0.92f, 0.8f, 0.6f), 0.25f, 0f, MaterialLibrary.Facade(0)),
            };
            m_Foliage = new[]
            {
                MaterialLibrary.Get("Leaves1", new Color(0.3f, 0.52f, 0.2f), 0.15f, 0f, MaterialLibrary.Grass()),
                MaterialLibrary.Get("Leaves2", new Color(0.42f, 0.6f, 0.22f), 0.15f, 0f, MaterialLibrary.Grass()),
                MaterialLibrary.Get("Leaves3", new Color(0.24f, 0.45f, 0.26f), 0.15f, 0f, MaterialLibrary.Grass()),
            };
            m_CarPaint = new[]
            {
                MaterialLibrary.Get("CarRed", new Color(0.72f, 0.1f, 0.08f), 0.85f, 0.3f),
                MaterialLibrary.Get("CarBlue", new Color(0.1f, 0.25f, 0.6f), 0.85f, 0.3f),
                MaterialLibrary.Get("CarWhite", new Color(0.9f, 0.9f, 0.88f), 0.85f, 0.3f),
                MaterialLibrary.Get("CarBlack", new Color(0.06f, 0.06f, 0.07f), 0.85f, 0.3f),
                MaterialLibrary.Get("CarYellow", new Color(0.95f, 0.7f, 0.08f), 0.85f, 0.3f),
                MaterialLibrary.Get("CarSilver", new Color(0.6f, 0.62f, 0.64f), 0.85f, 0.5f),
            };
            m_Toy = new[]
            {
                MaterialLibrary.Get("ToyOrange", new Color(0.95f, 0.5f, 0.2f), 0.3f, 0f, MaterialLibrary.Checker()),
                MaterialLibrary.Get("ToyTeal", new Color(0.25f, 0.7f, 0.72f), 0.3f, 0f, MaterialLibrary.Checker()),
                MaterialLibrary.Get("ToyYellow", new Color(0.95f, 0.82f, 0.3f), 0.3f, 0f, MaterialLibrary.Checker()),
                MaterialLibrary.Get("ToyPurple", new Color(0.6f, 0.45f, 0.85f), 0.3f, 0f, MaterialLibrary.Checker()),
            };
        }

        // ------------------------------------------------------------------ Helpers

        void Stairs(Vector3 bottomFront, Vector3 dir, float width, int steps, float rise, float run, Material mat)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            for (int k = 0; k < steps; k++)
            {
                float h = rise * (k + 1);
                Vector3 c = bottomFront + dir * (run * (k + 0.5f)) + Vector3.up * (h * 0.5f);
                m_B.Box(c, new Vector3(width, h, run), rot, mat, 0.5f);
            }
        }

        void Tree(Vector3 basePos, float scale)
        {
            float h = R(2.6f, 3.6f) * scale;
            m_B.Cylinder(basePos, 0.22f * scale, h, m_Trunk, 0.5f, true, 10, 0.15f * scale);
            var leaf = Pick(m_Foliage);
            Vector3 top = basePos + Vector3.up * h;
            m_B.Ellipsoid(top + Vector3.up * 0.6f * scale, new Vector3(1.9f, 1.6f, 1.9f) * scale, Quaternion.Euler(0, R(0, 360), 0), leaf, false);
            for (int i = 0; i < 3; i++)
            {
                Vector3 o = Quaternion.Euler(0, i * 120f + R(-20, 20), 0) * Vector3.forward * 1.2f * scale;
                m_B.Ellipsoid(top + o + Vector3.up * R(-0.2f, 0.5f) * scale, Vector3.one * R(1.0f, 1.3f) * scale, Quaternion.identity, leaf, false, 14, 10);
            }
        }

        void Car(Vector3 pos, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            var paint = Pick(m_CarPaint);
            bool van = Chance(0.2f);
            float len = van ? 5.2f : R(4.2f, 4.7f), wid = 1.85f, bodyH = van ? 1.9f : 0.75f;
            m_B.Box(pos + rot * new Vector3(0, 0.35f + bodyH * 0.5f, 0), new Vector3(wid, bodyH, len), rot, paint, 0.5f);
            if (!van)
            {
                m_B.Box(pos + rot * new Vector3(0, 1.1f + 0.3f, -0.2f), new Vector3(wid * 0.86f, 0.6f, len * 0.5f), rot, m_Glass, 0.5f);
                m_B.Box(pos + rot * new Vector3(0, 1.72f, -0.2f), new Vector3(wid * 0.84f, 0.06f, len * 0.46f), rot, paint, 0.5f, false);
            }
            else
            {
                m_B.Box(pos + rot * new Vector3(0, 1.75f, len * 0.5f - 0.02f), new Vector3(wid * 0.85f, 0.55f, 0.06f), rot, m_Glass, 0.5f, false);
            }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 w = pos + rot * new Vector3(sx * (wid * 0.5f - 0.05f), 0.35f, sz * (len * 0.5f - 0.8f));
                    m_B.Ellipsoid(w, new Vector3(0.14f, 0.35f, 0.35f), rot, m_Tire, false, 12, 8);
                }
        }

        void Lamp(Vector3 pos, Vector3 armDir)
        {
            m_B.Cylinder(pos, 0.09f, 5.2f, m_Metal, 1f, true, 10, 0.06f);
            Quaternion rot = Quaternion.LookRotation(armDir);
            m_B.Box(pos + Vector3.up * 5.1f + armDir * 0.7f, new Vector3(0.08f, 0.08f, 1.4f), rot, m_Metal, 1f, false);
            m_B.Box(pos + Vector3.up * 5.0f + armDir * 1.4f, new Vector3(0.35f, 0.14f, 0.6f), rot, m_Lamp, 1f, false);
        }

        void Bench(Vector3 pos, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            m_B.Box(pos + rot * new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.5f), rot, m_Wood, 1f);
            m_B.Box(pos + rot * new Vector3(0, 0.75f, -0.22f), new Vector3(1.8f, 0.4f, 0.06f), rot, m_Wood, 1f);
            for (int s = -1; s <= 1; s += 2)
                m_B.Box(pos + rot * new Vector3(s * 0.8f, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.45f), rot, m_Metal, 1f);
        }

        void Crate(Vector3 pos, float size, float yaw)
        {
            m_B.Box(pos + Vector3.up * size * 0.5f, Vector3.one * size, Quaternion.Euler(0, yaw, 0), m_Wood, 1f / size);
        }

        void Barrel(Vector3 pos, Material m) => m_B.Cylinder(pos, 0.38f, 1.0f, m, 1f, true, 14, -1f, true);

        // ------------------------------------------------------------------ Blocks

        void Buildings(Vector3 c, bool ring, int bx, int bz)
        {
            float top = Curb;
            float inner = BlockSize;
            int split = Chance(0.5f) ? 2 : (Chance(0.5f) ? 3 : 1);
            var lots = new List<Rect>();
            if (split == 1) lots.Add(new Rect(-inner / 2, -inner / 2, inner, inner));
            else if (split == 2)
            {
                float h = inner / 2 - 1f;
                lots.Add(new Rect(-inner / 2, -inner / 2, h, h)); lots.Add(new Rect(1f, -inner / 2, h, h));
                lots.Add(new Rect(-inner / 2, 1f, h, h)); lots.Add(new Rect(1f, 1f, h, h));
            }
            else
            {
                float w = (inner - 4f) / 3f;
                for (int i = 0; i < 3; i++) lots.Add(new Rect(-inner / 2 + i * (w + 2f), -inner / 2, w, inner));
            }
            foreach (var lot in lots)
            {
                float inset = R(0.5f, 2f);
                float w = lot.width - inset * 2f, d = lot.height - inset * 2f;
                if (w < 4f || d < 4f) continue;
                Vector3 center = c + new Vector3(lot.center.x, top, lot.center.y);
                // Low-rise around the central play areas so they stay sunlit; towers grow toward the edge.
                bool nearCenter = Mathf.Abs(bx) <= 1 && Mathf.Abs(bz) <= 1;
                float h = nearCenter ? R(8f, 18f) : ring ? R(30f, 75f) : R(14f, 45f);
                if (!nearCenter && Chance(0.1f)) h = R(70f, 110f);
                h = Mathf.Max(7f, Mathf.Round(h / 3.5f) * 3.5f);
                Building(center, w, d, h);
            }
        }

        void Building(Vector3 basePos, float w, float d, float h)
        {
            var facade = Pick(m_Facades);
            m_B.Box(basePos + Vector3.up * (h * 0.5f), new Vector3(w, h, d), facade, 1f / 7f);
            // Storefront band.
            m_B.Box(basePos + Vector3.up * 2.1f, new Vector3(w + 0.3f, 4.2f, d + 0.3f), m_Storefront, 1f / 7f);
            // Ledges on some buildings (bumps a climbing creature has to negotiate).
            if (Chance(0.35f))
                for (float y = 7f; y < h - 2f; y += 7f)
                    m_B.Box(basePos + Vector3.up * y, new Vector3(w + 0.5f, 0.25f, d + 0.5f), m_Roof, 0.5f);
            // Roof cap and parapet.
            m_B.Box(basePos + Vector3.up * h, new Vector3(w + 0.5f, 0.6f, d + 0.5f), m_Roof, 0.25f);
            float roofY = h + 0.3f;
            if (Chance(0.4f) && w > 10f && d > 10f)
            {
                float w2 = w * R(0.45f, 0.7f), d2 = d * R(0.45f, 0.7f), h2 = Mathf.Round(h * R(0.25f, 0.6f) / 3.5f) * 3.5f + 3.5f;
                Vector3 b2 = basePos + new Vector3(R(-1f, 1f) * (w - w2) * 0.4f, roofY, R(-1f, 1f) * (d - d2) * 0.4f);
                m_B.Box(b2 + Vector3.up * (h2 * 0.5f), new Vector3(w2, h2, d2), facade, 1f / 7f);
                m_B.Box(b2 + Vector3.up * h2, new Vector3(w2 + 0.4f, 0.5f, d2 + 0.4f), m_Roof, 0.25f);
            }
            int props = RI(1, 4);
            for (int i = 0; i < props; i++)
            {
                Vector3 p = basePos + new Vector3(R(-w * 0.35f, w * 0.35f), roofY, R(-d * 0.35f, d * 0.35f));
                if (Chance(0.3f))
                {
                    m_B.Box(p + Vector3.up * 1f, new Vector3(1.6f, 2f, 1.6f), m_Metal, 0.5f);
                    m_B.Cylinder(p + Vector3.up * 2f, 1.3f, 2.2f, m_Wood, 0.5f, true, 14, 1.2f, true);
                }
                else m_B.Box(p + Vector3.up * 0.6f, new Vector3(R(1.5f, 2.5f), 1.2f, R(1.2f, 2f)), m_Metal, 0.5f);
            }
        }

        void Plaza(Vector3 c)
        {
            float S = Curb;
            PlayerSpawn = c + new Vector3(0, S, -4f);
            PlayerForward = Vector3.forward;
            spawnPoints.Add(c + new Vector3(-4, S, -12));
            spawnPoints.Add(c + new Vector3(6, S, -2));
            // Concrete inlay across the plaza.
            m_B.Box(c + new Vector3(0, S + 0.005f, 0), new Vector3(34f, 0.01f, 34f), m_Concrete, 1f / 4f, false);

            // Raised platform with stairs and a ramp.
            m_B.Box(c + new Vector3(11f, S + 1f, 11f), new Vector3(9f, 2f, 9f), m_Toy[1], 0.5f);
            Stairs(c + new Vector3(2.5f, S, 11f), Vector3.right, 4f, 10, 0.2f, 0.4f, m_Concrete);
            m_B.Wedge(c + new Vector3(11f, S + 1f, 2f), new Vector3(4f, 2f, 9f), Quaternion.identity, m_Toy[2], 0.5f);

            // Quarter pipe.
            m_B.Mesh(ProcMesh.QuarterPipe(3f, 7f, 16, 0.5f), c + new Vector3(-12f, S, 9f), Quaternion.identity, m_Toy[0]);
            // Half-buried dome.
            m_B.Ellipsoid(c + new Vector3(-12f, S - 1f, -9f), Vector3.one * 3f, Quaternion.identity, m_Toy[3], true, 28, 18);
            // Tunnel with a ceiling.
            m_B.Box(c + new Vector3(8.5f, S + 1.2f, -11f), new Vector3(0.6f, 2.4f, 7f), m_Concrete, 0.5f);
            m_B.Box(c + new Vector3(13.5f, S + 1.2f, -11f), new Vector3(0.6f, 2.4f, 7f), m_Concrete, 0.5f);
            m_B.Box(c + new Vector3(11f, S + 2.65f, -11f), new Vector3(6.2f, 0.5f, 7f), m_Toy[1], 0.5f);
            // Pillars.
            for (int i = -1; i <= 1; i++) m_B.Cylinder(c + new Vector3(i * 4f, S, -16f), 0.45f, 5f, m_Concrete, 0.5f, true, 16, -1f, true);
            // Rubble field: uneven footing.
            for (int i = 0; i < 46; i++)
            {
                Vector3 p = c + new Vector3(R(-7f, 1f), S, R(3f, 9f));
                Vector3 size = new Vector3(R(0.3f, 1.1f), R(0.12f, 0.45f), R(0.3f, 1.1f));
                Quaternion rot = Quaternion.Euler(R(-12f, 12f), R(0, 360f), R(-12f, 12f));
                m_B.Box(p + Vector3.up * (size.y * 0.3f), size, rot, i % 3 == 0 ? m_Rock : m_Concrete, 1f);
            }
            // Crates and barrels.
            Crate(c + new Vector3(5f, S, -4f), 1f, 10f);
            Crate(c + new Vector3(6.1f, S, -4.2f), 1f, -5f);
            Crate(c + new Vector3(5.5f, S + 1f, -4.1f), 1f, 30f);
            Crate(c + new Vector3(4.2f, S, -6f), 0.7f, 45f);
            Barrel(c + new Vector3(7.5f, S, -6f), m_Toy[0]);
            Barrel(c + new Vector3(8.3f, S, -5.2f), m_Toy[1]);
            // Tilted slabs.
            m_B.Box(c + new Vector3(-5f, S + 0.9f, -12f), new Vector3(4f, 0.3f, 6f), Quaternion.Euler(-18f, 0, 0), m_Toy[2], 0.5f);
            m_B.Box(c + new Vector3(-9f, S + 1.1f, -3f), new Vector3(3f, 0.3f, 5f), Quaternion.Euler(0, 90f, 24f), m_Toy[3], 0.5f);
            // Steep wedges: walkable and too-steep slopes.
            m_B.Wedge(c + new Vector3(-16f, S + 1.25f, -2f), new Vector3(3f, 2.5f, 3.6f), Quaternion.Euler(0, 90f, 0), m_Toy[0], 0.5f);
            m_B.Wedge(c + new Vector3(-16f, S + 1.25f, 3f), new Vector3(3f, 2.5f, 2.1f), Quaternion.Euler(0, 90f, 0), m_Toy[2], 0.5f);
            // Curbs and low walls.
            m_B.Box(c + new Vector3(0f, S + 0.15f, -8f), new Vector3(6f, 0.3f, 1f), m_Concrete, 0.5f);
            m_B.Box(c + new Vector3(0f, S + 0.3f, -11f), new Vector3(6f, 0.6f, 0.4f), m_Concrete, 0.5f);
            // Speed bumps.
            var bump = ProcMesh.HalfCylinder(0.35f, 6f, 12, 0.5f);
            for (int i = 0; i < 3; i++) m_B.Mesh(bump, c + new Vector3(3f, S, 13f + i * 1.6f), Quaternion.identity, m_Toy[1]);
            // Corner trees and benches.
            Tree(c + new Vector3(-16f, S, -16f), 1f);
            Tree(c + new Vector3(16f, S, -16f), 1.1f);
            Tree(c + new Vector3(-16f, S, 16f), 0.9f);
            Bench(c + new Vector3(-3f, S, -15f), 0f);
            Bench(c + new Vector3(15f, S, 0f), -90f);
        }

        void Park(Vector3 c)
        {
            float S = Curb;
            spawnPoints.Add(c + new Vector3(-6, S, 0));
            float size = 38f;
            float Height(float x, float z)
            {
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((size * 0.5f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) / 5f));
                float h = 1.6f * Mathf.Sin(x * 0.19f) * Mathf.Cos(z * 0.16f) + 0.9f * Mathf.Sin(x * 0.37f + 1f) * Mathf.Sin(z * 0.31f + 2f) + 1.3f;
                return Mathf.Max(0f, h) * edge;
            }
            var hf = ProcMesh.Heightfield(56, size, Height, 1f / 6f);
            m_B.Mesh(hf, c + Vector3.up * (S + 0.01f), Quaternion.identity, m_Grass);
            for (int i = 0; i < 12; i++)
            {
                float x = R(-15f, 15f), z = R(-15f, 15f);
                if (Mathf.Abs(x) < 4f && Mathf.Abs(z) < 4f) continue;
                Tree(c + new Vector3(x, S + Height(x, z) - 0.1f, z), R(0.9f, 1.3f));
            }
            for (int i = 0; i < 10; i++)
            {
                float x = R(-16f, 16f), z = R(-16f, 16f);
                Vector3 r = new Vector3(R(0.5f, 1.4f), R(0.35f, 0.9f), R(0.5f, 1.3f));
                m_B.Ellipsoid(c + new Vector3(x, S + Height(x, z), z), r, Quaternion.Euler(R(-10, 10), R(0, 360), 0), m_Rock, true, 12, 8);
            }
        }

        void Skatepark(Vector3 c)
        {
            float S = Curb;
            spawnPoints.Add(c + new Vector3(0, S, 0));
            m_B.Box(c + new Vector3(0, S + 0.005f, 0), new Vector3(34f, 0.01f, 34f), m_Concrete, 1f / 4f, false);
            // Half pipe from two quarter pipes.
            var qp = ProcMesh.QuarterPipe(3.5f, 10f, 20, 0.5f);
            m_B.Mesh(qp, c + new Vector3(0, S, -3f), Quaternion.Euler(0, 180f, 0), m_Toy[1]);
            m_B.Mesh(qp, c + new Vector3(0, S, 3f), Quaternion.identity, m_Toy[1]);
            // Fun box with ramps.
            m_B.Box(c + new Vector3(-11f, S + 0.5f, 11f), new Vector3(4f, 1f, 4f), m_Toy[2], 0.5f);
            m_B.Wedge(c + new Vector3(-11f, S + 0.5f, 7.5f), new Vector3(4f, 1f, 3f), Quaternion.identity, m_Toy[0], 0.5f);
            m_B.Wedge(c + new Vector3(-11f, S + 0.5f, 14.5f), new Vector3(4f, 1f, 3f), Quaternion.Euler(0, 180f, 0), m_Toy[0], 0.5f);
            // Big tank: a curved wall to climb.
            m_B.Cylinder(c + new Vector3(11f, S, 12f), 3f, 8f, m_Toy[3], 0.5f, true, 32, -1f, true);
            // Stacked tower with overhangs.
            for (int i = 0; i < 4; i++)
            {
                Vector3 o = new Vector3(i % 2 == 0 ? 0f : 1.2f, S + 1.5f + i * 3f, i % 2 == 0 ? 0f : -1f);
                m_B.Box(c + new Vector3(12f, 0, -11f) + o, new Vector3(4f - i * 0.3f, 3f, 4f - i * 0.3f), m_Toy[i % m_Toy.Length], 0.5f);
            }
            // Bumps.
            var bump = ProcMesh.HalfCylinder(0.8f, 8f, 16, 0.5f);
            for (int i = 0; i < 3; i++) m_B.Mesh(bump, c + new Vector3(-12f, S, -6f - i * 3.2f), Quaternion.identity, m_Toy[2]);
            // Pyramid.
            for (int i = 0; i < 4; i++)
            {
                Quaternion rot = Quaternion.Euler(0, i * 90f, 0);
                m_B.Wedge(c + new Vector3(0, S + 1.25f, 13f) - rot * Vector3.forward * 1.5f, new Vector3(6f, 2.5f, 3f), rot, m_Toy[0], 0.5f);
            }
        }

        void Amphitheatre(Vector3 c)
        {
            float S = Curb;
            spawnPoints.Add(c + new Vector3(0, S, 12));
            m_B.Box(c + new Vector3(0, S + 0.005f, 0), new Vector3(34f, 0.01f, 34f), m_Concrete, 1f / 4f, false);
            float platH = 2.7f;
            m_B.Box(c + new Vector3(0, S + platH * 0.5f, -10f), new Vector3(20f, platH, 12f), m_Concrete, 0.25f);
            // Grand stairs up to the platform.
            Stairs(c + new Vector3(0, S, -4f + 15 * 0.35f), Vector3.back, 12f, 15, 0.18f, 0.35f, m_Pavers);
            // Side ramp.
            m_B.Wedge(c + new Vector3(12f, S + platH * 0.5f, -6f), new Vector3(4f, platH, 16f), Quaternion.Euler(0, 180f, 0), m_Concrete, 0.25f);
            // Giant steps only a climber can take.
            Stairs(c + new Vector3(-13f, S, 2f), Vector3.back, 4f, 4, 0.6f, 0.9f, m_Toy[3]);
            // Gazebo on the platform.
            Vector3 g = c + new Vector3(0, S + platH, -11f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m_B.Cylinder(g + new Vector3(sx * 2.5f, 0, sz * 2.5f), 0.2f, 3f, m_Paint, 0.5f, true, 12, -1f, true);
            m_B.Box(g + new Vector3(0, 3.15f, 0), new Vector3(6f, 0.3f, 6f), m_Paint, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                Quaternion rot = Quaternion.Euler(0, i * 90f, 0);
                m_B.Wedge(g + Vector3.up * 3.8f - rot * Vector3.forward * 1.5f, new Vector3(6f, 1f, 3f), rot, m_Toy[0], 0.5f);
            }
            // Terraced seating on the sides.
            for (int i = 0; i < 4; i++)
            {
                m_B.Box(c + new Vector3(-15f + i * 0.0f, S + 0.225f + i * 0.45f, 8f - i * 1.1f), new Vector3(6f, 0.45f + i * 0.9f, 1.1f), m_Concrete, 0.5f);
                m_B.Box(c + new Vector3(15f, S + 0.2f + i * 0.4f, 12f - i * 1.2f), new Vector3(5f, 0.4f + i * 0.8f, 1.2f), m_Pavers, 0.5f);
            }
        }

        // ------------------------------------------------------------------ Streets

        void StreetFurniture(Vector3 c, int bx, int bz)
        {
            float S = Curb;
            float edge = BlockSize * 0.5f + Sidewalk; // slab edge
            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0, side * 90f, 0);
                Vector3 outward = rot * Vector3.forward;
                Vector3 along = rot * Vector3.right;
                Vector3 curb = c + outward * (edge - 0.6f) + Vector3.up * S;
                for (float t = -BlockSize * 0.5f + 3f; t <= BlockSize * 0.5f - 3f; t += 12f)
                    Lamp(curb + along * t, outward);
                bool trees = (bx + bz + side) % 2 == 0;
                for (float t = -BlockSize * 0.5f + 9f; t <= BlockSize * 0.5f - 3f; t += 12f)
                {
                    Vector3 p = curb + along * t - outward * 0.3f;
                    if (trees) Tree(p, R(0.8f, 1.05f));
                    else if (Chance(0.5f)) Bench(p - outward * 0.4f, side * 90f + 180f);
                    else m_B.Cylinder(p, 0.3f, 0.9f, m_Metal, 1f, true, 12, 0.28f, true);
                }
                // Parked cars along the curb.
                Vector3 lane = c + outward * (edge + 1.4f);
                for (float t = -BlockSize * 0.5f + 2f; t <= BlockSize * 0.5f - 2f; t += 6.2f)
                    if (Chance(0.4f)) Car(lane + along * t, side * 90f + 90f + (Chance(0.5f) ? 180f : 0f));
                if (Chance(0.25f))
                    for (int k = 0; k < 3; k++)
                        m_B.Cylinder(lane + outward * 2.5f + along * (R(-10f, 10f)), 0.22f, 0.7f, m_Toy[0], 1f, true, 10, 0.03f, true);
            }
        }

        void Roads()
        {
            int half = Blocks / 2;
            float ext = Extent;
            for (int i = -half - 1; i <= half; i++)
            {
                float line = (i + 0.5f) * Pitch;
                for (int axis = 0; axis < 2; axis++)
                {
                    for (int b = -half; b <= half; b++)
                    {
                        float segCenter = b * Pitch;
                        for (float t = -BlockSize * 0.5f; t < BlockSize * 0.5f; t += 6f)
                        {
                            Vector3 p = axis == 0 ? new Vector3(line, 0.01f, segCenter + t + 1.5f) : new Vector3(segCenter + t + 1.5f, 0.01f, line);
                            Vector3 size = axis == 0 ? new Vector3(0.15f, 0.02f, 3f) : new Vector3(3f, 0.02f, 0.15f);
                            m_B.Box(p, size, i == 0 || i == -1 ? m_PaintYellow : m_Paint, 1f, false);
                        }
                        // Crosswalk at the block's end.
                        for (int k = -3; k <= 3; k++)
                        {
                            float across = k * 1.1f;
                            float at = segCenter + BlockSize * 0.5f + Sidewalk - 1.8f;
                            Vector3 p = axis == 0 ? new Vector3(line + across, 0.01f, at) : new Vector3(at, 0.01f, line + across);
                            Vector3 size = axis == 0 ? new Vector3(0.55f, 0.02f, 2.4f) : new Vector3(2.4f, 0.02f, 0.55f);
                            m_B.Box(p, size, m_Paint, 1f, false);
                        }
                    }
                }
            }
            // A few loose obstacles on the roads.
            for (int i = 0; i < 30; i++)
            {
                int line = RI(-half - 1, half + 1);
                float along = R(-ext, ext);
                bool xAxis = Chance(0.5f);
                Vector3 p = xAxis ? new Vector3((line + 0.5f) * Pitch + R(-2f, 2f), 0, along) : new Vector3(along, 0, (line + 0.5f) * Pitch + R(-2f, 2f));
                if (Chance(0.5f)) Crate(p, R(0.6f, 1.1f), R(0, 90));
                else Barrel(p, Pick(m_Toy));
            }
        }

        void Overpass()
        {
            // Elevated deck over the street east of the plaza: pillars to climb, an underside to hang from.
            float x = 0.5f * Pitch;
            float y = 7f;
            float len = Pitch * 4f;
            m_B.Box(new Vector3(x, y, 0), new Vector3(10f, 1f, len), m_Concrete, 0.25f);
            m_B.Box(new Vector3(x - 4.8f, y + 1f, 0), new Vector3(0.4f, 1f, len), m_Concrete, 0.25f);
            m_B.Box(new Vector3(x + 4.8f, y + 1f, 0), new Vector3(0.4f, 1f, len), m_Concrete, 0.25f);
            for (float z = -len * 0.5f + 10f; z <= len * 0.5f - 10f; z += 25f)
            {
                if (Mathf.Abs(Mathf.Repeat(z + Pitch * 0.5f, Pitch) - Pitch * 0.5f) > BlockSize * 0.5f - 4f) continue; // keep intersections clear
                m_B.Box(new Vector3(x, (y - 0.5f) * 0.5f, z), new Vector3(1.4f, y - 0.5f, 1.4f), m_Concrete, 0.25f);
            }
            // Access stairs at the south end so walkers can get up too.
            float zEnd = -len * 0.5f;
            Stairs(new Vector3(x, 0, zEnd - 36 * 0.39f + 0.01f), Vector3.forward, 4f, 36, (y + 0.5f) / 36f, 0.39f, m_Concrete);
        }

        void Skyline()
        {
            // Distant, collision-free silhouettes to fill the horizon.
            var mat = MaterialLibrary.Get("Skyline", new Color(0.62f, 0.68f, 0.78f), 0.2f, 0f, MaterialLibrary.Facade(0));
            for (int i = 0; i < 90; i++)
            {
                float a = R(0, Mathf.PI * 2f);
                float r = R(Extent + 60f, Extent + 260f);
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float h = R(20f, 90f);
                m_B.Box(p + Vector3.up * h * 0.5f, new Vector3(R(12f, 30f), h, R(12f, 30f)), Quaternion.Euler(0, R(0, 90), 0), mat, 1f / 7f, false);
            }
        }
    }
}
