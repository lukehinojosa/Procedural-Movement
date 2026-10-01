using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// Parametric description edited by the Creature Creator. <see cref="Build"/> derives a full
    /// <see cref="CreatureDefinition"/> from a template plus these parameters, including structural changes
    /// such as the number of leg pairs or joints per leg.
    /// </summary>
    [Serializable]
    public class CreatorModel
    {
        public CreaturePreset template = CreaturePreset.Spider;
        public string name = "My Creature";

        [Header("Body")]
        public float bodyLength = 1f;
        public float bodyGirth = 1f;
        public float stanceHeight = 1f;
        public float headSize = 1f;
        public float neckLength = 1f;
        public bool hasHead = true;
        public bool hasTail = true;
        public float tailLength = 1f;
        public float tailThickness = 1f;
        public float tailStiffness = -1f; // -1 = template

        [Header("Limbs")]
        public int legPairs = -1;        // -1 = template
        public int legSegments = -1;     // -1 = template
        public float legLength = 1f;
        public float legThickness = 1f;
        public float legSpread = 1f;
        public float kneeLift = 0f;      // bends knees further up (arachnid) or down
        public bool hasArms = true;
        public float armLength = 1f;

        [Header("Locomotion")]
        public float speed = 1f;
        public float stride = 1f;
        public float stepHeight = 1f;
        public float duty = 0f;          // offset added to every gait's duty factor
        public int gaitOverride = -1;    // -1 = template, else (int)GaitPattern
        public int climb = -1;           // -1 = template, 0 = walker, 1 = climber
        public float bob = 1f;

        [Header("Look")]
        public Color[] palette;
        public SkinPattern pattern = SkinPattern.Organic;
        public float smoothness = -1f;

        public CreatorModel Clone() => JsonUtility.FromJson<CreatorModel>(JsonUtility.ToJson(this));

        public void ResetTo(CreaturePreset preset)
        {
            var fresh = new CreatorModel { template = preset, name = "My " + preset };
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), this);
            var t = CreaturePresets.Get(preset);
            palette = (Color[])t.palette.Clone();
            pattern = t.bodyPattern;
            smoothness = t.smoothness;
        }

        public static int CountLegPairs(CreatureDefinition d)
        {
            int n = 0;
            foreach (var l in d.limbs) if (l.role == LimbRole.Leg) n++;
            return n;
        }

        public static int LegSegments(CreatureDefinition d)
        {
            foreach (var l in d.limbs) if (l.role == LimbRole.Leg) return l.bones.Length;
            return 2;
        }

        public CreatureDefinition Build()
        {
            var d = CreaturePresets.Get(template);
            d.name = string.IsNullOrWhiteSpace(name) ? "Creature" : name;
            var loco = d.locomotion;
            bool vertical = d.body.Count >= 2 && Mathf.Abs((d.body[d.body.Count - 1].position - d.body[0].position).normalized.y) > 0.7f;

            // Proportions along the body axis: Z for horizontal bodies, Y for upright ones.
            Vector3 Axis(Vector3 p) => vertical ? new Vector3(p.x * bodyGirth, p.y * bodyLength, p.z * bodyGirth) : new Vector3(p.x * bodyGirth, p.y * bodyGirth, p.z * bodyLength);

            foreach (var n in d.body) { n.position = Axis(n.position); n.radius *= bodyGirth; }
            d.frontPivot = Axis(d.frontPivot);

            // Head.
            var h = d.head;
            h.enabled &= hasHead;
            h.neckBase = Axis(h.neckBase);
            h.headOffset *= neckLength;
            h.headRadii *= headSize;
            h.snoutLength *= headSize; h.snoutRadius *= headSize; h.snoutTipRadius *= headSize;
            h.neckRadius *= Mathf.Lerp(1f, headSize, 0.5f) * bodyGirth;
            h.neckTipRadius *= Mathf.Lerp(1f, headSize, 0.5f);
            var shapes = new List<ShapeDef>();
            foreach (var s in d.shapes)
            {
                if (s.attach == Section.Head)
                {
                    if (!h.enabled) continue;
                    s.position *= headSize; s.size *= headSize;
                }
                else { s.position = Axis(s.position); s.size *= bodyGirth; }
                shapes.Add(s);
            }
            d.shapes = shapes;

            // Tail.
            var t = d.tail;
            t.enabled &= hasTail;
            t.attach = Axis(t.attach);
            t.length *= tailLength;
            for (int i = 0; i < t.radii.Length; i++) t.radii[i] *= tailThickness * bodyGirth;
            if (tailStiffness >= 0f) t.stiffness = tailStiffness;

            // Limbs.
            var legs = new List<LimbDef>();
            var arms = new List<LimbDef>();
            foreach (var l in d.limbs) (l.role == LimbRole.Leg ? legs : arms).Add(l);
            legs.Sort((a, b) => b.attach.z.CompareTo(a.attach.z));
            if (legPairs > 0 && legs.Count > 0 && legPairs != legs.Count) legs = ResampleLegs(legs, legPairs);
            var result = new List<LimbDef>();
            float reachSum = 0f;
            foreach (var l in legs)
            {
                l.attach = Axis(l.attach);
                Vector3 rel = l.rest - l.attach;
                rel.x *= legLength * legSpread; rel.z *= legLength;
                l.rest = new Vector3(l.attach.x, 0f, l.attach.z) + new Vector3(rel.x, 0f, rel.z);
                if (legSegments > 0 && legSegments != l.bones.Length) Resegment(l, legSegments);
                for (int i = 0; i < l.bones.Length; i++) l.bones[i] *= legLength;
                for (int i = 0; i < l.radii.Length; i++) l.radii[i] *= legThickness;
                l.footLength *= legLength; l.footHeight *= legLength;
                if (Mathf.Abs(kneeLift) > 0.01f && l.hints.Length > 0) l.hints[0] = (l.hints[0] + Vector3.up * kneeLift).normalized;
                reachSum += Sum(l.bones) + l.footHeight;
                result.Add(l);
            }
            foreach (var a in arms)
            {
                if (!hasArms) continue;
                a.attach = Axis(a.attach);
                Vector3 rel = a.rest - a.attach;
                a.rest = a.attach + rel * armLength;
                for (int i = 0; i < a.bones.Length; i++) a.bones[i] *= armLength;
                for (int i = 0; i < a.radii.Length; i++) a.radii[i] *= legThickness;
                result.Add(a);
            }
            d.limbs = result;

            // Body height: scale with the legs, but never beyond what the legs can reach.
            loco.bodyHeight *= legLength * stanceHeight;
            if (result.Count > 0)
            {
                float minAttachY = 0f;
                float maxReach = float.MaxValue;
                foreach (var l in result)
                {
                    if (l.role != LimbRole.Leg) continue;
                    minAttachY = Mathf.Min(minAttachY, l.attach.y);
                    float horiz = new Vector2(l.rest.x - l.attach.x, l.rest.z - l.attach.z).magnitude;
                    float reach = Sum(l.bones) * 0.97f;
                    float vert = Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - horiz * horiz)) + l.footHeight - l.attach.y;
                    maxReach = Mathf.Min(maxReach, vert);
                }
                if (maxReach < float.MaxValue) loco.bodyHeight = Mathf.Clamp(loco.bodyHeight, 0.08f, maxReach);
            }

            // Locomotion.
            float size = legLength;
            loco.minSpeed *= speed; loco.cruiseSpeed *= speed; loco.maxSpeed *= speed;
            loco.collisionRadius *= Mathf.Lerp(1f, bodyGirth, 0.7f);
            loco.bodyHalfLength *= bodyLength;
            loco.maxStepUp *= size;
            loco.settleTolerance *= size;
            if (climb >= 0)
            {
                bool c = climb == 1;
                if (c != loco.canClimb)
                {
                    loco.canClimb = c;
                    loco.surfaceAlign = c ? 1f : 0.3f;
                    loco.maxSlope = c ? 90f : 45f;
                    loco.tiltFromFeet = c ? 0.6f : Mathf.Min(loco.tiltFromFeet, 0.6f);
                }
            }
            foreach (var g in loco.gaits)
            {
                g.minSpeed *= speed;
                g.strideMin *= size * stride; g.strideMax *= size * stride;
                g.froudeStride *= stride; // Froude-scaled strides already follow leg length
                g.stepHeight *= size * stepHeight;
                g.clearance *= stepHeight; // data swings: already a fraction of leg length
                g.curl *= size;
                g.duty = Mathf.Clamp(g.duty + duty, 0.2f, 0.9f);
                g.bob *= bob * size;
                if (gaitOverride >= 0) g.pattern = (GaitPattern)gaitOverride;
            }

            if (palette != null && palette.Length > 0) d.palette = (Color[])palette.Clone();
            d.bodyPattern = pattern;
            if (smoothness >= 0f) d.smoothness = smoothness;
            return d;
        }

        static float Sum(float[] a) { float s = 0f; foreach (var v in a) s += v; return s; }

        /// <summary>Creates <paramref name="pairs"/> legs spread along the span of the template's legs, interpolating their shape.</summary>
        static List<LimbDef> ResampleLegs(List<LimbDef> src, int pairs)
        {
            var result = new List<LimbDef>();
            float zFront = src[0].attach.z, zBack = src[src.Count - 1].attach.z;
            if (src.Count == 1) { zFront += 0.15f * pairs * 0.5f; zBack -= 0.15f * pairs * 0.5f; }
            for (int i = 0; i < pairs; i++)
            {
                float t = pairs == 1 ? 0.5f : i / (float)(pairs - 1);
                float f = t * (src.Count - 1);
                int a = Mathf.Clamp(Mathf.FloorToInt(f), 0, src.Count - 1), b = Mathf.Min(src.Count - 1, a + 1);
                float k = f - a;
                var la = src[a]; var lb = src[b];
                var l = JsonUtility.FromJson<LimbDef>(JsonUtility.ToJson(la));
                l.name = "Leg" + (i + 1);
                float z = pairs == 1 ? (zFront + zBack) * 0.5f : Mathf.Lerp(zFront, zBack, t);
                Vector3 attach = Vector3.Lerp(la.attach, lb.attach, k);
                Vector3 rest = Vector3.Lerp(la.rest, lb.rest, k);
                float dz = z - attach.z;
                l.attach = new Vector3(attach.x, attach.y, z);
                l.rest = new Vector3(rest.x, 0f, rest.z + dz);
                if (la.bones.Length == lb.bones.Length)
                    for (int j = 0; j < l.bones.Length; j++) l.bones[j] = Mathf.Lerp(la.bones[j], lb.bones[j], k);
                if (la.hints.Length == lb.hints.Length)
                    for (int j = 0; j < l.hints.Length; j++) l.hints[j] = Vector3.Slerp(la.hints[j], lb.hints[j], k);
                result.Add(l);
            }
            return result;
        }

        /// <summary>Changes the number of bones in a leg while keeping its total length and bend style.</summary>
        static void Resegment(LimbDef l, int count)
        {
            float total = Sum(l.bones);
            float[] weights = count == 2 ? new[] { 0.5f, 0.5f }
                : count == 3 ? new[] { 0.36f, 0.36f, 0.28f }
                : count == 4 ? new[] { 0.14f, 0.32f, 0.3f, 0.24f }
                : new[] { 0.12f, 0.24f, 0.24f, 0.22f, 0.18f };
            l.bones = new float[count];
            for (int i = 0; i < count; i++) l.bones[i] = total * weights[i];
            var radii = new float[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float f = i / (float)count * (l.radii.Length - 1);
                int a = Mathf.Min(l.radii.Length - 1, Mathf.FloorToInt(f)), b = Mathf.Min(l.radii.Length - 1, a + 1);
                radii[i] = Mathf.Lerp(l.radii[a], l.radii[b], f - a);
            }
            l.radii = radii;
            var hints = new Vector3[count - 1];
            for (int i = 0; i < count - 1; i++)
                hints[i] = l.hints.Length == 0 ? Vector3.up : l.hints[Mathf.Min(i, l.hints.Length - 1)];
            // Alternate bends for extra joints so long legs zig-zag instead of bowing.
            if (count - 1 > l.hints.Length && l.hints.Length > 0)
                for (int i = l.hints.Length; i < count - 1; i++) hints[i] = (i % 2 == 0 ? l.hints[0] : -l.hints[0] + Vector3.up * 0.5f).normalized;
            l.hints = hints;
            if (l.boneColors != null && l.boneColors.Length > 0)
            {
                var bc = new int[count];
                for (int i = 0; i < count; i++) bc[i] = l.boneColors[Mathf.Min(i, l.boneColors.Length - 1)];
                l.boneColors = bc;
            }
        }
    }
}
