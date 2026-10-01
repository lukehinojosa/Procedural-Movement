using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ProceduralMovement
{
    /// <summary>In-game HUD: creature stats, live footfall diagram, creature switcher, camera settings and help.</summary>
    public class DemoHUD : MonoBehaviour
    {
        public DemoBootstrap bootstrap;
        bool m_ShowHelp = true;
        bool m_ShowSettings;
        float m_TimeScale = 1f;
        Vector2 m_SettingsScroll;

        // Footfall history: one row per leg, stance = true.
        const int HistoryLength = 150;
        bool[,] m_History;
        int m_HistoryHead, m_HistoryLegs;
        float m_SampleTimer;

        static readonly CreaturePreset[] s_Presets = { CreaturePreset.Spider, CreaturePreset.Human, CreaturePreset.Horse, CreaturePreset.Ant, CreaturePreset.Centipede };

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var cam = bootstrap.CameraRig;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) m_ShowSettings = !m_ShowSettings;
                if (kb.hKey.wasPressedThisFrame) m_ShowHelp = !m_ShowHelp;
                if (kb.gKey.wasPressedThisFrame) DebugOverlay.Enabled = !DebugOverlay.Enabled;
                if (kb.cKey.wasPressedThisFrame) cam.CycleMode();
                if (kb.vKey.wasPressedThisFrame) cam.ApplyPreset((cam.presetIndex + 1) % ThirdPersonCamera.Presets.Length);
                if (kb.rKey.wasPressedThisFrame) bootstrap.ResetToSpawn();
                if (kb.tKey.wasPressedThisFrame) m_TimeScale = m_TimeScale < 0.99f ? 1f : 0.25f;
                if (kb.pKey.wasPressedThisFrame) bootstrap.SetAutopilot(!bootstrap.Autopilot);
                if (kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
                if (kb.digit1Key.wasPressedThisFrame) Switch(0);
                if (kb.digit2Key.wasPressedThisFrame) Switch(1);
                if (kb.digit3Key.wasPressedThisFrame) Switch(2);
                if (kb.digit4Key.wasPressedThisFrame) Switch(3);
                if (kb.digit5Key.wasPressedThisFrame) Switch(4);
                if (kb.digit6Key.wasPressedThisFrame && CreatureSession.Custom != null) bootstrap.SwitchTo(CreatureSession.Custom.Clone(), true);
            }
            if (mouse != null)
            {
                bool overUi = GuiKit.IsOver(mouse.position.ReadValue());
                if (mouse.leftButton.wasPressedThisFrame && !overUi && !m_ShowSettings)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                if (Cursor.lockState != CursorLockMode.Locked) Cursor.visible = true;
                ThirdPersonCamera.LookEnabled = !overUi || Cursor.lockState == CursorLockMode.Locked;
            }
            if (m_ShowSettings && Cursor.lockState == CursorLockMode.Locked) Cursor.lockState = CursorLockMode.None;
            Time.timeScale = m_TimeScale;
            SampleFootfalls();
        }

        void Switch(int i)
        {
            bootstrap.SwitchTo(CreaturePresets.Get(s_Presets[i]), false);
            m_History = null;
        }

        void SampleFootfalls()
        {
            var c = bootstrap.Player;
            if (c == null) return;
            int legs = c.Legs.Count;
            if (m_History == null || m_HistoryLegs != legs) { m_History = new bool[legs, HistoryLength]; m_HistoryLegs = legs; m_HistoryHead = 0; }
            m_SampleTimer += Time.deltaTime;
            while (m_SampleTimer >= 1f / 50f)
            {
                m_SampleTimer -= 1f / 50f;
                for (int i = 0; i < legs; i++) m_History[i, m_HistoryHead] = !c.Legs[i].swinging && c.Motor.grounded;
                m_HistoryHead = (m_HistoryHead + 1) % HistoryLength;
            }
        }

        void OnGUI()
        {
            GuiKit.Begin();
            var c = bootstrap.Player;
            if (c == null) return;
            float W = GuiKit.Width, H = GuiKit.Height;

            StatsPanel(c, new Rect(20, 20, 360, 150 + Mathf.Min(c.Legs.Count, 18) * 9f));
            CreaturePanel(new Rect(W - 300, 20, 280, 236));
            if (m_ShowHelp) HelpPanel(new Rect(20, H - 268, 380, 248));
            if (m_ShowSettings) SettingsPanel(new Rect(W - 380, 270, 360, Mathf.Min(H - 290, 640)));

            if (Cursor.lockState != CursorLockMode.Locked && !m_ShowSettings)
            {
                var r = new Rect(W * 0.5f - 230, H - 56, 460, 36);
                GUI.Box(r, GUIContent.none, GuiKit.panel);
                GUI.Label(new Rect(r.x, r.y + 9, r.width, 20), "Click to steer the camera  ·  Esc releases  ·  Tab settings", new GUIStyle(GuiKit.small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        void StatsPanel(Creature c, Rect r)
        {
            GuiKit.BeginPanel(r);
            GUILayout.Label(c.def.name + (bootstrap.IsCustom ? "  (custom)" : ""), GuiKit.title);
            float speed = c.Speed;
            var loco = c.def.locomotion;
            string surface = !c.Motor.grounded ? "Airborne" : Vector3.Dot(c.Up, Vector3.up) > 0.75f ? "Ground" : Vector3.Dot(c.Up, Vector3.up) < -0.5f ? "Ceiling" : "Wall";
            GUILayout.Label($"{c.GaitName}  ·  {speed:0.0} m/s  ({speed * 3.6f:0} km/h)  ·  {surface}", GuiKit.label);
            GUILayout.Space(6);
            var bar = GUILayoutUtility.GetRect(300, 10, GUILayout.ExpandWidth(true));
            GuiKit.Bar(bar, speed / loco.maxSpeed, GuiKit.Accent);
            float cruiseX = bar.x + bar.width * Mathf.Clamp01(c.cruiseSpeed / loco.maxSpeed);
            GuiKit.Fill(new Rect(cruiseX - 1, bar.y - 3, 2, bar.height + 6), Color.white);
            GUILayout.Space(2);
            var ctrl = bootstrap.Controller;
            string mode = ctrl.Sprinting ? "Sprint" : ctrl.Creeping ? "Creep" : $"Cruise {c.cruiseSpeed:0.0} m/s";
            GUILayout.Label($"{mode}   ·   range {loco.minSpeed:0.0} to {loco.maxSpeed:0.0} m/s" + (ctrl.strafeToggle ? "   ·   Strafe" : ""), GuiKit.muted);

            // Footfall diagram.
            GUILayout.Space(8);
            GUILayout.Label("FOOTFALLS", GuiKit.header);
            int legs = Mathf.Min(c.Legs.Count, 18);
            var area = GUILayoutUtility.GetRect(300, legs * 9f, GUILayout.ExpandWidth(true));
            if (m_History != null && Event.current.type == EventType.Repaint)
            {
                float cw = area.width / HistoryLength;
                for (int i = 0; i < legs; i++)
                {
                    var leg = c.Legs[i];
                    float y = area.y + i * 9f;
                    GuiKit.Fill(new Rect(area.x, y + 1, area.width, 6), new Color(1, 1, 1, 0.05f));
                    Color col = leg.side > 0 ? GuiKit.Accent : new Color(0.45f, 0.75f, 1f);
                    for (int k = 0; k < HistoryLength; k++)
                    {
                        int idx = (m_HistoryHead + k) % HistoryLength;
                        if (m_History[i, idx]) GuiKit.Fill(new Rect(area.x + k * cw, y + 1, cw + 0.5f, 6), col);
                    }
                }
            }
            GuiKit.EndPanel();
        }

        void CreaturePanel(Rect r)
        {
            GuiKit.BeginPanel(r);
            GUILayout.Label("CREATURES", GuiKit.header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 3; i++)
                if (GuiKit.Button($"{i + 1} {s_Presets[i]}", !bootstrap.IsCustom && bootstrap.Player.def.name == s_Presets[i].ToString())) Switch(i);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int i = 3; i < 5; i++)
                if (GuiKit.Button($"{i + 1} {s_Presets[i]}", !bootstrap.IsCustom && bootstrap.Player.def.name == s_Presets[i].ToString())) Switch(i);
            GUI.enabled = CreatureSession.Custom != null;
            if (GuiKit.Button("6 Custom", bootstrap.IsCustom)) bootstrap.SwitchTo(CreatureSession.Custom.Clone(), true);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            if (GuiKit.Button("Open Creature Creator"))
            {
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.None;
                if (bootstrap.IsCustom && CreatureSession.Custom == null) CreatureSession.Custom = bootstrap.Player.def.Clone();
                SceneManager.LoadScene(CreatureSession.CreatorScene);
            }
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Reset (R)")) bootstrap.ResetToSpawn();
            if (GuiKit.Button(m_ShowSettings ? "Hide settings" : "Settings (Tab)", m_ShowSettings)) m_ShowSettings = !m_ShowSettings;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Debug (G)", DebugOverlay.Enabled)) DebugOverlay.Enabled = !DebugOverlay.Enabled;
            if (GuiKit.Button("Slow-mo (T)", m_TimeScale < 0.99f)) m_TimeScale = m_TimeScale < 0.99f ? 1f : 0.25f;
            if (GuiKit.Button("Auto (P)", bootstrap.Autopilot)) bootstrap.SetAutopilot(!bootstrap.Autopilot);
            GUILayout.EndHorizontal();
            GuiKit.EndPanel();
        }

        void HelpPanel(Rect r)
        {
            GuiKit.BeginPanel(r);
            GUILayout.Label("CONTROLS", GuiKit.header);
            Row("WASD / left stick", "Move (camera relative)");
            Row("Shift / RT", "Sprint to top speed");
            Row("Ctrl / LB", "Creep at minimum speed");
            Row("Q / E, D-pad", "Lower / raise cruise speed");
            Row("Space / A", "Jump: hold to crouch deeper, release to spring (climbers let go)");
            Row("F / hold RMB", "Strafe: face the camera");
            Row("Mouse / right stick", "Orbit camera, wheel zooms");
            Row("C / V", "Camera mode / position preset");
            Row("1 to 6", "Switch creature");
            Row("G / T / R", "Debug overlay / slow-mo / reset");
            Row("P", "Autopilot (creature wanders)");
            Row("H", "Hide this panel");
            GuiKit.EndPanel();
        }

        static void Row(string key, string what)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, GuiKit.small, GUILayout.Width(140));
            GUILayout.Label(what, GuiKit.muted);
            GUILayout.EndHorizontal();
        }

        void SettingsPanel(Rect r)
        {
            var cam = bootstrap.CameraRig;
            GuiKit.BeginPanel(r);
            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label("CAMERA MODE", GuiKit.header);
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Orbit", cam.mode == ThirdPersonCamera.Mode.Orbit)) cam.mode = ThirdPersonCamera.Mode.Orbit;
            if (GuiKit.Button("Surface", cam.mode == ThirdPersonCamera.Mode.Surface)) cam.mode = ThirdPersonCamera.Mode.Surface;
            if (GuiKit.Button("Chase", cam.mode == ThirdPersonCamera.Mode.Chase)) cam.mode = ThirdPersonCamera.Mode.Chase;
            GUILayout.EndHorizontal();
            GUILayout.Label(cam.mode == ThirdPersonCamera.Mode.Surface ? "Orbits in the creature's local frame: walls and ceilings become the floor."
                : cam.mode == ThirdPersonCamera.Mode.Chase ? "Swings in behind the creature's heading when you stop steering."
                : "Free orbit around the creature with world up.", GuiKit.muted);

            GUILayout.Label("LOCAL POSITION PRESETS", GuiKit.header);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 3; i < row * 3 + 3 && i < ThirdPersonCamera.Presets.Length; i++)
                    if (GuiKit.Button(ThirdPersonCamera.Presets[i].name, cam.presetIndex == i)) cam.ApplyPreset(i);
                GUILayout.EndHorizontal();
            }
            float s = cam.CreatureScale;
            GUILayout.Label("OFFSETS", GuiKit.header);
            cam.distance = GuiKit.Slider("Distance", cam.distance, 0.3f * s, 12f * s, "0.0");
            cam.height = GuiKit.Slider("Height", cam.height, -0.5f * s, 2f * s, "0.00");
            cam.shoulder = GuiKit.Slider("Shoulder", cam.shoulder, -1.2f * s, 1.2f * s, "0.00");
            cam.forwardOffset = GuiKit.Slider("Forward", cam.forwardOffset, -1f * s, 1f * s, "0.00");
            cam.fov = GuiKit.Slider("Field of view", cam.fov, 30f, 100f, "0");
            GUILayout.Label("CONTROL", GuiKit.header);
            cam.sensitivity = GuiKit.Slider("Sensitivity", cam.sensitivity, 0.02f, 0.5f, "0.00");
            cam.followSharpness = GuiKit.Slider("Follow", cam.followSharpness, 2f, 30f, "0");
            cam.invertY = GuiKit.Toggle("Invert Y", cam.invertY);
            cam.collision = GuiKit.Toggle("Camera collision", cam.collision);
            GUILayout.Label("WORLD", GuiKit.header);
            m_TimeScale = GuiKit.Slider("Time scale", m_TimeScale, 0.05f, 1f, "0.00");
            bool w = GuiKit.Toggle("Wandering creatures", bootstrap.WanderersActive);
            if (w != bootstrap.WanderersActive) bootstrap.SetWanderers(w);
            DebugOverlay.Enabled = GuiKit.Toggle("Debug overlay (IK, footholds)", DebugOverlay.Enabled);
            GUILayout.EndScrollView();
            GuiKit.EndPanel();
        }
    }
}
