using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Player;

namespace AshenSol.Level
{
    // ------------------------------------------------------------------ moving platform
    /// <summary>A solid platform that shuttles between two points and carries whatever stands on it.</summary>
    public class MovingPlatform : MonoBehaviour
    {
        /// <summary>Every live platform, so traversal logic can find one to wait for.</summary>
        public static readonly List<MovingPlatform> All = new List<MovingPlatform>();

        public Vector2 A, B;
        public float Speed = 2f;
        public float WaitSeconds = 0.6f;

        Rigidbody2D body;
        BoxCollider2D box;
        float t, wait;
        int dir = 1;
        Vector2 lastPos;

        /// <summary>Current world velocity. Riders add this to their own instead of being teleported,
        /// which keeps the physics solver happy and keeps the ground check honest.</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>Middle of the surface you can stand on.</summary>
        public Vector2 TopCenter
        {
            get { return (Vector2)transform.position + new Vector2(0f, box != null ? box.size.y * 0.5f : 0.17f); }
        }
        public float HalfWidth { get { return box != null ? box.size.x * 0.5f : 1.5f; } }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static MovingPlatform Create(Vector2 a, Vector2 b, float width, float speed, Transform parent, float waitSeconds = 0.6f)
        {
            var go = new GameObject("MovingPlatform");
            go.layer = Layers.Ground;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = a;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("tile_platform");
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(width, 0.34f);
            sr.sortingOrder = SortOrder.Ground + 2;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.useFullKinematicContacts = true;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 0.34f);

            // a teal underglow so it reads as machinery, not scenery
            var glow = new GameObject("glow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            var gs = glow.AddComponent<SpriteRenderer>();
            gs.sprite = Res.Sprite("fx_glow");
            gs.material = MaterialLibrary.Additive;
            gs.color = Palette.Teal.WithAlpha(0.35f);
            gs.sortingOrder = SortOrder.Ground + 1;
            glow.transform.localScale = new Vector3(width * 0.6f, 0.7f, 1f);

            var mp = go.AddComponent<MovingPlatform>();
            mp.A = a; mp.B = b; mp.Speed = speed; mp.WaitSeconds = waitSeconds;
            mp.body = rb; mp.box = col; mp.lastPos = a;
            return mp;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (wait > 0f) { wait -= dt; lastPos = body.position; Velocity = Vector2.zero; return; }

            float len = Vector2.Distance(A, B);
            if (len < 0.01f) return;
            t += dir * (Speed / len) * dt;
            if (t >= 1f) { t = 1f; dir = -1; wait = WaitSeconds; }
            else if (t <= 0f) { t = 0f; dir = 1; wait = WaitSeconds; }

            Vector2 next = Vector2.Lerp(A, B, t);
            Velocity = (next - lastPos) / dt;
            body.MovePosition(next);
            lastPos = next;
        }
    }

    // ------------------------------------------------------------------ qi vent
    /// <summary>Forge exhaust. A COLUMN gives sustained lift while you stand in it; a PAD launches you
    /// once, hard. Both refresh the air dash, so they chain into the rest of the moveset.</summary>
    public class QiVent : MonoBehaviour
    {
        public enum Mode { Column, Pad }

        public Mode Kind = Mode.Column;
        public float Lift = 16f;          // column: target rise speed / pad: launch speed
        public float Accel = 60f;         // column only

        ParticleSystem stream;
        Light2D light;
        float padCooldown;

        public static QiVent Create(Vector2 basePos, float width, float height, Mode kind, Transform parent, float lift = 16f)
        {
            var go = new GameObject("QiVent_" + kind);
            go.layer = Layers.Trigger;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = basePos;

            // grate at the bottom
            var grate = new GameObject("grate");
            grate.transform.SetParent(go.transform, false);
            grate.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            var gsr = grate.AddComponent<SpriteRenderer>();
            gsr.sprite = Res.Sprite("prop_vent");
            gsr.sortingOrder = SortOrder.GroundDecor + 2;
            grate.transform.localScale = new Vector3(width / Mathf.Max(0.01f, gsr.sprite.bounds.size.x), 1f, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(width, height);
            col.offset = new Vector2(0f, height * 0.5f);

            var v = go.AddComponent<QiVent>();
            v.Kind = kind;
            v.Lift = lift;
            v.stream = MakeStream(go.transform, width, height, kind);
            v.light = LevelDecor.Light(go.transform, new Vector2(0f, 0.6f), Palette.Teal, 1.4f, Mathf.Max(2.5f, width * 1.6f));
            return v;
        }

        static ParticleSystem MakeStream(Transform parent, float width, float height, Mode kind)
        {
            var go = new GameObject("stream");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(height / 7f, height / 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Palette.Teal.WithAlpha(0.7f), Color.Lerp(Palette.Teal, Color.white, 0.5f).WithAlpha(0.5f));
            main.gravityModifier = -0.02f;
            main.useUnscaledTime = true;   // the forge breathes through hit-stop
            var em = ps.emission; em.enabled = true; em.rateOverTime = kind == Mode.Column ? 34f : 16f;
            var sh = ps.shape;
            sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(width * 0.8f, 0.1f, 1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = VFX.VfxManager.AdditiveFor(Res.Sprite("fx_glow"));
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2.2f;
            r.velocityScale = 0.06f;
            r.sortingOrder = SortOrder.Fx - 3;
            ps.Play();
            return ps;
        }

        void Update()
        {
            if (padCooldown > 0f) padCooldown -= Time.deltaTime;
            if (light != null) light.intensity = 1.2f + 0.35f * Mathf.Sin(Time.time * 6f);
        }

        void OnTriggerStay2D(Collider2D other) { Affect(other); }
        void OnTriggerEnter2D(Collider2D other) { Affect(other); }

        void Affect(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null || !p.IsAlive) return;

            if (Kind == Mode.Column) p.ApplyUpdraft(Lift, Accel);
            else if (padCooldown <= 0f)
            {
                padCooldown = 0.35f;
                p.Launch(Lift);
                Services.Audio.PlaySfxAt("dash", transform.position, 0.9f);
                Services.Vfx.Shockwave(transform.position, 2.2f, Palette.Teal);
                Services.Vfx.FlashLight(transform.position, Palette.Teal, 3f, 4f, 0.25f);
                Services.Cam.Shake(0.2f);
            }
        }
    }

    // ------------------------------------------------------------------ climbable wall
    /// <summary>Chain-and-lattice face the player can cling to and climb.</summary>
    public class ClimbSurface : MonoBehaviour
    {
        public float Top, Bottom;

        public static ClimbSurface Create(Vector2 basePos, float height, Transform parent, float width = 0.9f)
        {
            var go = new GameObject("ClimbSurface");
            go.layer = Layers.Trigger;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = basePos;

            // the visual lives on a CHILD: moving the root would drag the trigger up with it
            var visual = new GameObject("chains");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite("prop_climb");
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            // tile vertically ONLY: sizing wider than the sprite drew a second ladder next to it
            sr.size = new Vector2(sr.sprite.bounds.size.x, height);
            sr.sortingOrder = SortOrder.GroundDecor + 1;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(width + 0.7f, height);
            col.offset = new Vector2(0f, height * 0.5f);

            var c = go.AddComponent<ClimbSurface>();
            c.Bottom = basePos.y;
            c.Top = basePos.y + height;
            LevelDecor.Light(go.transform, new Vector2(0f, height * 0.5f), Palette.TealDeep, 0.5f, 2.5f);
            return c;
        }

        void OnTriggerEnter2D(Collider2D other) { Offer(other, true); }
        void OnTriggerStay2D(Collider2D other) { Offer(other, false); }
        void OnTriggerExit2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null) p.LeaveClimb(this);
        }

        void Offer(Collider2D other, bool entered)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null && p.IsAlive) p.OfferClimb(this);
        }
    }
}
