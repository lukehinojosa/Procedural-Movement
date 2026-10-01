using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Which rigid part of the creature a piece is attached to.</summary>
    public enum Section { Root, Front, Head }

    public enum LimbRole { Leg, Arm }

    public enum FootType { Point, Plantigrade, Hoof }

    /// <summary>
    /// How a swinging foot travels. Arc: a lift arc of <see cref="GaitDef.stepHeight"/>. Walk / Run: the limb follows
    /// measured sagittal hip and knee angle curves for walking or running, so foot height and timing come from data.
    /// </summary>
    public enum SwingStyle { Arc, Walk, Run }

    public enum ShapeKind { Ellipsoid, Capsule }

    /// <summary>Foot-fall pattern. The gait controller turns this into a phase offset per leg.</summary>
    public enum GaitPattern
    {
        Alternating, // tripod / tetrapod: neighbouring legs in antiphase (insects, spiders)
        Wave,        // metachronal wave running front to back (centipedes, slow insects)
        Biped,       // left / right in antiphase
        QuadWalk,    // four beat lateral sequence
        QuadTrot,    // diagonal pairs
        QuadGallop,  // transverse gallop
        QuadBound,   // front pair, then hind pair
    }

    public enum SkinPattern { Plain, Bands, Organic }

    [Serializable]
    public class BodyNode
    {
        public Vector3 position;
        public float radius = 0.2f;
        public Vector2 aspect = Vector2.one; // x = width, y = height scale of the cross section

        public BodyNode() { }
        public BodyNode(Vector3 p, float r, float w = 1f, float h = 1f) { position = p; radius = r; aspect = new Vector2(w, h); }
    }

    [Serializable]
    public class HeadDef
    {
        public bool enabled = true;
        public Vector3 neckBase = new Vector3(0, 0.3f, 0.3f);   // root-local rest position
        public Vector3 headOffset = new Vector3(0, 0.3f, 0.2f); // from neck base to head centre, root-local
        public float neckRadius = 0.1f;
        public float neckTipRadius = 0.08f;
        public Vector3 headRadii = new Vector3(0.1f, 0.1f, 0.12f);
        public float snoutLength = 0f;
        public float snoutRadius = 0.06f;
        public float snoutTipRadius = 0.05f;
        public float restPitch = 0f;   // degrees, positive = nose down
        public float lookAmount = 0.6f; // how strongly the head turns toward the look direction
        public float bobAmount = 0f;    // head / neck bob driven by the gait
        public int color = 0;
        public bool mane = false;
        public int maneColor = 3;
    }

    [Serializable]
    public class TailDef
    {
        public bool enabled = false;
        public Section section = Section.Root;
        public Vector3 attach = new Vector3(0, 0, -0.5f);
        public Vector3 direction = new Vector3(0, -0.3f, -1f); // rest direction, root-local
        public int segments = 5;
        public float length = 0.8f;
        public float[] radii = { 0.06f, 0.05f, 0.04f, 0.03f, 0.02f, 0.01f };
        public Vector2 aspect = Vector2.one;
        public float stiffness = 0.3f; // 0 = floppy rope, 1 = rigid
        public float damping = 0.9f;
        public float gravity = 0.5f;
        public float wag = 0f;
        public int color = 0;
        public SkinPattern pattern = SkinPattern.Plain;
    }

    [Serializable]
    public class LimbDef
    {
        public string name = "Leg";
        public LimbRole role = LimbRole.Leg;
        public Section section = Section.Root;
        public bool mirror = true;             // builds a left copy with X negated
        public Vector3 attach = new Vector3(0.2f, 0, 0); // root-local, right side
        public float[] bones = { 0.4f, 0.4f };
        public float[] radii = { 0.05f, 0.04f, 0.03f };
        public Vector3[] hints = { new Vector3(0, 0, 1) }; // bend direction per interior joint, root-local (right side)
        public Vector3 rest = new Vector3(0.25f, 0, 0);  // legs: foot rest (y ignored, on ground). arms: hand rest.
        public FootType foot = FootType.Point;
        public float footLength = 0.2f;
        public float footHeight = 0.05f;
        public int color = 0;
        public int[] boneColors;   // optional per-bone palette index
        public int footColor = 3;
        public float phaseBias = 0f; // extra phase offset added to the generated gait pattern
        public float girdleSwing = 0f; // how far the limb root (e.g. a horse's shoulder on the scapula) slides fore/aft with the foot (m)
    }

    [Serializable]
    public class ShapeDef
    {
        public ShapeKind kind = ShapeKind.Ellipsoid;
        public Section attach = Section.Root;
        public bool mirror = false;
        public Vector3 position;
        public Vector3 euler;
        public Vector3 size = Vector3.one * 0.1f; // ellipsoid radii, or capsule (r0, r1, length)
        public int color = 0;
        public float smoothness = -1f;

        public ShapeDef() { }
        public ShapeDef(ShapeKind k, Section s, Vector3 pos, Vector3 size, int color, bool mirror = false, Vector3 euler = default, float smoothness = -1f)
        {
            kind = k; attach = s; position = pos; this.size = size; this.color = color; this.mirror = mirror; this.euler = euler; this.smoothness = smoothness;
        }
    }

    [Serializable]
    public class GaitDef
    {
        public string name = "Walk";
        public GaitPattern pattern = GaitPattern.Alternating;
        public float minSpeed = 0f;       // this gait is used from this speed upward
        public float duty = 0.6f;         // fraction of the cycle each foot is on the ground
        public float strideMin = 0.5f;    // cycle length (m) at low speed
        public float strideMax = 1.2f;    // cycle length cap; beyond it cadence rises instead
        public float minFrequency = 1f;   // cycles per second floor while moving (when froudeStride is 0)
        // Dynamic similarity: stride / hipHeight = froudeStride * Fr^froudeStrideExp, with Fr = v^2 / (g * hipHeight),
        // so stride and cadence scale with leg length. 0 = stride from minFrequency instead.
        public float froudeStride = 0f;
        public float froudeStrideExp = 0.3f;
        // Duty factor = froudeDuty * Fr^froudeDutyExp (feet spend less of the cycle on the ground as speed rises). 0 = fixed duty.
        public float froudeDuty = 0f;
        public float froudeDutyExp = 0f;
        public float stepHeight = 0.2f;   // Arc swings (and emergency steps): lift at mid-swing (m)
        public SwingStyle swing = SwingStyle.Arc;
        public float clearance = 0f;      // data swings: minimum mid-swing foot clearance as a fraction of leg length
        public float curl = 0f;           // how far the foot kicks back and up early in the swing (runs, trots)
        public float kneeFlex = 0f;       // peak knee flexion in swing (deg) at this gait's entry speed; 0 = off
        public float kneeFlexFast = -1f;  // peak knee flexion at top speed (deg); -1 = same as kneeFlex
        public float bob = 0.02f;        // + = high at mid-stance (walk), - = low at mid-stance (run)
        public float pitchRock = 0f;      // degrees of fore/aft rocking (gallop)
        public float sway = 0f;           // lateral shift over the stance feet
        public float lean = 0f;           // forward lean in degrees at full speed of this gait
        public float crouch = 0f;         // body lowering fraction at this gait

        public GaitDef Clone() => (GaitDef)MemberwiseClone();
    }

    [Serializable]
    public class LocomotionDef
    {
        public float bodyHeight = 0.5f;
        public float minSpeed = 0.3f;
        public float cruiseSpeed = 1.5f;
        public float maxSpeed = 5f;
        public float acceleration = 8f;
        public float turnSpeed = 360f;      // degrees per second at low speed
        public float turnSpeedAtMax = 180f;
        public bool canClimb = false;
        public float surfaceAlign = 0f;     // non-climbers: how much the body tilts to the ground normal
        public float maxSlope = 50f;
        public float maxStepUp = 0.35f;
        public float collisionRadius = 0.3f;
        public float bodyHalfLength = 0f;
        public float tiltFromFeet = 0.5f;   // body pitch / roll follows the foot plane
        public float spineTwist = 0f;       // chest counter-rotation (degrees)
        public float armSwing = 0f;
        public float jumpSpeed = 5f;
        public float settleTolerance = 0.12f;
        public float startDelay = 0.1f;     // gait initiation: weight shift before the first step (s)

        [Header("Heel-toe feet (foot angle to the ground; + = toe down / heel up, - = toe up)")]
        public float footStrikeWalk = -20f;   // walking heel strike
        public float footStrikeRun = 6f;      // jogging: midfoot / forefoot touch, then the heel settles
        public float footStrikeSprint = 15f;  // sprinting: forefoot strike
        public float heelSprint = 8f;         // at a sprint the heel stays this far up through stance
        public float toeRollWalk = 30f;       // heel rise before toe-off
        public float toeRollRun = 42f;
        public float toeRollSprint = 55f;
        public List<GaitDef> gaits = new List<GaitDef>();
    }

    /// <summary>Complete, JSON-serialisable description of a creature. The builder turns this into a rig.</summary>
    [Serializable]
    public class CreatureDefinition
    {
        public string name = "Creature";
        public Color[] palette =
        {
            new Color(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.3f, 0.3f), new Color(0.8f, 0.6f, 0.4f),
            new Color(0.1f, 0.1f, 0.1f), new Color(0.03f, 0.03f, 0.03f),
        };
        public float smoothness = 0.35f;
        public SkinPattern bodyPattern = SkinPattern.Organic;
        public int bodyColor = 0;
        public List<BodyNode> body = new List<BodyNode>();
        public int frontSectionStart = -1; // body node index where the front section begins (-1 = rigid body)
        public Vector3 frontPivot = Vector3.zero;
        public HeadDef head = new HeadDef();
        public TailDef tail = new TailDef();
        public List<LimbDef> limbs = new List<LimbDef>();
        public List<ShapeDef> shapes = new List<ShapeDef>();
        public LocomotionDef locomotion = new LocomotionDef();

        public string ToJson() => JsonUtility.ToJson(this, true);
        public static CreatureDefinition FromJson(string json) => JsonUtility.FromJson<CreatureDefinition>(json);
        public CreatureDefinition Clone() => FromJson(ToJson());

        public Color GetColor(int index) => palette != null && palette.Length > 0 ? palette[Mathf.Clamp(index, 0, palette.Length - 1)] : Color.gray;

        /// <summary>Largest vertical reach of any leg, used to keep body height sane in the creator.</summary>
        public float MaxLegReach()
        {
            float best = 0f;
            foreach (var l in limbs)
            {
                if (l.role != LimbRole.Leg) continue;
                float sum = l.footHeight;
                foreach (var b in l.bones) sum += b;
                best = Mathf.Max(best, sum);
            }
            return best;
        }
    }
}
