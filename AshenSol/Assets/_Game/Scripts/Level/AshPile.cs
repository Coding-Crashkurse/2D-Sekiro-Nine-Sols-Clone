using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Player;

namespace AshenSol.Level
{
    /// <summary>The ash you dropped when you died. Walk into it to take it back.</summary>
    public class AshPile : MonoBehaviour
    {
        SpriteRenderer glow, core;
        Light2D light;
        float t;
        bool taken;

        public static AshPile Create(Vector2 pos, Transform parent)
        {
            var go = new GameObject("AshPile");
            go.layer = Layers.Trigger;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos + new Vector2(0f, 0.5f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 2.2f);

            var p = go.AddComponent<AshPile>();
            p.glow = Sprite(go.transform, "fx_glow", 3.2f, Palette.Gold.WithAlpha(0.4f), SortOrder.Props - 1);
            p.core = Sprite(go.transform, "fx_flare", 1.5f, Palette.Gold.WithAlpha(0.85f), SortOrder.Props + 1);
            p.light = LevelDecor.Light(go.transform, Vector2.zero, Palette.Gold, 1.8f, 4.5f);
            AshenSol.VFX.VfxManager.CreateAmbientEmbers(new Rect(pos.x - 0.6f, pos.y, 1.2f, 2.4f), Palette.Gold, 10f, go.transform);
            return p;
        }

        static SpriteRenderer Sprite(Transform parent, string sprite, float scale, Color color, int sort)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.Sprite(sprite);
            sr.material = MaterialLibrary.Additive;
            sr.color = color;
            sr.sortingOrder = sort;
            go.transform.localScale = Vector3.one * scale;
            return sr;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 2.4f);
            glow.color = Palette.Gold.WithAlpha(0.35f * pulse);
            core.color = Palette.Gold.WithAlpha(0.8f * pulse);
            core.transform.localRotation = Quaternion.Euler(0f, 0f, t * 22f);
            light.intensity = 1.6f * pulse;
        }

        void OnTriggerEnter2D(Collider2D other) { Take(other); }
        void OnTriggerStay2D(Collider2D other) { Take(other); }

        void Take(Collider2D other)
        {
            if (taken || other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null || !p.IsAlive) return;
            taken = true;
            int amount = Progression.Recover();
            Services.Audio.PlaySfx("qi_gain", 1f);
            Services.Vfx.Embers((Vector2)transform.position, 34, Palette.Gold);
            Services.Vfx.FlashLight(transform.position, Palette.Gold, 3f, 5f, 0.5f);
            Services.Vfx.FloatingText((Vector2)transform.position + new Vector2(0f, 1f), "+" + amount + " ASH", Palette.Gold, 1.4f);
            Services.Ui.ShowPrompt(amount + " ash recovered", 1.8f);
            Destroy(gameObject);
        }
    }
}
