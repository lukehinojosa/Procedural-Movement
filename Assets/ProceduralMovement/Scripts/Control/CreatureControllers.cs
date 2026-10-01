using UnityEngine;
using UnityEngine.InputSystem;

namespace ProceduralMovement
{
    /// <summary>
    /// Keyboard / mouse / gamepad control of a creature, relative to the camera and the surface the creature is on.
    /// Speed is continuous: a cruise speed (adjustable with Q / E or the D-pad), Shift to sprint, Ctrl to creep,
    /// and analog stick deflection scales speed smoothly.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerCreatureController : MonoBehaviour
    {
        public Creature creature;
        public Camera viewCamera;
        public bool strafeToggle;
        public static bool InputEnabled = true;

        /// <summary>Scripted input for automation and demo tours. When active it replaces device input.</summary>
        public static bool ScriptActive;
        public static Vector2 ScriptMove;
        public static bool ScriptSprint, ScriptCreep, ScriptJump, ScriptJumpHeld;
        public static float ScriptSteerRate;    // deg/s: scripted input turns steadily (for tours and tests)

        public bool Sprinting { get; private set; }
        public bool Creeping { get; private set; }
        public float CommandedSpeed { get; private set; }

        void Update()
        {
            if (creature == null) return;
            if (viewCamera == null) viewCamera = Camera.main;
            var loco = creature.def.locomotion;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            Vector2 move = Vector2.zero;
            bool sprint = false, creep = false, jump = false, jumpHeld = false;
            float cruiseDelta = 0f;
            if (InputEnabled && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                sprint |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                creep |= kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                jumpHeld |= kb.spaceKey.isPressed;
                if (kb.eKey.isPressed) cruiseDelta += 1f;
                if (kb.qKey.isPressed) cruiseDelta -= 1f;
                if (kb.fKey.wasPressedThisFrame) strafeToggle = !strafeToggle;
            }
            if (ScriptActive)
            {
                if (ScriptSteerRate != 0f)
                    ScriptMove = (Vector2)(Quaternion.Euler(0f, 0f, -ScriptSteerRate * Time.deltaTime) * ScriptMove);
                move = ScriptMove;
                sprint = ScriptSprint; creep = ScriptCreep;
                jump |= ScriptJump; ScriptJump = false;
                jumpHeld |= ScriptJumpHeld;
            }
            if (move.sqrMagnitude > 1f) move.Normalize();
            float analog = 1f;
            if (InputEnabled && pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.02f) { move = stick; analog = Mathf.Clamp01(stick.magnitude); }
                sprint |= pad.rightTrigger.ReadValue() > 0.3f || pad.leftStickButton.isPressed;
                creep |= pad.leftShoulder.isPressed;
                jumpHeld |= pad.buttonSouth.isPressed;
                if (pad.dpad.up.isPressed) cruiseDelta += 1f;
                if (pad.dpad.down.isPressed) cruiseDelta -= 1f;
            }

            float range = loco.maxSpeed - loco.minSpeed;
            creature.cruiseSpeed = Mathf.Clamp(creature.cruiseSpeed + cruiseDelta * range * 0.35f * Time.deltaTime, loco.minSpeed, loco.maxSpeed);
            float speed = sprint ? loco.maxSpeed : creep ? loco.minSpeed : creature.cruiseSpeed;
            // Analog sticks give a continuous range down to a creep.
            if (analog < 1f) speed = Mathf.Lerp(loco.minSpeed, speed, Mathf.InverseLerp(0.15f, 1f, analog));
            Sprinting = sprint; Creeping = creep;

            // Camera-relative directions on the creature's current surface (works on walls and ceilings).
            Vector3 up = creature.Up;
            Transform cam = viewCamera != null ? viewCamera.transform : creature.transform;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, up);
            if (right.sqrMagnitude < 0.01f) right = Vector3.ProjectOnPlane(-Vector3.Cross(cam.up, up), up);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, up).normalized;
            Vector3 dir = fwd * move.y + right * move.x;

            bool strafe = strafeToggle || (InputEnabled && mouse != null && mouse.rightButton.isPressed && Cursor.lockState == CursorLockMode.Locked);
            creature.moveInput = dir;
            creature.desiredSpeed = move.sqrMagnitude > 0.001f ? speed : 0f;
            CommandedSpeed = creature.desiredSpeed;
            creature.faceDirection = strafe ? fwd : Vector3.zero;
            creature.lookDirection = cam.forward;
            if (jump) creature.jumpRequested = true;
            creature.jumpHeld = jumpHeld;
        }
    }

    /// <summary>Simple wandering behaviour for background creatures so the city feels alive.</summary>
    [DefaultExecutionOrder(-50)]
    public class WanderAI : MonoBehaviour
    {
        public Creature creature;
        public Vector3 center;
        public float radius = 60f;
        Vector3 m_Heading;
        float m_Speed, m_Timer, m_Pause, m_OffWallTime;
        System.Random m_Rng;

        void Start()
        {
            m_Rng = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            m_Heading = transform.forward;
            PickNew();
        }

        float Rand(float a, float b) => a + (float)m_Rng.NextDouble() * (b - a);

        void PickNew()
        {
            var loco = creature.def.locomotion;
            m_Timer = Rand(3f, 9f);
            m_Pause = m_Rng.NextDouble() < 0.25 ? Rand(1f, 3f) : 0f;
            float r = (float)m_Rng.NextDouble();
            m_Speed = r < 0.55f ? Rand(loco.minSpeed, loco.cruiseSpeed) : r < 0.85f ? Rand(loco.cruiseSpeed, loco.cruiseSpeed * 2f) : Rand(loco.cruiseSpeed * 2f, loco.maxSpeed);
            m_Speed = Mathf.Min(m_Speed, loco.maxSpeed);
            m_Heading = Quaternion.AngleAxis(Rand(-120f, 120f), creature.Up) * creature.transform.forward;
        }

        void Update()
        {
            if (creature == null) return;
            float dt = Time.deltaTime;
            Vector3 up = creature.Up;
            m_Timer -= dt;
            if (m_Pause > 0f) { m_Pause -= dt; creature.desiredSpeed = 0f; creature.moveInput = Vector3.zero; return; }
            if (m_Timer <= 0f) PickNew();

            Vector3 pos = creature.transform.position;
            Vector3 heading = Vector3.ProjectOnPlane(m_Heading, up).normalized;
            // Home back toward the play area.
            Vector3 toCenter = center - pos; toCenter.y = 0f;
            if (toCenter.magnitude > radius) heading = Vector3.Slerp(heading, Vector3.ProjectOnPlane(toCenter, up).normalized, 0.05f);
            // Climbers drift back down after a while on walls so they do not vanish onto rooftops.
            if (Vector3.Dot(up, Vector3.up) < 0.7f) m_OffWallTime += dt; else m_OffWallTime = 0f;
            if (m_OffWallTime > 6f) heading = Vector3.Slerp(heading, Vector3.ProjectOnPlane(Vector3.down, up).normalized, 0.1f);
            // Walkers steer around obstacles.
            if (!creature.def.locomotion.canClimb)
            {
                float r = creature.def.locomotion.collisionRadius;
                Vector3 origin = pos + up * (creature.def.locomotion.maxStepUp + r);
                if (Physics.SphereCast(origin, r, heading, out _, r * 4f + m_Speed * 0.6f, ~(1 << Creature.CreatureLayer), QueryTriggerInteraction.Ignore))
                    heading = Quaternion.AngleAxis(90f * dt * 3f, up) * heading;
            }
            m_Heading = heading;
            creature.moveInput = heading;
            creature.desiredSpeed = m_Speed;
            creature.faceDirection = Vector3.zero;
            creature.lookDirection = heading;
        }
    }
}
