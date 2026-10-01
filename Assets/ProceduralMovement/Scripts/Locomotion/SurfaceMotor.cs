using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// Kinematic character motor that moves a creature over arbitrary geometry.
    /// Walkers stay upright, step up curbs and stairs, slide along walls and fall off ledges.
    /// Climbers stick to any surface: they wrap over concave corners (floor to wall), convex edges
    /// (roof to facade) and can walk upside down on ceilings, by fitting a surface plane from a fan of probes.
    /// </summary>
    public class SurfaceMotor
    {
        // Configuration.
        public float radius = 0.3f;
        public float halfLength = 0f;
        public float stepHeight = 0.35f;
        public float maxSlope = 50f;
        public float gravity = 22f;
        public float acceleration = 8f;
        public float deceleration = 12f;
        public float turnSpeed = 360f;
        public float turnSpeedAtMax = 180f;
        public float maxSpeed = 5f;
        public float alignSpeed = 10f;
        public bool canClimb;
        public float surfaceAlign;
        public int mask = ~0;
        const float Skin = 0.02f;

        // State.
        public Vector3 position;
        public Vector3 up = Vector3.up;
        public Vector3 forward = Vector3.forward;
        public Vector3 velocity;
        public bool grounded = true;
        public Vector3 groundNormal = Vector3.up;
        public float yawRate;       // degrees per second about up
        public float airTime;
        public bool justLanded;
        public Vector3 lastLandingVelocity;

        public Quaternion Rotation => Quaternion.LookRotation(forward, up);
        public float PlanarSpeed => Vector3.ProjectOnPlane(velocity, MoveUp).magnitude;

        /// <summary>
        /// The plane the body moves in: the surface for climbers; the horizontal for walkers, whose body may tilt with
        /// the ground (surfaceAlign) while they still travel level-referenced and follow the terrain height.
        /// </summary>
        public Vector3 MoveUp => canClimb ? up : Vector3.up;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        public void Teleport(Vector3 pos, Vector3 fwd)
        {
            position = pos;
            up = Vector3.up;
            forward = Vector3.ProjectOnPlane(fwd, up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            velocity = Vector3.zero;
            grounded = false;
            // Drop to the ground below.
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 50f, mask, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
                grounded = true;
                groundNormal = hit.normal;
                if (canClimb) up = hit.normal;
            }
        }

        public void Jump(float speed)
        {
            if (!grounded) return;
            velocity += up * speed;
            grounded = false;
            airTime = 0f;
            position += up * 0.05f;
        }

        /// <param name="moveDir">Desired direction (world, any length &lt;= 1; projected onto the surface).</param>
        /// <param name="targetSpeed">Desired speed in m/s.</param>
        /// <param name="faceDir">Optional facing override (strafe); zero = face the direction of travel.</param>
        public void Step(float dt, Vector3 moveDir, float targetSpeed, Vector3 faceDir)
        {
            if (dt <= 0f) return;
            justLanded = false;
            Vector3 prevForward = forward;

            if (grounded) GroundedStep(dt, moveDir, targetSpeed);
            else AirStep(dt, moveDir, targetSpeed);

            // Facing.
            Vector3 face = faceDir.sqrMagnitude > 0.01f ? faceDir : Vector3.zero;
            Vector3 planarVel = Vector3.ProjectOnPlane(velocity, up);
            if (face == Vector3.zero && grounded && moveDir.sqrMagnitude > 0.01f && targetSpeed > 0.01f) face = moveDir;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(prevForward, up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            forward.Normalize();
            if (face != Vector3.zero)
            {
                Vector3 f = Vector3.ProjectOnPlane(face, up);
                if (f.sqrMagnitude > 1e-4f)
                {
                    float speedT = Mathf.Clamp01(planarVel.magnitude / Mathf.Max(0.1f, maxSpeed));
                    float rate = Mathf.Lerp(turnSpeed, turnSpeedAtMax, speedT) * Mathf.Deg2Rad;
                    // Ease in to the target so small corrections stay smooth.
                    float angle = Vector3.Angle(forward, f) * Mathf.Deg2Rad;
                    float step = Mathf.Min(rate * dt, angle * (1f - Mathf.Exp(-12f * dt)) + rate * dt * 0.25f);
                    forward = Vector3.RotateTowards(forward, f.normalized, step, 0f).normalized;
                }
            }
            yawRate = Vector3.SignedAngle(Vector3.ProjectOnPlane(prevForward, up), forward, up) / dt;
        }

        void GroundedStep(float dt, Vector3 moveDir, float targetSpeed)
        {
            airTime = 0f;
            // Walkers steer in the horizontal plane even when the body tilts with the ground: projecting the velocity
            // onto the tilted plane here and back onto the horizontal in WalkProbe shortened it twice every frame, so a
            // ground-aligned body (a horse) crawled up and down hills at a fraction of its speed.
            Vector3 moveUp = MoveUp;
            Vector3 dir = Vector3.ProjectOnPlane(moveDir, moveUp);
            float mag = Mathf.Min(1f, moveDir.magnitude);
            Vector3 target = dir.sqrMagnitude > 1e-6f ? dir.normalized * (targetSpeed * mag) : Vector3.zero;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, moveUp);
            float rate = target.sqrMagnitude >= planar.sqrMagnitude ? acceleration : deceleration;
            // Turning keeps momentum mostly, but steers faster than it accelerates.
            planar = Vector3.MoveTowards(planar, target, rate * dt * (1f + Vector3.Angle(planar, target) / 90f));
            velocity = planar;

            Vector3 delta = Sweep(velocity * dt);
            position += delta;

            if (canClimb) ClimbProbe(dt);
            else WalkProbe(dt, delta);
        }

        /// <summary>Collides the body capsule against the world, sliding along (or for climbers, stopping at) obstacles.</summary>
        Vector3 Sweep(Vector3 delta)
        {
            Vector3 total = Vector3.zero;
            float lift = canClimb ? radius + 0.02f : stepHeight + radius;
            for (int iter = 0; iter < 3; iter++)
            {
                float dist = delta.magnitude;
                if (dist < 1e-6f) break;
                Vector3 dir = delta / dist;
                Vector3 c = position + total + up * lift;
                Vector3 hl = forward * halfLength;
                if (!Physics.CapsuleCast(c - hl, c + hl, radius * 0.9f, dir, out var hit, dist + Skin, mask, QueryTriggerInteraction.Ignore))
                {
                    total += delta;
                    break;
                }
                float travel = Mathf.Max(0f, hit.distance - Skin);
                total += dir * travel;
                Vector3 remaining = delta - dir * travel;
                Vector3 n = canClimb ? hit.normal : Vector3.ProjectOnPlane(hit.normal, up);
                if (n.sqrMagnitude < 1e-5f) n = hit.normal;
                n.Normalize();
                remaining = Vector3.ProjectOnPlane(remaining, n);
                if (!canClimb) velocity = Vector3.ProjectOnPlane(velocity, n);
                delta = remaining;
            }
            return total;
        }

        void WalkProbe(float dt, Vector3 delta)
        {
            Vector3 worldUp = Vector3.up;
            float probeR = radius * 0.5f;
            Vector3 origin = position + worldUp * (stepHeight + probeR);
            float castDist = stepHeight * 2f + 0.05f;
            if (Physics.SphereCast(origin, probeR, -worldUp, out var hit, castDist, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 newPos = origin - worldUp * (hit.distance + probeR);
                // Use a ray for a precise normal (sphere casts report blended normals on edges).
                Vector3 n = hit.normal;
                if (Physics.Raycast(new Vector3(hit.point.x, origin.y, hit.point.z), -worldUp, out var rh, castDist + probeR * 2f, mask, QueryTriggerInteraction.Ignore))
                    n = rh.normal;
                float slope = Vector3.Angle(n, worldUp);
                if (slope > maxSlope && newPos.y > position.y + 0.02f)
                {
                    // Too steep to walk up: undo the horizontal move into it.
                    position -= delta;
                    Vector3 wall = Vector3.ProjectOnPlane(n, worldUp).normalized;
                    velocity = Vector3.ProjectOnPlane(velocity, wall);
                }
                else
                {
                    position.y = newPos.y;
                    groundNormal = n;
                }
            }
            else
            {
                grounded = false;
                airTime = 0f;
            }
            Vector3 targetUp = Vector3.Slerp(worldUp, groundNormal, surfaceAlign).normalized;
            up = Vector3.Slerp(up, targetUp, 1f - Mathf.Exp(-alignSpeed * dt)).normalized;
            if (!canClimb) velocity = Vector3.ProjectOnPlane(velocity, worldUp);
        }

        void ClimbProbe(float dt)
        {
            Vector3 c = position + up * radius;
            Vector3 travel = Vector3.ProjectOnPlane(velocity, up);
            Vector3 fwd = travel.sqrMagnitude > 0.01f ? travel.normalized : forward;
            Vector3 right = Vector3.Cross(up, fwd).normalized;
            float downLen = radius * 3.2f;
            Vector3 sumN = Vector3.zero, sumP = Vector3.zero;
            float wSum = 0f;
            int hits = 0;

            void Probe(Vector3 dir, float len, float weight)
            {
                if (Physics.Raycast(c, dir, out var h, len, mask, QueryTriggerInteraction.Ignore))
                {
                    float w = weight / (0.05f + h.distance);
                    sumN += h.normal * w;
                    sumP += h.point * w;
                    wSum += w;
                    hits++;
                }
            }

            float spread = 0.9f;
            Probe(-up, downLen, 1.5f);
            Probe((-up + fwd * spread).normalized, downLen, 1f);
            Probe((-up - fwd * spread).normalized, downLen, 0.8f);
            Probe((-up + right * spread).normalized, downLen, 0.8f);
            Probe((-up - right * spread).normalized, downLen, 0.8f);
            Probe(fwd, radius * 2.2f, 1.4f);
            if (halfLength > 0.01f)
            {
                Probe((-up + forward * 1.4f).normalized, downLen * 1.3f, 0.7f);
                Probe((-up - forward * 1.4f).normalized, downLen * 1.3f, 0.7f);
            }

            if (hits == 0)
            {
                // Lost the surface: we walked past a convex edge. Look back underneath for the face we came over.
                Vector3 below = position - up * (radius * 1.5f);
                if (Physics.Raycast(below, -fwd, out var wrap, radius * 5f, mask, QueryTriggerInteraction.Ignore) ||
                    Physics.Raycast(position + up * 0.05f, (-up - fwd).normalized, out wrap, radius * 5f, mask, QueryTriggerInteraction.Ignore))
                {
                    sumN = wrap.normal; sumP = wrap.point; wSum = 1f; hits = 1;
                    up = Vector3.Slerp(up, wrap.normal, 0.5f).normalized;
                }
                else
                {
                    grounded = false;
                    airTime = 0f;
                    return;
                }
            }

            Vector3 targetUp = sumN.normalized;
            Vector3 plane = sumP / wSum;
            up = Vector3.Slerp(up, targetUp, 1f - Mathf.Exp(-alignSpeed * 1.4f * dt)).normalized;
            groundNormal = targetUp;
            // Snap onto the fitted surface plane.
            float d = Vector3.Dot(position - plane, targetUp);
            position -= targetUp * d;
            // Keep velocity tangent to the new surface.
            velocity = Vector3.ProjectOnPlane(velocity, up);
        }

        void AirStep(float dt, Vector3 moveDir, float targetSpeed)
        {
            airTime += dt;
            // A little air control.
            Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
            Vector3 dir = Vector3.ProjectOnPlane(moveDir, Vector3.up);
            if (dir.sqrMagnitude > 1e-4f)
                planar = Vector3.MoveTowards(planar, dir.normalized * targetSpeed, acceleration * 0.25f * dt);
            velocity = planar + Vector3.up * velocity.y;
            velocity += Vector3.down * (gravity * dt);

            Vector3 delta = velocity * dt;
            float dist = delta.magnitude;
            Vector3 c = position + up * radius;
            if (dist > 1e-6f && Physics.SphereCast(c, radius * 0.9f, delta / dist, out var hit, dist + Skin, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitCenter = c + delta / dist * Mathf.Max(0f, hit.distance - Skin);
                bool walkable = canClimb || Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
                if (walkable && airTime > 0.05f)
                {
                    lastLandingVelocity = velocity;
                    grounded = true;
                    justLanded = true;
                    Vector3 n = canClimb ? hit.normal : Vector3.up;
                    position = hitCenter - n * radius * 0.9f;
                    if (canClimb) { position = hit.point; up = Vector3.Slerp(up, hit.normal, 0.6f).normalized; }
                    groundNormal = hit.normal;
                    // Keep the horizontal momentum through the landing; only the impact (normal) component is absorbed.
                    velocity = Vector3.ProjectOnPlane(velocity, n);
                    return;
                }
                position = hitCenter - up * radius;
                velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
            }
            else
            {
                position += delta;
            }
            // Right the body while falling.
            up = Vector3.Slerp(up, Vector3.up, 1f - Mathf.Exp(-3f * dt)).normalized;
            if (position.y < -50f) position = new Vector3(position.x, 20f, position.z);
        }
    }
}
