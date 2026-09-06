using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Player;

namespace AshenSol.Boss
{
    /// <summary>Unblockable wave travelling along the floor after the boss slam. Jump over it.</summary>
    public class GroundShockwave : MonoBehaviour
    {
        public static readonly List<GroundShockwave> Active = new List<GroundShockwave>();

        public static GroundShockwave Spawn(Vector2 pos, int dir, float speed, int damage, float life, Transform parent)
        {
            var go = new GameObject("GroundShockwave");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var w = go.AddComponent<GroundShockwave>();
            w.Velocity = new Vector2(dir * speed, 0f);
            w.damage = damage; w.life = life; w.dir = dir;
            var srGo = new GameObject("sprite");
            srGo.transform.SetParent(go.transform, false);
            srGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            w.sr = srGo.AddComponent<SpriteRenderer>();
            w.sr.sprite = Res.Sprite("fx_shockwave");
            w.sr.material = MaterialLibrary.Additive;
            w.sr.color = Palette.Red;
            w.sr.sortingOrder = SortOrder.Fx;
            srGo.transform.localScale = new Vector3(0.9f, 1.1f, 1f);
            var gGo = new GameObject("glow");
            gGo.transform.SetParent(go.transform, false);
            gGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            w.glow = gGo.AddComponent<SpriteRenderer>();
            w.glow.sprite = Res.Sprite("fx_glow");
            w.glow.material = MaterialLibrary.Additive;
            w.glow.color = Palette.Red.WithAlpha(0.7f);
            w.glow.sortingOrder = SortOrder.Fx - 1;
            gGo.transform.localScale = Vector3.one * 2.2f;
            var lGo = new GameObject("light");
            lGo.transform.SetParent(go.transform, false);
            lGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            w.light = lGo.AddComponent<Light2D>();
            w.light.lightType = Light2D.LightType.Point;
            w.light.color = Palette.Red;
            w.light.intensity = 1.6f;
            w.light.pointLightOuterRadius = 2.5f;
            Active.Add(w);
            return w;
        }

        public static bool AnyApproaching(Vector2 p, float within)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var w = Active[i];
                if (w == null) continue;
                Vector2 d = (Vector2)w.transform.position - p;
                if (Mathf.Abs(d.x) < within && Mathf.Abs(d.y) < 1.6f && Mathf.Sign(w.Velocity.x) == -Mathf.Sign(d.x)) return true;
            }
            return false;
        }

        public static void ClearAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--) if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }

        public Vector2 Velocity { get; private set; }
        int damage, dir; float life, dustTimer, t; bool hit;
        SpriteRenderer sr, glow; Light2D light;

        void OnDestroy() { Active.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt; life -= dt;
            transform.position += (Vector3)(Velocity * dt);
            float pulse = 1f + 0.15f * Mathf.Sin(t * 30f);
            sr.transform.localScale = new Vector3(0.9f * pulse, 1.1f, 1f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 40f) * 4f);
            float fade = Mathf.Clamp01(life / 0.3f);
            sr.color = Palette.Red.WithAlpha(fade);
            glow.color = Palette.Red.WithAlpha(0.7f * fade);
            light.intensity = 1.6f * fade;

            dustTimer -= dt;
            if (dustTimer <= 0f) { dustTimer = 0.07f; Services.Vfx.DustPuff(transform.position, 0.8f); }

            Vector2 pos = transform.position;
            if (!hit)
            {
                var col = Physics2D.OverlapBox(pos + new Vector2(0f, 0.45f), new Vector2(0.9f, 0.85f), 0f, Layers.PlayerMask);
                if (col != null)
                {
                    var p = col.GetComponentInParent<PlayerController>();
                    if (p != null && p.IsAlive)
                    {
                        var info = new AttackInfo { Source = gameObject, Team = Team.Enemy, Damage = damage, Origin = pos, HitPoint = p.Center, Kind = AttackKind.Unblockable, Knockback = 6f, Tag = "boss_shockwave" };
                        var o = p.ReceiveAttack(info);
                        if (o == HitOutcome.Hit || o == HitOutcome.Blocked) { hit = true; life = Mathf.Min(life, 0.15f); }
                    }
                }
            }
            bool ground = Physics2D.Raycast(pos + new Vector2(0f, 0.5f), Vector2.down, 1.1f, 1 << Layers.Ground).collider != null;
            bool wall = Physics2D.Raycast(pos + new Vector2(0f, 0.4f), new Vector2(dir, 0f), 0.7f, 1 << Layers.Ground).collider != null;
            if (!ground || wall) life = Mathf.Min(life, 0.1f);
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
