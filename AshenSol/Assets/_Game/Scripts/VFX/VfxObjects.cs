using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.VFX
{
    /// <summary>One-shot tweened sprite (slash arcs, rings, flares). Pooled by VfxManager.</summary>
    public class SpriteFx : MonoBehaviour
    {
        public SpriteRenderer Renderer { get; private set; }
        float t, life; Color c0, c1; float s0, s1; float rot0, rotSpeed; bool active; System.Func<float, float> ease;

        public static SpriteFx Make(Transform parent)
        {
            var go = new GameObject("SpriteFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<SpriteFx>();
            fx.Renderer = go.AddComponent<SpriteRenderer>();
            go.SetActive(false);
            return fx;
        }

        public bool IsActive { get { return active; } }

        public void Play(Sprite sprite, Material mat, Vector2 pos, float angle, bool flipX, Color colorStart, Color colorEnd, float scaleStart, float scaleEnd, float seconds, int sort, float rotSpeedDeg = 0f, System.Func<float, float> easeFn = null)
        {
            gameObject.SetActive(true);
            Renderer.sprite = sprite; Renderer.material = mat; Renderer.flipX = flipX; Renderer.sortingOrder = sort;
            transform.position = pos; transform.rotation = Quaternion.Euler(0f, 0f, angle);
            c0 = colorStart; c1 = colorEnd; s0 = scaleStart; s1 = scaleEnd; life = Mathf.Max(0.01f, seconds); t = 0f; rot0 = angle; rotSpeed = rotSpeedDeg;
            ease = easeFn ?? Ease.OutCubic;
            active = true;
            Apply(0f);
        }

        void Update()
        {
            if (!active) return;
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / life);
            Apply(p);
            if (p >= 1f) { active = false; gameObject.SetActive(false); }
        }

        void Apply(float p)
        {
            float e = ease(p);
            Renderer.color = Color.Lerp(c0, c1, e);
            float s = Mathf.Lerp(s0, s1, e);
            transform.localScale = new Vector3(s, s, 1f);
            if (rotSpeed != 0f) transform.rotation = Quaternion.Euler(0f, 0f, rot0 + rotSpeed * t);
        }
    }

    /// <summary>Temporary point light. Pooled.</summary>
    public class LightFx : MonoBehaviour
    {
        public Light2D Light { get; private set; }
        float t, life, i0; bool active;

        public static LightFx Make(Transform parent)
        {
            var go = new GameObject("LightFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<LightFx>();
            fx.Light = go.AddComponent<Light2D>();
            fx.Light.lightType = Light2D.LightType.Point;
            fx.Light.pointLightInnerRadius = 0.1f;
            fx.Light.falloffIntensity = 0.7f;
            go.SetActive(false);
            return fx;
        }

        public bool IsActive { get { return active; } }

        public void Play(Vector2 pos, Color color, float intensity, float radius, float seconds)
        {
            gameObject.SetActive(true);
            transform.position = pos;
            Light.color = color; Light.intensity = intensity; Light.pointLightOuterRadius = radius;
            i0 = intensity; life = Mathf.Max(0.01f, seconds); t = 0f; active = true;
        }

        void Update()
        {
            if (!active) return;
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / life);
            Light.intensity = i0 * (1f - Ease.OutQuad(p));
            if (p >= 1f) { active = false; gameObject.SetActive(false); }
        }
    }

    /// <summary>Copy of a sprite that fades (afterimage) or drifts apart (dissolve). Pooled.</summary>
    public class GhostFx : MonoBehaviour
    {
        public SpriteRenderer Renderer { get; private set; }
        float t, life; Color c0; Vector2 vel; float spin; bool active; bool useScaled;

        public static GhostFx Make(Transform parent)
        {
            var go = new GameObject("GhostFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<GhostFx>();
            fx.Renderer = go.AddComponent<SpriteRenderer>();
            fx.Renderer.material = MaterialLibrary.SpriteUnlit;
            go.SetActive(false);
            return fx;
        }

        public bool IsActive { get { return active; } }

        public void Play(SpriteRenderer src, Color tint, float seconds, Vector2 velocity, float spinDeg, bool scaledTime)
        {
            gameObject.SetActive(true);
            Renderer.sprite = src.sprite;
            Renderer.flipX = src.flipX; Renderer.flipY = src.flipY;
            Renderer.sortingOrder = src.sortingOrder - 1;
            Renderer.drawMode = src.drawMode;
            if (src.drawMode != SpriteDrawMode.Simple) Renderer.size = src.size;
            transform.position = src.transform.position;
            transform.rotation = src.transform.rotation;
            transform.localScale = src.transform.lossyScale;
            c0 = tint; Renderer.color = tint;
            life = Mathf.Max(0.01f, seconds); t = 0f; vel = velocity; spin = spinDeg; active = true; useScaled = scaledTime;
        }

        void Update()
        {
            if (!active) return;
            float dt = useScaled ? Time.deltaTime : Time.unscaledDeltaTime;
            t += dt;
            float p = Mathf.Clamp01(t / life);
            Renderer.color = new Color(c0.r, c0.g, c0.b, c0.a * (1f - Ease.InCubic(p)));
            if (vel != Vector2.zero) { transform.position += (Vector3)(vel * dt); vel += Vector2.down * 4f * dt; }
            if (spin != 0f) transform.Rotate(0f, 0f, spin * dt);
            if (p >= 1f) { active = false; gameObject.SetActive(false); }
        }
    }

    /// <summary>World-space floating number/text. Pooled.</summary>
    public class FloatingTextFx : MonoBehaviour
    {
        TextMesh mesh; MeshRenderer mr; float t, life; Color c0; float baseSize; Vector3 start; float drift; bool active;

        public static FloatingTextFx Make(Transform parent, Font font)
        {
            var go = new GameObject("FloatingText");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<FloatingTextFx>();
            fx.mesh = go.AddComponent<TextMesh>();
            fx.mesh.font = font;
            fx.mesh.fontSize = 64;
            fx.mesh.characterSize = 0.06f;
            fx.mesh.anchor = TextAnchor.MiddleCenter;
            fx.mesh.alignment = TextAlignment.Center;
            fx.mesh.fontStyle = FontStyle.Bold;
            fx.mr = go.GetComponent<MeshRenderer>();
            if (font != null) fx.mr.material = font.material;
            fx.mr.sortingOrder = SortOrder.FxFront + 5;
            go.SetActive(false);
            return fx;
        }

        public bool IsActive { get { return active; } }

        public void Play(Vector2 pos, string text, Color color, float scale)
        {
            gameObject.SetActive(true);
            mesh.text = text; mesh.color = color; c0 = color;
            baseSize = 0.06f * scale; mesh.characterSize = baseSize * 1.35f;
            start = new Vector3(pos.x, pos.y, 0f); transform.position = start;
            drift = Random.Range(-0.25f, 0.25f);
            life = 0.85f; t = 0f; active = true;
        }

        void Update()
        {
            if (!active) return;
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / life);
            transform.position = start + new Vector3(drift * p, Ease.OutCubic(p) * 1.0f, 0f);
            mesh.characterSize = Mathf.Lerp(baseSize * 1.35f, baseSize, Mathf.Clamp01(p * 3f));
            mesh.color = new Color(c0.r, c0.g, c0.b, 1f - Ease.InCubic(p));
            if (p >= 1f) { active = false; gameObject.SetActive(false); }
        }
    }

    /// <summary>Slow drifting fog sprite that wraps inside an area.</summary>
    public class MistDrift : MonoBehaviour
    {
        public Rect Area; public float Speed; SpriteRenderer sr; float phase; Color baseColor;
        public void Init(Rect area, float speed, SpriteRenderer r) { Area = area; Speed = speed; sr = r; baseColor = r.color; phase = Random.value * 10f; }
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var p = transform.position;
            p.x += Speed * dt;
            p.y += Mathf.Sin(Time.unscaledTime * 0.3f + phase) * 0.05f * dt;
            float w = sr.bounds.size.x;
            if (Speed > 0f && p.x - w * 0.5f > Area.xMax) p.x = Area.xMin - w * 0.5f;
            if (Speed < 0f && p.x + w * 0.5f < Area.xMin) p.x = Area.xMax + w * 0.5f;
            transform.position = p;
            sr.color = baseColor.WithAlpha(baseColor.a * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 0.5f + phase)));
        }
    }
}
