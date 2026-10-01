using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralMovement
{
    /// <summary>
    /// Draws the procedural machinery on top of the scene: IK chains, planted and swinging feet,
    /// rest (home) positions, predicted footholds, the surface normal and velocity.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        public static bool Enabled;
        public Creature target;
        Material m_Mat;

        void OnEnable() => RenderPipelineManager.endCameraRendering += OnEndCamera;
        void OnDisable() => RenderPipelineManager.endCameraRendering -= OnEndCamera;

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Enabled || target == null || cam.cameraType != CameraType.Game) return;
            if (m_Mat == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                m_Mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m_Mat.SetInt("_ZTest", (int)CompareFunction.Always);
                m_Mat.SetInt("_ZWrite", 0);
                m_Mat.SetInt("_Cull", (int)CullMode.Off);
                m_Mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m_Mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }
            m_Mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(GL.GetGPUProjectionMatrix(cam.projectionMatrix, false));
            GL.modelview = cam.worldToCameraMatrix;
            GL.Begin(GL.LINES);

            float s = Mathf.Max(0.05f, target.def.locomotion.bodyHeight * 0.12f);
            Vector3 up = target.Up;
            foreach (var leg in target.Legs)
            {
                var j = leg.chain.joints;
                for (int i = 0; i < j.Length - 1; i++) Line(j[i], j[i + 1], new Color(0.3f, 0.9f, 1f, 0.9f));
                for (int i = 1; i < j.Length - 1; i++) Cross(j[i], s * 0.3f, new Color(0.3f, 0.9f, 1f, 0.9f));
                Cross(leg.home, s * 0.5f, new Color(1f, 1f, 1f, 0.6f));
                if (leg.swinging)
                {
                    Cross(leg.foot, s, new Color(1f, 0.6f, 0.2f, 1f));
                    Cross(leg.target, s * 0.8f, new Color(1f, 0.95f, 0.2f, 1f));
                    Line(leg.foot, leg.target, new Color(1f, 0.95f, 0.2f, 0.5f));
                    // Remaining arc preview.
                    Vector3 prev = leg.foot;
                    for (int k = 1; k <= 8; k++)
                    {
                        float t = Mathf.Lerp(leg.swingT, 1f, k / 8f);
                        float e = t * t * (3f - 2f * t);
                        Vector3 p = Vector3.Lerp(leg.swingFrom, leg.target, e) + up * (leg.liftHeight * Mathf.Sin(t * Mathf.PI));
                        Line(prev, p, new Color(1f, 0.6f, 0.2f, 0.6f));
                        prev = p;
                    }
                }
                else
                {
                    Cross(leg.foot, s, leg.grounded ? new Color(0.3f, 1f, 0.4f, 1f) : new Color(1f, 0.2f, 0.2f, 1f));
                    Line(leg.foot, leg.foot + leg.footNormal * s * 2f, new Color(0.3f, 1f, 0.4f, 0.6f));
                }
            }
            foreach (var arm in target.Arms)
            {
                var j = arm.chain.joints;
                for (int i = 0; i < j.Length - 1; i++) Line(j[i], j[i + 1], new Color(0.8f, 0.5f, 1f, 0.9f));
            }
            Vector3 root = target.transform.position;
            Line(root, root + up * target.def.locomotion.bodyHeight * 1.6f, new Color(1f, 0.3f, 0.9f, 1f));
            Line(root, root + target.Motor.velocity * 0.4f, new Color(0.3f, 0.5f, 1f, 1f));
            Line(target.BodyTransform.position, target.BodyTransform.position + target.BodyTransform.forward * s * 4f, new Color(1f, 1f, 1f, 0.8f));

            GL.End();
            GL.PopMatrix();
        }

        static void Line(Vector3 a, Vector3 b, Color c)
        {
            GL.Color(c);
            GL.Vertex(a);
            GL.Vertex(b);
        }

        static void Cross(Vector3 p, float s, Color c)
        {
            Line(p - Vector3.right * s, p + Vector3.right * s, c);
            Line(p - Vector3.up * s, p + Vector3.up * s, c);
            Line(p - Vector3.forward * s, p + Vector3.forward * s, c);
        }
    }
}
