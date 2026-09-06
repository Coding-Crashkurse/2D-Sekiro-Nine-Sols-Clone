using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;

namespace AshenSol.Level
{
    /// <summary>Props, lights, background walls and parallax stacks.</summary>
    public static class LevelDecor
    {
        public static readonly Color BackTint = new Color(0.62f, 0.68f, 0.8f, 1f);   // pushes props behind the play plane
        public static readonly Color WallTint = new Color(0.45f, 0.5f, 0.62f, 1f);

        public static SpriteRenderer Sprite(Transform parent, string sprite, Vector2 localPos, int sort, Color tint)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.sortingOrder = sort;
            sr.color = tint;
            return sr;
        }

        public static Light2D Light(Transform parent, Vector2 localPos, Color color, float intensity, float radius)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.pointLightOuterRadius = radius;
            l.pointLightInnerRadius = 0.1f;
            l.falloffIntensity = 0.65f;
            return l;
        }

        /// <summary>Hanging lantern with a flickering warm light. pos = lantern centre.</summary>
        public static GameObject Lantern(Vector2 pos, Transform parent, Color? color = null, float intensity = 1f)
        {
            Color c = color ?? Palette.Amber;
            var go = new GameObject("Lantern");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Sprite(go.transform, "prop_lantern", Vector2.zero, SortOrder.Props, Color.white);
            var glow = Sprite(go.transform, "fx_glow", new Vector2(0f, -0.05f), SortOrder.Props - 1, c.WithAlpha(0.35f));
            glow.material = MaterialLibrary.Additive;
            glow.transform.localScale = Vector3.one * 1.6f;
            var l = Light(go.transform, new Vector2(0f, -0.05f), c, intensity, 3.6f);
            var f = go.AddComponent<LightFlicker>();
            f.Init(l, glow, intensity);
            var sway = go.AddComponent<Sway>();
            sway.Amplitude = 2.5f; sway.Speed = 0.9f + Random.value * 0.4f;
            return go;
        }

        public static GameObject Pillar(Vector2 groundPos, bool broken, Transform parent)
        {
            var go = new GameObject(broken ? "PillarBroken" : "Pillar");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            string s = broken ? "prop_pillar_broken" : "prop_pillar";
            float h = broken ? 1.3f : 2.3f;
            Sprite(go.transform, s, new Vector2(0f, h * 0.5f), SortOrder.BgProps, BackTint);
            return go;
        }

        public static GameObject Banner(Vector2 topPos, Transform parent)
        {
            var go = new GameObject("Banner");
            go.transform.SetParent(parent, false);
            go.transform.position = topPos;
            Sprite(go.transform, "prop_banner", new Vector2(0f, -0.65f), SortOrder.BgProps + 1, BackTint);
            var sway = go.AddComponent<Sway>();
            sway.Amplitude = 4f; sway.Speed = 0.7f + Random.value * 0.5f;
            return go;
        }

        public static GameObject Statue(Vector2 groundPos, Transform parent, bool foreground = false)
        {
            var go = new GameObject("Statue");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            Sprite(go.transform, "prop_statue", new Vector2(0f, 1.4f), foreground ? SortOrder.Props - 1 : SortOrder.BgProps, foreground ? Color.white : BackTint);
            Light(go.transform, new Vector2(0f, 2.1f), Palette.Teal, 0.5f, 2.5f);
            return go;
        }

        public static GameObject Bamboo(Vector2 groundPos, float scale, Transform parent)
        {
            var go = new GameObject("Bamboo");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            var sr = Sprite(go.transform, "prop_bamboo", new Vector2(0f, 1.6f * scale), SortOrder.BgProps - 1, new Color(0.5f, 0.6f, 0.7f, 1f));
            sr.transform.localScale = Vector3.one * scale;
            var sway = go.AddComponent<Sway>();
            sway.Amplitude = 1.5f; sway.Speed = 0.5f + Random.value * 0.4f;
            return go;
        }

        public static GameObject Rock(Vector2 groundPos, Transform parent)
        {
            var go = new GameObject("Rock");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            Sprite(go.transform, "prop_rock", new Vector2(0f, 0.28f), SortOrder.Props - 3, Color.white);
            return go;
        }

        public static GameObject Chain(Vector2 topPos, float length, Transform parent)
        {
            var go = new GameObject("Chain");
            go.transform.SetParent(parent, false);
            go.transform.position = topPos;
            var sr = Sprite(go.transform, "prop_chain", new Vector2(0f, -length * 0.5f), SortOrder.BgProps + 2, BackTint);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(0.2f, length);
            var sway = go.AddComponent<Sway>();
            sway.Amplitude = 1.2f; sway.Speed = 0.4f + Random.value * 0.3f;
            return go;
        }

        public static GameObject WallPanel(Rect r, Transform parent, string sprite = "prop_wall_panel", float alpha = 1f)
        {
            var go = new GameObject("Wall_" + sprite);
            go.transform.SetParent(parent, false);
            go.transform.position = r.center;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = r.size;
            sr.sortingOrder = SortOrder.BgWall;
            sr.color = WallTint.WithAlpha(alpha);
            return go;
        }

        public static void ParallaxStack(Transform parent, string sky, string far, string mid, string near, float groundY, Color farTint, Color midTint, Color nearTint)
        {
            ParallaxLayer.Create(sky, 1f, SortOrder.BgSky, groundY, 3f, parent, false, Color.white, 0f, true);
            ParallaxLayer.Create(far, 0.85f, SortOrder.BgFar, groundY + 2.2f, 2.2f, parent, true, farTint, 0f);
            ParallaxLayer.Create(mid, 0.62f, SortOrder.BgMid, groundY + 1.6f, 2.0f, parent, true, midTint, 0f);
            ParallaxLayer.Create(near, 0.38f, SortOrder.BgNear, groundY + 1.2f, 1.8f, parent, true, nearTint, 0f);
            var fog = ParallaxLayer.Create("bg_fog", -0.25f, SortOrder.Fog, groundY - 0.5f, 3.2f, parent, true, Palette.Bone.WithAlpha(0.13f), 0.35f);
            fog.FactorY = 0f;
        }
    }

    public class LightFlicker : MonoBehaviour
    {
        Light2D light; SpriteRenderer glow; float baseIntensity; float seed; Color glowBase;
        public void Init(Light2D l, SpriteRenderer g, float intensity) { light = l; glow = g; baseIntensity = intensity; seed = Random.value * 100f; if (g != null) glowBase = g.color; }
        void Update()
        {
            if (light == null) return;
            float n = Mathf.PerlinNoise(seed, Time.unscaledTime * 1.7f);
            float f = 0.86f + 0.28f * n;
            light.intensity = baseIntensity * f;
            if (glow != null) glow.color = glowBase.WithAlpha(glowBase.a * f);
        }
    }

    public class Sway : MonoBehaviour
    {
        public float Amplitude = 3f, Speed = 1f;
        float phase;
        void Start() { phase = Random.value * 10f; }
        void Update() { transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * Speed + phase) * Amplitude); }
    }
}
