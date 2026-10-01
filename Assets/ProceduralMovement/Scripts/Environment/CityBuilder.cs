using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralMovement
{
    /// <summary>
    /// Accumulates geometry into batched meshes per (material, spatial cell) and creates lightweight colliders
    /// separately. A dense city ends up as a few hundred draw calls with working frustum culling.
    /// </summary>
    public class CityBuilder
    {
        readonly Transform m_Root;
        readonly Transform m_Colliders;
        readonly float m_CellSize;
        readonly Dictionary<(Material, int, int), ProcMesh.Builder> m_Batches = new Dictionary<(Material, int, int), ProcMesh.Builder>();
        readonly Dictionary<string, Mesh> m_ColliderMeshes = new Dictionary<string, Mesh>();
        public int ColliderCount { get; private set; }

        public CityBuilder(Transform root, float cellSize = 96f)
        {
            m_Root = root;
            m_CellSize = cellSize;
            m_Colliders = new GameObject("Colliders").transform;
            m_Colliders.SetParent(root, false);
        }

        ProcMesh.Builder Batch(Material mat, Vector3 pos)
        {
            var key = (mat, Mathf.FloorToInt(pos.x / m_CellSize), Mathf.FloorToInt(pos.z / m_CellSize));
            if (!m_Batches.TryGetValue(key, out var b) || b.verts.Count > 60000)
            {
                if (b != null) Flush(key.Item1, b);
                b = new ProcMesh.Builder();
                m_Batches[key] = b;
            }
            return b;
        }

        GameObject ColliderObject(string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Colliders, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.isStatic = true;
            ColliderCount++;
            return go;
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Material mat, float uvScale, bool collider = true)
        {
            ProcMesh.AddBox(Batch(mat, center), center, size, rot, uvScale);
            if (collider)
                ColliderObject("Box", center, rot).AddComponent<BoxCollider>().size = size;
        }

        public void Box(Vector3 center, Vector3 size, Material mat, float uvScale, bool collider = true) => Box(center, size, Quaternion.identity, mat, uvScale, collider);

        public void Cylinder(Vector3 baseCenter, float radius, float height, Material mat, float uvScale, bool collider = true, int sides = 16, float topRadius = -1f, bool exactCollider = false)
        {
            ProcMesh.AddCylinder(Batch(mat, baseCenter), baseCenter, radius, height, Quaternion.identity, sides, true, uvScale, topRadius);
            if (!collider) return;
            if (exactCollider)
            {
                string key = $"cyl{radius:F2}_{height:F2}_{topRadius:F2}_{sides}";
                if (!m_ColliderMeshes.TryGetValue(key, out var mesh))
                {
                    var b = new ProcMesh.Builder();
                    ProcMesh.AddCylinder(b, Vector3.zero, radius, height, Quaternion.identity, sides, true, 1f, topRadius);
                    mesh = b.ToMesh(key);
                    m_ColliderMeshes[key] = mesh;
                }
                var mc = ColliderObject("Cylinder", baseCenter, Quaternion.identity).AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
            }
            else
            {
                var cc = ColliderObject("Cylinder", baseCenter + Vector3.up * height * 0.5f, Quaternion.identity).AddComponent<CapsuleCollider>();
                cc.radius = Mathf.Max(radius, topRadius);
                cc.height = Mathf.Max(height, cc.radius * 2f);
                cc.direction = 1;
            }
        }

        public void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rot, Material mat, bool collider = true, int lon = 20, int lat = 14)
        {
            ProcMesh.AddEllipsoid(Batch(mat, center), center, radii, rot, lon, lat);
            if (!collider) return;
            if (Mathf.Approximately(radii.x, radii.y) && Mathf.Approximately(radii.y, radii.z))
            {
                ColliderObject("Sphere", center, rot).AddComponent<SphereCollider>().radius = radii.x;
            }
            else
            {
                string key = $"ell{radii.x:F2}_{radii.y:F2}_{radii.z:F2}";
                if (!m_ColliderMeshes.TryGetValue(key, out var mesh))
                {
                    mesh = ProcMesh.Ellipsoid(radii, 12, 8);
                    m_ColliderMeshes[key] = mesh;
                }
                var mc = ColliderObject("Ellipsoid", center, rot).AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
            }
        }

        public void Wedge(Vector3 center, Vector3 size, Quaternion rot, Material mat, float uvScale, bool collider = true)
        {
            ProcMesh.AddWedge(Batch(mat, center), center, size, rot, uvScale);
            if (!collider) return;
            string key = $"wedge{size.x:F2}_{size.y:F2}_{size.z:F2}";
            if (!m_ColliderMeshes.TryGetValue(key, out var mesh))
            {
                var b = new ProcMesh.Builder();
                ProcMesh.AddWedge(b, Vector3.zero, size, Quaternion.identity, 1f);
                mesh = b.ToMesh(key);
                m_ColliderMeshes[key] = mesh;
            }
            var mc = ColliderObject("Wedge", center, rot).AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
        }

        /// <summary>Adds an arbitrary mesh (e.g. heightfield, quarter pipe) with a matching non-convex collider.</summary>
        public void Mesh(Mesh mesh, Vector3 pos, Quaternion rot, Material mat, bool collider = true)
        {
            var b = Batch(mat, pos);
            int start = b.verts.Count;
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var t = mesh.triangles;
            for (int i = 0; i < v.Length; i++)
                b.AddVertex(pos + rot * v[i], rot * n[i], uv.Length > i ? uv[i] : Vector2.zero);
            for (int i = 0; i < t.Length; i++) b.tris.Add(start + t[i]);
            if (collider)
                ColliderObject("Mesh", pos, rot).AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        void Flush(Material mat, ProcMesh.Builder b)
        {
            if (b.verts.Count == 0) return;
            var go = new GameObject("Batch_" + mat.name);
            go.transform.SetParent(m_Root, false);
            go.isStatic = true;
            var mesh = b.ToMesh(go.name);
            if (mesh.vertexCount > 65000) mesh.indexFormat = IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
        }

        public void Finish()
        {
            foreach (var kv in m_Batches) Flush(kv.Key.Item1, kv.Value);
            m_Batches.Clear();
        }
    }
}
