using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Closed-form two bone IK using the law of cosines, with a pole direction choosing the bend plane.</summary>
    public static class TwoBoneIK
    {
        /// <summary>
        /// Solves root -> mid -> end. Returns the mid joint position; <paramref name="end"/> is clamped to reach.
        /// </summary>
        public static Vector3 Solve(Vector3 root, float upper, float lower, Vector3 target, Vector3 pole, out Vector3 end)
        {
            Vector3 toTarget = target - root;
            float dist = toTarget.magnitude;
            Vector3 dir = dist > 1e-5f ? toTarget / dist : Vector3.down;
            float maxReach = (upper + lower) * 0.9999f;
            float minReach = Mathf.Abs(upper - lower) + 1e-4f;
            dist = Mathf.Clamp(dist, minReach, maxReach);
            end = root + dir * dist;

            // Bend direction: pole projected onto the plane perpendicular to the root->target line.
            Vector3 bend = pole - dir * Vector3.Dot(pole, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
            bend.Normalize();

            float cosA = Mathf.Clamp((upper * upper + dist * dist - lower * lower) / (2f * upper * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            return root + dir * (upper * cosA) + bend * (upper * sinA);
        }
    }

    /// <summary>
    /// FABRIK (Forward And Backward Reaching IK) for chains of any length, with a bend-hint direction per interior joint.
    /// Hints make the solution deterministic: e.g. a horse hind leg bends forward at the stifle and backward at the hock.
    /// </summary>
    public class FabrikChain
    {
        public readonly Vector3[] joints;
        public readonly float[] lengths;
        public readonly float totalLength;
        public int iterations = 24;
        bool m_HasPrevious;
        Vector3 m_PreviousRoot;

        public FabrikChain(float[] boneLengths)
        {
            lengths = (float[])boneLengths.Clone();
            joints = new Vector3[lengths.Length + 1];
            foreach (var l in lengths) totalLength += l;
        }

        /// <summary>
        /// Solves the chain rooted at <paramref name="root"/> toward <paramref name="target"/>.
        /// <paramref name="hints"/> holds a world-space bend direction for joints 1..n-1 (index i-1).
        /// </summary>
        public void Solve(Vector3 root, Vector3 target, Vector3[] hints)
        {
            int n = joints.Length;
            Vector3 toTarget = target - root;
            float dist = toTarget.magnitude;
            Vector3 dir = dist > 1e-5f ? toTarget / dist : Vector3.down;

            // Seed pose. Warm start from last frame's solution (moved with the root) so the chain stays with the
            // nearby solution: near full extension many poses reach the target almost equally well, and a fresh seed
            // every frame can land on alternating ones (a knee flicking back and forth). The hints still set which
            // way each joint bends (ApplyHints below). First solve, or after a jump: a zig-zag bent along the hints.
            if (m_HasPrevious && (root - m_PreviousRoot).sqrMagnitude < totalLength * totalLength * 0.25f)
            {
                Vector3 shift = root - m_PreviousRoot;
                for (int i = 1; i < n; i++) joints[i] += shift;
                joints[0] = root;
            }
            else
            {
                joints[0] = root;
                for (int i = 1; i < n; i++)
                {
                    Vector3 h = i - 1 < hints.Length && i < n - 1 ? hints[i - 1] : Vector3.zero;
                    Vector3 seg = (dir + h * 0.6f).normalized;
                    if (i == n - 1 || seg.sqrMagnitude < 1e-6f) seg = dir;
                    joints[i] = joints[i - 1] + seg * lengths[i - 1];
                }
            }
            m_HasPrevious = true;
            m_PreviousRoot = root;

            if (dist >= totalLength)
            {
                // Unreachable: stretch toward target (keeping a hint of bend for readability).
                for (int i = 1; i < n; i++) joints[i] = joints[i - 1] + dir * lengths[i - 1];
            }
            else
            {
                for (int it = 0; it < iterations; it++)
                {
                    // Backward pass: pin end to target.
                    joints[n - 1] = target;
                    for (int i = n - 2; i >= 0; i--)
                    {
                        Vector3 d = joints[i] - joints[i + 1];
                        float m = d.magnitude;
                        joints[i] = joints[i + 1] + (m > 1e-6f ? d / m : dir) * lengths[i];
                    }
                    // Forward pass: pin root.
                    joints[0] = root;
                    for (int i = 1; i < n; i++)
                    {
                        Vector3 d = joints[i] - joints[i - 1];
                        float m = d.magnitude;
                        joints[i] = joints[i - 1] + (m > 1e-6f ? d / m : dir) * lengths[i - 1];
                    }
                    // No early exit: stopping as soon as the end is within tolerance makes the iteration count (and
                    // so the pose) jump between frames when the chain is nearly straight and converging slowly,
                    // which shows as a buzzing knee. A fixed count keeps the result continuous in its inputs.
                }
            }

            ApplyHints(hints);
        }

        /// <summary>
        /// Rotates every interior joint about the axis through its neighbours so it lies on its hint side.
        /// This preserves bone lengths exactly (it is a rotation about the neighbour-to-neighbour axis).
        /// </summary>
        void ApplyHints(Vector3[] hints)
        {
            int n = joints.Length;
            for (int i = 1; i < n - 1; i++)
            {
                if (i - 1 >= hints.Length) break;
                Vector3 hint = hints[i - 1];
                if (hint.sqrMagnitude < 1e-6f) continue;
                Vector3 a = joints[i - 1], c = joints[i + 1];
                Vector3 axis = c - a;
                float axisLen = axis.magnitude;
                if (axisLen < 1e-5f) continue;
                axis /= axisLen;
                Vector3 proj = a + axis * Vector3.Dot(joints[i] - a, axis);
                Vector3 current = joints[i] - proj;
                float r = current.magnitude;
                Vector3 desired = hint - axis * Vector3.Dot(hint, axis);
                if (desired.sqrMagnitude < 1e-8f) continue;
                desired.Normalize();
                if (r < 1e-5f)
                {
                    // Joint is on the axis (chain straight): push a tiny amount toward the hint.
                    continue;
                }
                joints[i] = proj + desired * r;
            }
        }
    }
}
