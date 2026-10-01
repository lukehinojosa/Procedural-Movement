using UnityEngine;
using UnityEngine.InputSystem;

namespace ProceduralMovement
{
    /// <summary>
    /// Third person camera for any creature.
    /// Modes: Orbit (free orbit, world up), Surface (orbit in the creature's local frame, so walls and ceilings
    /// read as floors) and Chase (swings in behind the creature's heading).
    /// Local position options: distance, height, shoulder offset, forward offset, FOV, plus presets.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ThirdPersonCamera : MonoBehaviour
    {
        public enum Mode { Orbit, Surface, Chase }

        [System.Serializable]
        public struct Preset
        {
            public string name;
            public float distance, height, shoulder, forward, pitch, fov;
            public Preset(string n, float d, float h, float s, float f, float p, float fov) { name = n; distance = d; height = h; shoulder = s; forward = f; pitch = p; this.fov = fov; }
        }

        /// <summary>Presets in units of the creature's size (1 = a human-sized frame).</summary>
        public static readonly Preset[] Presets =
        {
            new Preset("Default", 2.2f, 0.25f, 0f, 0f, 14f, 60f),
            new Preset("Close", 1.2f, 0.15f, 0.28f, 0f, 8f, 55f),
            new Preset("Shoulder", 1.5f, 0.2f, 0.45f, 0.1f, 6f, 55f),
            new Preset("Far", 4.2f, 0.45f, 0f, 0f, 20f, 60f),
            new Preset("Low", 1.8f, -0.05f, 0f, 0f, -6f, 65f),
            new Preset("Overhead", 5.5f, 0.3f, 0f, 0f, 62f, 55f),
        };

        public Creature target;
        public Mode mode = Mode.Orbit;
        [Header("Local position")]
        public float distance = 5f;
        public float height = 0.6f;
        public float shoulder = 0f;
        public float forwardOffset = 0f;
        public float fov = 60f;
        [Header("Control")]
        public float sensitivity = 0.12f;
        public float stickSensitivity = 140f;
        public bool invertY;
        public float followSharpness = 14f;
        public float rotationSharpness = 18f;
        public bool collision = true;
        public float yaw, pitch = 14f;
        public int presetIndex;

        Camera m_Cam;
        Quaternion m_Frame = Quaternion.identity;
        Vector3 m_Pivot;
        float m_CurrentDistance;
        float m_LastManualInput = -10f;
        float m_SmoothYaw, m_SmoothPitch;
        bool m_HasPivot;

        public static bool LookEnabled = true; // cleared by UI panels that need the mouse
        /// <summary>Metres per preset unit: about 2.2 for a human, smaller for a spider, larger for a horse.</summary>
        public float CreatureScale => target != null ? Mathf.Clamp(target.def.locomotion.bodyHeight * 2.2f + target.def.locomotion.collisionRadius, 0.6f, 4f) / 2.4f * 2.2f : 2.2f;

        void Awake()
        {
            m_Cam = GetComponent<Camera>();
        }

        public void ApplyPreset(int index)
        {
            presetIndex = Mathf.Clamp(index, 0, Presets.Length - 1);
            var p = Presets[presetIndex];
            float s = CreatureScale;
            distance = p.distance * s;
            height = p.height * s;
            shoulder = p.shoulder * s;
            forwardOffset = p.forward * s;
            pitch = p.pitch;
            fov = p.fov;
        }

        public void SetTarget(Creature c, bool snap)
        {
            target = c;
            if (c == null) return;
            ApplyPreset(presetIndex);
            if (snap)
            {
                m_Frame = Quaternion.identity;
                yaw = Quaternion.LookRotation(c.transform.forward, Vector3.up).eulerAngles.y;
                m_SmoothYaw = yaw; m_SmoothPitch = pitch;
                m_HasPivot = false;
                m_CurrentDistance = distance;
            }
        }

        public void CycleMode() => mode = (Mode)(((int)mode + 1) % 3);

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);

            // --- Input.
            Vector2 look = Vector2.zero;
            var mouse = Mouse.current;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (mouse != null && LookEnabled && (locked || mouse.rightButton.isPressed))
                look += mouse.delta.ReadValue() * sensitivity;
            var pad = Gamepad.current;
            if (pad != null) look += pad.rightStick.ReadValue() * stickSensitivity * dt;
            if (look.sqrMagnitude > 1e-4f) m_LastManualInput = Time.unscaledTime;
            yaw += look.x;
            pitch += (invertY ? 1f : -1f) * look.y;
            pitch = Mathf.Clamp(pitch, -70f, 85f);
            if (mouse != null && LookEnabled)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float s = CreatureScale;
                    distance = Mathf.Clamp(distance * (1f - Mathf.Sign(scroll) * 0.1f), 0.5f * s * 0.5f, 12f * s);
                }
            }

            // --- Reference frame: world up, or parallel-transported to the creature's surface normal.
            Vector3 targetUp = mode == Mode.Surface ? target.Up : Vector3.up;
            Vector3 frameUp = m_Frame * Vector3.up;
            Quaternion toUp = Quaternion.FromToRotation(frameUp, targetUp);
            m_Frame = Quaternion.Slerp(m_Frame, toUp * m_Frame, 1f - Mathf.Exp(-5f * dt));
            frameUp = m_Frame * Vector3.up;

            // Chase: ease the yaw in behind the creature's heading when not being steered by hand.
            if (mode == Mode.Chase && Time.unscaledTime - m_LastManualInput > 0.6f && target.Speed > 0.2f)
            {
                Vector3 heading = Quaternion.Inverse(m_Frame) * target.transform.forward;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.01f)
                {
                    float targetYaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
                    yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-2.2f * dt));
                }
            }
            m_SmoothYaw = Mathf.LerpAngle(m_SmoothYaw, yaw, 1f - Mathf.Exp(-rotationSharpness * dt));
            m_SmoothPitch = Mathf.Lerp(m_SmoothPitch, pitch, 1f - Mathf.Exp(-rotationSharpness * dt));
            Quaternion rot = m_Frame * Quaternion.Euler(m_SmoothPitch, m_SmoothYaw, 0f);

            // --- Pivot with local offsets.
            Vector3 camRight = rot * Vector3.right;
            Vector3 creatureFwd = Vector3.ProjectOnPlane(target.transform.forward, frameUp).normalized;
            Vector3 pivotTarget = target.CameraPivot + frameUp * height + camRight * shoulder + creatureFwd * forwardOffset;
            if (!m_HasPivot || (pivotTarget - m_Pivot).sqrMagnitude > 400f) { m_Pivot = pivotTarget; m_HasPivot = true; m_CurrentDistance = distance; }
            m_Pivot = Vector3.Lerp(m_Pivot, pivotTarget, 1f - Mathf.Exp(-followSharpness * dt));

            // --- Collision: pull in in front of walls, recover smoothly.
            float want = distance;
            Vector3 back = rot * Vector3.back;
            if (collision && Physics.SphereCast(m_Pivot, 0.22f, back, out var hit, distance, ~(1 << Creature.CreatureLayer), QueryTriggerInteraction.Ignore))
                want = Mathf.Max(0.3f, hit.distance - 0.05f);
            m_CurrentDistance = want < m_CurrentDistance ? want : Mathf.Lerp(m_CurrentDistance, want, 1f - Mathf.Exp(-4f * dt));

            transform.SetPositionAndRotation(m_Pivot + back * m_CurrentDistance, rot);
            if (m_Cam != null) m_Cam.fieldOfView = Mathf.Lerp(m_Cam.fieldOfView, fov + Mathf.Clamp01(target.Speed / 12f) * 8f, 1f - Mathf.Exp(-4f * dt));
        }
    }
}
