using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// A procedurally animated creature. Every frame:
    /// motor (moves over surfaces) -> gait clock -> foot planting / stepping -> body pose from the feet ->
    /// leg IK -> arms -> head and neck -> tail -> skin.
    /// Controllers drive it by writing the input fields (<see cref="moveInput"/>, <see cref="desiredSpeed"/>, ...).
    /// </summary>
    public partial class Creature : MonoBehaviour
    {
        public CreatureDefinition def;

        [Header("Input (written by a controller)")]
        public Vector3 moveInput;          // world-space direction, magnitude 0..1
        public float desiredSpeed;         // m/s
        public Vector3 faceDirection;      // optional facing override (strafing)
        public Vector3 lookDirection;      // where the head looks
        public bool jumpRequested;         // a tap: crouch the minimum and jump
        public bool jumpHeld;              // jump input held: crouch deeper the longer it is held, jump on release
        public float cruiseSpeed = 1.5f;

        // Built parts.
        bool m_Built;
        Transform m_Body, m_Front, m_HeadT;
        TubeRenderer m_BodyTube, m_NeckTube, m_ManeTube;
        Vector3[] m_BodyPoints, m_BodyLocal;
        float[] m_BodyRadii;
        Vector2[] m_BodyAspects;
        bool[] m_BodyOnFront;
        bool m_BodyVertical;
        TailRig m_Tail;
        readonly List<LimbRig> m_Legs = new List<LimbRig>();
        readonly List<LimbRig> m_Arms = new List<LimbRig>();
        int m_PairCount = 1;
        float m_LegCentroidZ, m_LegCentroidX;
        GaitController m_Gait;
        SurfaceMotor m_Motor;

        // Runtime state.
        Vector3 m_RootPos;
        Quaternion m_BodyRot = Quaternion.identity;
        Quaternion m_HeadRot = Quaternion.identity;
        float m_Height, m_HeightVel;
        bool m_Airborne, m_Leaping;
        // Gait initiation (starting from a standstill).
        bool m_WantedMove;
        float m_InitTimer;
        LimbRig m_InitLeg, m_PendingFirstStep;
        float m_FlightTime = 0.5f;
        float m_Drop;
        float m_Sway, m_SwayVel;
        float m_Pitch, m_Roll, m_Rock;
        Vector3 m_PrevVelocity;
        Vector3 m_SmoothAccel;
        float m_Time;
        float m_StrideSignal;
        readonly Vector3[] m_NeckPts = new Vector3[4];
        readonly float[] m_NeckRadii = new float[4];
        readonly Vector3[] m_ManePts = new Vector3[4];
        readonly float[] m_ManeRadii = new float[4];
        readonly Vector2[] m_ManeAspects = new Vector2[4];

        public SurfaceMotor Motor => m_Motor;
        public GaitController Gait => m_Gait;
        public IReadOnlyList<LimbRig> Legs => m_Legs;
        public IReadOnlyList<LimbRig> Arms => m_Arms;
        public Transform BodyTransform => m_Body;
        public Transform HeadTransform => def != null && def.head.enabled ? m_HeadT : m_Body;
        public float Speed => m_Motor != null ? m_Motor.PlanarSpeed : 0f;
        public string GaitName => m_Motor == null ? "" : !m_Motor.grounded ? "Airborne" : (Speed < 0.1f ? (m_Gait.settling ? "Settling" : "Idle") : m_Gait.Gait.name);
        public Vector3 Up => m_Motor != null ? m_Motor.up : Vector3.up;
        /// <summary>Point the camera should orbit: the body, lifted a little.</summary>
        public Vector3 CameraPivot => m_Body != null ? m_Body.position + Up * (def.locomotion.bodyHeight * 0.35f) : transform.position;

        public void Teleport(Vector3 position, Vector3 forward)
        {
            if (!m_Built) return;
            m_Motor.Teleport(position, forward);
            m_RootPos = m_Motor.position;
            transform.SetPositionAndRotation(m_RootPos, m_Motor.Rotation);
            m_BodyRot = transform.rotation;
            m_Height = def.locomotion.bodyHeight;
            m_Body.SetPositionAndRotation(m_RootPos + m_Motor.up * m_Height, m_BodyRot);
            foreach (var leg in m_Legs)
            {
                leg.home = transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z));
                var (p, n, ok) = FindFoothold(leg, leg.home);
                leg.foot = p; leg.footNormal = n; leg.grounded = ok; leg.swinging = false;
                leg.footForward = transform.forward;
                leg.needsPlant = false;
            }
            m_Tail?.Reset();
            m_HeadRot = transform.rotation;
        }

        void Update()
        {
            if (m_Built && m_Motor == null && def != null)
            {
                // Script hot-reload in play mode drops non-serialised rig state: rebuild in place.
                Vector3 p = transform.position, f = transform.forward;
                Build(def);
                Teleport(p + Vector3.up * 0.3f, f);
            }
            if (!m_Built) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            if (dt <= 0f) return;
            m_Time += dt;
            var loco = def.locomotion;

            UpdateJump(dt, loco);
            float speedCmd = Mathf.Clamp(desiredSpeed, 0f, loco.maxSpeed);
            speedCmd = GaitInitiation(dt, speedCmd, loco);
            m_Motor.Step(dt, moveInput, speedCmd, faceDirection);

            // Root: follows the motor; motion along the surface normal is smoothed (curbs, stairs, corner snaps).
            Vector3 target = m_Motor.position;
            Vector3 up = m_Motor.up;
            if ((target - m_RootPos).sqrMagnitude > 9f || !m_Motor.grounded) m_RootPos = target;
            else
            {
                Vector3 d = target - m_RootPos;
                float normal = Vector3.Dot(d, up);
                m_RootPos += (d - up * normal) + up * (normal * (1f - Mathf.Exp(-18f * dt)));
            }
            transform.SetPositionAndRotation(m_RootPos, m_Motor.Rotation);

            float speed = m_Motor.grounded ? m_Motor.PlanarSpeed : 0f;
            bool needSettle = false;
            foreach (var leg in m_Legs)
            {
                leg.home = transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z));
                if (!leg.swinging && Vector3.ProjectOnPlane(leg.foot - leg.home, up).magnitude > loco.settleTolerance) needSettle = true;
            }
            UpdateSlope(dt);
            m_YawRate = Mathf.Lerp(m_YawRate, m_Motor.yawRate, 1f - Mathf.Exp(-dt / 0.1f));
            m_Gait.Update(dt, speed, needSettle, SettleFrequency());
            if (m_Gait.GaitChanged)
            {
                var g = m_Gait.Gait;
                var states = new List<(float, bool)>(m_Legs.Count);
                foreach (var leg in m_Legs)
                    states.Add((GaitController.Offset(g.pattern, leg.pairIndex, m_PairCount, leg.side) + leg.def.phaseBias, leg.swinging));
                m_Gait.Resync(states);
            }

            UpdateLegs(dt, speed);
            UpdateBody(dt, speed);
            SolveLegs(dt, speed);
            UpdateArms(dt, speed);
            UpdateHead(dt, speed);
            m_Tail?.Update(dt, speed, m_Time);
            UpdateBodySkin();
            m_PrevVelocity = m_Motor.velocity;
        }

        // Stance knee flexion (deg) from contact to toe-off, 9 even samples. Walking: normative adult gait (heel
        // strike ~5 deg, loading-response peak ~18 deg at ~15% of the cycle, ~5 deg by mid-stance, then flexing
        // for push-off). Running (Novacheck 1998): ~20 deg at contact, ~45 deg at mid-stance, ~25 deg at toe-off.
        static readonly float[] s_WalkStanceKnee = { 5f, 12f, 18f, 15f, 9f, 5f, 5f, 12f, 38f };
        static readonly float[] s_RunStanceKnee = { 20f, 30f, 39f, 44f, 45f, 41f, 35f, 29f, 25f };
        float m_StanceHeight;
        float m_Slope;          // smoothed ground slope along the direction of travel (deg, + uphill)

        /// <summary>Ground slope (deg, + uphill) along the direction of travel, averaged over about a stride.</summary>
        public float Slope => m_Slope;

        /// <summary>
        /// Slope used for the gait adjustments measured in people (stride, knee and hip angles, trunk lean). Other
        /// body plans keep only the physical effects (reach on sloped ground, settling onto the next foothold).
        /// </summary>
        float GaitSlope => m_Legs.Count == 2 ? m_Slope : 0f;

        /// <summary>
        /// Measures the ground slope along the direction of travel by probing the ground a stride-length apart ahead
        /// of and behind the body (so a flight of stairs counts as its average slope), and sets the gait's slope
        /// response: longer, slower strides uphill and shorter, quicker ones downhill (about 1% per degree).
        /// </summary>
        /// <summary>Body velocity along the ground, including the climb or descent of the measured slope.</summary>
        Vector3 TravelVelocity(Vector3 up)
        {
            Vector3 planar = Vector3.ProjectOnPlane(m_Motor.velocity, up);
            return planar + up * (planar.magnitude * m_Gait.slopeTan);
        }

        void UpdateSlope(float dt)
        {
            float target = 0f;
            var loco = def.locomotion;
            if (!loco.canClimb && m_Motor.grounded)
            {
                Vector3 up = Vector3.up;
                Vector3 dir = Vector3.ProjectOnPlane(m_Motor.velocity, up);
                if (dir.sqrMagnitude < 0.04f) dir = Vector3.ProjectOnPlane(transform.forward, up);
                dir.Normalize();
                float half = Mathf.Max(0.2f, m_Gait.hipHeight * 0.6f);
                float castUp = loco.bodyHeight + loco.maxStepUp;
                Vector3 c = transform.position;
                bool okA = Physics.Raycast(c + dir * half + up * castUp, -up, out RaycastHit a, castUp * 2.5f, m_Motor.mask, QueryTriggerInteraction.Ignore);
                bool okB = Physics.Raycast(c - dir * half + up * castUp, -up, out RaycastHit b, castUp * 2.5f, m_Motor.mask, QueryTriggerInteraction.Ignore);
                if (okA && okB) target = Mathf.Clamp(Mathf.Atan2(a.point.y - b.point.y, half * 2f) * Mathf.Rad2Deg, -30f, 30f);
            }
            m_Slope = Mathf.Lerp(m_Slope, target, 1f - Mathf.Exp(-5f * dt));
            float s = Mathf.Clamp(GaitSlope, -15f, 15f);
            m_Gait.strideScale = 1f + 0.01f * s;
            m_Gait.slopeTan = Mathf.Tan(m_Slope * Mathf.Deg2Rad);
        }

        float SettleFrequency()
        {
            // About a quarter second per foot lift when shuffling into a neat stance.
            float duty = m_Gait.Duty;
            return Mathf.Clamp((1f - duty) / 0.22f, 0.8f, 3f);
        }

        // ------------------------------------------------------------------ Legs

        /// <summary>Finds where a foot can stand near <paramref name="p"/> on any surface.</summary>
        (Vector3 point, Vector3 normal, bool ok) FindFoothold(LimbRig leg, Vector3 p)
        {
            var loco = def.locomotion;
            Vector3 up = m_Motor.up;
            int mask = m_Motor.mask;
            float h = loco.bodyHeight;
            float castUp = h * 0.6f + loco.maxStepUp;
            float castDown = h * 1.2f + loco.maxStepUp;
            RaycastHit hit;
            if (loco.canClimb)
            {
                // Climbers hug nearby walls: anything between the hip and the rest spot becomes a foothold.
                Vector3 hip = leg.Hip;
                Vector3 d = p - hip;
                float len = d.magnitude;
                if (len > 1e-3f && Physics.Raycast(hip, d / len, out hit, len * 1.15f, mask, QueryTriggerInteraction.Ignore))
                    return (hit.point, hit.normal, true);
            }
            if (Physics.Raycast(p + up * castUp, -up, out hit, castUp + castDown, mask, QueryTriggerInteraction.Ignore))
                return (hit.point, hit.normal, true);
            if (loco.canClimb)
            {
                // Wrap around a convex edge: look back underneath toward the body.
                Vector3 o = p - up * (castDown * 0.6f);
                Vector3 toward = (transform.position - up * (h * 0.3f)) - o;
                float len = toward.magnitude;
                if (len > 1e-3f && Physics.Raycast(o, toward / len, out hit, len * 1.2f, mask, QueryTriggerInteraction.Ignore))
                    return (hit.point, hit.normal, true);
            }
            return (p - up * (h * 0.15f), up, false);
        }

        Vector3 Predict(LimbRig leg, float ahead, Vector3 rootPos, Vector3 up)
        {
            Vector3 offset = leg.home - rootPos;
            Quaternion yaw = Quaternion.AngleAxis(Mathf.Clamp(m_YawRate * ahead, -120f, 120f), up);
            Vector3 vel = Vector3.ProjectOnPlane(m_Motor.velocity, up);
            // Include acceleration so footholds keep up while speeding up or braking.
            Vector3 accel = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(m_SmoothAccel, up), def.locomotion.acceleration * 1.5f);
            Vector3 p = rootPos + vel * ahead + accel * (0.5f * ahead * ahead) + yaw * offset;
            Vector3 lead = p - leg.home;
            float maxLead = Mathf.Max(0.15f, leg.reach * 0.55f);
            if (lead.magnitude > maxLead) p = leg.home + lead.normalized * maxLead;
            // Turning: out wide of the path, on top of the fore/aft lead.
            return p + TurnPlacement(leg, up);
        }

        /// <summary>
        /// Sideways (centripetal) acceleration of the body: the part of its acceleration across the direction of
        /// travel, e.g. v^2 / r when following a curve.
        /// </summary>
        /// <summary>
        /// How much a leg can support the body given how far its foot is from its hip horizontally, as a fraction of the
        /// leg's length: fully when the foot is within about half a leg of being under the hip, fading out by 80%.
        /// </summary>
        static float SupportShare(float horizontalRatio) => 1f - Mathf.SmoothStep(0f, 1f, (horizontalRatio - 0.55f) / 0.25f);

        Vector3 m_TurnAccel;
        float m_YawRate;        // turn rate smoothed over ~0.1 s (the raw rate is recomputed from each frame's facing change)

        /// <summary>
        /// Sustained sideways acceleration of a turn, for foot placement and lean. Steering input swings the velocity
        /// round in a sharp burst; a person spreads a small change of heading over a step or two and keeps their normal
        /// step width, so brief bursts are smoothed out (~0.35 s) and the first ~0.6 m/s^2 (gentle curves) is ignored.
        /// </summary>
        Vector3 TurnAcceleration()
        {
            float m = m_TurnAccel.magnitude;
            const float DeadZone = 0.6f;
            return m > DeadZone ? m_TurnAccel * ((m - DeadZone) / m) : Vector3.zero;
        }

        Vector3 LateralAcceleration(Vector3 up)
        {
            Vector3 a = Vector3.ProjectOnPlane(m_SmoothAccel, up);
            Vector3 v = Vector3.ProjectOnPlane(m_Motor.velocity, up);
            if (v.sqrMagnitude < 0.04f) return Vector3.zero;
            Vector3 dir = v.normalized;
            return a - dir * Vector3.Dot(a, dir);
        }

        /// <summary>
        /// Where a foot must land relative to the hips when the body is following a curve. To turn, the ground force
        /// has to pass through the centre of mass while pushing it toward the centre of the turn, so the feet land out
        /// wide of the hips by hip height x (sideways acceleration / g) while the body leans in. The hips keep turning
        /// toward the new heading and the feet sweep round on the outside of their path.
        /// </summary>
        Vector3 TurnPlacement(LimbRig leg, Vector3 up)
        {
            if (def.locomotion.canClimb || !m_Motor.grounded) return Vector3.zero;
            float hip = Mathf.Max(0.05f, m_Gait.hipHeight);
            // Slightly under the full balance offset, matching the body's slight under-lean.
            Vector3 outward = -TurnAcceleration() / 9.81f * hip * 0.8f;
            return Vector3.ClampMagnitude(outward, leg.reach * 0.3f);
        }

        // ------------------------------------------------------------------ Jumping

        enum JumpPhase { None, Crouch, Push }
        JumpPhase m_JumpPhase;
        float m_JumpDepth, m_JumpTime, m_JumpPushDepth, m_JumpPushTime, m_JumpSpeed;
        bool m_JumpHeldPrev, m_JumpTap;

        /// <summary>How deep the body is crouched for a jump (m); applied on top of the gait's body height.</summary>
        public float JumpCrouch => m_JumpDepth;

        /// <summary>Deepest countermovement: ~38% of hip height (a maximal countermovement jump), less at a run.</summary>
        float JumpMaxDepth(LocomotionDef loco)
        {
            float run = Mathf.InverseLerp(loco.cruiseSpeed, loco.maxSpeed, m_Motor.PlanarSpeed);
            return m_Gait.hipHeight * 0.38f * Mathf.Lerp(1f, 0.35f, run);
        }

        /// <summary>
        /// Countermovement jump. Pressing jump starts the crouch: the body drops as the legs unload, falling under a
        /// share of gravity and braking into the deepest point, so how fast it gets there depends on gravity. Holding
        /// sinks deeper, up to ~38% of hip height; a tap still dips a minimum amount. On release the legs extend
        /// with constant force, so take-off speed grows with the square root of the push distance. Horizontal
        /// momentum is untouched throughout. Climbers on a wall or ceiling let go immediately instead.
        /// </summary>
        void UpdateJump(float dt, LocomotionDef loco)
        {
            bool pressed = (jumpHeld && !m_JumpHeldPrev) || jumpRequested;
            if (jumpRequested && !jumpHeld) m_JumpTap = true;
            jumpRequested = false;
            m_JumpHeldPrev = jumpHeld;

            if (!m_Motor.grounded || m_Airborne)
            {
                m_JumpPhase = JumpPhase.None;
                m_JumpDepth = 0f;
                return;
            }
            if (m_JumpPhase == JumpPhase.None)
            {
                if (!pressed) return;
                bool onWall = loco.canClimb && Vector3.Dot(m_Motor.up, Vector3.up) < 0.7f;
                if (onWall) { m_Motor.Jump(loco.jumpSpeed); return; }
                m_JumpPhase = JumpPhase.Crouch;
                m_JumpTime = 0f;
                m_JumpTap |= !jumpHeld;
            }

            float maxDepth = JumpMaxDepth(loco);
            float minDepth = maxDepth * 0.3f;
            if (m_JumpPhase == JumpPhase.Crouch)
            {
                m_JumpTime += dt;
                // Unweighting: about half the body weight comes off the legs, so the body falls at ~g/2, and the legs
                // brake it smoothly into the bottom of the crouch.
                float fall = 0.5f * m_Motor.gravity * 0.45f * m_JumpTime * m_JumpTime;
                m_JumpDepth = maxDepth * (1f - Mathf.Exp(-fall / Mathf.Max(0.01f, maxDepth)));
                bool release = !jumpHeld || m_JumpTap;
                if (release && m_JumpDepth >= minDepth)
                {
                    m_JumpPhase = JumpPhase.Push;
                    m_JumpPushDepth = m_JumpDepth;
                    m_JumpPushTime = 0f;
                    // Constant push force: v^2 = 2 a d, scaled so a mid-depth crouch gives the creature's jump speed.
                    m_JumpSpeed = loco.jumpSpeed * Mathf.Sqrt(m_JumpDepth / Mathf.Max(0.01f, maxDepth * 0.6f));
                    m_JumpTap = false;
                }
            }
            if (m_JumpPhase == JumpPhase.Push)
            {
                // Accelerating from rest over the push distance: depth falls with the square of time.
                float pushDuration = Mathf.Max(0.08f, 2f * m_JumpPushDepth / Mathf.Max(0.5f, m_JumpSpeed));
                m_JumpPushTime += dt;
                float t = Mathf.Clamp01(m_JumpPushTime / pushDuration);
                m_JumpDepth = m_JumpPushDepth * (1f - t * t);
                if (t >= 1f)
                {
                    m_JumpPhase = JumpPhase.None;
                    m_JumpDepth = 0f;
                    m_Motor.Jump(m_JumpSpeed);
                }
            }
        }

        // ------------------------------------------------------------------ Gait initiation

        /// <summary>
        /// Starting to move from a standstill, after gait-initiation studies: a short anticipatory weight shift
        /// (the body barely moves, ~0.1 m/s at heel-off, and shifts over the stance foot), then ONE leg (the one best
        /// placed to step) lifts first and the gait clock is aligned to it so the others follow in order.
        /// Returns the speed the motor may use this frame.
        /// </summary>
        float GaitInitiation(float dt, float speedCmd, LocomotionDef loco)
        {
            bool wantsMove = speedCmd > 0.05f && moveInput.sqrMagnitude > 0.01f;
            bool standing = m_Motor.grounded && m_Motor.PlanarSpeed < 0.15f && m_Legs.Count > 0;
            if (standing) foreach (var leg in m_Legs) if (leg.swinging) { standing = false; break; }
            if (wantsMove && !m_WantedMove && standing)
            {
                m_InitLeg = ChooseFirstStepLeg();
                m_InitTimer = loco.startDelay;
            }
            m_WantedMove = wantsMove;
            if (m_InitLeg == null) return speedCmd;
            if (!wantsMove) { m_InitLeg = null; m_InitTimer = 0f; return speedCmd; }

            m_InitTimer -= dt;
            if (m_InitTimer > 0f) return Mathf.Min(speedCmd, 0.12f);
            // Weight shift done: step off with the chosen leg; everything after follows the gait clock.
            var g = m_Gait.Gait;
            m_Gait.SetPhase(GaitController.Offset(g.pattern, m_InitLeg.pairIndex, m_PairCount, m_InitLeg.side) + m_InitLeg.def.phaseBias);
            m_PendingFirstStep = m_InitLeg;
            m_InitLeg = null;
            return speedCmd;
        }

        /// <summary>The foot furthest behind relative to where we want to go steps first; ties go to the gait's lead leg.</summary>
        LimbRig ChooseFirstStepLeg()
        {
            Vector3 dir = Vector3.ProjectOnPlane(moveInput, m_Motor.up);
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            var g = m_Gait.Gait;
            LimbRig best = null;
            float bestScore = float.MinValue, bestOffset = float.MaxValue;
            foreach (var leg in m_Legs)
            {
                float behind = -Vector3.Dot(leg.foot - leg.home, dir);
                float offset = Mathf.Repeat(GaitController.Offset(g.pattern, leg.pairIndex, m_PairCount, leg.side) + leg.def.phaseBias, 1f);
                bool better = behind > bestScore + 0.03f || (Mathf.Abs(behind - bestScore) <= 0.03f && offset < bestOffset);
                if (best == null || better) { best = leg; bestScore = behind; bestOffset = offset; }
            }
            return best;
        }

        // ------------------------------------------------------------------ Jumps and leaps

        /// <summary>
        /// Take-off. A jump from standing (or a slow walk) is a two-footed hop. A jump on the move is a running
        /// leap: the rear-most foot is the take-off leg and trails behind, the others lead and land first.
        /// </summary>
        void BeginFlight(LocomotionDef loco)
        {
            m_Airborne = true;
            float vUp = Vector3.Dot(m_Motor.velocity, Vector3.up);
            m_FlightTime = Mathf.Max(0.3f, 2f * vUp / Mathf.Max(1f, m_Motor.gravity));
            float leapSpeed = Mathf.Max(loco.minSpeed * 2f, loco.bodyHeight * 1.2f);
            m_Leaping = m_Motor.PlanarSpeed > leapSpeed && m_Legs.Count >= 2;

            float mean = 0f;
            foreach (var leg in m_Legs)
            {
                leg.airStartLocal = transform.InverseTransformPoint(leg.foot);
                leg.swinging = false;
                mean += leg.airStartLocal.z - leg.restRootLocal.z;
            }
            mean /= Mathf.Max(1, m_Legs.Count);
            int leads = 0;
            foreach (var leg in m_Legs)
            {
                leg.leapLead = leg.airStartLocal.z - leg.restRootLocal.z > mean + 1e-3f;
                if (leg.leapLead) leads++;
            }
            // Feet level with each other (e.g. both in the air mid-stride): pick sides so there is a lead and a trail.
            if (leads == 0 || leads == m_Legs.Count)
                foreach (var leg in m_Legs) leg.leapLead = leg.side > 0;
        }

        static Vector3 KeyframePath(Vector3 k0, Vector3 k1, Vector3 k2, float t1, float p)
        {
            p = Mathf.Clamp01(p);
            if (p < t1) return Vector3.Lerp(k0, k1, Mathf.SmoothStep(0f, 1f, p / t1));
            return Vector3.Lerp(k1, k2, Mathf.SmoothStep(0f, 1f, (p - t1) / (1f - t1)));
        }

        /// <summary>Two-footed hop: from the take-off spread, fold up mid-air and land with the feet together.</summary>
        Vector3 HopFootLocal(LimbRig leg, float p, LocomotionDef loco)
        {
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.7f));
            float tuck = loco.bodyHeight * 0.22f * Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
            float ahead = Mathf.Min(m_Motor.PlanarSpeed * 0.04f, leg.reach * 0.3f);
            Vector3 land = new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z + ahead);
            return Vector3.Lerp(leg.airStartLocal, land, blend) + Vector3.up * tuck;
        }

        /// <summary>
        /// Running leap, after long-jump / split-leap kinematics: the take-off leg leaves the ground almost straight
        /// (hip ~163 deg, knee ~172 deg) and stays extended behind before starting its recovery; the free leg drives
        /// the knee forward and up, then extends to reach for the landing. The legs stay apart through the flight.
        /// </summary>
        Vector3 LeapFootLocal(LimbRig leg, float p, LocomotionDef loco)
        {
            float h = loco.bodyHeight;
            float r = leg.reach;
            Vector3 rest = new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z);
            if (leg.leapLead)
            {
                Vector3 kneeDrive = rest + new Vector3(0f, h * 0.4f, r * 0.25f);
                Vector3 reachOut = rest + new Vector3(0f, 0f, Mathf.Min(r * 0.45f, m_Motor.PlanarSpeed * 0.06f));
                return KeyframePath(leg.airStartLocal, kneeDrive, reachOut, 0.4f, p);
            }
            Vector3 trail = rest + new Vector3(0f, h * 0.12f, -r * 0.6f);
            Vector3 recover = rest + new Vector3(0f, h * 0.3f, -r * 0.35f);
            return KeyframePath(leg.airStartLocal, trail, recover, 0.55f, p);
        }

        void Touchdown(float speed)
        {
            m_Airborne = false;
            var g = m_Gait.Gait;
            LimbRig lead = null;
            foreach (var leg in m_Legs)
            {
                leg.needsPlant = false;
                if (m_Leaping && !leg.leapLead) continue;
                // Landing feet plant where they touch down.
                var (fp, fn, ok) = FindFoothold(leg, leg.foot);
                leg.foot = fp; leg.footNormal = fn; leg.grounded = ok;
                leg.footForward = Vector3.ProjectOnPlane(transform.forward, fn).normalized;
                leg.swinging = false;
                leg.stanceTime = 0f;
                if (lead == null) lead = leg;
            }
            if (m_Leaping && lead != null)
            {
                // Resume the gait as if the lead foot had just struck, and swing the trailing leg(s) through
                // as the next step, so a leap flows straight back into the run.
                float strike = GaitController.Offset(g.pattern, lead.pairIndex, m_PairCount, lead.side) + lead.def.phaseBias + (1f - m_Gait.Duty);
                m_Gait.SetPhase(strike + 0.02f);
                Vector3 up = m_Motor.up, rootPos = transform.position;
                float dur = m_Gait.SwingDuration * 0.7f;
                float stanceAhead = m_Gait.StanceDuration * 0.4f;
                foreach (var leg in m_Legs)
                {
                    if (leg.leapLead) continue;
                    leg.home = transform.TransformPoint(new Vector3(leg.restRootLocal.x, 0f, leg.restRootLocal.z));
                    var (tp, tn, _) = FindFoothold(leg, Predict(leg, dur + stanceAhead, rootPos, up));
                    leg.BeginSwing(tp, tn, dur, g.stepHeight, g.curl * 0.5f, LimbRig.FlexToRatio(SwingKneeFlex(speed)));
                }
            }
            m_Leaping = false;
            // Absorb the impact with the legs.
            float impact = Mathf.Max(0f, -Vector3.Dot(m_Motor.lastLandingVelocity, Vector3.up));
            m_HeightVel -= Mathf.Min(impact, 12f) * 0.35f;
        }

        /// <summary>How far a planted foot may roll onto its toe: heel rise grows with speed (walk to sprint); ~40 deg hoof breakover.</summary>
        float MaxToePitch(LimbRig leg, float speed) => leg.def.foot == FootType.Plantigrade ? FootProfile(speed).toeRoll : leg.def.foot == FootType.Hoof ? 40f : 0f;

        /// <summary>
        /// Heel-toe foot behaviour by speed: shod walking lands on the heel (toe up); jogging lands on the midfoot
        /// or forefoot and lets the heel settle; sprinting stays on the forefoot. Toe-off heel rise grows with speed
        /// (ankle push-off ~5 to 20 deg walking, up to ~30 deg running).
        /// </summary>
        (float strike, float heelUp, float toeRoll) FootProfile(float speed)
        {
            var loco = def.locomotion;
            float runStart = loco.maxSpeed * 0.35f;
            foreach (var gd in loco.gaits) if (gd.duty < 0.5f) { runStart = gd.minSpeed; break; }
            float tRun = Mathf.InverseLerp(runStart - 0.3f, runStart + 0.6f, speed);
            float tSprint = Mathf.InverseLerp(loco.maxSpeed * 0.65f, loco.maxSpeed * 0.95f, speed);
            float strike = Mathf.Lerp(Mathf.Lerp(loco.footStrikeWalk, loco.footStrikeRun, tRun), loco.footStrikeSprint, tSprint);
            strike *= Mathf.Clamp01(speed / 0.8f); // slow shuffles land flat
            float heelUp = loco.heelSprint * tSprint;
            float toeRoll = Mathf.Lerp(Mathf.Lerp(loco.toeRollWalk, loco.toeRollRun, tRun), loco.toeRollSprint, tSprint);
            return (strike, heelUp, toeRoll);
        }

        /// <summary>Shin tilt from vertical in the body's sagittal plane: positive when the ankle trails the knee.</summary>
        float ShinTilt(LimbRig leg, Vector3 up)
        {
            var j = leg.chain.joints;
            if (j.Length < 3) return 0f;
            Vector3 shin = j[j.Length - 1] - j[j.Length - 2];
            Vector3 right = transform.right;
            Vector3 s = Vector3.ProjectOnPlane(shin, right);
            if (s.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(-up, s, right);
        }

        /// <summary>Peak swing knee flexion for the current gait, blended from its entry speed to top speed.</summary>
        float SwingKneeFlex(float speed)
        {
            var g = m_Gait.Gait;
            if (g.kneeFlex <= 0f) return 0f;
            float fast = g.kneeFlexFast >= 0f ? g.kneeFlexFast : g.kneeFlex;
            float t = Mathf.InverseLerp(g.minSpeed, def.locomotion.maxSpeed, speed);
            return Mathf.Lerp(g.kneeFlex, fast, t);
        }

        void UpdateLegs(float dt, float speed)
        {
            var loco = def.locomotion;
            Vector3 up = m_Motor.up;
            Vector3 rootPos = transform.position;

            if (!m_Motor.grounded)
            {
                if (!m_Airborne) BeginFlight(loco);
                float p = m_Motor.airTime / m_FlightTime;
                foreach (var leg in m_Legs)
                {
                    leg.foot = transform.TransformPoint(m_Leaping ? LeapFootLocal(leg, p, loco) : HopFootLocal(leg, p, loco));
                    leg.footNormal = Vector3.Slerp(leg.footNormal, up, 1f - Mathf.Exp(-10f * dt));
                }
                return;
            }

            if (m_Airborne) Touchdown(speed);

            var g = m_Gait.Gait;
            bool moving = speed > 0.15f;
            // Land ahead of the hip by the share of the stance travel the leg can reach in front; the rest is covered
            // behind the hip, where toe roll extends the leg (the stride cap keeps both ends within reach).
            float stanceAhead = moving ? m_Gait.StanceDuration * m_Gait.LeadFraction(g, speed) : 0f;
            float lift = g.stepHeight * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(speed / Mathf.Max(0.5f, loco.cruiseSpeed * 1.5f)));
            int swingingCount = 0;
            foreach (var leg in m_Legs) if (leg.swinging) swingingCount++;
            int maxSwinging = Mathf.Max(1, m_Legs.Count / 2);

            for (int i = 0; i < m_Legs.Count; i++)
            {
                var leg = m_Legs[i];
                if (leg.swinging)
                {
                    float remaining = (1f - leg.swingT) * leg.swingDuration;
                    var (tp, tn, ok) = FindFoothold(leg, Predict(leg, remaining + stanceAhead, rootPos, up));
                    leg.target = tp; leg.targetNormal = tn;
                    if (leg.UpdateSwing(dt, up, transform.forward, TravelVelocity(up)))
                    {
                        leg.grounded = ok;
                        leg.footForward = Vector3.ProjectOnPlane(transform.forward, tn).normalized;
                        swingingCount--;
                    }
                    continue;
                }

                leg.stanceTime += dt;
                if (!moving) leg.liftPending = false;
                float offset = GaitController.Offset(g.pattern, leg.pairIndex, m_PairCount, leg.side) + leg.def.phaseBias;
                float err = Vector3.ProjectOnPlane(leg.foot - leg.home, up).magnitude;
                bool want = false;
                bool urgent = false;
                bool firstStep = leg == m_PendingFirstStep;
                if (firstStep) { want = true; m_PendingFirstStep = null; }
                else if (m_InitLeg != null) continue; // weight shift in progress: nobody steps yet
                if (leg.needsPlant) { want = true; urgent = true; }
                else if (m_Gait.Crossed(offset) && (moving || err > loco.settleTolerance)) want = true;
                else if (leg.liftPending && moving) want = true; // scheduled lift that was waiting for support

                // Overstretched or far off: step now. When barely moving, only if enough other feet hold the body.
                float hipDist = Vector3.Distance(leg.foot, leg.Hip);
                // Moving legs may straighten fully at the end of stance (the foot rolls up); only a badly overstretched leg steps early.
                // Heel-toe feet and hooves pivot on the toe at push-off, which adds up to ~70% of the foot length.
                float toeReach = leg.def.foot == FootType.Point ? 0f : leg.def.footLength * 0.7f;
                bool overstretched = hipDist > (leg.reach + leg.def.footHeight + toeReach) * (moving ? 1.08f : 1f) + 0.02f;
                if (overstretched || err > Mathf.Max(g.strideMax * 0.75f, leg.reach * 0.8f) || !leg.grounded)
                {
                    bool walkingGait = g.duty >= 0.5f && m_Legs.Count > 1;
                    int plantedOthers = 0;
                    foreach (var o in m_Legs) if (o != leg && !o.swinging) plantedOthers++;
                    if (walkingGait && moving && leg.grounded && plantedOthers * 2 < m_Legs.Count - 1)
                    {
                        // Walking has no flight phase: rather than lift this foot too, bring the airborne feet
                        // down sooner, then this one goes as soon as they land.
                        foreach (var o in m_Legs)
                            if (o.swinging) o.swingDuration = Mathf.Min(o.swingDuration, 0.06f / Mathf.Max(0.05f, 1f - o.swingT));
                        leg.liftPending = true;
                    }
                    else if (moving || swingingCount < maxSwinging || hipDist > (leg.reach + leg.def.footHeight) * 1.15f) { want = true; urgent = true; }
                }

                // Toe-off: once even a full heel roll (or hoof breakover) cannot keep the planted foot in reach,
                // lift it now instead of dragging a straightened leg. Walking gaits keep another foot planted;
                // running gaits (duty below one half) are allowed a flight phase.
                // Only after at least half its expected stance, so early lifts never replace the gait's rhythm.
                if (!want && moving && leg.stanceTime > m_Gait.StanceDuration * 0.5f)
                {
                    leg.SolveToeOff(transform.forward, 0f, MaxToePitch(leg, speed), out bool reachable);
                    if (!reachable)
                    {
                        bool support = false;
                        foreach (var o in m_Legs) if (o != leg && !o.swinging) { support = true; break; }
                        if (support || g.duty < 0.5f) { want = true; leg.toeOffLifts++; }
                    }
                }

                if (!want) continue;
                // Walking gaits have no flight phase: a scheduled lift waits until at least half of the other legs
                // are planted (a biped waits for the other foot to land).
                if (!urgent && !firstStep && g.duty >= 0.5f && m_Legs.Count > 1)
                {
                    int planted = 0;
                    foreach (var o in m_Legs) if (o != leg && !o.swinging) planted++;
                    if (planted * 2 < m_Legs.Count - 1) { leg.liftPending = true; continue; }
                }
                leg.liftPending = false;
                // Gait-scheduled steps always go (a gallop has three feet in the air); settling shuffles take turns.
                if (!urgent && !moving && swingingCount >= maxSwinging && m_Legs.Count > 2) continue;

                if (urgent)
                {
                    leg.urgentLifts++;
                    leg.lastUrgentReason = leg.needsPlant ? "plant" : overstretched ? $"stretch{hipDist:0.00}/{leg.reach + leg.def.footHeight:0.00} v{speed:0.0} {g.name}"
                        : !leg.grounded ? "noground" : $"err{err:0.00}";
                }
                float dur = urgent ? 0.18f : (m_Gait.settling ? 0.22f : m_Gait.SwingDuration);
                // The first step from a standstill is timed for the speed we are heading to (a little slower),
                // not for the crawl we start at, and it lands short because the body is still slow.
                if (firstStep) dur = m_Gait.SwingDurationAt(Mathf.Max(desiredSpeed, loco.minSpeed)) * 1.3f;
                var (p, n, valid) = FindFoothold(leg, Predict(leg, dur + stanceAhead, rootPos, up));
                float kneePeak = SwingKneeFlex(speed) * (m_Gait.settling ? 0.6f : 1f);
                float flex = urgent ? 1f : LimbRig.FlexToRatio(kneePeak);
                // Gait steps with swing data follow the measured joint-angle curves; emergency steps use a plain arc.
                var style = urgent ? SwingStyle.Arc : g.swing;
                // Slopes: uphill the leg lifts higher (more hip and knee flexion in swing, to clear rising ground and
                // reach up to the next foothold); downhill the swing is lower and flatter.
                float slope = Mathf.Clamp(GaitSlope, -20f, 20f);
                kneePeak += slope > 0f ? 0.8f * slope : 0.3f * slope;
                float hipBias = slope > 0f ? 1f * slope : 0.3f * slope;
                leg.BeginSwing(p, n, dur, urgent && leg.needsPlant ? lift * 0.4f : (m_Gait.settling ? lift * 0.6f : lift), moving && !urgent ? g.curl : 0f, flex,
                    style, kneePeak, g.clearance * (leg.reach + leg.def.footHeight), hipBias);
                // Place the foot on its swing path right away, so the lift-off frame continues from the stance pose
                // (otherwise the foot flattens for one frame and the knee snaps).
                leg.UpdateSwing(0f, up, transform.forward, TravelVelocity(up));
                leg.needsPlant = false;
                swingingCount++;
            }
        }

        void SolveLegs(float dt, float speed)
        {
            Vector3 up = m_Motor.up;
            Vector3 fwd = transform.forward;
            float moving = Mathf.Clamp01(speed / Mathf.Max(0.3f, def.locomotion.cruiseSpeed));
            float stride = Mathf.Max(0.2f, m_Gait.stride);
            foreach (var leg in m_Legs)
            {
                float target = 0f;
                if (m_Airborne)
                {
                    // In flight: the trailing take-off foot stays pointed; the lead foot lands heel first.
                    bool trailing = m_Leaping && !leg.leapLead;
                    if (leg.def.foot == FootType.Plantigrade) target = trailing ? 40f : (m_Leaping ? -12f : 10f);
                    else if (leg.def.foot == FootType.Hoof) target = trailing ? 70f : 25f;
                    leg.footPitch = Mathf.MoveTowards(leg.footPitch, target, 300f * dt);
                }
                else if (leg.def.foot == FootType.Plantigrade)
                {
                    var (strike, heelUp, toeRoll) = FootProfile(speed);
                    if (leg.swinging)
                    {
                        // The foot hangs from the ankle. Solve the leg to a flat-foot ankle target first (independent
                        // of the foot angle, so there is no feedback loop), read the shin from that pose, then set the
                        // foot square to the shin plus the push-off plantarflexion, which relaxes to neutral by
                        // mid-swing. Before contact the angle blends into the strike angle for this speed, and the
                        // ankle target blends into the ground-contact model so touch-down hands over smoothly.
                        float t = leg.swingT;
                        Vector3 flatAnkle = leg.FootEffector(fwd, 0f, false);
                        leg.Solve(flatAnkle, up);
                        float ankle = toeRoll * 0.35f * (1f - Mathf.SmoothStep(0f, 1f, t / 0.45f));
                        float hang = Mathf.Clamp(ShinTilt(leg, up) + ankle, -30f, 150f);
                        target = Mathf.Lerp(hang, strike, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.95f, t)));
                        leg.footPitch = Mathf.MoveTowards(leg.footPitch, target, 1500f * dt);
                        leg.landPitch = leg.footPitch;
                        float contact = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t));
                        Vector3 swingEffector = Vector3.Lerp(flatAnkle, leg.FootEffector(fwd, leg.footPitch, false), contact);
                        if (leg.swingStyle != SwingStyle.Arc)
                        {
                            // The lowest point of the angled foot (toe when pointed, heel when cocked for the strike)
                            // stays above the ground by the measured minimum toe clearance.
                            var (toeP, heelP) = leg.SolePoints(swingEffector, fwd, leg.footPitch);
                            float lowest = Mathf.Min(Vector3.Dot(toeP, up), Vector3.Dot(heelP, up));
                            float need = leg.swingGround + leg.swingClearance - lowest;
                            if (need > 0f) swingEffector += up * need;
                        }
                        leg.Solve(swingEffector, up);
                        leg.PlaceFootAtAnkle(leg.End, fwd, leg.footPitch);
                        continue;
                    }
                    else
                    {
                        // Early stance: from the touch-down angle to the stance angle (flat, or heel up at a sprint).
                        // A heel strike rolls down onto the forefoot; a forefoot strike lets the heel settle,
                        // which is the "suspension" of a jogging footfall.
                        float settle = Mathf.Max(0.05f, m_Gait.StanceDuration * 0.2f);
                        float early = Mathf.SmoothStep(0f, 1f, leg.stanceTime / settle);
                        float stancePitch = Mathf.Lerp(leg.landPitch, heelUp, early);
                        // Late stance: the heel rises as the foot passes behind the hip, up to this speed's toe roll.
                        Vector3 hipGround = leg.Hip - up * Vector3.Dot(leg.Hip - leg.foot, up);
                        float behind = -Vector3.Dot(leg.foot - hipGround, fwd);
                        float heelRise = Mathf.Clamp01((behind - leg.def.footLength * 0.15f) / (stride * 0.25f)) * toeRoll * moving;
                        // Descending, the heel stays down longer (the ankle dorsiflexes as the body lowers over the foot).
                        if (m_Slope < 0f) heelRise *= Mathf.Clamp01(1f + m_Slope / 15f);
                        target = Mathf.Max(stancePitch, heelRise);
                        leg.plannedPitch = target;
                        leg.footPitch = leg.stanceTime < dt * 1.5f ? leg.landPitch : Mathf.MoveTowards(leg.footPitch, target, 400f * dt);
                    }
                }
                else if (leg.def.foot == FootType.Hoof)
                {
                    // Fetlock swing flexion: ~62 deg range at the walk, ~77 deg at the trot (3D MCP kinematics).
                    float fetlockFlex = Mathf.Lerp(62f, 78f, Mathf.Clamp01((speed - 1.5f) / 3f));
                    // Stance: the fetlock sinks (hyperextends) under load, most at mid-stance and more with speed
                    // (roughly 20 deg beyond standing at the walk, ~35 deg at the trot and gallop): the horse's suspension.
                    float stanceT = Mathf.Clamp01(leg.stanceTime / Mathf.Max(0.05f, m_Gait.StanceDuration));
                    float sink = speed > 0.15f ? Mathf.Lerp(20f, 35f, Mathf.InverseLerp(1.5f, 5f, speed)) * Mathf.Sin(stanceT * Mathf.PI) : 0f;
                    target = leg.swinging ? Mathf.Sin(leg.swingT * Mathf.PI) * fetlockFlex : -sink;
                    leg.footPitch = Mathf.MoveTowards(leg.footPitch, target, 700f * dt);
                }
                if (!leg.swinging && !m_Airborne)
                {
                    // Toe-off roll: lift the heel (or break the hoof over its toe) just enough to keep the planted
                    // foot within reach, so a straightening leg rolls onto the toe instead of being stretched.
                    // A foot ahead of the hip rocks back on its heel; one behind rolls onto its toe.
                    Vector3 hipGround = leg.Hip - up * Vector3.Dot(leg.Hip - leg.foot, up);
                    bool ahead = Vector3.Dot(leg.foot - hipGround, fwd) > 0f;
                    // Absolute limits (never relative to last frame's angle, which would ratchet): a heel can rock
                    // back ~35 deg (toe up); the toe roll is capped by this speed's heel rise.
                    const float MaxHeelRock = -35f;
                    float maxToe = MaxToePitch(leg, speed);
                    float basePitch = Mathf.Clamp(leg.footPitch, MaxHeelRock, maxToe);
                    // A heel strike rocks back on the heel; a forefoot / midfoot landing stays on the forefoot (heel a
                    // little higher) and lets the heel settle as the body passes over, instead of flipping heel-first.
                    bool heelStrike = leg.landPitch < 0f;
                    float limitPitch = ahead && heelStrike ? MaxHeelRock : maxToe;
                    if (leg.def.foot == FootType.Hoof && ahead) limitPitch = basePitch; // hooves do not rock back
                    // Rate-limited so a small reach shortfall never shows as a pop (the foot mesh follows the leg meanwhile).
                    float solved = Mathf.Clamp(leg.SolveToeOff(fwd, basePitch, limitPitch, out _), MaxHeelRock, maxToe);
                    leg.footPitch = Mathf.MoveTowards(basePitch, solved, 600f * dt);
                }
                Vector3 effector = leg.FootEffector(fwd);
                leg.Solve(effector, up);
                // Out of reach for a moment: let the foot follow the chain end rather than show a gap.
                Vector3 gap = leg.End - effector;
                if (gap.sqrMagnitude > 1e-6f)
                {
                    if (leg.footVisual != null) leg.footVisual.position += gap;
                    if (leg.hoofVisual != null) leg.hoofVisual.position += gap;
                }
            }
        }

        // ------------------------------------------------------------------ Body

        void UpdateBody(float dt, float speed)
        {
            var loco = def.locomotion;
            var g = m_Gait.Gait;
            Vector3 up = m_Motor.up;
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            Vector3 rootPos = transform.position;
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(0.3f, loco.cruiseSpeed));
            float stanceDur = m_Gait.StanceDuration;

            // Fit a plane through the (planned) foot heights: body height and tilt follow the ground under the feet.
            float n = 0f, hm = 0f, zm = 0f, xm = 0f;
            foreach (var leg in m_Legs)
            {
                Vector3 gp = leg.swinging ? leg.target : leg.foot;
                float h = Mathf.Clamp(Vector3.Dot(gp - rootPos, up), -loco.bodyHeight * 0.6f, loco.bodyHeight * 0.6f);
                hm += h; zm += leg.restRootLocal.z; xm += leg.restRootLocal.x; n++;
            }
            float pitchT = 0f, rollT = 0f;
            if (n > 0)
            {
                hm /= n; zm /= n; xm /= n;
                float szz = 0f, szh = 0f, sxx = 0f, sxh = 0f;
                foreach (var leg in m_Legs)
                {
                    Vector3 gp = leg.swinging ? leg.target : leg.foot;
                    float h = Mathf.Clamp(Vector3.Dot(gp - rootPos, up), -loco.bodyHeight * 0.6f, loco.bodyHeight * 0.6f);
                    float dz = leg.restRootLocal.z - zm, dx = leg.restRootLocal.x - xm;
                    szz += dz * dz; szh += dz * (h - hm);
                    sxx += dx * dx; sxh += dx * (h - hm);
                }
                if (szz > 0.01f) pitchT = -Mathf.Atan(szh / szz) * Mathf.Rad2Deg * loco.tiltFromFeet;
                if (sxx > 0.01f) rollT = Mathf.Atan(sxh / sxx) * Mathf.Rad2Deg * loco.tiltFromFeet;
            }

            // Gait signals from the stance feet: bob, sway, fore/aft rocking.
            float stanceSum = 0f, stanceCount = 0f, swaySum = 0f, frontW = 0f, frontN = 0f, hindW = 0f, hindN = 0f;
            float strideSignal = 0f;
            foreach (var leg in m_Legs)
            {
                bool front = leg.restRootLocal.z > m_LegCentroidZ;
                float w = 0f;
                if (!leg.swinging)
                {
                    w = stanceDur > 0f ? Mathf.Sin(Mathf.Clamp01(leg.stanceTime / stanceDur) * Mathf.PI) : 1f;
                    stanceSum += w; stanceCount++;
                    swaySum += leg.side * w;
                }
                if (front) { frontW += w; frontN++; } else { hindW += w; hindN++; }
                strideSignal += leg.side * Vector3.Dot(leg.foot - leg.home, fwd);
            }
            m_StrideSignal = Mathf.Clamp(strideSignal / Mathf.Max(0.2f, m_Gait.stride * 0.5f), -1.2f, 1.2f);
            float stanceAvg = stanceCount > 0 ? stanceSum / stanceCount : 0f;
            float bob = g.bob * (stanceAvg - 0.5f) * 2f * speedFactor;
            float swayTarget = g.sway * (stanceCount > 0 ? swaySum / stanceCount : 0f) * Mathf.Clamp01(speed / 0.5f);
            // Anticipatory weight shift before a biped's first step: move over the stance foot.
            if (m_InitLeg != null && m_Legs.Count == 2) swayTarget = -m_InitLeg.side * Mathf.Max(0.035f, g.sway * 1.5f);
            m_Sway = Mathf.SmoothDamp(m_Sway, swayTarget, ref m_SwayVel, 0.12f);
            // Fore/aft rocking locked to the footfall phase: nose down while the forelimbs are loaded, up while
            // the hind limbs push. Amplitude rises with speed within the gait (Thoroughbred gallop: trunk pitch
            // range 11 deg rising to 19 deg peak-to-peak, i.e. ~5.6 to ~9.5 deg either side).
            float rock = 0f;
            if (frontN > 0 && hindN > 0 && g.pitchRock > 0f && m_Motor.grounded)
            {
                Vector2 sum = Vector2.zero;
                foreach (var leg in m_Legs)
                {
                    if (leg.restRootLocal.z <= m_LegCentroidZ) continue;
                    float mid = GaitController.Offset(g.pattern, leg.pairIndex, m_PairCount, leg.side) + leg.def.phaseBias + (1f - m_Gait.Duty) + m_Gait.Duty * 0.5f;
                    sum += new Vector2(Mathf.Cos(mid * Mathf.PI * 2f), Mathf.Sin(mid * Mathf.PI * 2f));
                }
                float foreLoaded = Mathf.Atan2(sum.y, sum.x) / (Mathf.PI * 2f);
                float withinGait = Mathf.InverseLerp(g.minSpeed, loco.maxSpeed, speed);
                float amplitude = g.pitchRock * Mathf.Lerp(1f, 1.7f, withinGait) * Mathf.Clamp01(speed / Mathf.Max(0.5f, g.minSpeed + 0.5f));
                rock = amplitude * Mathf.Cos((m_Gait.phase - foreLoaded) * Mathf.PI * 2f);
            }

            // Lean into acceleration and bank into turns.
            Vector3 accel = (m_Motor.velocity - m_PrevVelocity) / Mathf.Max(dt, 1e-4f);
            m_SmoothAccel = Vector3.Lerp(m_SmoothAccel, accel, 1f - Mathf.Exp(-6f * dt));
            float accelPitch = Mathf.Clamp(Vector3.Dot(m_SmoothAccel, fwd) * 0.8f, -6f, 6f);
            // Lean into turns like a real runner: tan(lean) = sideways acceleration / g (the same balance that puts the
            // feet out wide, see TurnPlacement), slightly under-leaned. Measured from the path of the body, not from
            // how fast the hips swing round.
            m_TurnAccel = Vector3.Lerp(m_TurnAccel, LateralAcceleration(up), 1f - Mathf.Exp(-dt / 0.35f));
            float centripetal = Vector3.Dot(TurnAcceleration(), Vector3.Cross(up, fwd));
            float bank = -Mathf.Clamp(Mathf.Atan2(centripetal, 9.81f) * Mathf.Rad2Deg * 0.8f, -28f, 28f);
            if (loco.canClimb) bank *= 0.3f;

            // In flight the feet are tucked, not standing on anything: the body keeps its own pose.
            if (!m_Motor.grounded) { hm = 0f; pitchT = 0f; rollT = 0f; }
            float crouch = g.crouch * speedFactor;
            float heightTarget = loco.bodyHeight * (1f - crouch) + hm * 0.7f + bob - m_Drop;
            if (g.swing != SwingStyle.Arc && m_Motor.grounded)
            {
                // The body rides on its stance legs: each supporting leg holds the hip where its knee has the measured
                // stance flexion for this point of the stance (walking: ~5 deg at heel strike, ~18 deg loading
                // response, straightening by mid-stance; running: ~20 deg at contact, ~45 deg absorbing at
                // mid-stance). Vaulting over a straight walking leg lifts the body at mid-stance and lowers it in
                // double support; a running leg compresses like a spring. The trailing foot rolling off its toe
                // follows the body instead of holding it up.
                bool run = g.duty < 0.5f;
                float[] curve = run ? s_RunStanceKnee : s_WalkStanceKnee;
                bool moving = speed > 0.15f;
                Vector3 bodyPos = m_Body.position;
                float sum = 0f, count = 0f, aheadSum = 0f, aheadCount = 0f, aheadMax = 0f, ceiling = float.MaxValue;
                Vector3 travel = TravelVelocity(up);
                foreach (var leg in m_Legs)
                {
                    if (leg.swinging && moving && leg.grounded)
                    {
                        // Anticipation: from about a third of the way through its swing a leg shares in holding the body,
                        // from where the hip will be when it lands on its foothold with the measured contact knee
                        // angle. The body settles toward the next foothold (down a descent, up a climb) before the foot
                        // arrives, instead of vaulting over the old one until the new leg has to reach for the ground.
                        float ws = Mathf.SmoothStep(0f, 1f, (leg.swingT - 0.3f) / 0.7f);
                        if (ws <= 1e-3f) continue;
                        float remaining = (1f - leg.swingT) * leg.swingDuration;
                        float slopeC = Mathf.Clamp(GaitSlope, -20f, 20f);
                        float kneeC = LimbRig.Sample(curve, 0f) + (slopeC > 0f ? 1.2f * slopeC : 0f);
                        if (leg.def.foot == FootType.Hoof) kneeC *= 0.4f;
                        float chordC = leg.reach * Mathf.Min(Mathf.Cos(kneeC * 0.5f * Mathf.Deg2Rad), 0.997f);
                        Vector2 off = LimbRig.EffectorOffset(leg.def.foot, leg.def.footLength, leg.def.footHeight, 0f);
                        Vector3 effC = leg.target + fwd * off.x + up * off.y;
                        Vector3 shift = travel * remaining;
                        Vector3 hipC = leg.Hip + shift;
                        float horizC = Vector3.ProjectOnPlane(hipC - effC, up).magnitude;
                        // A foothold (momentarily) out of reach sideways or fore/aft cannot say where the hip goes.
                        ws *= SupportShare(horizC / chordC);
                        if (ws <= 1e-3f) continue;
                        // Height above the root as it will be then (the root moves along the ground meanwhile).
                        float hipHC = Vector3.Dot(effC - (rootPos + shift), up) + Mathf.Sqrt(Mathf.Max(0f, chordC * chordC - horizC * horizC));
                        aheadSum += (hipHC - Vector3.Dot(leg.Hip - bodyPos, up)) * ws;
                        aheadCount += ws;
                        aheadMax = Mathf.Max(aheadMax, ws);
                        continue;
                    }
                    if (leg.swinging || !leg.grounded) continue;
                    float ts = moving ? Mathf.Clamp01(leg.stanceTime / Mathf.Max(0.05f, stanceDur)) : 0.7f;
                    // Weight is handed over smoothly: a new foot takes load over the first ~15% of its stance (the
                    // loading response) while the trailing foot unloads as it rolls onto its toe.
                    float w = Mathf.SmoothStep(0f, 1f, ts / 0.15f) * (1f - Mathf.SmoothStep(0f, 1f, (ts - 0.7f) / 0.2f));
                    if (w <= 1e-3f) continue;
                    // Hoofed limbs flex less at the elbow / stifle; their give is at the fetlock (see SolveLegs).
                    float knee = LimbRig.Sample(curve, ts);
                    // Slopes: uphill the knee meets the ground more flexed and extends through stance to push the body
                    // up; downhill it keeps bending through stance (~1 to 2 deg per degree of slope), lowering the body
                    // onto the next, lower foothold instead of vaulting over a straight leg.
                    float slope = moving ? Mathf.Clamp(GaitSlope, -20f, 20f) : 0f;
                    knee += slope > 0f ? 1.2f * slope * (1f - ts) : -slope * (1f + ts) * Mathf.SmoothStep(0f, 1f, ts / 0.3f);
                    if (leg.def.foot == FootType.Hoof) knee *= 0.4f;
                    // Never ask for a dead-straight leg (the toe roll would have to make up the difference).
                    float chord = leg.reach * Mathf.Min(Mathf.Cos(knee * 0.5f * Mathf.Deg2Rad), 0.997f);
                    // Hold the body from the foot's own posture, not from the toe roll the reach solve adds: a raised
                    // heel lifts the ankle, which would raise the body, which would need more heel rise (the body
                    // would climb onto its toes). Walking feet use the planned push-off heel rise (from how far the foot
                    // trails the hip, which is what lifts the body off a trailing leg); running feet keep their
                    // forefoot landing; hooves keep the fetlock sink but not the breakover.
                    float bodyPitch = leg.def.foot == FootType.Plantigrade
                        ? (run ? Mathf.Min(leg.footPitch, Mathf.Max(leg.landPitch, 0f)) : Mathf.Max(0f, leg.plannedPitch))
                        : Mathf.Min(leg.footPitch, 0f);
                    Vector3 eff = leg.FootEffector(fwd, bodyPitch, false);
                    Vector3 hip = leg.Hip;
                    float horiz = Vector3.ProjectOnPlane(hip - eff, up).magnitude;
                    // The body is held up by the legs under it. A planted foot left far out from the hip (mid-turn or
                    // reversing, about to be picked up) hands its share to the others instead of dragging the body
                    // down toward the ground.
                    w *= SupportShare(horiz / chord);
                    if (w <= 1e-3f) continue;
                    float hipH = Vector3.Dot(eff - rootPos, up) + Mathf.Sqrt(Mathf.Max(0f, chord * chord - horiz * horiz));
                    // Running legs behave like springs: the centre of mass sinks ~5.5% of leg length into mid-stance at
                    // jogging speeds, less when sprinting (stiffer legs, shorter contact). Most of that give is in the
                    // ankle and foot, which a hip-to-ankle chord does not capture, so it is added here.
                    if (run && leg.def.foot == FootType.Plantigrade)
                        hipH -= (leg.reach + leg.def.footHeight) * Mathf.Lerp(0.055f, 0.035f, Mathf.InverseLerp(3f, 7f, speed)) * Mathf.Sin(ts * Mathf.PI);
                    sum += (hipH - Vector3.Dot(hip - bodyPos, up)) * w;
                    count += w;
                    // Ceiling: no loaded leg may be stretched past its reach (legs disagree on slopes and uneven ground).
                    float full = leg.reach * 0.997f;
                    if (w > 0.5f && horiz < full * 0.6f)
                    {
                        float top = Vector3.Dot(eff - rootPos, up) + Mathf.Sqrt(Mathf.Max(0f, full * full - horiz * horiz));
                        ceiling = Mathf.Min(ceiling, top - Vector3.Dot(hip - bodyPos, up));
                    }
                }
                // Flight (running): no leg holds the body; it carries on from the last support height.
                // The stance leg yields (bends under control) as the next foot approaches, handing the body over to
                // the coming foothold: lowering it onto a lower step, raising it for a higher one.
                if (aheadCount > 0.05f && count > 0.05f) m_StanceHeight = Mathf.Lerp(sum / count, aheadSum / aheadCount, 0.8f * aheadMax);
                else if (aheadCount > 0.05f) m_StanceHeight = aheadSum / aheadCount;
                else if (count > 0.05f) m_StanceHeight = sum / count;
                else if (m_StanceHeight <= 0f) m_StanceHeight = heightTarget;
                m_StanceHeight = Mathf.Min(m_StanceHeight, ceiling);
                // Safety floor: whatever the legs report, the body never sinks below about half its standing height.
                m_StanceHeight = Mathf.Max(m_StanceHeight, loco.bodyHeight * 0.55f);
                heightTarget = m_StanceHeight - m_Drop;
            }
            else m_StanceHeight = 0f;
            m_Height = Mathf.SmoothDamp(m_Height, heightTarget, ref m_HeightVel, g.swing != SwingStyle.Arc ? 0.035f : 0.07f);

            float k = 1f - Mathf.Exp(-10f * dt);
            m_Pitch = Mathf.Lerp(m_Pitch, pitchT, k);
            m_Roll = Mathf.Lerp(m_Roll, rollT, k);
            bool hasFront = m_Front != m_Body;
            float gaitLean = g.lean * Mathf.Clamp01(speed / Mathf.Max(1f, loco.maxSpeed * 0.7f));
            // Upright walkers lean the trunk into a climb and back on a descent (Leroux 2002; MacDonald 2022).
            if (m_Legs.Count == 2) gaitLean += m_Slope > 0f ? Mathf.Min(10f, 0.6f * m_Slope) : Mathf.Max(-8f, 0.4f * m_Slope);
            // Crouching to jump folds the trunk forward over the hips (~35 deg at the bottom of a deep crouch).
            if (m_Legs.Count == 2) gaitLean += 35f * m_JumpDepth / Mathf.Max(0.05f, m_Gait.hipHeight * 0.38f);
            float rootPitch = m_Pitch + (hasFront ? 0f : accelPitch);

            // Biped pelvis: yaw with the stride, dip toward the swing side.
            float pelvisYaw = -m_StrideSignal * loco.spineTwist * 0.5f * speedFactor;
            float pelvisRoll = loco.spineTwist > 0f ? -m_Sway * 60f : 0f;

            Quaternion target = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(rootPitch, pelvisYaw, m_Roll + bank + pelvisRoll);
            m_BodyRot = Quaternion.Slerp(m_BodyRot, target, 1f - Mathf.Exp(-14f * dt));
            // The gait rock is already a smooth oscillation: apply it after smoothing so it keeps its full amplitude.
            m_Rock = Mathf.Lerp(m_Rock, rock, 1f - Mathf.Exp(-40f * dt));
            // The jump crouch is applied after smoothing so the dip and the push keep their timing.
            m_Body.SetPositionAndRotation(rootPos + up * (m_Height - m_JumpDepth) + right * m_Sway, m_BodyRot * Quaternion.Euler(m_Rock, 0f, 0f));

            if (hasFront)
            {
                float chestYaw = m_StrideSignal * loco.spineTwist * speedFactor;
                float breathe = Mathf.Sin(m_Time * 1.6f) * 1.2f * (1f - speedFactor);
                m_Front.localRotation = Quaternion.Slerp(m_Front.localRotation,
                    Quaternion.Euler(gaitLean + accelPitch + breathe, chestYaw, 0f), 1f - Mathf.Exp(-10f * dt));
            }

            // Keep stance legs within reach: lower the body if a planted leg would over-extend.
            float residual = -1f;
            foreach (var leg in m_Legs)
            {
                if (leg.swinging || !leg.grounded) continue;
                // Trailing feet about to lift off may straighten fully; do not sink the body for them.
                if (stanceDur > 0f && leg.stanceTime > stanceDur * 0.85f) continue;
                Vector3 hip = leg.Hip;
                Vector3 eff = leg.foot + leg.footNormal * leg.def.footHeight;
                Vector3 d = hip - eff;
                float v = Vector3.Dot(d, up);
                float horiz = Vector3.ProjectOnPlane(d, up).magnitude;
                float maxLen = leg.reach * 0.985f;
                float maxV = Mathf.Sqrt(Mathf.Max(0f, maxLen * maxLen - horiz * horiz));
                residual = Mathf.Max(residual, v - maxV);
            }
            // Bodies riding their stance legs (above) already keep them in reach; the dip is for the other gaits.
            if (g.swing != SwingStyle.Arc) residual = -1f;
            float dropTarget = Mathf.Clamp(m_Drop + residual, 0f, loco.bodyHeight * 0.08f);
            m_Drop = Mathf.MoveTowards(m_Drop, dropTarget, (dropTarget > m_Drop ? 1.2f : 1.0f) * dt);
        }

        // ------------------------------------------------------------------ Arms, head, skin

        void UpdateArms(float dt, float speed)
        {
            if (m_Arms.Count == 0) return;
            var loco = def.locomotion;
            Vector3 up = m_Body.up;
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(0.3f, loco.cruiseSpeed));
            float runT = Mathf.Clamp01((speed - loco.cruiseSpeed * 1.4f) / Mathf.Max(0.5f, loco.maxSpeed * 0.5f));
            foreach (var arm in m_Arms)
            {
                Vector3 rest = arm.section.TransformPoint(ToSectionLocal(arm.def.section, arm.restRootLocal));
                float swing = loco.armSwing * -arm.side * m_StrideSignal * Mathf.Lerp(0.6f, 1.25f, runT) * speedFactor;
                Vector3 t = rest + fwd * swing
                            + up * (runT * 0.1f + Mathf.Max(0f, swing) * 0.3f)
                            - right * (arm.side * runT * 0.05f)
                            + up * (Mathf.Sin(m_Time * 1.6f + arm.side) * 0.006f);
                if (loco.armSwing <= 0f)
                    t += (fwd * Mathf.Sin(m_Time * 2.3f + arm.side * 1.3f) + up * Mathf.Cos(m_Time * 1.7f + arm.side)) * (0.015f + speedFactor * 0.02f);
                arm.Solve(t, up);
                if (arm.footVisual != null)
                {
                    int last = arm.chain.joints.Length - 1;
                    Vector3 dir = arm.chain.joints[last] - arm.chain.joints[last - 1];
                    arm.footVisual.SetPositionAndRotation(arm.chain.joints[last] + dir.normalized * arm.def.footLength * 0.35f,
                        Quaternion.LookRotation(dir, fwd));
                }
            }
        }

        void UpdateHead(float dt, float speed)
        {
            if (!def.head.enabled) return;
            var h = def.head;
            bool hasFront = m_Front != m_Body;
            Transform sec = hasFront ? m_Front : m_Body;
            Vector3 neckBase = sec.TransformPoint(hasFront ? h.neckBase - def.frontPivot : h.neckBase);
            Quaternion secRot = sec.rotation;

            Vector3 look = lookDirection.sqrMagnitude > 0.01f ? lookDirection.normalized : transform.forward;
            Vector3 localLook = Quaternion.Inverse(secRot) * look;
            float yaw = Mathf.Clamp(Mathf.Atan2(localLook.x, localLook.z) * Mathf.Rad2Deg, -75f, 75f) * h.lookAmount;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(localLook.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 35f) * h.lookAmount;
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(0.3f, def.locomotion.cruiseSpeed));
            float phase = m_Gait.phase * Mathf.PI * 2f;
            float bobPitch = h.bobAmount * 120f * Mathf.Sin(phase * 2f) * speedFactor;
            // Counter-rotate against body pitch so the gaze stays level (head stabilisation).
            Quaternion stabilise = Quaternion.Euler(-m_Pitch * 0.6f, 0, -(m_Roll) * 0.6f);
            Quaternion headTarget = secRot * stabilise * Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(h.restPitch + bobPitch, 0f, 0f);
            m_HeadRot = Quaternion.Slerp(m_HeadRot, headTarget, 1f - Mathf.Exp(-9f * dt));

            Quaternion neckTurn = secRot * Quaternion.Euler(pitch * 0.3f + bobPitch * 0.25f, yaw * 0.45f, 0f);
            Vector3 headPos = neckBase + neckTurn * h.headOffset + sec.up * (h.bobAmount * Mathf.Sin(phase * 2f + 0.6f) * speedFactor);
            m_HeadT.SetPositionAndRotation(headPos, m_HeadRot);

            // Neck: base -> arched middle -> just behind the skull.
            Vector3 neckVec = headPos - neckBase;
            float neckLen = neckVec.magnitude;
            Vector3 headBack = headPos - (m_HeadRot * Vector3.forward) * (h.headRadii.z * 0.3f);
            m_NeckPts[0] = neckBase - neckVec.normalized * Mathf.Min(h.neckRadius, neckLen * 0.2f);
            m_NeckPts[1] = neckBase + neckVec * 0.35f + sec.up * (neckLen * 0.05f) + sec.forward * (neckLen * 0.04f);
            m_NeckPts[2] = neckBase + neckVec * 0.72f + sec.up * (neckLen * 0.04f);
            m_NeckPts[3] = headBack;
            m_NeckRadii[0] = h.neckRadius;
            m_NeckRadii[1] = Mathf.Lerp(h.neckRadius, h.neckTipRadius, 0.35f);
            m_NeckRadii[2] = Mathf.Lerp(h.neckRadius, h.neckTipRadius, 0.75f);
            m_NeckRadii[3] = h.neckTipRadius;
            m_NeckTube.Build(m_NeckPts, m_NeckRadii, null, sec.right);

            if (m_ManeTube != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 tangent = i < 3 ? (m_NeckPts[i + 1] - m_NeckPts[i]).normalized : (m_NeckPts[3] - m_NeckPts[2]).normalized;
                    Vector3 crest = Vector3.ProjectOnPlane(sec.up - sec.forward * 0.4f, tangent).normalized;
                    m_ManePts[i] = m_NeckPts[i] + crest * (m_NeckRadii[i] * 0.85f);
                    m_ManeRadii[i] = m_NeckRadii[i] * (i == 0 ? 0.25f : 0.38f);
                    m_ManeAspects[i] = new Vector2(1.6f, 0.45f);
                }
                m_ManePts[0] += sec.forward * -0.02f;
                m_ManeTube.Build(m_ManePts, m_ManeRadii, m_ManeAspects, sec.right);
            }
        }

        void UpdateBodySkin()
        {
            if (m_BodyTube == null) return;
            for (int i = 0; i < m_BodyPoints.Length; i++)
                m_BodyPoints[i] = (m_BodyOnFront[i] ? m_Front : m_Body).TransformPoint(m_BodyLocal[i]);
            m_BodyTube.Build(m_BodyPoints, m_BodyRadii, m_BodyAspects, m_BodyVertical ? m_Body.forward : m_Body.up);
        }
    }
}
