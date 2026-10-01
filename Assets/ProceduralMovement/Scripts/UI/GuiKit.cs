using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Styled immediate-mode GUI helpers: rounded translucent panels, scaled for the screen size.</summary>
    public static class GuiKit
    {
        public static readonly Color Accent = new Color(1f, 0.62f, 0.26f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Muted = new Color(0.66f, 0.69f, 0.74f);

        static bool s_Init;
        static Texture2D s_Panel, s_Button, s_ButtonHover, s_ButtonOn, s_Bar, s_White, s_Field, s_Thumb;
        public static GUIStyle panel, title, label, small, muted, button, buttonOn, header, field, sliderStyle, thumb, toggle, value;
        public static float Scale { get; private set; } = 1f;
        static readonly List<Rect> s_Blocking = new List<Rect>();

        static Texture2D Rounded(int size, int radius, Color fill, Color border)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float coverage = Mathf.Clamp01(radius - d + 0.5f);
                    float edge = Mathf.Clamp01(d - (radius - 1.5f));
                    var rim = new Color(border.r, border.g, border.b, Mathf.Max(fill.a, border.a));
                    Color c = Color.Lerp(fill, rim, edge * Mathf.Clamp01(border.a * 3f));
                    px[y * size + x] = new Color(c.r, c.g, c.b, c.a * coverage);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        static void Init()
        {
            if (s_Init && s_Panel != null) return;
            s_Init = true;
            s_Panel = Rounded(32, 10, new Color(0.07f, 0.08f, 0.1f, 0.84f), new Color(1f, 1f, 1f, 0.08f));
            s_Button = Rounded(24, 7, new Color(0.2f, 0.22f, 0.26f, 0.95f), new Color(1, 1, 1, 0.06f));
            s_ButtonHover = Rounded(24, 7, new Color(0.28f, 0.3f, 0.35f, 0.95f), new Color(1, 1, 1, 0.1f));
            s_ButtonOn = Rounded(24, 7, new Color(Accent.r * 0.9f, Accent.g * 0.9f, Accent.b * 0.9f, 1f), new Color(1, 1, 1, 0.15f));
            s_Bar = Rounded(12, 5, new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0f));
            s_Field = Rounded(16, 5, new Color(0.14f, 0.15f, 0.18f, 1f), new Color(1, 1, 1, 0.08f));
            s_Thumb = Rounded(16, 8, new Color(0.95f, 0.95f, 0.97f, 1f), new Color(0, 0, 0, 0.2f));
            s_White = Solid(Color.white);

            panel = new GUIStyle { normal = { background = s_Panel }, border = new RectOffset(12, 12, 12, 12), padding = new RectOffset(16, 16, 14, 14) };
            title = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Text } };
            header = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Accent }, margin = new RectOffset(0, 0, 10, 4) };
            label = new GUIStyle { fontSize = 14, normal = { textColor = Text }, wordWrap = true };
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight, normal = { textColor = Muted } };
            small = new GUIStyle { fontSize = 12, normal = { textColor = Text }, wordWrap = true };
            muted = new GUIStyle { fontSize = 12, normal = { textColor = Muted }, wordWrap = true };
            button = new GUIStyle
            {
                fontSize = 13, alignment = TextAnchor.MiddleCenter, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(2, 2, 2, 2),
                normal = { background = s_Button, textColor = Text }, hover = { background = s_ButtonHover, textColor = Color.white },
                active = { background = s_ButtonOn, textColor = Color.white },
            };
            buttonOn = new GUIStyle(button) { normal = { background = s_ButtonOn, textColor = new Color(0.1f, 0.07f, 0.03f) }, hover = { background = s_ButtonOn, textColor = new Color(0.1f, 0.07f, 0.03f) } };
            field = new GUIStyle
            {
                fontSize = 13, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(8, 8, 5, 5), margin = new RectOffset(2, 2, 2, 2),
                normal = { background = s_Field, textColor = Text }, focused = { background = s_Field, textColor = Color.white },
            };
            sliderStyle = new GUIStyle { normal = { background = s_Bar }, border = new RectOffset(5, 5, 5, 5), fixedHeight = 8, margin = new RectOffset(4, 4, 9, 9) };
            thumb = new GUIStyle { normal = { background = s_Thumb }, hover = { background = s_Thumb }, active = { background = s_Thumb }, fixedWidth = 16, fixedHeight = 16, margin = new RectOffset(0, 0, -4, 0) };
            toggle = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft };
        }

        /// <summary>Call at the top of OnGUI. Scales the UI to the screen and resets the input-blocking rects.</summary>
        public static void Begin()
        {
            Init();
            Scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            if (Event.current.type == EventType.Layout) s_Blocking.Clear();
        }

        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        /// <summary>Registers a rect (in GUI coordinates) that should swallow mouse clicks meant for the world.</summary>
        public static void Block(Rect r)
        {
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) s_Blocking.Add(r);
        }

        /// <summary>True if the mouse (screen coordinates, bottom-left origin) is over any panel.</summary>
        public static bool IsOver(Vector2 screenPos)
        {
            Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y) / Scale;
            foreach (var r in s_Blocking) if (r.Contains(gui)) return true;
            return false;
        }

        public static Rect BeginPanel(Rect r)
        {
            Block(r);
            GUI.Box(r, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 14, r.width - 32, r.height - 28));
            return r;
        }

        public static void EndPanel() => GUILayout.EndArea();

        public static bool Button(string text, bool on = false, params GUILayoutOption[] opts) => GUILayout.Button(text, on ? buttonOn : button, opts);

        public static float Slider(string name, float v, float min, float max, string format = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(120));
            float r = GUILayout.HorizontalSlider(v, min, max, sliderStyle, thumb, GUILayout.ExpandWidth(true));
            GUILayout.Label(r.ToString(format), value, GUILayout.Width(48));
            GUILayout.EndHorizontal();
            return r;
        }

        public static int IntSlider(string name, int v, int min, int max) => Mathf.RoundToInt(Slider(name, v, min, max, "0"));

        public static bool Toggle(string name, bool v)
        {
            if (GUILayout.Button((v ? "◉  " : "○  ") + name, v ? buttonOn : toggle)) v = !v;
            return v;
        }

        public static void Bar(Rect r, float t, Color c)
        {
            GUI.DrawTexture(r, s_Bar, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, 0.5f), 0, 4);
            var f = new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height);
            GUI.DrawTexture(f, s_White, ScaleMode.StretchToFill, true, 0, c, 0, 4);
        }

        public static void Fill(Rect r, Color c) => GUI.DrawTexture(r, s_White, ScaleMode.StretchToFill, true, 0, c, 0, 0);

        public static void Header(string text) => GUILayout.Label(text.ToUpperInvariant(), header);

        public static Color ColorEditor(Color c)
        {
            GUILayout.BeginHorizontal();
            var sw = GUILayoutUtility.GetRect(26, 26, GUILayout.Width(26));
            GUI.DrawTexture(sw, s_White, ScaleMode.StretchToFill, true, 0, c, 0, 6);
            GUILayout.BeginVertical();
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = GUILayout.HorizontalSlider(h, 0f, 1f, sliderStyle, thumb);
            s = GUILayout.HorizontalSlider(s, 0f, 1f, sliderStyle, thumb);
            v = GUILayout.HorizontalSlider(v, 0.02f, 1f, sliderStyle, thumb);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return Color.HSVToRGB(h, s, v);
        }
    }
}
