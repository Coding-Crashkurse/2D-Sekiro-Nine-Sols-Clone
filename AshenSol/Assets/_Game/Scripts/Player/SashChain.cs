using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Player
{
    /// <summary>Verlet ribbon (sash / cape) made of sprite segments, simulated in world space.</summary>
    public class SashChain
    {
        Transform anchor;
        Vector2[] pos, prev;
        SpriteRenderer[] segs;
        float segLen;
        Color color;
        float scaleStart, scaleEnd;
        Transform holder;
        bool visible = true;
        float time;

        public float Gravity = 6f;
        public float Damping = 0.92f;
        public float Wind = 0.6f;

        public static SashChain Build(Transform parent, Transform anchor, int segments, Color color, string sprite, float segLen, int sortOrder, float scaleStart, float scaleEnd)
        {
            var s = new SashChain();
            s.anchor = anchor; s.segLen = segLen; s.color = color; s.scaleStart = scaleStart; s.scaleEnd = scaleEnd;
            s.holder = new GameObject("Sash").transform;
            s.holder.SetParent(parent, false);
            s.pos = new Vector2[segments];
            s.prev = new Vector2[segments];
            s.segs = new SpriteRenderer[segments];
            var sp = Res.Sprite(sprite);
            for (int i = 0; i < segments; i++)
            {
                var go = new GameObject("seg" + i);
                go.transform.SetParent(s.holder, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.color = color;
                sr.sortingOrder = sortOrder;
                s.segs[i] = sr;
            }
            s.Reset();
            return s;
        }

        public void Reset()
        {
            Vector2 a = anchor.position;
            for (int i = 0; i < pos.Length; i++) { pos[i] = a + new Vector2(0f, -segLen * i); prev[i] = pos[i]; }
            Place();
        }

        public void SetVisible(bool v)
        {
            visible = v;
            for (int i = 0; i < segs.Length; i++) segs[i].enabled = v;
        }

        public void Tick(float dt, int facing, Vector2 bodyVelocity)
        {
            if (dt <= 0f) return;
            time += dt;
            Vector2 a = anchor.position;
            pos[0] = a; prev[0] = a;
            Vector2 wind = new Vector2(-facing * (Wind + Mathf.Sin(time * 5f) * 0.4f) - bodyVelocity.x * 0.15f, Mathf.Sin(time * 3.1f) * 0.3f);
            float dt2 = dt * dt;
            for (int i = 1; i < pos.Length; i++)
            {
                Vector2 vel = (pos[i] - prev[i]) * Damping;
                prev[i] = pos[i];
                pos[i] += vel + (new Vector2(0f, -Gravity) + wind * 4f) * dt2 * 60f * 0.5f;
            }
            for (int iter = 0; iter < 3; iter++)
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
            Place();
        }

        void Place()
        {
            for (int i = 0; i < segs.Length; i++)
            {
                float f = (float)i / Mathf.Max(1, segs.Length - 1);
                var t = segs[i].transform;
                t.position = new Vector3(pos[i].x, pos[i].y, 0f);
                Vector2 dir = i < pos.Length - 1 ? pos[i + 1] - pos[i] : pos[i] - pos[i - 1];
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
                t.rotation = Quaternion.Euler(0f, 0f, ang);
                float sc = Mathf.Lerp(scaleStart, scaleEnd, f);
                t.localScale = new Vector3(sc, sc * 1.3f, 1f);
                segs[i].color = new Color(color.r, color.g, color.b, color.a * Mathf.Lerp(1f, 0.75f, f));
            }
        }
    }
}
