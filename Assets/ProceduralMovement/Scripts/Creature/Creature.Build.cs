using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Construction half of <see cref="Creature"/>: turns a <see cref="CreatureDefinition"/> into a rig.</summary>
    public partial class Creature
    {
        public const int CreatureLayer = 2; // Built-in "Ignore Raycast": creatures never hit their own probes.

        readonly List<Object> m_Owned = new List<Object>();
        Material[] m_PaletteMats;
        Material m_BodyMat, m_TailMat;

        public static Creature Spawn(CreatureDefinition def, Vector3 position, Vector3 forward, Transform parent = null)
        {
            var go = new GameObject(def.name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.layer = CreatureLayer;
            var c = go.AddComponent<Creature>();
            c.Build(def);
            c.Teleport(position, forward);
            return c;
        }

        T Own<T>(T obj) where T : Object { m_Owned.Add(obj); return obj; }

        GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = CreatureLayer;
            go.transform.SetParent(parent, false);
            return go;
        }

        GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = Child(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        Texture2D PatternTexture(SkinPattern p)
        {
            switch (p)
            {
                case SkinPattern.Bands: return MaterialLibrary.Bands();
                case SkinPattern.Organic: return MaterialLibrary.Skin();
                default: return null;
            }
        }

        Material PaletteMat(int index) => m_PaletteMats[Mathf.Clamp(index, 0, m_PaletteMats.Length - 1)];

        public void Build(CreatureDefinition definition)
        {
            Teardown();
            def = definition;
            var loco = def.locomotion;

            // Materials.
            int pc = Mathf.Max(1, def.palette.Length);
            m_PaletteMats = new Material[pc];
            for (int i = 0; i < pc; i++)
                m_PaletteMats[i] = Own(MaterialLibrary.Create(def.GetColor(i), def.smoothness, 0f, def.bodyPattern == SkinPattern.Organic ? MaterialLibrary.Skin() : null));
            m_BodyMat = Own(MaterialLibrary.Create(def.GetColor(def.bodyColor), def.smoothness, 0f, PatternTexture(def.bodyPattern)));
            m_TailMat = Own(MaterialLibrary.Create(def.GetColor(def.tail.color), def.smoothness, 0f, PatternTexture(def.tail.pattern)));

            // Transforms.
            m_Body = Child("Body", transform).transform;
            bool hasFront = def.frontSectionStart > 0 && def.frontSectionStart < def.body.Count;
            m_Front = hasFront ? Child("Front", m_Body).transform : m_Body;
            if (hasFront) m_Front.localPosition = def.frontPivot;
            m_HeadT = Child("Head", transform).transform;

            // Body tube.
            if (def.body.Count >= 2)
            {
                var tubeGo = Child("BodySkin", transform);
                tubeGo.AddComponent<MeshFilter>();
                tubeGo.AddComponent<MeshRenderer>();
                m_BodyTube = tubeGo.AddComponent<TubeRenderer>();
                m_BodyTube.Init(m_BodyMat, 20, 6);
                m_BodyPoints = new Vector3[def.body.Count];
                m_BodyRadii = new float[def.body.Count];
                m_BodyAspects = new Vector2[def.body.Count];
                m_BodyLocal = new Vector3[def.body.Count];
                m_BodyOnFront = new bool[def.body.Count];
                for (int i = 0; i < def.body.Count; i++)
                {
                    bool front = hasFront && i >= def.frontSectionStart;
                    m_BodyOnFront[i] = front;
                    m_BodyLocal[i] = front ? def.body[i].position - def.frontPivot : def.body[i].position;
                    m_BodyRadii[i] = def.body[i].radius;
                    m_BodyAspects[i] = def.body[i].aspect;
                }
                Vector3 axis = def.body[def.body.Count - 1].position - def.body[0].position;
                m_BodyVertical = Mathf.Abs(axis.normalized.y) > 0.7f;
            }
            else if (def.body.Count == 1)
            {
                var n = def.body[0];
                var mesh = Own(ProcMesh.Ellipsoid(new Vector3(n.radius * n.aspect.x, n.radius * n.aspect.y, n.radius)));
                MeshObject("BodyBlob", m_Body, mesh, m_BodyMat).transform.localPosition = n.position;
            }

            // Head and neck.
            if (def.head.enabled)
            {
                var h = def.head;
                var headMesh = Own(ProcMesh.Ellipsoid(h.headRadii));
                MeshObject("Skull", m_HeadT, headMesh, PaletteMat(h.color));
                if (h.snoutLength > 0.01f)
                {
                    var snout = Own(ProcMesh.TaperedCapsule(h.snoutLength, h.snoutRadius, h.snoutTipRadius, 16, 5));
                    MeshObject("Snout", m_HeadT, snout, PaletteMat(h.color));
                }
                var neckGo = Child("Neck", transform);
                neckGo.AddComponent<MeshFilter>(); neckGo.AddComponent<MeshRenderer>();
                m_NeckTube = neckGo.AddComponent<TubeRenderer>();
                m_NeckTube.capStart = false;
                m_NeckTube.Init(PaletteMat(h.color), 16, 5);
                if (h.mane)
                {
                    var maneGo = Child("Mane", transform);
                    maneGo.AddComponent<MeshFilter>(); maneGo.AddComponent<MeshRenderer>();
                    m_ManeTube = maneGo.AddComponent<TubeRenderer>();
                    m_ManeTube.Init(PaletteMat(h.maneColor), 10, 4);
                }
                m_HeadRot = Quaternion.identity;
            }

            // Tail.
            if (def.tail.enabled && def.tail.segments > 0)
            {
                var t = def.tail;
                var sec = SectionTransform(t.section);
                m_Tail = new TailRig(t)
                {
                    section = sec,
                    attachLocal = ToSectionLocal(t.section, t.attach),
                    dirLocal = t.direction,
                };
                var tailGo = Child("Tail", transform);
                tailGo.AddComponent<MeshFilter>(); tailGo.AddComponent<MeshRenderer>();
                m_Tail.tube = tailGo.AddComponent<TubeRenderer>();
                m_Tail.tube.Init(m_TailMat, 16, 6);
            }

            // Limbs.
            var legDefs = new List<(LimbDef def, int side)>();
            foreach (var l in def.limbs)
            {
                if (l.bones == null || l.bones.Length == 0) continue;
                legDefs.Add((l, 1));
                if (l.mirror) legDefs.Add((l, -1));
            }
            foreach (var (l, side) in legDefs)
            {
                var rig = new LimbRig(l, side);
                Vector3 M(Vector3 v) => new Vector3(v.x * side, v.y, v.z);
                rig.section = SectionTransform(l.section);
                rig.attachLocal = ToSectionLocal(l.section, M(l.attach));
                rig.restRootLocal = M(l.rest);
                rig.hintsLocal = new Vector3[l.hints != null ? l.hints.Length : 0];
                for (int i = 0; i < rig.hintsLocal.Length; i++) rig.hintsLocal[i] = M(l.hints[i]).normalized;
                // Rest end point in root space: legs stand on the ground below the body, arms hang at their rest.
                Vector3 restEnd = l.role == LimbRole.Leg
                    ? M(new Vector3(l.rest.x, -loco.bodyHeight + l.footHeight, l.rest.z))
                    : M(l.rest);
                rig.InitHinges(ToSectionLocal(l.section, restEnd));
                rig.bones = new Transform[l.bones.Length];
                for (int b = 0; b < l.bones.Length; b++)
                {
                    float r0 = SafeRadius(l.radii, b, 0.04f), r1 = SafeRadius(l.radii, b + 1, r0 * 0.8f);
                    var mesh = Own(ProcMesh.TaperedCapsule(l.bones[b], r0, r1, 12, 4));
                    int colorIndex = l.boneColors != null && b < l.boneColors.Length ? l.boneColors[b] : l.color;
                    rig.bones[b] = MeshObject($"{l.name}{(side > 0 ? "R" : "L")}_{b}", transform, mesh, PaletteMat(colorIndex)).transform;
                }
                float endR = SafeRadius(l.radii, l.bones.Length, 0.03f);
                if (l.role == LimbRole.Leg && l.foot == FootType.Plantigrade)
                {
                    var mesh = Own(ProcMesh.Ellipsoid(new Vector3(l.footLength * 0.22f, l.footHeight * 0.55f, l.footLength * 0.55f)));
                    rig.footVisual = MeshObject($"{l.name}Foot", transform, mesh, PaletteMat(l.footColor)).transform;
                }
                else if (l.role == LimbRole.Leg && l.foot == FootType.Hoof)
                {
                    float len = l.footHeight * 1.08f;
                    var pastern = Own(ProcMesh.TaperedCapsule(len, endR, endR * 0.9f, 12, 4));
                    rig.footVisual = MeshObject($"{l.name}Pastern", transform, pastern, PaletteMat(l.boneColors != null && l.boneColors.Length > 0 ? l.boneColors[l.boneColors.Length - 1] : l.color)).transform;
                    var hoof = Own(ProcMesh.TaperedCapsule(l.footHeight * 0.3f, endR * 1.05f, endR * 1.35f, 12, 3));
                    rig.hoofVisual = MeshObject($"{l.name}Hoof", transform, hoof, PaletteMat(l.footColor)).transform;
                }
                else if (l.role == LimbRole.Arm && l.footLength > 0.01f)
                {
                    var mesh = Own(ProcMesh.Ellipsoid(new Vector3(l.footLength * 0.38f, l.footLength * 0.22f, l.footLength * 0.55f)));
                    rig.footVisual = MeshObject($"{l.name}Hand", transform, mesh, PaletteMat(l.footColor)).transform;
                }

                if (l.role == LimbRole.Leg) m_Legs.Add(rig);
                else m_Arms.Add(rig);
            }

            // Order legs into front-to-back pairs for the gait patterns.
            var zs = new List<float>();
            foreach (var leg in m_Legs)
            {
                float z = leg.restRootLocal.z;
                bool found = false;
                foreach (var e in zs) if (Mathf.Abs(e - z) < 0.05f) { found = true; break; }
                if (!found) zs.Add(z);
            }
            zs.Sort((a, b) => b.CompareTo(a));
            m_PairCount = Mathf.Max(1, zs.Count);
            foreach (var leg in m_Legs)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < zs.Count; i++) { float d = Mathf.Abs(zs[i] - leg.restRootLocal.z); if (d < bd) { bd = d; best = i; } }
                leg.pairIndex = best;
            }
            m_LegCentroidZ = 0f; m_LegCentroidX = 0f;
            foreach (var leg in m_Legs) { m_LegCentroidZ += leg.restRootLocal.z; m_LegCentroidX += leg.restRootLocal.x; }
            if (m_Legs.Count > 0) { m_LegCentroidZ /= m_Legs.Count; m_LegCentroidX /= m_Legs.Count; }

            // Decorative shapes.
            foreach (var s in def.shapes)
            {
                for (int side = 1; side >= (s.mirror ? -1 : 1); side -= 2)
                {
                    Mesh mesh = s.kind == ShapeKind.Capsule
                        ? Own(ProcMesh.TaperedCapsule(s.size.z, s.size.x, s.size.y, 12, 4))
                        : Own(ProcMesh.Ellipsoid(s.size, 16, 12));
                    Material mat = PaletteMat(s.color);
                    if (s.smoothness >= 0f)
                        mat = Own(MaterialLibrary.Create(def.GetColor(s.color), s.smoothness));
                    Transform parent = s.attach == Section.Head ? m_HeadT : SectionTransform(s.attach);
                    var go = MeshObject("Shape", parent, mesh, mat);
                    Vector3 pos = new Vector3(s.position.x * side, s.position.y, s.position.z);
                    go.transform.localPosition = s.attach == Section.Head ? pos : ToSectionLocal(s.attach, pos);
                    Vector3 e = s.euler;
                    go.transform.localRotation = Quaternion.Euler(e.x, e.y * side, e.z * side);
                }
            }

            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            m_Gait = new GaitController(loco.gaits) { maxExcursion = ComputeMaxExcursion(), bodyHeight = loco.bodyHeight };
            float hipSum = 0f;
            foreach (var leg in m_Legs)
            {
                float hip = Mathf.Max(0.02f, loco.bodyHeight + leg.def.attach.y);
                hipSum += hip;
                m_Gait.legGeometry.Add((hip, leg.reach * 0.99f, leg.def.girdleSwing, leg.def.rest.x - leg.def.attach.x, leg.def.rest.z - leg.def.attach.z,
                    leg.def.foot, leg.def.footLength, leg.def.footHeight));
            }
            if (m_Legs.Count > 0) m_Gait.hipHeight = hipSum / m_Legs.Count;
            m_Gait.footPitch = (i, speed) =>
            {
                var leg = m_Legs[i];
                if (leg.def.foot == FootType.Plantigrade) return (FootProfile(speed).strike, MaxToePitch(leg, speed));
                return (0f, MaxToePitch(leg, speed));
            };
            m_Motor = new SurfaceMotor
            {
                radius = loco.collisionRadius,
                halfLength = loco.bodyHalfLength,
                stepHeight = loco.maxStepUp,
                maxSlope = loco.maxSlope,
                acceleration = loco.acceleration,
                deceleration = loco.acceleration * 1.4f,
                turnSpeed = loco.turnSpeed,
                turnSpeedAtMax = loco.turnSpeedAtMax,
                maxSpeed = loco.maxSpeed,
                canClimb = loco.canClimb,
                surfaceAlign = loco.surfaceAlign,
                mask = ~(1 << CreatureLayer),
            };
            cruiseSpeed = loco.cruiseSpeed;
            m_Built = true;
        }

        /// <summary>
        /// How far a planted foot can travel fore/aft of its rest spot before the leg is nearly straight,
        /// allowing the body to dip a little (the most limited leg decides).
        /// </summary>
        float ComputeMaxExcursion()
        {
            float best = float.MaxValue;
            foreach (var leg in m_Legs)
            {
                float hipHeight = def.locomotion.bodyHeight + leg.def.attach.y;
                float v = Mathf.Max(0.02f, hipHeight - leg.def.footHeight) * 0.93f;
                Vector2 lateral = new Vector2(leg.def.rest.x - leg.def.attach.x, 0f);
                float r = leg.reach * 0.98f;
                float h = Mathf.Sqrt(Mathf.Max(0f, r * r - v * v - lateral.sqrMagnitude));
                // Rolling onto the toe at push-off (heel-toe feet) or hoof breakover lengthens the reachable
                // stride behind the hip: add about half the foot length.
                if (leg.def.foot != FootType.Point) h += leg.def.footLength * 0.5f;
                h += leg.def.girdleSwing;
                best = Mathf.Min(best, h);
            }
            return best == float.MaxValue ? 10f : Mathf.Max(0.05f, best);
        }

        static float SafeRadius(float[] radii, int i, float fallback)
        {
            if (radii == null || radii.Length == 0) return fallback;
            return radii[Mathf.Clamp(i, 0, radii.Length - 1)];
        }

        Transform SectionTransform(Section s)
        {
            switch (s)
            {
                case Section.Front: return m_Front;
                case Section.Head: return m_HeadT;
                default: return m_Body;
            }
        }

        /// <summary>Converts a root-local rest position into the local space of a section.</summary>
        Vector3 ToSectionLocal(Section s, Vector3 rootLocal)
        {
            if (s == Section.Front && m_Front != m_Body) return rootLocal - def.frontPivot;
            return rootLocal;
        }

        void Teardown()
        {
            m_Built = false;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            foreach (var o in m_Owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
            }
            m_Owned.Clear();
            m_Legs.Clear();
            m_Arms.Clear();
            m_Tail = null;
            m_BodyTube = null; m_NeckTube = null; m_ManeTube = null;
        }

        void OnDestroy() => Teardown();
    }
}
