using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Verlet ribbon (sash / cape) drawn as one tapered strip through the simulated points.
    /// Simulated in fixed sub-steps so it hangs and swings the same at every frame rate.</summary>
    public class SashChain
    {
        const float Step = 1f / 120f;
        const int Iterations = 3;
        const float MaxCatchUp = 0.1f;     // never simulate more than this per frame (hitches, hit-stop exits)
        const int MaxSteps = 16;

        Transform anchor;
        Vector2[] pos, prev;
        Vector3[] points;
        LineRenderer line;
        float segLen;
        Color color;
        float widthStart, widthEnd;
        Transform holder;
        bool visible = true;
        float time, acc;
        float alpha = 1f;

        /// <summary>Downward pull, units per second squared.</summary>
        public float Gravity = 24f;
        /// <summary>Velocity kept per sub-step (at 120 Hz).</summary>
        public float Damping = 0.95f;
        /// <summary>Air drag against the body's own velocity: the cloth streams behind a runner and lifts on a fall.</summary>
        public float Drag = 2.0f;
        /// <summary>Steady breeze against the facing direction, units per second squared.</summary>
        public float Wind = 1.6f;

        public static SashChain Build(Transform parent, Transform anchor, int segments, Color color, string sprite, float segLen, int sortOrder, float scaleStart, float scaleEnd)
        {
            var s = new SashChain();
            s.anchor = anchor; s.segLen = segLen; s.color = color;
            s.holder = new GameObject("Sash").transform;
            s.holder.SetParent(parent, false);
            s.pos = new Vector2[segments];
            s.prev = new Vector2[segments];
            s.points = new Vector3[segments];
            var sp = Res.Sprite(sprite);
            float w = sp != null ? sp.bounds.size.x : 0.12f;
            s.widthStart = w * scaleStart * 1.15f;
            s.widthEnd = w * scaleEnd * 0.9f;

            var go = new GameObject("ribbon");
            go.transform.SetParent(s.holder, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = segments;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = AshenSol.VFX.VfxManager.UnlitFor(sp);
            line.sortingOrder = sortOrder;
            line.widthCurve = AnimationCurve.Linear(0f, s.widthStart, 1f, s.widthEnd);
            line.widthMultiplier = 1f;
            s.line = line;
            s.ApplyColor();
            s.Reset();
            return s;
        }

        public void Reset()
        {
            Vector2 a = anchor.position;
            for (int i = 0; i < pos.Length; i++) { pos[i] = a + new Vector2(0f, -segLen * i); prev[i] = pos[i]; }
            acc = 0f;
            Place();
        }

        public void SetVisible(bool v)
        {
            visible = v;
            if (line != null) line.enabled = v;
        }

        /// <summary>Overall opacity (i-frame pulse).</summary>
        public void SetAlpha(float a)
        {
            a = Mathf.Clamp01(a);
            if (Mathf.Abs(a - alpha) < 0.001f) return;
            alpha = a;
            ApplyColor();
        }

        void ApplyColor()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(color.a * alpha, 0f), new GradientAlphaKey(color.a * alpha * 0.9f, 0.6f), new GradientAlphaKey(color.a * alpha * 0.7f, 1f) });
            line.colorGradient = g;
        }

        public void Tick(float dt, int facing, Vector2 bodyVelocity)
        {
            if (dt <= 0f) return;
            Vector2 a = anchor.position;
            acc += Mathf.Min(dt, MaxCatchUp);
            int steps = 0;
            while (acc >= Step && steps < MaxSteps)
            {
                acc -= Step; steps++;
                time += Step;
                Substep(a, facing, bodyVelocity);
            }
            // the root rides the shoulder even on frames without a sub-step
            pos[0] = a; prev[0] = a;
            Place();
        }

        void Substep(Vector2 a, int facing, Vector2 bodyVelocity)
        {
            pos[0] = a; prev[0] = a;
            Vector2 force = new Vector2(-facing * (Wind + Mathf.Sin(time * 5f) * Wind * 0.6f), Mathf.Sin(time * 3.1f) * 0.5f)
                          + new Vector2(0f, -Gravity)
                          - bodyVelocity * Drag;
            float s2 = Step * Step;
            for (int i = 1; i < pos.Length; i++)
            {
                Vector2 vel = (pos[i] - prev[i]) * Damping;
                prev[i] = pos[i];
                pos[i] += vel + force * s2;
            }
            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 1; i < pos.Length; i++)
                {
                    Vector2 d = pos[i] - pos[i - 1];
                    float len = d.magnitude;
                    if (len < 0.0001f) continue;
                    Vector2 corr = d * ((len - segLen) / len);
                    if (i - 1 == 0) pos[i] -= corr;
                    else { pos[i - 1] += corr * 0.5f; pos[i] -= corr * 0.5f; }
                }
            }
        }

        void Place()
        {
            for (int i = 0; i < pos.Length; i++) points[i] = new Vector3(pos[i].x, pos[i].y, 0f);
            line.SetPositions(points);
        }
    }
}
