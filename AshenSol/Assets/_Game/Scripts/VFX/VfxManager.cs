using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.VFX
{
    /// <summary>All visual feedback: particle bursts, tweened sprites, lights, ghosts, screen effects, ambient systems.</summary>
    public class VfxManager : MonoBehaviour, IVfxService
    {
        public static VfxManager Instance { get; private set; }

        Transform fxRoot;
        readonly List<SpriteFx> spritePool = new List<SpriteFx>();
        readonly List<SlashFx> slashPool = new List<SlashFx>();
        readonly List<LightFx> lightPool = new List<LightFx>();
        readonly List<GhostFx> ghostPool = new List<GhostFx>();
        readonly List<FloatingTextFx> textPool = new List<FloatingTextFx>();
        static readonly Dictionary<Texture, Material> additiveCache = new Dictionary<Texture, Material>();
        static readonly Dictionary<Texture, Material> unlitCache = new Dictionary<Texture, Material>();

        ParticleSystem sparks, hitSparks, ink, dust, embers;
        SpriteRenderer screenQuad; Coroutine screenFlashCo, chromaCo;
        Font font;

        void Awake()
        {
            Instance = this;
            Services.Vfx = this;
            fxRoot = new GameObject("FX").transform;
            fxRoot.SetParent(transform, false);
            font = Resources.Load<Font>("Fonts/Cinzel-Regular");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            sparks = MakeSystem("Sparks", Res.Sprite("fx_spark"), true, true, 0.6f, SortOrder.Fx + 6, 1.6f, 0.05f);
            hitSparks = MakeSystem("SwordSparks", Res.Sprite("fx_glow"), true, true, 0.3f, SortOrder.Fx + 6, 1.1f, 0.025f);
            // impact sparks fly during the hit-stop: the world may freeze, the hit must not
            var sparksMain = sparks.main; sparksMain.useUnscaledTime = true;
            var hitMain = hitSparks.main; hitMain.useUnscaledTime = true;
            var sparkSize = hitSparks.sizeOverLifetime;
            sparkSize.enabled = true;
            sparkSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            ink = MakeSystem("Ink", Res.Sprite("fx_ink"), false, true, 1.3f, SortOrder.Fx + 2, 1.3f, 0.03f);
            dust = MakeSystem("Dust", Res.Sprite("fx_dust"), false, false, -0.04f, SortOrder.Fx - 2, 1f, 0f);
            embers = MakeSystem("Embers", Res.Sprite("fx_glow"), true, false, -0.2f, SortOrder.Fx + 4, 1f, 0f);
            var dsize = dust.sizeOverLifetime; dsize.enabled = true; dsize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            var elim = embers.limitVelocityOverLifetime; elim.enabled = true; elim.dampen = 0.35f; elim.limit = 0.5f;

            for (int i = 0; i < 24; i++) spritePool.Add(SpriteFx.Make(fxRoot));
            for (int i = 0; i < 12; i++) slashPool.Add(SlashFx.Make(fxRoot));
            for (int i = 0; i < 12; i++) lightPool.Add(LightFx.Make(fxRoot));
            for (int i = 0; i < 80; i++) ghostPool.Add(GhostFx.Make(fxRoot));
            for (int i = 0; i < 16; i++) textPool.Add(FloatingTextFx.Make(fxRoot, font));
        }

        void Start()
        {
            // screen quad follows the camera
            var cam = Services.Cam != null ? Services.Cam.Transform : null;
            var go = new GameObject("ScreenFlash");
            if (cam != null) go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 5f);
            screenQuad = go.AddComponent<SpriteRenderer>();
            screenQuad.sprite = Res.White;
            screenQuad.material = MaterialLibrary.SpriteUnlit;
            screenQuad.sortingOrder = SortOrder.Foreground + 50;
            screenQuad.color = new Color(1f, 1f, 1f, 0f);
            screenQuad.enabled = false;
        }

        void LateUpdate()
        {
            if (screenQuad != null && screenQuad.enabled && Services.Cam != null && Services.Cam.Camera != null)
            {
                float h = Services.Cam.Camera.orthographicSize * 2f * 1.3f, w = h * Services.Cam.Camera.aspect * 1.2f;
                screenQuad.transform.localScale = new Vector3(w / 0.04f, h / 0.04f, 1f);
                screenQuad.transform.localRotation = Quaternion.identity;
            }
        }

        // ---------------- materials ----------------
        public static Material AdditiveFor(Sprite s)
        {
            var tex = s != null ? s.texture : Res.White.texture;
            Material m;
            if (additiveCache.TryGetValue(tex, out m)) return m;
            m = new Material(MaterialLibrary.Additive) { mainTexture = tex };
            additiveCache[tex] = m;
            return m;
        }

        public static Material UnlitFor(Sprite s)
        {
            var tex = s != null ? s.texture : Res.White.texture;
            Material m;
            if (unlitCache.TryGetValue(tex, out m)) return m;
            m = new Material(MaterialLibrary.SpriteUnlit) { mainTexture = tex };
            unlitCache[tex] = m;
            return m;
        }

        ParticleSystem MakeSystem(string name, Sprite sprite, bool additive, bool stretched, float gravity, int sort, float lengthScale, float velocityScale)
        {
            var go = new GameObject("PS_" + name);
            go.transform.SetParent(fxRoot, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = 2000; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity; main.startLifetime = 0.5f; main.startSpeed = 0f; main.startSize = 0.1f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = additive ? AdditiveFor(sprite) : UnlitFor(sprite);
            r.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.lengthScale = lengthScale; r.velocityScale = velocityScale;
            r.sortingOrder = sort;
            r.alignment = ParticleSystemRenderSpace.View;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector2 pos, Vector2 vel, Color color, float size, float life, float rotation = 0f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y, 0f),
                velocity = new Vector3(vel.x, vel.y, 0f),
                startColor = color, startSize = size, startLifetime = life, rotation = rotation
            };
            ps.Emit(ep, 1);
        }

        // ---------------- pools ----------------
        SpriteFx GetSprite() { foreach (var s in spritePool) if (!s.IsActive) return s; var n = SpriteFx.Make(fxRoot); spritePool.Add(n); return n; }
        SlashFx GetSlash() { foreach (var s in slashPool) if (!s.IsActive) return s; var n = SlashFx.Make(fxRoot); slashPool.Add(n); return n; }
        LightFx GetLight() { foreach (var l in lightPool) if (!l.IsActive) return l; var n = LightFx.Make(fxRoot); lightPool.Add(n); return n; }
        GhostFx GetGhost() { foreach (var g in ghostPool) if (!g.IsActive) return g; if (ghostPool.Count > 400) return null; var n = GhostFx.Make(fxRoot); ghostPool.Add(n); return n; }
        FloatingTextFx GetText() { foreach (var t in textPool) if (!t.IsActive) return t; var n = FloatingTextFx.Make(fxRoot, font); textPool.Add(n); return n; }

        // ---------------- IVfxService ----------------
        public void SlashArc(Vector2 pos, float angleDeg, bool flipX, Color color, float scale = 1f)
        {
            SlashArc(pos, angleDeg, flipX, color, scale, 1f, 1f);
        }

        public void SlashArc(Vector2 pos, float angleDeg, bool flipX, Color color, float scale, float span, float aspect)
        {
            GetSlash().Play(pos, angleDeg, flipX, color, scale, span, aspect);
            FlashLight(pos, color, 1.5f, 2.5f * scale, 0.12f);
        }

        public void ParrySpark(Vector2 pos, bool perfect)
        {
            int n = perfect ? 26 : 10;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float sp = perfect ? Random.Range(7f, 13f) : Random.Range(4f, 8f);
                Color c = perfect ? Color.Lerp(Color.white, Palette.Teal, Random.value) : Palette.Amber;
                Emit(sparks, pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, c, Random.Range(0.08f, 0.16f), Random.Range(0.3f, 0.45f));
            }
            // The ring and the star sit BEHIND the fighters and run on unscaled time: the hit-stop then
            // shows two dark silhouettes against the flash instead of a white star with nobody in it.
            var ring = Res.Sprite("fx_ring");
            GetSprite().Play(ring, AdditiveFor(ring), pos, 0f, false, Color.white, Color.white.WithAlpha(0f), 0.3f, perfect ? 2.4f : 1.3f, 0.22f, SortOrder.EnemyBack - 2, 0f, null, true);
            var flare = Res.Sprite("fx_flare");
            GetSprite().Play(flare, AdditiveFor(flare), pos, Random.Range(0f, 90f), false, perfect ? Color.white : Palette.Amber, Color.white.WithAlpha(0f), perfect ? 1.7f : 1.0f, perfect ? 1.1f : 0.6f, 0.18f, SortOrder.EnemyBack - 3, 45f, null, true);
            FlashLight(pos, perfect ? Color.white : Palette.Amber, perfect ? 3.5f : 1.5f, perfect ? 5f : 3f, 0.15f);
            if (perfect) ScreenFlash(Color.white.WithAlpha(0.22f), 0.09f);
        }

        public void HitSpark(Vector2 pos, Vector2 dir, Color color, float scale = 1f)
        {
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
            float baseA = Mathf.Atan2(dir.y, dir.x);
            int n = Mathf.RoundToInt(7 * scale);
            for (int i = 0; i < n; i++)
            {
                float a = baseA + Random.Range(-0.6f, 0.6f);
                float sp = Random.Range(4f, 9f) * scale;
                Emit(hitSparks, pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, color, Random.Range(0.05f, 0.09f) * scale, Random.Range(0.14f, 0.24f));
            }
            var glow = Res.Sprite("fx_glow");
            GetSprite().Play(glow, AdditiveFor(glow), pos, 0f, false, Color.Lerp(color, Color.white, 0.45f), color.WithAlpha(0f), 0.7f * scale, 1.25f * scale, 0.14f, SortOrder.Fx + 9, 0f, null, true);
        }

        public void InkSplatter(Vector2 pos, Vector2 dir, Color color, int count = 12)
        {
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
            float baseA = Mathf.Atan2(dir.y, dir.x);
            for (int i = 0; i < count; i++)
            {
                float a = baseA + Random.Range(-0.9f, 0.9f);
                float sp = Random.Range(3f, 8f);
                Emit(ink, pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp + Vector2.up * 1.5f, color, Random.Range(0.08f, 0.2f), Random.Range(0.45f, 0.7f), Random.Range(0f, 360f));
            }
        }

        public void DustPuff(Vector2 pos, float scale = 1f)
        {
            int n = Mathf.RoundToInt(6 * scale);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0.2f, Mathf.PI - 0.2f);
                float sp = Random.Range(0.5f, 1.6f) * scale;
                Emit(dust, pos + new Vector2(Random.Range(-0.3f, 0.3f) * scale, 0.05f), new Vector2(Mathf.Cos(a) * sp * 1.5f, Mathf.Sin(a) * sp * 0.6f),
                    Palette.Bone.WithAlpha(Random.Range(0.25f, 0.45f)), Random.Range(0.25f, 0.45f) * scale, Random.Range(0.4f, 0.7f), Random.Range(0f, 360f));
            }
        }

        public void Dissolve(SpriteRenderer[] renderers, Vector2 pos, Color tint)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                var g = GetGhost();
                if (g == null) break;
                Vector2 away = ((Vector2)r.transform.position - pos);
                Vector2 v = (away.sqrMagnitude > 0.001f ? away.normalized : Random.insideUnitCircle.normalized) * Random.Range(1.5f, 3.5f) + Vector2.up * Random.Range(1f, 3f);
                g.Play(r, Color.Lerp(Color.white, tint, 0.6f), Random.Range(0.5f, 0.75f), v, Random.Range(-240f, 240f), true);
            }
            Embers(pos, 22, tint);
            InkSplatter(pos, Vector2.up, Palette.Ink, 10);
        }

        public void Shockwave(Vector2 pos, float radius, Color color)
        {
            var ring = Res.Sprite("fx_shockwave");
            GetSprite().Play(ring, AdditiveFor(ring), pos + new Vector2(0f, 0.15f), 0f, false, color, color.WithAlpha(0f), 0.4f, radius * 0.85f, 0.38f, SortOrder.Fx + 3);
            var ring2 = Res.Sprite("fx_ring");
            GetSprite().Play(ring2, AdditiveFor(ring2), pos + new Vector2(0f, 0.6f), 0f, false, color, color.WithAlpha(0f), 0.3f, radius * 1.4f, 0.32f, SortOrder.Fx + 3);
            for (int i = 0; i < 6; i++) DustPuff(pos + new Vector2(Random.Range(-radius, radius) * 0.6f, 0f), 1.2f);
            FlashLight(pos + new Vector2(0f, 0.8f), color, 3f, radius * 1.6f, 0.3f);
            for (int i = 0; i < 18; i++)
            {
                float a = Random.Range(0.1f, Mathf.PI - 0.1f);
                Emit(sparks, pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(4f, 10f), color, Random.Range(0.08f, 0.15f), Random.Range(0.3f, 0.5f));
            }
        }

        public void Afterimage(SpriteRenderer[] renderers, Color tint, float lifeSeconds = 0.3f)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                var g = GetGhost();
                if (g == null) break;
                g.Play(r, tint, lifeSeconds, Vector2.zero, 0f, false);
            }
        }

        public void FlashLight(Vector2 pos, Color color, float intensity, float radius, float seconds)
        {
            GetLight().Play(pos, color, intensity, radius, seconds);
        }

        public void ScreenFlash(Color color, float seconds)
        {
            if (screenQuad == null) return;
            if (screenFlashCo != null) StopCoroutine(screenFlashCo);
            screenFlashCo = StartCoroutine(ScreenFlashRoutine(color, seconds));
        }

        IEnumerator ScreenFlashRoutine(Color color, float seconds)
        {
            screenQuad.enabled = true;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                screenQuad.color = color.WithAlpha(color.a * (1f - Ease.OutQuad(t / seconds)));
                yield return null;
            }
            screenQuad.enabled = false;
            screenFlashCo = null;
        }

        public void ChromaticPulse(float strength, float seconds)
        {
            if (chromaCo != null) StopCoroutine(chromaCo);
            chromaCo = StartCoroutine(ChromaRoutine(strength, seconds));
        }

        IEnumerator ChromaRoutine(float strength, float seconds)
        {
            var vol = CameraController.GlobalVolume;
            if (vol == null || vol.sharedProfile == null) yield break;
            ChromaticAberration ca; LensDistortion ld;
            vol.sharedProfile.TryGet(out ca);
            vol.sharedProfile.TryGet(out ld);
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float p = 1f - Ease.OutQuad(t / seconds);
                if (ca != null) ca.intensity.value = 0.04f + strength * p;
                if (ld != null) ld.intensity.value = -0.22f * strength * p;
                yield return null;
            }
            if (ca != null) ca.intensity.value = 0.04f;
            if (ld != null) ld.intensity.value = 0f;
            chromaCo = null;
        }

        public void FloatingText(Vector2 worldPos, string text, Color color, float scale = 1f)
        {
            GetText().Play(worldPos, text, color, scale);
        }

        public void Embers(Vector2 pos, int count, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float sp = Random.Range(1f, 4f);
                Emit(embers, pos + Random.insideUnitCircle * 0.4f, new Vector2(Mathf.Cos(a) * sp, Mathf.Abs(Mathf.Sin(a)) * sp + 1f), color.WithAlpha(0.9f), Random.Range(0.05f, 0.14f), Random.Range(0.6f, 1.3f));
            }
        }

        public void ProjectileTrail(Transform follow, Color color)
        {
            var tr = follow.GetComponent<TrailRenderer>();
            if (tr == null)
            {
                tr = follow.gameObject.AddComponent<TrailRenderer>();
                tr.time = 0.22f;
                tr.startWidth = 0.18f; tr.endWidth = 0f;
                tr.material = AdditiveFor(Res.White);
                tr.sortingOrder = SortOrder.Projectile - 1;
                tr.minVertexDistance = 0.05f;
                tr.autodestruct = false;
            }
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.Clear();
        }

        // ---------------- ambient ----------------
        public static ParticleSystem CreateAmbientEmbers(Rect area, Color color, float rate, Transform parent)
        {
            var go = new GameObject("AmbientEmbers");
            go.transform.SetParent(parent, false);
            go.transform.position = area.center;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(color.WithAlpha(0.7f), Color.Lerp(color, Color.white, 0.3f).WithAlpha(0.5f));
            main.gravityModifier = -0.01f;
            main.useUnscaledTime = true;   // the atmosphere keeps drifting through hit-stop and slow-motion
            var em = ps.emission; em.enabled = true; em.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(area.width, area.height, 1f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            // All three curves must use the same MinMaxCurve mode, otherwise Unity logs an error every frame.
            vel.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.4f; noise.scrollSpeed = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = AdditiveFor(Res.Sprite("fx_glow"));
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingOrder = SortOrder.Props - 4;
            ps.Play();
            // pre-warm
            ps.Simulate(6f, true, false); ps.Play();
            return ps;
        }

        public static GameObject CreateMist(Rect area, Color tint, int count, Transform parent)
        {
            var root = new GameObject("Mist");
            root.transform.SetParent(parent, false);
            var sp = Res.Sprite("bg_fog");
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("fog" + i);
                go.transform.SetParent(root.transform, false);
                float x = Random.Range(area.xMin, area.xMax);
                float y = Random.Range(area.yMin, area.yMin + area.height * (i % 2 == 0 ? 0.55f : 1f));
                go.transform.position = new Vector3(x, y, 0f);
                float s = Random.Range(2f, 4.2f);
                go.transform.localScale = new Vector3(s * Random.Range(0.9f, 1.4f), s * 0.6f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.material = MaterialLibrary.SpriteUnlit;
                bool front = i % 3 == 0;
                sr.sortingOrder = front ? SortOrder.Fog : SortOrder.GroundDecor + 2;
                sr.color = tint.WithAlpha(front ? Random.Range(0.08f, 0.16f) : Random.Range(0.12f, 0.24f));
                var d = go.AddComponent<MistDrift>();
                d.Init(area, Random.Range(0.12f, 0.4f) * (Random.value < 0.5f ? -1f : 1f), sr);
            }
            return root;
        }
    }
}
