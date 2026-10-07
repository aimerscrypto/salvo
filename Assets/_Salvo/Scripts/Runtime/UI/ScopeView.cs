using UnityEngine;
using UnityEngine.UIElements;

namespace Salvo
{
    /// <summary>
    /// Sniper scope drawn as vector shapes at screen resolution (a stretched texture went soft and
    /// oval): black surround with a perfectly round eyepiece, darkened lens edge, thin crosshair
    /// with thick duplex posts, mil dots and a small red center dot.
    /// </summary>
    public class ScopeView : VisualElement
    {
        static readonly Color Black = new Color(0.01f, 0.01f, 0.012f, 1f);
        static readonly Color Line = new Color(0.02f, 0.02f, 0.02f, 0.95f);
        static readonly Color Dot = new Color(0.95f, 0.16f, 0.1f, 1f);

        public ScopeView()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        void Draw(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            float w = rect.width, h = rect.height;
            if (w <= 1f || h <= 1f) return;
            var c = rect.center;
            float R = h * 0.475f;
            float px = Mathf.Max(1f, h / 720f);   // one 720p pixel at any resolution
            var p = mgc.painter2D;

            // eyepiece: the screen with a round hole
            p.fillColor = Black;
            p.BeginPath();
            p.MoveTo(new Vector2(-2f, -2f)); p.LineTo(new Vector2(w + 2f, -2f)); p.LineTo(new Vector2(w + 2f, h + 2f)); p.LineTo(new Vector2(-2f, h + 2f));
            p.ClosePath();
            p.MoveTo(new Vector2(c.x + R, c.y));
            p.Arc(c, R, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);

            // lens edge falloff
            const int rings = 32;
            for (int i = 0; i < rings; i++)
            {
                float k = 1f - i / (float)rings;
                p.strokeColor = new Color(0f, 0f, 0f, 0.22f * k * k);
                p.lineWidth = R * 0.006f + 1f;
                p.BeginPath();
                p.Arc(c, R - i * R * 0.005f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }

            // thin crosshair across the lens
            p.strokeColor = Line;
            p.lineWidth = 1.4f * px;
            p.lineCap = LineCap.Butt;
            Seg(p, new Vector2(c.x - R, c.y), new Vector2(c.x + R, c.y));
            Seg(p, new Vector2(c.x, c.y - R), new Vector2(c.x, c.y + R));

            // duplex posts: thick from the rim to 55% of the radius (left, right, bottom)
            p.lineWidth = 6f * px;
            float inner = R * 0.55f;
            Seg(p, new Vector2(c.x - R, c.y), new Vector2(c.x - inner, c.y));
            Seg(p, new Vector2(c.x + inner, c.y), new Vector2(c.x + R, c.y));
            Seg(p, new Vector2(c.x, c.y + inner), new Vector2(c.x, c.y + R));

            // mil dots on the thin section
            p.fillColor = Line;
            for (int i = 1; i <= 4; i++)
            {
                float d = inner * i / 5f;
                Circle(p, new Vector2(c.x - d, c.y), 2.2f * px); Circle(p, new Vector2(c.x + d, c.y), 2.2f * px);
                Circle(p, new Vector2(c.x, c.y - d), 2.2f * px); Circle(p, new Vector2(c.x, c.y + d), 2.2f * px);
            }

            // center dot
            p.fillColor = Dot;
            Circle(p, c, 2.6f * px);
        }

        static void Seg(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        static void Circle(Painter2D p, Vector2 at, float r)
        {
            p.BeginPath();
            p.Arc(at, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
