using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// A cyclic gait clock. Cadence and stride come from the current speed (optionally Froude-scaled to the leg
    /// length, as measured across mammals and humans), capped by how far the legs can reach; each leg lifts off when
    /// the clock passes its phase offset. Gaits switch by speed (with hysteresis), e.g. walk, trot, gallop.
    /// </summary>
    public class GaitController
    {
        public readonly List<GaitDef> gaits;
        public int current;
        public float phase;
        public float previousPhase;
        public float frequency;
        public float stride;
        public bool settling;

        public GaitDef Gait => gaits[current];
        /// <summary>Duty factor at the current speed (the gait's fixed duty, or its Froude-scaled law).</summary>
        public float Duty => m_Duty > 0f ? m_Duty : Gait.duty;
        float m_Duty;

        /// <summary>Hip height above the ground (m): the leg length that stride and duty scale with.</summary>
        public float hipHeight = 1f;

        /// <summary>Froude number v^2 / (g h): speeds are dynamically similar across leg lengths at equal Froude numbers.</summary>
        public float Froude(float speed) => speed * speed / (9.81f * Mathf.Max(0.05f, hipHeight));

        /// <summary>Duty factor gait <paramref name="g"/> uses at <paramref name="speed"/>.</summary>
        public float DutyAt(GaitDef g, float speed)
        {
            if (g.froudeDuty <= 0f || speed < 0.05f) return g.duty;
            float d = g.froudeDuty * Mathf.Pow(Mathf.Max(0.005f, Froude(speed)), g.froudeDutyExp);
            // Walking gaits keep a double-support phase; running gaits keep a flight phase.
            return g.duty >= 0.5f ? Mathf.Clamp(d, 0.52f, 0.8f) : Mathf.Clamp(d, 0.2f, 0.48f);
        }

        /// <summary>
        /// Stride multiplier for the ground slope (set by the creature each frame). At the same speed people take
        /// longer, slower steps uphill and shorter, quicker steps downhill (Kawamura 1991, Leroux 2002, Lay 2006).
        /// </summary>
        public float strideScale = 1f;

        /// <summary>Ground slope along the direction of travel, as a tangent (+ uphill). Changes how far a foot can reach.</summary>
        public float slopeTan;

        /// <summary>Preferred stride (m) for gait <paramref name="g"/> at <paramref name="speed"/>, before the reach cap.</summary>
        float PreferredStride(GaitDef g, float speed)
        {
            if (g.froudeStride > 0f)
                return g.froudeStride * hipHeight * Mathf.Pow(Mathf.Max(1e-4f, Froude(speed)), g.froudeStrideExp) * strideScale;
            return speed / Mathf.Max(0.1f, g.minFrequency);
        }

        /// <summary>
        /// Horizontal distance d at which a foot on ground rising at <paramref name="t"/> (tangent, + toward the foot)
        /// is exactly <paramref name="r"/> from a hip <paramref name="v"/> above the level contact: d^2 + (v - d t)^2 = r^2.
        /// </summary>
        static float ReachOnSlope(float v, float r, float t)
        {
            float a = 1f + t * t, b = v * t, c = v * v - r * r;
            float disc = b * b - a * c;
            return disc > 0f ? (b + Mathf.Sqrt(disc)) / a : 0f;
        }

        /// <summary>Seconds a foot spends in the air for the current cadence.</summary>
        public float SwingDuration => frequency > 1e-3f ? Mathf.Clamp((1f - Duty) / frequency, 0.07f, 0.55f) : 0.3f;
        /// <summary>Seconds a foot spends on the ground for the current cadence.</summary>
        public float StanceDuration => frequency > 1e-3f ? Mathf.Clamp(Duty / frequency, 0.05f, 2f) : 0f;

        public GaitController(List<GaitDef> gaits)
        {
            this.gaits = gaits != null && gaits.Count > 0 ? gaits : new List<GaitDef> { new GaitDef() };
            this.gaits.Sort((a, b) => a.minSpeed.CompareTo(b.minSpeed));
        }

        /// <summary>
        /// Largest comfortable fore/aft foot travel from the hip (m). Strides are capped so a stance foot never
        /// has to travel further than this either side of its rest spot; cadence rises instead.
        /// </summary>
        public float maxExcursion = 10f;

        /// <summary>
        /// Per-leg geometry for the stride cap: standing hip height above the foot contact, leg reach, girdle swing
        /// (helps both ways), the hip's lateral offset from the foot, how far the foot's rest spot sits ahead of the
        /// hip (feet are planned around it), and the foot's shape.
        /// </summary>
        public readonly List<(float hipH, float reach, float girdle, float lateral, float restAhead, FootType foot, float footLength, float footHeight)> legGeometry =
            new List<(float, float, float, float, float, FootType, float, float)>();
        public float bodyHeight = 1f;

        /// <summary>
        /// Foot angle a leg (by index) has at touch-down and at toe-off for a speed, e.g. a heel strike with the toe up
        /// and a heel rise before toe-off. Unset means flat feet.
        /// </summary>
        public System.Func<int, float, (float touchDown, float toeOff)> footPitch;

        /// <summary>
        /// How far a planted foot can be ahead of its rest spot (touch-down) and behind it (toe-off) in gait
        /// <paramref name="g"/> at <paramref name="speed"/>. Uses the real foot geometry: a heel rise pivots on the
        /// toe and lifts the ankle, which lets the leg reach much further back than a flat foot; girdle swing helps
        /// both ways. The most limited leg decides.
        /// </summary>
        public (float ahead, float behind) ExcursionFor(GaitDef g, float speed)
        {
            if (legGeometry.Count == 0) return (maxExcursion, maxExcursion);
            float ahead = float.MaxValue, behind = float.MaxValue;
            for (int i = 0; i < legGeometry.Count; i++)
            {
                var (hipH, reach, girdle, lateral, restAhead, foot, footLength, footHeight) = legGeometry[i];
                var (down, off) = footPitch != null ? footPitch(i, speed) : (0f, 0f);
                // Hip height at touch-down and toe-off: the body may sink ~7% of the leg there (it dips when a
                // planted leg would over-extend); running bodies also ride high at the ends of stance (the bob).
                float ends = -Mathf.Max(0f, hipH - footHeight) * 0.07f + Mathf.Max(0f, -g.bob);
                float h = Mathf.Max(0.02f, hipH - g.crouch * bodyHeight + ends);
                float r2 = reach * reach - lateral * lateral;
                // Effector (ankle / fetlock) offset from the foot contact: x forward, y up.
                Vector2 a = LimbRig.EffectorOffset(foot, footLength, footHeight, down);
                Vector2 b = LimbRig.EffectorOffset(foot, footLength, footHeight, off);
                // On a slope the ground ahead is higher (uphill) and behind is lower, so a leg reaches further forward
                // and less far back going up, and the opposite going down.
                float r = Mathf.Sqrt(Mathf.Max(0f, r2));
                ahead = Mathf.Min(ahead, ReachOnSlope(h - a.y, r, slopeTan) - a.x + girdle - restAhead);
                behind = Mathf.Min(behind, ReachOnSlope(h - b.y, r, -slopeTan) + b.x + girdle + restAhead);
            }
            return (Mathf.Max(0.05f, ahead), Mathf.Max(0.05f, behind));
        }

        /// <summary>Share of the stance travel the foot should land ahead of the hip, so both ends stay in reach.</summary>
        public float LeadFraction(GaitDef g, float speed)
        {
            var (ahead, behind) = ExcursionFor(g, speed);
            return ahead / (ahead + behind);
        }

        float StrideCap(GaitDef g, float speed)
        {
            var (ahead, behind) = ExcursionFor(g, speed);
            return Mathf.Max(g.strideMin, Mathf.Min(g.strideMax, (ahead + behind) / Mathf.Max(0.1f, DutyAt(g, speed))));
        }

        /// <summary>True for the update in which the gait switched (e.g. walk to run).</summary>
        public bool GaitChanged { get; private set; }

        /// <summary>
        /// Picks the clock phase whose footfall pattern best matches which legs are actually in the air right now,
        /// so a gait switch continues from the current footfalls instead of jumping to an unrelated point.
        /// </summary>
        public void Resync(System.Collections.Generic.IReadOnlyList<(float offset, bool swinging)> legs)
        {
            float duty = Duty, bestPhase = phase;
            int bestMismatch = int.MaxValue;
            float bestDist = float.MaxValue;
            for (int k = 0; k < 100; k++)
            {
                float p = k / 100f;
                int mismatch = 0;
                foreach (var (offset, swinging) in legs)
                {
                    bool expected = Mathf.Repeat(p - offset, 1f) < 1f - duty;
                    if (expected != swinging) mismatch++;
                }
                float dist = Mathf.Abs(Mathf.DeltaAngle(p * 360f, phase * 360f));
                if (mismatch < bestMismatch || (mismatch == bestMismatch && dist < bestDist))
                {
                    bestMismatch = mismatch; bestDist = dist; bestPhase = p;
                }
            }
            SetPhase(bestPhase);
        }

        public void Update(float dt, float speed, bool needSettle, float settleFrequency)
        {
            // Pick the gait by speed, with hysteresis so we do not flicker at a threshold.
            int best = 0;
            for (int i = 0; i < gaits.Count; i++)
            {
                float threshold = gaits[i].minSpeed * (i > current ? 1.05f : 0.9f);
                if (speed >= threshold) best = i;
            }
            GaitChanged = best != current;
            current = best;

            var g = Gait;
            settling = false;
            m_Duty = DutyAt(g, speed);
            if (speed > 0.05f)
            {
                stride = Mathf.Clamp(PreferredStride(g, speed), g.strideMin, StrideCap(g, speed));
                frequency = speed / stride;
                // Never crawl at a glacial cadence: blend toward the settle cadence at very low speeds.
                if (speed < 0.6f && needSettle && frequency < settleFrequency * 0.5f) frequency = settleFrequency * 0.5f;
            }
            else if (needSettle)
            {
                frequency = settleFrequency;
                settling = true;
            }
            else
            {
                frequency = 0f;
            }

            previousPhase = phase;
            phase = Mathf.Repeat(phase + frequency * dt, 1f);
        }

        /// <summary>Swing time the current gait would use at <paramref name="speed"/> (for steps planned ahead of the speed).</summary>
        public float SwingDurationAt(float speed)
        {
            var g = Gait;
            if (speed <= 0.05f) return 0.3f;
            float s = Mathf.Clamp(PreferredStride(g, speed), g.strideMin, StrideCap(g, speed));
            return Mathf.Clamp((1f - DutyAt(g, speed)) / (speed / s), 0.07f, 0.55f);
        }

        /// <summary>Jumps the clock (e.g. after a leap) without triggering any lift-offs.</summary>
        public void SetPhase(float value)
        {
            phase = previousPhase = Mathf.Repeat(value, 1f);
        }

        /// <summary>True if the clock passed <paramref name="offset"/> during the last update.</summary>
        public bool Crossed(float offset)
        {
            if (frequency <= 0f) return false;
            float a = previousPhase, b = phase, o = Mathf.Repeat(offset, 1f);
            if (a <= b) return o > a && o <= b;
            return o > a || o <= b;
        }

        /// <summary>
        /// Phase at which a leg lifts off for a footfall pattern.
        /// <paramref name="pair"/> counts from the front (0), <paramref name="side"/> is +1 right, -1 left.
        /// </summary>
        public static float Offset(GaitPattern pattern, int pair, int pairCount, int side)
        {
            bool right = side > 0;
            bool front = pairCount <= 1 || pair < pairCount * 0.5f;
            switch (pattern)
            {
                case GaitPattern.Biped:
                    return right ? 0.5f : 0f;
                case GaitPattern.Wave:
                {
                    // Metachronal wave: hind legs lead, the wave runs forward along the body.
                    float w = (pairCount - 1 - pair) / (float)Mathf.Max(1, pairCount);
                    return Mathf.Repeat(w + (right ? 0.5f : 0f), 1f);
                }
                case GaitPattern.QuadWalk:
                    // Lateral sequence: LH, LF, RH, RF.
                    if (!right) return front ? 0.25f : 0f;
                    return front ? 0.75f : 0.5f;
                case GaitPattern.QuadTrot:
                    // Diagonal pairs: LF + RH, then RF + LH.
                    return (front ^ right) ? 0f : 0.5f;
                case GaitPattern.QuadGallop:
                    // Transverse gallop, right lead (Robilliard et al. 2007): trailing hind (LH), leading hind (RH),
                    // trailing fore (LF), leading fore (RF). Footfalls come in couplets: a short interval within
                    // the hind pair and the fore pair, a longer one between them, then a single suspension.
                    if (!front) return right ? 0.12f : 0f;
                    return right ? 0.48f : 0.35f;
                case GaitPattern.QuadBound:
                    return front ? (right ? 0.55f : 0.5f) : (right ? 0.05f : 0f);
                default:
                    // Alternating tripod / tetrapod: neighbours in antiphase along each side and across.
                    return ((pair + (right ? 1 : 0)) % 2) * 0.5f;
            }
        }
    }
}
