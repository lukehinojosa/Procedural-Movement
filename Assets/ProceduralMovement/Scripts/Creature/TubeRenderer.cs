using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// Smooth tube skinned over moving control points (Catmull-Rom). Used for torsos, necks, tails and manes,
    /// so the body deforms continuously as the spine bends, the neck bobs or the tail swings.
    /// Cross sections can be elliptical, oriented by a reference direction.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TubeRenderer : MonoBehaviour
    {
        public int sides = 16;
        public int samplesPerSpan = 6;
        public int capRings = 5;
        public bool capStart = true, capEnd = true;

        Mesh m_Mesh;
        Vector3[] m_Verts, m_Normals;
        Vector2[] m_Uvs;
        int m_PointCount = -1;
        int m_RingCount;

        // Scratch buffers for sampled rings.
        Vector3[] m_SP, m_ST;
        float[] m_SR;
        Vector2[] m_SA;

        public void Init(Material material, int sides = 16, int samplesPerSpan = 6)
        {
            this.sides = sides;
            this.samplesPerSpan = samplesPerSpan;
            GetComponent<MeshRenderer>().sharedMaterial = material;
            m_Mesh = new Mesh { name = "Tube" };
            m_Mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;
        }

        void Allocate(int pointCount)
        {
            m_PointCount = pointCount;
            int samples = (pointCount - 1) * samplesPerSpan + 1;
            m_SP = new Vector3[samples]; m_ST = new Vector3[samples]; m_SR = new float[samples]; m_SA = new Vector2[samples];
            m_RingCount = samples + (capStart ? capRings : 0) + (capEnd ? capRings : 0);
            int vc = m_RingCount * (sides + 1);
            m_Verts = new Vector3[vc]; m_Normals = new Vector3[vc]; m_Uvs = new Vector2[vc];
            var tris = new System.Collections.Generic.List<int>(m_RingCount * sides * 6);
            ProcMesh.AddRingStrip(tris, 0, m_RingCount, sides);
            m_Mesh.Clear();
            m_Mesh.vertices = m_Verts;
            m_Mesh.normals = m_Normals;
            m_Mesh.uv = m_Uvs;
            m_Mesh.SetTriangles(tris, 0);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static float CR(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// Rebuilds the tube. Points are world space. <paramref name="refDir"/> orients elliptical cross sections:
        /// aspect.x scales along cross(tangent, refDir), aspect.y along the remaining axis.
        /// </summary>
        public void Build(Vector3[] points, float[] radii, Vector2[] aspects, Vector3 refDir)
        {
            int n = points.Length;
            if (n < 2 || m_Mesh == null) return;
            if (n != m_PointCount) Allocate(n);

            // Sample the spline.
            int s = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(n - 1, i + 2)];
                // Mirror end tangents so the spline does not flatten at the ends.
                if (i == 0) p0 = p1 - (p2 - p1);
                if (i + 2 >= n) p3 = p2 + (p2 - p1);
                float r0 = radii[Mathf.Max(0, i - 1)], r1 = radii[i], r2 = radii[i + 1], r3 = radii[Mathf.Min(n - 1, i + 2)];
                Vector2 a1 = aspects != null ? aspects[i] : Vector2.one, a2 = aspects != null ? aspects[i + 1] : Vector2.one;
                int count = (i == n - 2) ? samplesPerSpan + 1 : samplesPerSpan;
                for (int k = 0; k < count; k++)
                {
                    float t = k / (float)samplesPerSpan;
                    m_SP[s] = CR(p0, p1, p2, p3, t);
                    float lo = Mathf.Min(r1, r2) * 0.85f, hi = Mathf.Max(r1, r2) * 1.1f;
                    m_SR[s] = Mathf.Clamp(CR(r0, r1, r2, r3, t), lo, hi);
                    float st = t * t * (3f - 2f * t);
                    m_SA[s] = Vector2.Lerp(a1, a2, st);
                    s++;
                }
            }
            int samples = s;
            for (int i = 0; i < samples; i++)
            {
                Vector3 a = m_SP[Mathf.Max(0, i - 1)], b = m_SP[Mathf.Min(samples - 1, i + 1)];
                Vector3 t = b - a;
                m_ST[i] = t.sqrMagnitude > 1e-10f ? t.normalized : (i > 0 ? m_ST[i - 1] : Vector3.forward);
            }

            Matrix4x4 w2l = transform.worldToLocalMatrix;
            int v = 0;
            Vector3 prevSide = Vector3.zero;
            float along = 0f;

            // Frames: side = cross(tangent, refDir), with continuity fallback.
            Vector3 Side(Vector3 tangent)
            {
                Vector3 sd = Vector3.Cross(tangent, refDir);
                if (sd.sqrMagnitude < 1e-6f) sd = prevSide.sqrMagnitude > 0 ? Vector3.ProjectOnPlane(prevSide, tangent) : Vector3.Cross(tangent, Vector3.up);
                if (sd.sqrMagnitude < 1e-6f) sd = Vector3.Cross(tangent, Vector3.right);
                prevSide = sd.normalized;
                return prevSide;
            }

            void Ring(Vector3 center, Vector3 tangent, Vector3 side, float r, Vector2 asp, float nz, float vCoord)
            {
                Vector3 other = Vector3.Cross(tangent, side);
                float nr = Mathf.Sqrt(Mathf.Max(0f, 1f - nz * nz));
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k / (float)sides * Mathf.PI * 2f;
                    float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    Vector3 offset = side * (c * r * asp.x) + other * (sn * r * asp.y);
                    Vector3 radial = (side * (c / asp.x) + other * (sn / asp.y)).normalized;
                    Vector3 nrm = radial * nr + tangent * nz;
                    m_Verts[v] = w2l.MultiplyPoint3x4(center + offset);
                    m_Normals[v] = w2l.MultiplyVector(nrm).normalized;
                    m_Uvs[v] = new Vector2(k / (float)sides, vCoord);
                    v++;
                }
            }

            if (capStart)
            {
                Vector3 t0 = m_ST[0];
                Vector3 sd = Side(t0);
                for (int c = capRings; c >= 1; c--)
                {
                    float a = c / (float)capRings * Mathf.PI * 0.5f;
                    float r = m_SR[0] * Mathf.Cos(a);
                    Vector3 center = m_SP[0] - t0 * (m_SR[0] * Mathf.Sin(a) * Mathf.Min(m_SA[0].x, m_SA[0].y));
                    Ring(center, t0, sd, Mathf.Max(r, 1e-4f), m_SA[0], -Mathf.Sin(a), -m_SR[0] * Mathf.Sin(a));
                }
                prevSide = Vector3.zero;
            }
            for (int i = 0; i < samples; i++)
            {
                if (i > 0) along += Vector3.Distance(m_SP[i], m_SP[i - 1]);
                Vector3 sd = Side(m_ST[i]);
                float drds = 0f;
                if (i > 0 && i < samples - 1)
                {
                    float ds = Vector3.Distance(m_SP[i + 1], m_SP[i - 1]);
                    if (ds > 1e-5f) drds = (m_SR[i + 1] - m_SR[i - 1]) / ds;
                }
                float nz = Mathf.Clamp(-drds, -0.8f, 0.8f);
                Ring(m_SP[i], m_ST[i], sd, m_SR[i], m_SA[i], nz, along);
            }
            if (capEnd)
            {
                int last = samples - 1;
                Vector3 t1 = m_ST[last];
                Vector3 sd = prevSide;
                for (int c = 1; c <= capRings; c++)
                {
                    float a = c / (float)capRings * Mathf.PI * 0.5f;
                    float r = m_SR[last] * Mathf.Cos(a);
                    Vector3 center = m_SP[last] + t1 * (m_SR[last] * Mathf.Sin(a) * Mathf.Min(m_SA[last].x, m_SA[last].y));
                    Ring(center, t1, sd, Mathf.Max(r, 1e-4f), m_SA[last], Mathf.Sin(a), along + m_SR[last] * Mathf.Sin(a));
                }
            }

            m_Mesh.vertices = m_Verts;
            m_Mesh.normals = m_Normals;
            m_Mesh.uv = m_Uvs;
            m_Mesh.RecalculateBounds();
        }
    }
}
