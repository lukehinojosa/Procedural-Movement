using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// One limb instance (a leg or an arm on one side). Owns its IK chain, its bone visuals and, for legs,
    /// the foot planting state machine: planted (stance) or swinging toward a predicted foothold.
    /// </summary>
    public class LimbRig
    {
        public LimbDef def;
        public int side = 1;                // +1 right, -1 left
        public int pairIndex;               // legs only: 0 = front-most pair
        public Transform section;
        public Vector3 attachLocal;         // in section space
        public Vector3 restRootLocal;       // root-local rest (mirrored)
        public Vector3[] hintsLocal;        // in section space (mirrored)
        public Vector3[] hingeLocal;        // hinge axis per interior joint, section space (from the rest pose)
        public Vector3 restDirLocal;        // hip-to-rest-foot direction, section space
        public float reach;

        public FabrikChain chain;
        public Transform[] bones;
        public Transform footVisual, hoofVisual;
        readonly Vector3[] m_HintsWorld;

        // Foot state (legs).
        public Vector3 foot;
        public Vector3 footNormal = Vector3.up;
        public Vector3 footForward = Vector3.forward;
        public bool swinging;
        public bool grounded = true;        // false = foot found no surface
        public float swingT;
        public float swingDuration = 0.3f;
        public Vector3 swingFrom, swingFromNormal;
        public Vector3 target, targetNormal = Vector3.up;
        public float stanceTime;
        public float footPitch;
        public float liftHeight;
        public float curl;
        public float flexRatio = 1f;    // swing: shortest hip-to-foot distance as a fraction of full leg length
        public Vector3 home;
        public bool needsPlant;
        public bool liftPending;
        public float plannedPitch;      // stance: the foot angle the gait wants (heel rise at push-off), before any reach roll
        public float landPitch;         // foot angle at touch-down, eased toward flat through early stance
        public int stepCount;           // diagnostics: steps taken since spawn
        public int toeOffLifts, urgentLifts; // diagnostics: steps not triggered by the gait clock
        public string lastUrgentReason = "";        // gait wanted this foot up but it had to wait for support
        public Vector3 airStartLocal;   // foot position relative to the root at take-off
        public bool leapLead;           // running leap: this foot reaches forward and lands first

        /// <summary>
        /// Limb root in world space. With <see cref="LimbDef.girdleSwing"/> the root slides fore/aft after the foot,
        /// like a horse's scapula rotating on the chest wall, which is where much of a foreleg's stride comes from.
        /// </summary>
        public Vector3 Hip
        {
            get
            {
                Vector3 h = section.TransformPoint(attachLocal);
                if (def.girdleSwing <= 0f) return h;
                Vector3 fwd = section.forward;
                float along = Vector3.Dot(foot - h, fwd);
                return h + fwd * Mathf.Clamp(along * 0.35f, -def.girdleSwing, def.girdleSwing);
            }
        }
        public Vector3 End => chain.joints[chain.joints.Length - 1];

        public LimbRig(LimbDef def, int side)
        {
            this.def = def;
            this.side = side;
            chain = new FabrikChain(def.bones);
            m_HintsWorld = new Vector3[Mathf.Max(0, def.bones.Length - 1)];
            reach = chain.totalLength;
        }

        /// <summary>
        /// Converts a knee flexion angle into how short the hip-to-foot chord becomes, using an equal-segment
        /// approximation that also works for 3 and 4 bone legs: chord / length = cos(flexion / 2).
        /// </summary>
        public static float FlexToRatio(float kneeFlexDeg) => kneeFlexDeg <= 0f ? 1f : Mathf.Cos(Mathf.Clamp(kneeFlexDeg, 0f, 150f) * 0.5f * Mathf.Deg2Rad);

        // Data swings (SwingStyle.Walk / Run).
        public SwingStyle swingStyle;
        public float swingKnee;         // peak swing knee flexion (deg) the angle curves are scaled to
        public float swingHipBias;      // extra hip flexion (deg) through mid and late swing, e.g. stepping up a slope
        public float swingGround;       // this frame: ground height under the swinging foot (along up)
        public float swingClearance;    // this frame: minimum clearance of the lowest point of the foot (m)
        float m_ClearanceMax;
        Vector3 m_LiftChord;            // hip-to-effector (ankle / fetlock) vector at lift-off
        float m_F0, m_U0, m_R0;
        bool m_ChordInit;

        /// <summary>Limb root without the girdle slide (the frame the swing angle curves are measured in).</summary>
        public Vector3 HipBase => section.TransformPoint(attachLocal);

        // Sagittal joint angles through swing, toe-off to contact in 9 even samples. Thigh: from vertical, + forward;
        // knee: flexion. Walking (normative adult gait, toe-off ~60% of the cycle): the hip flexes roughly
        // sinusoidally to its maximum in terminal swing; the knee leaves the ground ~38 deg flexed, peaks ~60 deg
        // about a third into swing and is straight again just before heel contact. Running (~3 to 4 m/s, toe-off
        // ~39%, Novacheck 1998): the knee folds to ~90 deg near mid-swing (105 to 130 deg sprinting); the hip
        // reaches maximum flexion late in swing, then extends before contact (swing-leg retraction), landing with
        // the knee ~20 deg flexed.
        static readonly float[] s_WalkThigh = { -8f, 0f, 8f, 15f, 20f, 24f, 25f, 24f, 22f };
        static readonly float[] s_WalkKnee = { 38f, 52f, 60f, 60f, 48f, 30f, 14f, 4f, 3f };
        static readonly float[] s_RunThigh = { -18f, -8f, 5f, 18f, 28f, 35f, 38f, 35f, 28f };
        static readonly float[] s_RunKnee = { 25f, 55f, 80f, 95f, 95f, 80f, 55f, 30f, 20f };

        /// <summary>Smooth (Catmull-Rom) sample of evenly spaced keys at <paramref name="t"/> in 0..1.</summary>
        public static float Sample(float[] k, float t)
        {
            float x = Mathf.Clamp01(t) * (k.Length - 1);
            int i = Mathf.Min((int)x, k.Length - 2);
            float f = x - i;
            float p0 = k[Mathf.Max(0, i - 1)], p1 = k[i], p2 = k[i + 1], p3 = k[Mathf.Min(k.Length - 1, i + 2)];
            return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f * f + (3f * p1 - p0 - 3f * p2 + p3) * f * f * f);
        }

        public void BeginSwing(Vector3 initialTarget, Vector3 normal, float duration, float height, float curlAmount = 0f, float flex = 1f,
            SwingStyle style = SwingStyle.Arc, float kneePeak = 0f, float clearance = 0f, float hipBias = 0f)
        {
            swingStyle = style;
            swingHipBias = hipBias;
            swingKnee = kneePeak;
            m_ClearanceMax = clearance;
            m_LiftChord = End - HipBase;
            m_ChordInit = false;
            swingClearance = 0f;
            swingGround = Vector3.Dot(foot, footNormal);
            curl = curlAmount;
            flexRatio = flex;
            stepCount++;
            swinging = true;
            swingT = 0f;
            swingDuration = Mathf.Max(0.05f, duration);
            swingFrom = foot;
            swingFromNormal = footNormal;
            target = initialTarget;
            targetNormal = normal;
            liftHeight = height;
        }

        /// <summary>Advances the swing. Returns true on touchdown.</summary>
        public bool UpdateSwing(float dt, Vector3 up, Vector3 forward, Vector3 bodyVelocity)
        {
            swingT = Mathf.Min(1f, swingT + dt / swingDuration);
            float t = swingT;
            if (swingStyle != SwingStyle.Arc)
            {
                DataSwing(up, forward, bodyVelocity);
                return FinishSwingIfDone();
            }
            // Ease in and out horizontally, lift on a sine arc and clear any rise between start and end.
            float s = t * t * (3f - 2f * t);
            Vector3 p = Vector3.Lerp(swingFrom, target, s);
            float rise = Mathf.Max(0f, Vector3.Dot(target - swingFrom, up));
            float arc = Mathf.Sin(t * Mathf.PI);
            float lift = liftHeight * arc + rise * Mathf.Sin(Mathf.Min(1f, t * 1.6f) * Mathf.PI * 0.5f) * (1f - s) * 0.8f;
            // Curl: early in the swing the foot kicks up and back behind the body, then reaches forward.
            float kick = curl * Mathf.Sin(t * Mathf.PI) * (1f - t) * 2f;
            lift += kick * 0.7f;
            p -= Vector3.ProjectOnPlane(forward, up).normalized * kick;
            foot = p + up * lift;
            ApplyKneeFlex(t, up);
            footNormal = Vector3.Slerp(swingFromNormal, targetNormal, s).normalized;
            return FinishSwingIfDone();
        }

        bool FinishSwingIfDone()
        {
            if (swingT < 1f) return false;
            swinging = false;
            foot = target;
            footNormal = targetNormal;
            stanceTime = 0f;
            swingClearance = 0f;
            return true;
        }

        /// <summary>
        /// Swing driven by measured joint angles. In the hip's sagittal frame, the hip-to-foot chord of a two-segment
        /// leg points at (thigh angle - knee flexion / 2) from vertical and is cos(knee flexion / 2) of full length.
        /// The data curves give how that angle and length evolve from toe-off to contact; they are mapped onto this
        /// step's actual lift-off chord and landing chord, so the timing (a slow start while the knee folds, a fast
        /// forward swing, a slight retraction before contact) and the foot height (heel rise from knee flexion,
        /// small clearance at mid-swing) follow the data while the foot still lands exactly on its target.
        /// </summary>
        void DataSwing(Vector3 up, Vector3 forward, Vector3 bodyVelocity)
        {
            float t = swingT;
            Vector3 fwd = Vector3.ProjectOnPlane(forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 hip = HipBase;
            if (!m_ChordInit)
            {
                m_F0 = Vector3.Dot(m_LiftChord, fwd); m_U0 = Vector3.Dot(m_LiftChord, up); m_R0 = Vector3.Dot(m_LiftChord, right);
                m_ChordInit = true;
            }
            // The joint angles place the ankle (the chain end), not the sole: flat-foot effector offset from the sole.
            Vector2 eff = EffectorOffset(def.foot, def.footLength, def.footHeight, 0f);
            Vector3 effOffset = fwd * eff.x + up * eff.y;
            // Landing chord: from where the hip will be at touch-down to the effector over the target.
            float remaining = (1f - t) * swingDuration;
            Vector3 a1 = target + effOffset - (hip + bodyVelocity * remaining);
            float f1 = Vector3.Dot(a1, fwd), u1 = Vector3.Dot(a1, up), r1 = Vector3.Dot(a1, right);
            float phi0 = Mathf.Atan2(m_F0, -m_U0), l0 = Mathf.Sqrt(m_F0 * m_F0 + m_U0 * m_U0);
            float phi1 = Mathf.Atan2(f1, -u1), l1 = Mathf.Sqrt(f1 * f1 + u1 * u1);

            bool run = swingStyle == SwingStyle.Run;
            float[] thigh = run ? s_RunThigh : s_WalkThigh, knee = run ? s_RunKnee : s_WalkKnee;
            float kScale = swingKnee > 0f ? swingKnee / (run ? 95f : 60f) : 1f;
            float K(float x) => Sample(knee, x) * kScale;
            float Chord(float x) => Sample(thigh, x) + swingHipBias * Mathf.SmoothStep(0f, 1f, x / 0.6f) - K(x) * 0.5f;
            float c0 = Chord(0f), c1 = Chord(1f);
            float progress = Mathf.Abs(c1 - c0) > 1e-3f ? (Chord(t) - c0) / (c1 - c0) : t;
            // Follow the measured (absolute) chord angle and length through mid-swing; the differences between this
            // step's actual lift-off / landing chords and the data's are blended out near each end, so the foot
            // leaves and lands exactly where it must.
            // Short steps (shuffles, turning on the spot) keep the data's timing but scale its angles to the step.
            float w0 = (1f - t) * (1f - t) * (1f - t), w1 = t * t * t;
            float LenAt(float x) => reach * Mathf.Cos(K(x) * 0.5f * Mathf.Deg2Rad);
            float phiAbs = Chord(t) * Mathf.Deg2Rad + (phi0 - c0 * Mathf.Deg2Rad) * w0 + (phi1 - c1 * Mathf.Deg2Rad) * w1;
            float lenAbs = LenAt(t) + (l0 - LenAt(0f)) * w0 + (l1 - LenAt(1f)) * w1;
            float phiRel = Mathf.LerpUnclamped(phi0, phi1, progress);
            float lenRel = Mathf.Lerp(l0, l1, t) * LenAt(t) / Mathf.Max(1e-3f, Mathf.Lerp(LenAt(0f), LenAt(1f), t));
            float fullStep = Mathf.Abs(c1 - c0) > 1e-3f ? Mathf.Clamp01((phi1 - phi0) * Mathf.Rad2Deg / (c1 - c0)) : 0f;
            float phi = Mathf.Lerp(phiRel, phiAbs, fullStep);
            float len = Mathf.Lerp(lenRel, lenAbs, fullStep);
            float along = Mathf.Clamp01(progress);
            Vector3 p = hip + fwd * (Mathf.Sin(phi) * len) - up * (Mathf.Cos(phi) * len) + right * Mathf.Lerp(m_R0, r1, along) - effOffset;

            // Keep the foot above the ground it passes over (stepping up clears the new level by 60% of the swing),
            // by at least the minimum toe clearance measured at mid-swing.
            float h0 = Vector3.Dot(swingFrom, up), h1 = Vector3.Dot(target, up);
            swingGround = h1 > h0 ? Mathf.Lerp(h0, h1, Mathf.SmoothStep(0f, 1f, t / 0.6f)) : Mathf.Lerp(h0, h1, along);
            swingClearance = m_ClearanceMax * Mathf.SmoothStep(0f, 1f, t / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f));
            float h = Vector3.Dot(p, up);
            if (h < swingGround + swingClearance) p += up * (swingGround + swingClearance - h);
            foot = p;
            footNormal = Vector3.Slerp(swingFromNormal, targetNormal, along).normalized;
        }

        /// <summary>
        /// Swing shaping from joint-angle data: the hip-to-foot distance shrinks to the gait's peak knee flexion
        /// about a third of the way into the swing (when the real knee peaks), then the leg extends to land.
        /// The foot is raised (not pulled sideways) to satisfy it, so early in the swing the heel rises behind
        /// the body and at mid swing the knee comes up under it.
        /// </summary>
        void ApplyKneeFlex(float t, Vector3 up)
        {
            if (flexRatio >= 0.999f) return;
            // Peak flexion ~35% into the swing; the knee is fully extending again by ~80% for the reach to contact.
            const float peak = 0.35f, extended = 0.8f;
            float bump = t < peak ? Mathf.Sin(t / peak * Mathf.PI * 0.5f)
                : t < extended ? Mathf.Cos((t - peak) / (extended - peak) * Mathf.PI * 0.5f) : 0f;
            bump = Mathf.SmoothStep(0f, 1f, bump);
            float full = reach + def.footHeight;
            float L = full * (1f - (1f - flexRatio) * bump);
            Vector3 hip = Hip;
            Vector3 d = foot - hip;
            if (d.magnitude <= L) return;
            float a = Vector3.Dot(d, up);                       // negative: foot below the hip
            Vector3 horiz = d - up * a;
            float h = horiz.magnitude;
            if (h < L * 0.98f)
            {
                // Raise the foot straight up until it sits at distance L from the hip.
                float below = Mathf.Sqrt(L * L - h * h);
                foot = hip + horiz - up * below;
            }
            else
            {
                // Too far fore or aft to reach by lifting alone: draw it in along the same bearing.
                foot = hip + horiz.normalized * (L * 0.98f) - up * (L * 0.2f);
            }
        }

        /// <summary>
        /// Computes each hinge axis from the rest pose: the joint bends in the plane that contains the rest
        /// leg direction and its hint, so the axis is their cross product. Called once after building.
        /// </summary>
        public void InitHinges(Vector3 restFootSectionLocal)
        {
            restDirLocal = (restFootSectionLocal - attachLocal).normalized;
            hingeLocal = new Vector3[hintsLocal.Length];
            for (int i = 0; i < hintsLocal.Length; i++)
            {
                Vector3 axis = Vector3.Cross(restDirLocal, hintsLocal[i]);
                hingeLocal[i] = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// Bend direction for joint <paramref name="i"/> given the current hip-to-effector direction: perpendicular to
        /// both the hinge axis and that line, so the joint never drifts out of the leg's plane.
        /// Legs whose rest pose sticks out sideways (arthropods) turn their hinge with the foot about the body's
        /// vertical axis, like a spider's coxa; upright legs (humans, horses) keep a hinge fixed to the body.
        /// </summary>
        Vector3 BendDirection(int i, Vector3 dir)
        {
            Vector3 fallback = section.TransformDirection(hintsLocal[i]);
            if (hingeLocal == null || i >= hingeLocal.Length || hingeLocal[i] == Vector3.zero) return fallback;
            Vector3 n = section.TransformDirection(hingeLocal[i]);
            Vector3 up = section.up;
            Vector3 restH = Vector3.ProjectOnPlane(section.TransformDirection(restDirLocal), up);
            Vector3 curH = Vector3.ProjectOnPlane(dir, up);
            float yawFollow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, restH.magnitude));
            if (yawFollow > 0f && curH.sqrMagnitude > 1e-4f && restH.sqrMagnitude > 1e-4f)
                n = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(restH, curH), yawFollow) * n;
            Vector3 b = Vector3.Cross(n, dir);
            return b.sqrMagnitude > 1e-8f ? b.normalized : fallback;
        }

        public void SetHintsWorld(Vector3 effector)
        {
            Vector3 dir = effector - Hip;
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : -section.up;
            for (int i = 0; i < m_HintsWorld.Length && i < hintsLocal.Length; i++)
                m_HintsWorld[i] = BendDirection(i, dir);
        }

        /// <summary>Solves the chain from the hip to <paramref name="effector"/> and poses the bone visuals.</summary>
        public void Solve(Vector3 effector, Vector3 boneUp)
        {
            SetHintsWorld(effector);
            Vector3 hip = Hip;
            // Soft reach: near full extension a tiny change in target distance swings the knee a long way (and the
            // chain snaps between straight and bent), which shows as a shaking knee. Ease the last 0.4% of the reach
            // exponentially instead, so the leg approaches straight smoothly and never locks.
            Vector3 toEffector = effector - hip;
            float dist = toEffector.magnitude;
            // Chains of three or more bones are solved iteratively and are poorly conditioned near straight, so they
            // get a wider easing zone than the analytic two-bone solve.
            float soft = reach * (def.bones.Length > 2 ? 0.02f : 0.004f), start = reach - soft;
            if (dist > start && dist > 1e-5f)
                effector = hip + toEffector / dist * (start + soft * (1f - Mathf.Exp(-(dist - start) / soft)));
            if (def.bones.Length == 2)
            {
                Vector3 pole = m_HintsWorld.Length > 0 ? m_HintsWorld[0] : boneUp;
                Vector3 mid = TwoBoneIK.Solve(hip, def.bones[0], def.bones[1], effector, pole, out Vector3 end);
                chain.joints[0] = hip; chain.joints[1] = mid; chain.joints[2] = end;
            }
            else
            {
                chain.Solve(hip, effector, m_HintsWorld);
            }
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 a = chain.joints[i], b = chain.joints[i + 1];
                Vector3 dir = b - a;
                if (dir.sqrMagnitude < 1e-8f) continue;
                Vector3 upRef = i < m_HintsWorld.Length ? m_HintsWorld[i] : boneUp;
                if (Mathf.Abs(Vector3.Dot(dir.normalized, upRef.normalized)) > 0.98f) upRef = section.forward;
                bones[i].SetPositionAndRotation(a, Quaternion.LookRotation(dir, upRef));
            }
        }

        /// <summary>
        /// Computes the IK effector from the foot contact for each foot type, and poses the foot visuals.
        /// Plantigrade feet roll heel-to-toe; hooves flex at the fetlock during the swing.
        /// </summary>
        public Vector3 FootEffector(Vector3 bodyForward) => FootEffector(bodyForward, footPitch, true);

        /// <summary>
        /// Foot roll to stay in reach. Starting from <paramref name="basePitch"/>, rolls the planted foot toward
        /// <paramref name="limitPitch"/> just far enough to keep the ankle within reach of the hip: toward a larger
        /// pitch rolls onto the toe (heel rise, hoof breakover) for a foot behind the hip; toward a smaller pitch
        /// rocks back on the heel (toe up) for a foot ahead of it. <paramref name="reachable"/> is false when even
        /// the limit is not enough.
        /// </summary>
        public float SolveToeOff(Vector3 bodyForward, float basePitch, float limitPitch, out bool reachable)
        {
            Vector3 hip = Hip;
            float limit = reach * 0.998f;
            reachable = true;
            if ((FootEffector(bodyForward, basePitch, false) - hip).magnitude <= limit) return basePitch;
            if (Mathf.Approximately(limitPitch, basePitch) || (FootEffector(bodyForward, limitPitch, false) - hip).magnitude > limit)
            {
                reachable = false;
                return limitPitch;
            }
            // Binary search between an out-of-reach pitch and a reachable one, in either direction.
            float bad = basePitch, good = limitPitch;
            for (int i = 0; i < 10; i++)
            {
                float mid = (bad + good) * 0.5f;
                if ((FootEffector(bodyForward, mid, false) - hip).magnitude > limit) bad = mid; else good = mid;
            }
            return good;
        }

        /// <summary>
        /// Swing: poses a heel-toe foot hanging from the ankle (the chain end) at <paramref name="pitch"/>, instead of
        /// deriving the ankle from a ground contact. Keeps the foot attached to the shin whatever its angle.
        /// </summary>
        /// <summary>World toe and heel points of a heel-toe foot hanging from <paramref name="ankle"/> at <paramref name="pitch"/>.</summary>
        public (Vector3 toe, Vector3 heel) SolePoints(Vector3 ankle, Vector3 bodyForward, float pitch)
        {
            Vector3 n = footNormal.sqrMagnitude > 0.5f ? footNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(bodyForward, n);
            if (fwd.sqrMagnitude < 1e-5f) fwd = Vector3.Cross(n, Vector3.right);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(n, fwd).normalized;
            Quaternion rot = Quaternion.AngleAxis(pitch, right) * Quaternion.LookRotation(fwd, n);
            float L = def.footLength;
            Vector3 ankleL = new Vector3(0, def.footHeight, -0.1f * L);
            return (ankle + rot * (new Vector3(0, 0, 0.7f * L) - ankleL), ankle + rot * (new Vector3(0, 0, -0.3f * L) - ankleL));
        }

        public void PlaceFootAtAnkle(Vector3 ankle, Vector3 bodyForward, float pitch)
        {
            if (footVisual == null || def.foot != FootType.Plantigrade) return;
            Vector3 n = footNormal.sqrMagnitude > 0.5f ? footNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(bodyForward, n);
            if (fwd.sqrMagnitude < 1e-5f) fwd = Vector3.Cross(n, Vector3.right);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(n, fwd).normalized;
            Quaternion rot = Quaternion.AngleAxis(pitch, right) * Quaternion.LookRotation(fwd, n);
            float L = def.footLength;
            Vector3 ankleL = new Vector3(0, def.footHeight, -0.1f * L);
            Vector3 visualL = new Vector3(0, def.footHeight * 0.4f, 0.22f * L);
            footVisual.SetPositionAndRotation(ankle + rot * (visualL - ankleL), rot);
        }

        /// <summary>
        /// The IK effector's offset from the foot contact in the foot's sagittal plane (x forward, y up) at a foot
        /// angle (+ = toe down / heel up): the same geometry as <see cref="FootEffector(Vector3, float, bool)"/>
        /// on flat ground. Used to plan how far a planted foot can be from the hip.
        /// </summary>
        public static Vector2 EffectorOffset(FootType type, float footLength, float footHeight, float pitch)
        {
            float c = Mathf.Cos(pitch * Mathf.Deg2Rad), s = Mathf.Sin(pitch * Mathf.Deg2Rad);
            // Positive pitch turns forward-pointing vectors down.
            Vector2 Rot(Vector2 v) => new Vector2(v.x * c + v.y * s, v.y * c - v.x * s);
            switch (type)
            {
                case FootType.Plantigrade:
                {
                    Vector2 pivot = new Vector2(pitch >= 0f ? 0.7f * footLength : -0.3f * footLength, 0f);
                    return pivot + Rot(new Vector2(-0.1f * footLength, footHeight) - pivot);
                }
                case FootType.Hoof:
                    return Rot(new Vector2(-0.4f * footHeight, footHeight));
                default:
                    return new Vector2(0f, footHeight);
            }
        }

        public Vector3 FootEffector(Vector3 bodyForward, float pitch, bool applyVisuals)
        {
            float footPitch = pitch;
            Vector3 n = footNormal.sqrMagnitude > 0.5f ? footNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(swinging ? bodyForward : footForward, n);
            if (fwd.sqrMagnitude < 1e-5f) fwd = Vector3.ProjectOnPlane(bodyForward, n);
            if (fwd.sqrMagnitude < 1e-5f) fwd = Vector3.Cross(n, Vector3.right);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(n, fwd).normalized;
            Quaternion flat = Quaternion.LookRotation(fwd, n);

            switch (def.foot)
            {
                case FootType.Plantigrade:
                {
                    float L = def.footLength;
                    Vector3 heelL = new Vector3(0, 0, -0.3f * L), toeL = new Vector3(0, 0, 0.7f * L);
                    Vector3 ankleL = new Vector3(0, def.footHeight, -0.1f * L);
                    Vector3 pivotL = footPitch >= 0f ? toeL : heelL;
                    Quaternion pitchRot = Quaternion.AngleAxis(footPitch, right);
                    Vector3 pivotW = foot + flat * pivotL;
                    Vector3 W(Vector3 local) => pivotW + pitchRot * (flat * (local - pivotL));
                    Vector3 ankle = W(ankleL);
                    if (applyVisuals && footVisual != null)
                        footVisual.SetPositionAndRotation(W(new Vector3(0, def.footHeight * 0.4f, 0.22f * L)), pitchRot * flat);
                    return ankle;
                }
                case FootType.Hoof:
                {
                    float h = def.footHeight;
                    Vector3 fetlockFlat = n * h - fwd * (h * 0.4f);
                    Vector3 fetlock = foot + Quaternion.AngleAxis(footPitch, right) * fetlockFlat;
                    if (applyVisuals && hoofVisual != null)
                    {
                        Vector3 down = (foot - fetlock).normalized;
                        Vector3 hoofTop = foot - down * (h * 0.35f);
                        hoofVisual.SetPositionAndRotation(hoofTop, Quaternion.LookRotation(down, fwd));
                    }
                    if (applyVisuals && footVisual != null)
                    {
                        Vector3 d = foot - fetlock;
                        footVisual.SetPositionAndRotation(fetlock, Quaternion.LookRotation(d, fwd));
                    }
                    return fetlock;
                }
                default:
                    return foot + n * def.footHeight;
            }
        }
    }

    /// <summary>Verlet spring chain for tails (and the spider's abdomen). Rendered as a smooth tube.</summary>
    public class TailRig
    {
        public TailDef def;
        public Transform section;
        public Vector3 attachLocal;
        public Vector3 dirLocal;
        public Vector3[] points, previous;
        public float segLength;
        public TubeRenderer tube;
        float[] m_Radii;
        Vector2[] m_Aspects;
        bool m_Initialised;

        public TailRig(TailDef def)
        {
            this.def = def;
            int n = Mathf.Max(1, def.segments) + 1;
            points = new Vector3[n];
            previous = new Vector3[n];
            segLength = def.length / Mathf.Max(1, def.segments);
            m_Radii = new float[n];
            m_Aspects = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                m_Radii[i] = def.radii != null && def.radii.Length > 0
                    ? SampleArray(def.radii, t)
                    : 0.05f;
                m_Aspects[i] = def.aspect;
            }
        }

        static float SampleArray(float[] arr, float t)
        {
            if (arr.Length == 1) return arr[0];
            float f = t * (arr.Length - 1);
            int i = Mathf.Min(arr.Length - 2, Mathf.FloorToInt(f));
            return Mathf.Lerp(arr[i], arr[i + 1], f - i);
        }

        public void Reset()
        {
            m_Initialised = false;
        }

        public void Update(float dt, float speed, float time)
        {
            dt = Mathf.Min(dt, 1f / 30f);
            Vector3 basePos = section.TransformPoint(attachLocal);
            Vector3 up = section.up;
            float wag = def.wag * Mathf.Sin(time * 2.1f) * (0.6f + 0.4f * Mathf.Sin(time * 0.7f));
            Vector3 dir = Quaternion.AngleAxis(wag * 40f, up) * section.TransformDirection(dirLocal.normalized);
            // Tails stream out a little more at speed.
            dir = Vector3.Slerp(dir, -section.forward, Mathf.Clamp01(speed / 12f) * 0.5f).normalized;

            if (!m_Initialised)
            {
                for (int i = 0; i < points.Length; i++) previous[i] = points[i] = basePos + dir * (segLength * i);
                m_Initialised = true;
            }

            points[0] = basePos;
            float follow = 1f - Mathf.Exp(-def.stiffness * def.stiffness * 60f * dt);
            Vector3 g = Vector3.down * (def.gravity * 9.81f * dt * dt);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 vel = (points[i] - previous[i]) * def.damping;
                previous[i] = points[i];
                points[i] += vel + g;
                // Pull toward the rest pose hanging off the parent.
                Vector3 parentDir = i >= 2 ? (points[i - 1] - points[i - 2]).normalized : dir;
                Vector3 restDir = Vector3.Slerp(parentDir, dir, 0.5f);
                Vector3 rest = points[i - 1] + restDir * segLength;
                points[i] = Vector3.Lerp(points[i], rest, follow);
                // Length constraint.
                Vector3 d = points[i] - points[i - 1];
                float m = d.magnitude;
                points[i] = points[i - 1] + (m > 1e-5f ? d / m : dir) * segLength;
            }
            tube.Build(points, m_Radii, m_Aspects, up);
        }
    }
}
