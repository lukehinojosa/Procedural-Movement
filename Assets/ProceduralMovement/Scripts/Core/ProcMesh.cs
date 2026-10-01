using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralMovement
{
    /// <summary>
    /// Procedural mesh generators. Every shape used by the creatures and the city is built here,
    /// so the project has no imported models at all.
    /// Winding convention: a triangle (a, b, c) faces along cross(b - a, c - a).
    /// </summary>
    public static class ProcMesh
    {
        public class Builder
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> tris = new List<int>();

            public int AddVertex(Vector3 p, Vector3 n, Vector2 uv)
            {
                verts.Add(p);
                normals.Add(n);
                uvs.Add(uv);
                return verts.Count - 1;
            }

            /// <summary>Adds a quad, flipping the winding if needed so it faces <paramref name="outward"/>.</summary>
            public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Vector2 uvSize, Vector2 uvOffset = default)
            {
                Vector3 n = outward.normalized;
                int i0 = AddVertex(p0, n, uvOffset + new Vector2(0, 0));
                int i1 = AddVertex(p1, n, uvOffset + new Vector2(uvSize.x, 0));
                int i2 = AddVertex(p2, n, uvOffset + new Vector2(uvSize.x, uvSize.y));
                int i3 = AddVertex(p3, n, uvOffset + new Vector2(0, uvSize.y));
                Vector3 c = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(c, n) >= 0f)
                {
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i0); tris.Add(i2); tris.Add(i3);
                }
                else
                {
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i0); tris.Add(i3); tris.Add(i2);
                }
            }

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float uvScale)
            {
                Vector3 n = outward.normalized;
                Vector3 t = (b - a).normalized;
                Vector3 bt = Vector3.Cross(n, t);
                int ia = AddVertex(a, n, new Vector2(0, 0));
                int ib = AddVertex(b, n, new Vector2(Vector3.Dot(b - a, t), Vector3.Dot(b - a, bt)) * uvScale);
                int ic = AddVertex(c, n, new Vector2(Vector3.Dot(c - a, t), Vector3.Dot(c - a, bt)) * uvScale);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) { tris.Add(ia); tris.Add(ib); tris.Add(ic); }
                else { tris.Add(ia); tris.Add(ic); tris.Add(ib); }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(normals);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        /// <summary>Axis aligned box centred on the origin. UVs are in metres * uvScale so textures tile at a constant density.</summary>
        public static Mesh Box(Vector3 size, float uvScale = 1f)
        {
            var b = new Builder();
            AddBox(b, Vector3.zero, size, Quaternion.identity, uvScale);
            return b.ToMesh("Box");
        }

        public static void AddBox(Builder b, Vector3 center, Vector3 size, Quaternion rot, float uvScale)
        {
            Vector3 h = size * 0.5f;
            Vector3 X = rot * Vector3.right, Y = rot * Vector3.up, Z = rot * Vector3.forward;
            Vector3 P(float x, float y, float z) => center + X * (x * h.x) + Y * (y * h.y) + Z * (z * h.z);
            // +X / -X
            b.AddQuad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), X, new Vector2(size.z, size.y) * uvScale);
            b.AddQuad(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), -X, new Vector2(size.z, size.y) * uvScale);
            // +Z / -Z
            b.AddQuad(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), Z, new Vector2(size.x, size.y) * uvScale);
            b.AddQuad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -Z, new Vector2(size.x, size.y) * uvScale);
            // +Y / -Y
            b.AddQuad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Y, new Vector2(size.x, size.z) * uvScale);
            b.AddQuad(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), -Y, new Vector2(size.x, size.z) * uvScale);
        }

        /// <summary>
        /// Capsule running along +Z from z = 0 to z = length, radius r0 at the start and r1 at the end,
        /// with hemispherical caps. Used for every limb bone.
        /// </summary>
        public static Mesh TaperedCapsule(float length, float r0, float r1, int sides = 14, int capRings = 5)
        {
            var rings = new List<(float z, float r, float nz)>();
            for (int i = 0; i <= capRings; i++)
            {
                float a = Mathf.Lerp(-90f, 0f, i / (float)capRings) * Mathf.Deg2Rad;
                rings.Add((r0 * Mathf.Sin(a), r0 * Mathf.Cos(a), Mathf.Sin(a)));
            }
            float slope = (r0 - r1) / Mathf.Max(length, 1e-4f);
            int bodyRings = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.05f, (r0 + r1))) + 1);
            for (int i = 1; i < bodyRings; i++)
            {
                float t = i / (float)(bodyRings - 1);
                rings.Add((length * t, Mathf.Lerp(r0, r1, t), slope * 0.5f));
            }
            for (int i = 1; i <= capRings; i++)
            {
                float a = Mathf.Lerp(0f, 90f, i / (float)capRings) * Mathf.Deg2Rad;
                rings.Add((length + r1 * Mathf.Sin(a), r1 * Mathf.Cos(a), Mathf.Sin(a)));
            }

            var b = new Builder();
            float total = length + r0 + r1;
            for (int ri = 0; ri < rings.Count; ri++)
            {
                var ring = rings[ri];
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    Vector3 p = radial * ring.r + new Vector3(0, 0, ring.z);
                    float nr = Mathf.Sqrt(Mathf.Max(0f, 1f - ring.nz * ring.nz));
                    Vector3 n = (radial * nr + new Vector3(0, 0, ring.nz)).normalized;
                    b.AddVertex(p, n, new Vector2(s / (float)sides, (ring.z + r0) / total));
                }
            }
            AddRingStrip(b.tris, 0, rings.Count, sides);
            return b.ToMesh("TaperedCapsule");
        }

        /// <summary>Connects consecutive rings (each sides+1 vertices) into quads that face outward.</summary>
        public static void AddRingStrip(List<int> tris, int firstVertex, int ringCount, int sides)
        {
            int stride = sides + 1;
            for (int r = 0; r < ringCount - 1; r++)
            {
                int a = firstVertex + r * stride;
                int c = a + stride;
                for (int s = 0; s < sides; s++)
                {
                    tris.Add(a + s); tris.Add(a + s + 1); tris.Add(c + s);
                    tris.Add(a + s + 1); tris.Add(c + s + 1); tris.Add(c + s);
                }
            }
        }

        public static Mesh Ellipsoid(Vector3 radii, int lon = 24, int lat = 16)
        {
            var b = new Builder();
            AddEllipsoid(b, Vector3.zero, radii, Quaternion.identity, lon, lat);
            return b.ToMesh("Ellipsoid");
        }

        public static void AddEllipsoid(Builder b, Vector3 center, Vector3 radii, Quaternion rot, int lon = 24, int lat = 16)
        {
            int first = b.verts.Count;
            // Rings along local Z (pole to pole) so the ring-strip winding matches the capsule.
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / (float)lat) * Mathf.Deg2Rad;
                float z = Mathf.Sin(a), rr = Mathf.Cos(a);
                for (int s = 0; s <= lon; s++)
                {
                    float ang = s / (float)lon * Mathf.PI * 2f;
                    Vector3 unit = new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr, z);
                    Vector3 p = Vector3.Scale(unit, radii);
                    Vector3 n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized;
                    b.AddVertex(center + rot * p, rot * n, new Vector2(s / (float)lon, i / (float)lat));
                }
            }
            AddRingStrip(b.tris, first, lat + 1, lon);
        }

        /// <summary>Cylinder standing on the XZ plane from y = 0 to y = height.</summary>
        public static void AddCylinder(Builder b, Vector3 baseCenter, float radius, float height, Quaternion rot, int sides = 16, bool caps = true, float uvScale = 1f, float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            int first = b.verts.Count;
            float circ = 2f * Mathf.PI * radius;
            float slope = (radius - topRadius) / Mathf.Max(0.001f, height);
            for (int r = 0; r < 2; r++)
            {
                float y = r * height;
                float rad = r == 0 ? radius : topRadius;
                for (int s = 0; s <= sides; s++)
                {
                    float ang = -s / (float)sides * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 n = (radial + Vector3.up * slope).normalized;
                    b.AddVertex(baseCenter + rot * (radial * rad + Vector3.up * y), rot * n, new Vector2(s / (float)sides * circ, y) * uvScale);
                }
            }
            // Ring strip along +Y with angle decreasing gives outward faces.
            int stride = sides + 1;
            for (int s = 0; s < sides; s++)
            {
                int a = first + s, c = first + stride + s;
                b.tris.Add(a); b.tris.Add(a + 1); b.tris.Add(c);
                b.tris.Add(a + 1); b.tris.Add(c + 1); b.tris.Add(c);
            }
            if (!caps) return;
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap * height;
                float rad = cap == 0 ? radius : topRadius;
                Vector3 n = rot * (cap == 0 ? Vector3.down : Vector3.up);
                int center = b.AddVertex(baseCenter + rot * (Vector3.up * y), n, Vector2.zero);
                int ringStart = b.verts.Count;
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(ang) * rad, y, Mathf.Sin(ang) * rad);
                    b.AddVertex(baseCenter + rot * p, n, new Vector2(p.x, p.z) * uvScale);
                }
                for (int s = 0; s < sides; s++)
                {
                    int i0 = ringStart + s, i1 = ringStart + s + 1;
                    Vector3 pa = b.verts[center], pb = b.verts[i0], pc = b.verts[i1];
                    if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), n) >= 0f) { b.tris.Add(center); b.tris.Add(i0); b.tris.Add(i1); }
                    else { b.tris.Add(center); b.tris.Add(i1); b.tris.Add(i0); }
                }
            }
        }

        /// <summary>Solid wedge (ramp). Base is size.x by size.z, rising along +Z to size.y.</summary>
        public static void AddWedge(Builder b, Vector3 center, Vector3 size, Quaternion rot, float uvScale)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 a0 = P(-1, -1, -1), a1 = P(1, -1, -1), a2 = P(1, -1, 1), a3 = P(-1, -1, 1);
            Vector3 t2 = P(1, 1, 1), t3 = P(-1, 1, 1);
            Vector3 slopeN = Vector3.Cross(t2 - a1, a0 - a1);
            if (Vector3.Dot(slopeN, rot * Vector3.up) < 0) slopeN = -slopeN;
            float slopeLen = Mathf.Sqrt(size.y * size.y + size.z * size.z);
            b.AddQuad(a0, a1, t2, t3, slopeN, new Vector2(size.x, slopeLen) * uvScale);
            b.AddQuad(a0, a1, a2, a3, rot * Vector3.down, new Vector2(size.x, size.z) * uvScale);
            b.AddQuad(a3, a2, t2, t3, rot * Vector3.forward, new Vector2(size.x, size.y) * uvScale);
            b.AddTriangle(a1, a2, t2, rot * Vector3.right, uvScale);
            b.AddTriangle(a0, a3, t3, rot * Vector3.left, uvScale);
        }

        /// <summary>
        /// Concave quarter pipe: a curved surface bending from the floor (y = 0) up to vertical (z = radius) along +Z,
        /// plus a solid back so it can be climbed over.
        /// </summary>
        public static Mesh QuarterPipe(float radius, float width, int segments, float uvScale)
        {
            var b = new Builder();
            int first = b.verts.Count;
            float arc = Mathf.PI * 0.5f * radius;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 0.5f;
                // Circle centred at (y = radius, z = 0); surface goes from (0,0) to (radius, radius).
                float z = Mathf.Sin(a) * radius;
                float y = radius - Mathf.Cos(a) * radius;
                Vector3 n = new Vector3(0, Mathf.Cos(a), -Mathf.Sin(a));
                for (int s = 0; s <= 1; s++)
                {
                    float x = (s - 0.5f) * width;
                    b.AddVertex(new Vector3(x, y, z), n, new Vector2(x, a * radius) * uvScale);
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 2;
                // Quad (a, a+1, a+3, a+2) facing the concave side.
                Vector3 pa = b.verts[a], pb = b.verts[a + 1], pc = b.verts[a + 3];
                Vector3 nn = b.normals[a];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), nn) >= 0f)
                { b.tris.Add(a); b.tris.Add(a + 1); b.tris.Add(a + 3); b.tris.Add(a); b.tris.Add(a + 3); b.tris.Add(a + 2); }
                else
                { b.tris.Add(a); b.tris.Add(a + 3); b.tris.Add(a + 1); b.tris.Add(a); b.tris.Add(a + 2); b.tris.Add(a + 3); }
            }
            float hw = width * 0.5f;
            // Back wall, top deck and side profiles.
            float deck = 1.0f;
            b.AddQuad(new Vector3(-hw, 0, radius + deck), new Vector3(hw, 0, radius + deck), new Vector3(hw, radius, radius + deck), new Vector3(-hw, radius, radius + deck), Vector3.forward, new Vector2(width, radius) * uvScale);
            b.AddQuad(new Vector3(-hw, radius, radius), new Vector3(hw, radius, radius), new Vector3(hw, radius, radius + deck), new Vector3(-hw, radius, radius + deck), Vector3.up, new Vector2(width, deck) * uvScale);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * hw;
                Vector3 n = new Vector3(side, 0, 0);
                Vector3 back0 = new Vector3(x, 0, radius + deck);
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i / (float)segments * Mathf.PI * 0.5f, a1 = (i + 1) / (float)segments * Mathf.PI * 0.5f;
                    Vector3 p0 = new Vector3(x, radius - Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
                    Vector3 p1 = new Vector3(x, radius - Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    b.AddQuad(new Vector3(x, 0, p0.z), new Vector3(x, 0, p1.z), p1, p0, n, Vector2.one * uvScale);
                }
                b.AddQuad(new Vector3(x, 0, radius), back0, new Vector3(x, radius, radius + deck), new Vector3(x, radius, radius), n, new Vector2(deck, radius) * uvScale);
            }
            return b.ToMesh("QuarterPipe");
        }

        /// <summary>Grid heightfield centred on the origin.</summary>
        public static Mesh Heightfield(int res, float size, System.Func<float, float, float> height, float uvScale)
        {
            var b = new Builder();
            float step = size / res;
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    float px = -size * 0.5f + x * step, pz = -size * 0.5f + z * step;
                    float hC = height(px, pz);
                    float hX = height(px + 0.1f, pz) - height(px - 0.1f, pz);
                    float hZ = height(px, pz + 0.1f) - height(px, pz - 0.1f);
                    Vector3 n = new Vector3(-hX / 0.2f, 1f, -hZ / 0.2f).normalized;
                    b.AddVertex(new Vector3(px, hC, pz), n, new Vector2(px, pz) * uvScale);
                }
            int w = res + 1;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i = z * w + x;
                    // Faces up: (i, i+w, i+1) -> cross((0,0,1),(1,0,0)) = +Y.
                    b.tris.Add(i); b.tris.Add(i + w); b.tris.Add(i + 1);
                    b.tris.Add(i + 1); b.tris.Add(i + w); b.tris.Add(i + w + 1);
                }
            return b.ToMesh("Heightfield");
        }

        /// <summary>Half cylinder bump lying along X on the ground.</summary>
        public static Mesh HalfCylinder(float radius, float length, int segments, float uvScale)
        {
            var b = new Builder();
            int first = b.verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * i / segments;
                Vector3 n = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
                for (int s = 0; s <= 1; s++)
                {
                    float x = (s - 0.5f) * length;
                    b.AddVertex(new Vector3(x, n.y * radius, n.z * radius), n, new Vector2(x, a * radius) * uvScale);
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 2;
                Vector3 pa = b.verts[a], pb = b.verts[a + 1], pc = b.verts[a + 3];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), b.normals[a] + b.normals[a + 2]) >= 0f)
                { b.tris.Add(a); b.tris.Add(a + 1); b.tris.Add(a + 3); b.tris.Add(a); b.tris.Add(a + 3); b.tris.Add(a + 2); }
                else
                { b.tris.Add(a); b.tris.Add(a + 3); b.tris.Add(a + 1); b.tris.Add(a); b.tris.Add(a + 2); b.tris.Add(a + 3); }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * length * 0.5f;
                Vector3 n = new Vector3(side, 0, 0);
                int c = b.AddVertex(new Vector3(x, 0, 0), n, Vector2.zero);
                int start = b.verts.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.PI * i / segments;
                    b.AddVertex(new Vector3(x, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius), n, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * uvScale);
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = start + i, i1 = start + i + 1;
                    if (Vector3.Dot(Vector3.Cross(b.verts[i0] - b.verts[c], b.verts[i1] - b.verts[c]), n) >= 0f) { b.tris.Add(c); b.tris.Add(i0); b.tris.Add(i1); }
                    else { b.tris.Add(c); b.tris.Add(i1); b.tris.Add(i0); }
                }
            }
            return b.ToMesh("HalfCylinder");
        }
    }
}
