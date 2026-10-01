using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ProceduralMovement
{
    /// <summary>
    /// Creature Creator: edit body, limbs, gait and colours with live rebuilds, watch the result walk in a
    /// test showroom (idle, walk a loop, or drive it yourself), save / load, and take it into the city.
    /// </summary>
    public class CreatureCreator : MonoBehaviour
    {
        public enum PreviewMode { Idle, WalkLoop, Drive }

        CreatorModel m_Model = new CreatorModel();
        Creature m_Creature;
        ThirdPersonCamera m_Cam;
        PlayerCreatureController m_Controller;
        PreviewMode m_Mode = PreviewMode.WalkLoop;
        float m_LoopSpeed = -1f;
        string m_LastJson;
        float m_DirtyTime = -1f;
        Vector2 m_Scroll, m_LoadScroll;
        int m_ColorSlot;
        string m_Status = "";
        float m_StatusTime;
        List<string> m_Saved = new List<string>();
        int m_Tab;
        float m_Angle;

        static readonly string[] s_Tabs = { "Body", "Limbs", "Motion", "Look" };
        static readonly string[] s_ColorNames = { "Primary", "Secondary", "Accent", "Dark", "Eyes" };
        static readonly string[] s_GaitNames = { "Template", "Alternating", "Wave", "Biped", "Quad walk", "Quad trot", "Gallop", "Bound" };

        void Start()
        {
            var cam = WorldSetup.ConfigureCamera();
            WorldSetup.ConfigureLighting(38f, -140f);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 180f;
            BuildShowroom();
            Physics.SyncTransforms();

            m_Cam = cam.gameObject.AddComponent<ThirdPersonCamera>();
            m_Cam.mode = ThirdPersonCamera.Mode.Orbit;
            m_Cam.presetIndex = 3; // "Far": frame the whole creature and the walk loop
            var overlay = cam.gameObject.AddComponent<DebugOverlay>();
            m_Controller = gameObject.AddComponent<PlayerCreatureController>();
            m_Controller.viewCamera = cam;
            m_Controller.enabled = false;

            if (!string.IsNullOrEmpty(CreatureSession.CreatorStateJson))
                JsonUtility.FromJsonOverwrite(CreatureSession.CreatorStateJson, m_Model);
            else
                m_Model.ResetTo(CreaturePreset.Spider);
            Rebuild(true);
            overlay.target = m_Creature;
            m_Saved = CreatureSession.ListSaved();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void BuildShowroom()
        {
            var root = new GameObject("Showroom");
            var b = new CityBuilder(root.transform, 1000f);
            var floor = MaterialLibrary.Get("ShowFloor", new Color(0.78f, 0.8f, 0.84f), 0.25f, 0f, MaterialLibrary.Checker());
            var accent = MaterialLibrary.Get("ShowAccent", new Color(0.95f, 0.55f, 0.25f), 0.3f, 0f, MaterialLibrary.Checker());
            var teal = MaterialLibrary.Get("ShowTeal", new Color(0.3f, 0.72f, 0.75f), 0.3f, 0f, MaterialLibrary.Checker());
            var concrete = MaterialLibrary.Get("Concrete", Color.white, 0.2f, 0f, MaterialLibrary.Concrete());
            b.Box(new Vector3(0, -0.5f, 0), new Vector3(90f, 1f, 90f), floor, 0.5f);
            // Turntable disc.
            b.Cylinder(new Vector3(0, -0.98f, 0), 4f, 1.0f, MaterialLibrary.Get("ShowDisc", new Color(0.3f, 0.32f, 0.36f), 0.5f), 0.5f, false, 64);
            // Test obstacles ringed around the loop.
            for (int k = 0; k < 10; k++)
            {
                float rise = 0.18f;
                Vector3 p = new Vector3(-17.8f, 0, 0f) + Vector3.right * (0.4f * k);
                b.Box(p + Vector3.up * (rise * (k + 1) * 0.5f), new Vector3(0.4f, rise * (k + 1), 6f), concrete, 0.5f);
            }
            b.Box(new Vector3(-12f, 0.9f, 0f), new Vector3(4f, 1.8f, 6f), concrete, 0.5f);
            b.Wedge(new Vector3(-12f, 0.9f, 6f), new Vector3(4f, 1.8f, 6f), Quaternion.Euler(0, 180f, 0), teal, 0.5f);
            b.Box(new Vector3(12f, 2f, 0f), new Vector3(2f, 4f, 10f), accent, 0.5f);
            b.Box(new Vector3(10f, 4.2f, 0f), new Vector3(6f, 0.4f, 10f), accent, 0.5f);
            b.Cylinder(new Vector3(0f, 0f, 13f), 1.5f, 5f, teal, 0.5f, true, 32, -1f, true);
            b.Ellipsoid(new Vector3(0f, -1f, -13f), Vector3.one * 2.6f, Quaternion.identity, accent, true, 28, 18);
            for (int i = 0; i < 30; i++)
            {
                var rng = new System.Random(i * 31);
                Vector3 p = new Vector3(6f + (float)rng.NextDouble() * 5f, 0, -10f + (float)rng.NextDouble() * 5f);
                Vector3 s = new Vector3(0.3f + (float)rng.NextDouble() * 0.8f, 0.1f + (float)rng.NextDouble() * 0.3f, 0.3f + (float)rng.NextDouble() * 0.8f);
                b.Box(p + Vector3.up * s.y * 0.3f, s, Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, 0), concrete, 1f);
            }
            b.Finish();
        }

        void Rebuild(bool snapCamera)
        {
            var def = m_Model.Build();
            Vector3 pos = Vector3.zero, fwd = Vector3.forward;
            if (m_Creature != null)
            {
                pos = m_Creature.transform.position;
                fwd = m_Creature.transform.forward;
                if (Vector3.Dot(m_Creature.Up, Vector3.up) < 0.8f || !m_Creature.Motor.grounded) { pos = new Vector3(pos.x, 0f, pos.z); fwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward; }
                float cruise = m_Creature.cruiseSpeed;
                m_Creature.Build(def);
                m_Creature.cruiseSpeed = Mathf.Clamp(cruise, def.locomotion.minSpeed, def.locomotion.maxSpeed);
                m_Creature.Teleport(pos + Vector3.up * 0.5f, fwd);
            }
            else
            {
                m_Creature = Creature.Spawn(def, new Vector3(0f, 0.5f, -5f), Vector3.right);
            }
            if (m_LoopSpeed < 0f) m_LoopSpeed = def.locomotion.cruiseSpeed;
            m_LoopSpeed = Mathf.Clamp(m_LoopSpeed, def.locomotion.minSpeed, def.locomotion.maxSpeed);
            m_Controller.creature = m_Creature;
            if (snapCamera) m_Cam.SetTarget(m_Creature, true);
            else m_Cam.target = m_Creature;
            var overlay = m_Cam.GetComponent<DebugOverlay>();
            if (overlay != null) overlay.target = m_Creature;
            m_LastJson = JsonUtility.ToJson(m_Model);
            CreatureSession.CreatorStateJson = m_LastJson;
        }

        void Update()
        {
            string json = JsonUtility.ToJson(m_Model);
            if (json != m_LastJson)
            {
                if (m_DirtyTime < 0f) m_DirtyTime = Time.unscaledTime;
                var mouse = Mouse.current;
                bool dragging = mouse != null && mouse.leftButton.isPressed;
                if (!dragging || Time.unscaledTime - m_DirtyTime > 0.25f) { Rebuild(false); m_DirtyTime = -1f; }
            }

            var kb = Keyboard.current;
            bool typing = GUIUtility.keyboardControl != 0;
            PlayerCreatureController.InputEnabled = !typing;
            if (kb != null && !typing && kb.gKey.wasPressedThisFrame) DebugOverlay.Enabled = !DebugOverlay.Enabled;
            var m = Mouse.current;
            ThirdPersonCamera.LookEnabled = m == null || !GuiKit.IsOver(m.position.ReadValue());

            m_Controller.enabled = m_Mode == PreviewMode.Drive;
            if (m_Creature == null) return;
            if (m_Mode == PreviewMode.Idle)
            {
                m_Creature.desiredSpeed = 0f;
                m_Creature.moveInput = Vector3.zero;
                m_Creature.faceDirection = Vector3.zero;
                m_Creature.lookDirection = (m_Cam.transform.position - m_Creature.transform.position).normalized;
            }
            else if (m_Mode == PreviewMode.WalkLoop)
            {
                // Follow a circle of radius 5 around the turntable.
                Vector3 p = m_Creature.transform.position; p.y = 0f;
                float r = Mathf.Max(0.1f, p.magnitude);
                Vector3 radial = p / r;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                Vector3 dir = (tangent + radial * Mathf.Clamp((5f - r) * 0.6f, -1f, 1f)).normalized;
                m_Creature.moveInput = dir;
                m_Creature.desiredSpeed = m_LoopSpeed;
                m_Creature.faceDirection = Vector3.zero;
                m_Creature.lookDirection = dir;
            }
        }

        void Status(string s) { m_Status = s; m_StatusTime = Time.unscaledTime; }

        void OnGUI()
        {
            GuiKit.Begin();
            float W = GuiKit.Width, H = GuiKit.Height;
            EditorPanel(new Rect(20, 20, 420, H - 40));
            PreviewPanel(new Rect(W - 330, 20, 310, 300));
            LibraryPanel(new Rect(W - 330, 336, 310, Mathf.Min(H - 356, 380)));
            if (Time.unscaledTime - m_StatusTime < 3f && !string.IsNullOrEmpty(m_Status))
            {
                var r = new Rect(W * 0.5f - 200, H - 60, 400, 36);
                GUI.Box(r, GUIContent.none, GuiKit.panel);
                GUI.Label(new Rect(r.x, r.y + 9, r.width, 20), m_Status, new GUIStyle(GuiKit.small) { alignment = TextAnchor.MiddleCenter });
            }
            else
            {
                var r = new Rect(W * 0.5f - 250, H - 60, 500, 36);
                GUI.Box(r, GUIContent.none, GuiKit.panel);
                GUI.Label(new Rect(r.x, r.y + 9, r.width, 20), "Right-drag to orbit  ·  wheel to zoom  ·  G shows IK and footholds", new GUIStyle(GuiKit.small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        void EditorPanel(Rect r)
        {
            GuiKit.BeginPanel(r);
            GUILayout.Label("Creature Creator", GuiKit.title);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GuiKit.small, GUILayout.Width(50));
            m_Model.name = GUILayout.TextField(m_Model.name, 32, GuiKit.field);
            GUILayout.EndHorizontal();

            GuiKit.Header("Start from");
            GUILayout.BeginHorizontal();
            foreach (CreaturePreset p in System.Enum.GetValues(typeof(CreaturePreset)))
                if (GuiKit.Button(p.ToString(), m_Model.template == p)) { m_Model.ResetTo(p); Rebuild(true); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Randomize")) Randomize();
            if (GuiKit.Button("Reset sliders")) { var t = m_Model.template; string n = m_Model.name; m_Model.ResetTo(t); m_Model.name = n; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < s_Tabs.Length; i++) if (GuiKit.Button(s_Tabs[i], m_Tab == i)) m_Tab = i;
            GUILayout.EndHorizontal();
            m_Scroll = GUILayout.BeginScrollView(m_Scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            var tmpl = CreaturePresets.Get(m_Model.template);
            switch (m_Tab)
            {
                case 0:
                    GuiKit.Header("Body");
                    m_Model.bodyLength = GuiKit.Slider("Length", m_Model.bodyLength, 0.4f, 2.5f);
                    m_Model.bodyGirth = GuiKit.Slider("Girth", m_Model.bodyGirth, 0.4f, 2.2f);
                    m_Model.stanceHeight = GuiKit.Slider("Stance height", m_Model.stanceHeight, 0.4f, 1.3f);
                    GuiKit.Header("Head");
                    m_Model.hasHead = GuiKit.Toggle("Head and neck", m_Model.hasHead);
                    m_Model.headSize = GuiKit.Slider("Head size", m_Model.headSize, 0.4f, 2.5f);
                    m_Model.neckLength = GuiKit.Slider("Neck length", m_Model.neckLength, 0.2f, 2.5f);
                    GuiKit.Header("Tail");
                    m_Model.hasTail = GuiKit.Toggle("Tail / abdomen", m_Model.hasTail);
                    m_Model.tailLength = GuiKit.Slider("Tail length", m_Model.tailLength, 0.2f, 3f);
                    m_Model.tailThickness = GuiKit.Slider("Tail thickness", m_Model.tailThickness, 0.3f, 2.5f);
                    float stiff = m_Model.tailStiffness < 0f ? tmpl.tail.stiffness : m_Model.tailStiffness;
                    float ns = GuiKit.Slider("Tail stiffness", stiff, 0.05f, 1f);
                    if (Mathf.Abs(ns - stiff) > 1e-4f) m_Model.tailStiffness = ns;
                    break;
                case 1:
                    GuiKit.Header("Legs");
                    int pairs = m_Model.legPairs > 0 ? m_Model.legPairs : CreatorModel.CountLegPairs(tmpl);
                    int np = GuiKit.IntSlider("Leg pairs", pairs, 1, 10);
                    if (np != pairs) m_Model.legPairs = np;
                    int segs = m_Model.legSegments > 0 ? m_Model.legSegments : CreatorModel.LegSegments(tmpl);
                    int nsg = GuiKit.IntSlider("Joints per leg", segs, 2, 5);
                    if (nsg != segs) m_Model.legSegments = nsg;
                    m_Model.legLength = GuiKit.Slider("Leg length", m_Model.legLength, 0.3f, 2.5f);
                    m_Model.legThickness = GuiKit.Slider("Leg thickness", m_Model.legThickness, 0.3f, 2.5f);
                    m_Model.legSpread = GuiKit.Slider("Leg spread", m_Model.legSpread, 0.3f, 2f);
                    m_Model.kneeLift = GuiKit.Slider("Knee lift", m_Model.kneeLift, -1f, 2f);
                    GUILayout.Label("Legs are resampled along the body; each gets its own IK chain and foot planting.", GuiKit.muted);
                    GuiKit.Header("Arms / palps");
                    m_Model.hasArms = GuiKit.Toggle("Arms", m_Model.hasArms);
                    m_Model.armLength = GuiKit.Slider("Arm length", m_Model.armLength, 0.3f, 2f);
                    break;
                case 2:
                    GuiKit.Header("Speed");
                    m_Model.speed = GuiKit.Slider("Speed scale", m_Model.speed, 0.2f, 3f);
                    m_Model.stride = GuiKit.Slider("Stride", m_Model.stride, 0.4f, 2f);
                    m_Model.stepHeight = GuiKit.Slider("Step height", m_Model.stepHeight, 0.2f, 3f);
                    m_Model.duty = GuiKit.Slider("Ground contact", m_Model.duty, -0.3f, 0.3f);
                    m_Model.bob = GuiKit.Slider("Body bob", m_Model.bob, 0f, 3f);
                    GuiKit.Header("Gait pattern");
                    for (int row = 0; row < 2; row++)
                    {
                        GUILayout.BeginHorizontal();
                        for (int i = row * 4; i < row * 4 + 4; i++)
                            if (GuiKit.Button(s_GaitNames[i], m_Model.gaitOverride == i - 1)) m_Model.gaitOverride = i - 1;
                        GUILayout.EndHorizontal();
                    }
                    GuiKit.Header("Surfaces");
                    bool climbs = m_Model.climb < 0 ? tmpl.locomotion.canClimb : m_Model.climb == 1;
                    bool nc = GuiKit.Toggle("Climbs walls and ceilings", climbs);
                    if (nc != climbs) m_Model.climb = nc ? 1 : 0;
                    var gl = m_Creature != null ? m_Creature.def.locomotion : null;
                    if (gl != null)
                        GUILayout.Label($"Speed range {gl.minSpeed:0.0} to {gl.maxSpeed:0.0} m/s  ·  body height {gl.bodyHeight:0.00} m", GuiKit.muted);
                    break;
                case 3:
                    GuiKit.Header("Palette");
                    if (m_Model.palette == null || m_Model.palette.Length == 0) m_Model.palette = (Color[])tmpl.palette.Clone();
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < m_Model.palette.Length; i++)
                        if (GuiKit.Button(i < s_ColorNames.Length ? s_ColorNames[i] : "Color " + i, m_ColorSlot == i)) m_ColorSlot = i;
                    GUILayout.EndHorizontal();
                    m_ColorSlot = Mathf.Clamp(m_ColorSlot, 0, m_Model.palette.Length - 1);
                    m_Model.palette[m_ColorSlot] = GuiKit.ColorEditor(m_Model.palette[m_ColorSlot]);
                    GUILayout.Label("Sliders: hue, saturation, brightness.", GuiKit.muted);
                    GuiKit.Header("Skin");
                    GUILayout.BeginHorizontal();
                    foreach (SkinPattern p in System.Enum.GetValues(typeof(SkinPattern)))
                        if (GuiKit.Button(p.ToString(), m_Model.pattern == p)) m_Model.pattern = p;
                    GUILayout.EndHorizontal();
                    float sm = m_Model.smoothness < 0f ? tmpl.smoothness : m_Model.smoothness;
                    m_Model.smoothness = GuiKit.Slider("Gloss", sm, 0f, 1f);
                    break;
            }
            GUILayout.EndScrollView();
            GuiKit.EndPanel();
        }

        void PreviewPanel(Rect r)
        {
            GuiKit.BeginPanel(r);
            GUILayout.Label("Preview", GuiKit.title);
            GuiKit.Header("Mode");
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Idle", m_Mode == PreviewMode.Idle)) m_Mode = PreviewMode.Idle;
            if (GuiKit.Button("Walk loop", m_Mode == PreviewMode.WalkLoop)) m_Mode = PreviewMode.WalkLoop;
            if (GuiKit.Button("Drive", m_Mode == PreviewMode.Drive)) m_Mode = PreviewMode.Drive;
            GUILayout.EndHorizontal();
            if (m_Creature != null)
            {
                var loco = m_Creature.def.locomotion;
                if (m_Mode == PreviewMode.WalkLoop) m_LoopSpeed = GuiKit.Slider("Loop speed", m_LoopSpeed, loco.minSpeed, loco.maxSpeed, "0.0");
                else if (m_Mode == PreviewMode.Drive) GUILayout.Label("WASD to move, Shift sprint, Space jump (hold to jump higher). Try the stairs, wall and dome.", GuiKit.muted);
                GUILayout.Label($"{m_Creature.GaitName}  ·  {m_Creature.Speed:0.0} m/s  ·  {m_Creature.Legs.Count} legs", GuiKit.small);
            }
            GUILayout.Space(6);
            DebugOverlay.Enabled = GuiKit.Toggle("Show IK and footholds (G)", DebugOverlay.Enabled);
            GUILayout.Space(6);
            if (GuiKit.Button("Test in the city  ▶", true, GUILayout.Height(34)))
            {
                CreatureSession.Custom = m_Model.Build();
                CreatureSession.UseCustomOnNextLoad = true;
                CreatureSession.CreatorStateJson = JsonUtility.ToJson(m_Model);
                SceneManager.LoadScene(CreatureSession.SpiderScene);
            }
            GuiKit.EndPanel();
        }

        void LibraryPanel(Rect r)
        {
            GuiKit.BeginPanel(r);
            GuiKit.Header("Library");
            GUILayout.BeginHorizontal();
            if (GuiKit.Button("Save"))
            {
                string path = CreatureSession.Save(m_Model.Clone(), m_Model.Build());
                m_Saved = CreatureSession.ListSaved();
                Status("Saved to " + path);
            }
            if (GuiKit.Button("Copy JSON")) { GUIUtility.systemCopyBuffer = m_Model.Build().ToJson(); Status("Creature JSON copied to the clipboard"); }
            if (GuiKit.Button("Paste JSON")) PasteJson();
            GUILayout.EndHorizontal();
            m_LoadScroll = GUILayout.BeginScrollView(m_LoadScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (m_Saved.Count == 0) GUILayout.Label("No saved creatures yet.", GuiKit.muted);
            foreach (var name in m_Saved.ToArray())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, GuiKit.small, GUILayout.Width(150));
                if (GuiKit.Button("Load"))
                {
                    var saved = CreatureSession.Load(name);
                    if (saved != null && saved.model != null) { JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(saved.model), m_Model); Rebuild(true); Status("Loaded " + name); }
                    else Status("Could not load " + name);
                }
                if (GuiKit.Button("✕")) { CreatureSession.Delete(name); m_Saved = CreatureSession.ListSaved(); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Files: " + CreatureSession.SaveDirectory, GuiKit.muted);
            GuiKit.EndPanel();
        }

        void PasteJson()
        {
            try
            {
                var def = CreatureDefinition.FromJson(GUIUtility.systemCopyBuffer);
                if (def == null || def.limbs == null || def.body == null) { Status("Clipboard does not hold a creature"); return; }
                CreatureSession.Custom = def;
                m_Creature.Build(def);
                m_Creature.Teleport(m_Creature.transform.position + Vector3.up * 0.5f, m_Creature.transform.forward);
                Status($"Pasted '{def.name}'. It is set as the custom creature (6 in the demos).");
            }
            catch { Status("Clipboard does not hold valid creature JSON"); }
        }

        void Randomize()
        {
            var rng = new System.Random();
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var values = (CreaturePreset[])System.Enum.GetValues(typeof(CreaturePreset));
            var t = values[rng.Next(values.Length)];
            m_Model.ResetTo(t);
            m_Model.name = "Critter " + rng.Next(100, 999);
            m_Model.bodyLength = R(0.7f, 1.6f);
            m_Model.bodyGirth = R(0.7f, 1.5f);
            m_Model.legLength = R(0.7f, 1.5f);
            m_Model.legThickness = R(0.7f, 1.5f);
            m_Model.legSpread = R(0.8f, 1.4f);
            m_Model.headSize = R(0.7f, 1.6f);
            m_Model.tailLength = R(0.5f, 2f);
            if (t != CreaturePreset.Human && rng.NextDouble() < 0.6) m_Model.legPairs = rng.Next(2, 7);
            if (rng.NextDouble() < 0.4) m_Model.legSegments = rng.Next(2, 5);
            float hue = (float)rng.NextDouble();
            m_Model.palette[0] = Color.HSVToRGB(hue, R(0.3f, 0.8f), R(0.3f, 0.8f));
            m_Model.palette[1] = Color.HSVToRGB(Mathf.Repeat(hue + R(-0.1f, 0.1f), 1f), R(0.4f, 0.9f), R(0.2f, 0.6f));
            m_Model.palette[2] = Color.HSVToRGB(Mathf.Repeat(hue + 0.5f, 1f), R(0.5f, 0.9f), R(0.6f, 1f));
            m_Model.pattern = (SkinPattern)rng.Next(3);
            Rebuild(true);
        }
    }
}
