using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Player;

namespace AshenSol.Enemies
{
    /// <summary>Straight-flying bolt. Enemy bolts can be perfect-parried, which reflects them at the attacker.</summary>
    public class Projectile : MonoBehaviour, IReflectable
    {
        public static readonly List<Projectile> Active = new List<Projectile>();

        public static Projectile Fire(Vector2 pos, Vector2 velocity, Team team, int damage, AttackKind kind, Color color, string tag, float lifetime = 4f, float radius = 0.18f)
        {
            var go = new GameObject("Projectile_" + tag);
            go.layer = Layers.Projectile;
            go.transform.position = pos;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = radius;

            var sprGo = new GameObject("sprite");
            sprGo.transform.SetParent(go.transform, false);
            var sr = sprGo.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("fx_bolt");
            sr.material = MaterialLibrary.Additive;
            sr.color = color;
            sr.sortingOrder = SortOrder.Projectile;
            sprGo.transform.localScale = Vector3.one * (radius / 0.18f);

            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = 1.2f;
            l.pointLightOuterRadius = 1.4f;
            l.pointLightInnerRadius = 0.05f;

            var p = go.AddComponent<Projectile>();
            p.Team = team; p.Velocity = velocity; p.damage = damage; p.kind = kind; p.color = color; p.tag = tag; p.life = lifetime;
            p.body = rb; p.sr = sr; p.light = l;
            p.Orient();
            Services.Vfx.ProjectileTrail(go.transform, color);
            Active.Add(p);
            return p;
        }

        public static void ClearAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Destroy(Active[i].gameObject);
            Active.Clear();
        }

        public Team Team { get; private set; }
        public Vector2 Velocity { get; private set; }
        public int Damage { get { return damage; } }

        int damage; AttackKind kind; Color color; new string tag; float life;
        Rigidbody2D body; SpriteRenderer sr; Light2D light; bool dead;

        void OnDestroy() { Active.Remove(this); }

        void FixedUpdate()
        {
            if (dead) return;
            body.MovePosition(body.position + Velocity * Time.fixedDeltaTime);
            life -= Time.fixedDeltaTime;
            if (life <= 0f) Kill(false);
        }

        void Orient()
        {
            float ang = Mathf.Atan2(Velocity.y, Velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (dead) return;
            int layer = other.gameObject.layer;
            if (layer == Layers.Ground)
            {
                Services.Vfx.HitSpark(transform.position, -Velocity.normalized, color, 0.7f);
                Kill(true);
                return;
            }
            if (Team == Team.Enemy && layer == Layers.Player)
            {
                var p = other.GetComponentInParent<PlayerController>();
                if (p == null || !p.IsAlive) return;
                var info = new AttackInfo
                {
                    Source = gameObject, Team = Team.Enemy, Damage = damage, Origin = transform.position, HitPoint = transform.position,
                    Kind = kind, Knockback = 4f, IsProjectile = true, Payload = this, Tag = tag
                };
                var outcome = p.ReceiveAttack(info);
                if (outcome == HitOutcome.Parried || outcome == HitOutcome.Dodged) return;
                Services.Vfx.HitSpark(transform.position, -Velocity.normalized, color, 0.8f);
                Kill(true);
            }
            else if (Team == Team.Player && layer == Layers.Enemy)
            {
                var d = other.GetComponentInParent<IDamageable>();
                if (d == null || !d.IsAlive || d.Team != Team.Enemy) return;
                var info = new AttackInfo
                {
                    Source = gameObject, Team = Team.Player, Damage = damage, Origin = transform.position, HitPoint = transform.position,
                    Kind = AttackKind.Parryable, Knockback = 3f, IsProjectile = true, Payload = this, Tag = tag + "_reflected"
                };
                d.ReceiveAttack(info);
                Services.Vfx.HitSpark(transform.position, Velocity.normalized, Palette.Teal, 1.2f);
                HitStop.Request(0.04f);
                Kill(true);
            }
        }

        public void Reflect(Vector2 newDirection, Team newTeam, int newDamage)
        {
            if (dead) return;
            Team = newTeam;
            damage = newDamage;
            Velocity = newDirection.normalized * Velocity.magnitude * 1.5f;
            life = 4f;
            color = Palette.Teal;
            sr.color = color;
            light.color = color;
            light.intensity = 2f;
            Orient();
            Services.Vfx.ProjectileTrail(transform, color);
            Services.Vfx.HitSpark(transform.position, Velocity.normalized, Palette.Teal, 1f);
            Services.Audio.PlaySfx("projectile_reflect");
            GameEvents.RaiseLog("projectile reflected: " + tag);
        }

        void Kill(bool effects)
        {
            if (dead) return;
            dead = true;
            Active.Remove(this);
            Destroy(gameObject);
        }
    }
}
