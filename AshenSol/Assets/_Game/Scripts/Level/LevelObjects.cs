using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using AshenSol.Core;
using AshenSol.Enemies;
using AshenSol.Player;

namespace AshenSol.Level
{
    // ------------------------------------------------------------------ Checkpoint
    public class Checkpoint : MonoBehaviour
    {
        public string Id;
        public Vector2 RespawnPoint;
        public bool IsActive { get; private set; }
        public event Action<Checkpoint> Activated;

        Light2D light; SpriteRenderer glow; float targetIntensity = 0.5f; float t;

        public static Checkpoint Create(Vector2 groundPos, string id, Transform parent)
        {
            var go = new GameObject("Checkpoint_" + id);
            go.layer = Layers.Trigger;
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            var c = go.AddComponent<Checkpoint>();
            c.Id = id; c.RespawnPoint = groundPos + new Vector2(0.9f, 0.05f);

            var sr = LevelDecor.Sprite(go.transform, "prop_shrine", new Vector2(0f, 0.75f), SortOrder.Props, Color.white);
            c.glow = LevelDecor.Sprite(go.transform, "fx_glow", new Vector2(0f, 1.05f), SortOrder.Props + 1, Palette.Teal.WithAlpha(0.25f));
            c.glow.material = MaterialLibrary.Additive;
            c.glow.transform.localScale = Vector3.one * 2.2f;
            c.light = LevelDecor.Light(go.transform, new Vector2(0f, 1.1f), Palette.Teal, 0.5f, 3f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.6f, 2.2f);
            col.offset = new Vector2(0f, 1.1f);
            return c;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            Activate(false);
            OfferUpgrade();
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            OfferUpgrade();
        }

        /// <summary>A shrine is where ash becomes levels.</summary>
        void OfferUpgrade()
        {
            if (!Progression.CanAffordLevel || Services.Ui.UpgradePanelOpen) return;
            if (GameFlow.Instance != null && (GameFlow.Instance.Busy || GameFlow.Instance.IsPaused)) return;
            Services.Ui.ShowUpgradePanel(kind =>
            {
                if (!Progression.Buy(kind)) return;
                var p = PlayerController.Instance;
                if (p != null && kind == UpgradeKind.Vigor) p.Heal(Progression.VigorHp);
                Services.Audio.PlaySfx("qi_gain", 1f);
                Services.Vfx.Embers((Vector2)transform.position + new Vector2(0f, 1.2f), 26, Palette.Gold);
                Services.Vfx.FlashLight((Vector2)transform.position + new Vector2(0f, 1.2f), Palette.Gold, 3f, 5f, 0.5f);
                if (p != null)
                {
                    GameEvents.RaisePlayerHealthChanged(p.Hp, p.MaxHp);
                    GameEvents.RaisePlayerQiChanged(p.Qi, p.MaxQi);
                }
            });
        }

        public void Activate(bool silent)
        {
            if (IsActive) return;
            IsActive = true;
            targetIntensity = 1.4f;
            if (GameFlow.Instance != null) GameFlow.Instance.SetRespawnPoint(RespawnPoint);
            if (!silent)
            {
                Services.Audio.PlaySfx("checkpoint");
                Services.Ui.ShowPrompt("Checkpoint", 1.8f);
                Services.Vfx.Embers((Vector2)transform.position + new Vector2(0f, 1.2f), 26, Palette.Teal);
                Services.Vfx.FlashLight((Vector2)transform.position + new Vector2(0f, 1.2f), Palette.Teal, 3f, 5f, 0.5f);
                light.intensity = 3f;
            }
            GameEvents.RaiseCheckpointActivated(Id);
            var a = Activated; if (a != null) a(this);
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float flick = 1f + 0.12f * Mathf.Sin(t * 3.7f) * Mathf.Sin(t * 1.3f);
            light.intensity = Mathf.Lerp(light.intensity, targetIntensity * flick, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            glow.color = Palette.Teal.WithAlpha((IsActive ? 0.55f : 0.2f) * flick);
        }
    }

    // ------------------------------------------------------------------ Gate
    public class Gate : MonoBehaviour
    {
        public bool IsOpen { get; private set; }
        public string SealedPrompt = "The seal holds. Slay the guardians.";
        public event Action PlayerEntered;

        SpriteRenderer door, glyph, glow; Light2D light; Collider2D solid; float promptCooldown; Coroutine anim; float t;

        public static Gate Create(Vector2 groundPos, Transform parent, bool startOpen)
        {
            var go = new GameObject("Gate");
            go.layer = Layers.Trigger;
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            var g = go.AddComponent<Gate>();

            LevelDecor.Sprite(go.transform, "prop_torii", new Vector2(0f, 1.45f), SortOrder.Props - 2, Color.white);
            g.door = LevelDecor.Sprite(go.transform, "prop_gate_sealed", new Vector2(0f, 1.4f), SortOrder.Props, Color.white);
            g.glyph = LevelDecor.Sprite(go.transform, "prop_gate_glyph", new Vector2(0f, 1.6f), SortOrder.Props + 1, Palette.Red);
            g.glyph.material = MaterialLibrary.Additive;
            g.glow = LevelDecor.Sprite(go.transform, "fx_glow", new Vector2(0f, 1.6f), SortOrder.Props - 1, Palette.Red.WithAlpha(0.35f));
            g.glow.material = MaterialLibrary.Additive;
            g.glow.transform.localScale = Vector3.one * 3.5f;
            g.light = LevelDecor.Light(go.transform, new Vector2(0f, 1.6f), Palette.Red, 1.2f, 4f);

            var solidGo = new GameObject("solid");
            solidGo.layer = Layers.Ground;
            solidGo.transform.SetParent(go.transform, false);
            var sc = solidGo.AddComponent<BoxCollider2D>();
            sc.size = new Vector2(0.7f, 2.8f);
            sc.offset = new Vector2(0f, 1.4f);
            g.solid = sc;

            var trig = go.AddComponent<BoxCollider2D>();
            trig.isTrigger = true;
            trig.size = new Vector2(1.4f, 2.8f);
            trig.offset = new Vector2(0f, 1.4f);

            g.IsOpen = startOpen;
            g.ApplyState(startOpen ? 1f : 0f);
            return g;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (promptCooldown > 0f) promptCooldown -= Time.unscaledDeltaTime;
            float pulse = 0.8f + 0.2f * Mathf.Sin(t * 2.5f);
            if (IsOpen) { light.intensity = Mathf.Lerp(light.intensity, 0.9f * pulse, 0.1f); light.color = Palette.Teal; glow.color = Palette.Teal.WithAlpha(0.3f * pulse); glyph.color = Palette.Teal.WithAlpha(glyph.color.a); }
            else { light.intensity = Mathf.Lerp(light.intensity, 1.3f * pulse, 0.1f); light.color = Palette.Red; glow.color = Palette.Red.WithAlpha(0.35f * pulse); }
        }

        void ApplyState(float open)
        {
            // open: door fades and shrinks upward into the beam
            door.color = Color.white.WithAlpha(1f - open);
            door.transform.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.05f, open), 1f);
            door.transform.localPosition = new Vector3(0f, Mathf.Lerp(1.4f, 2.7f, open), 0f);
            glyph.color = (IsOpen ? Palette.Teal : Palette.Red).WithAlpha(1f - open * 0.6f);
            solid.enabled = open < 0.5f;
        }

        public void Open(bool silent = false)
        {
            if (IsOpen) return;
            IsOpen = true;
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(1f, silent ? 0.3f : 1.2f));
            if (!silent)
            {
                Services.Audio.PlaySfx("gate_open");
                Vector2 p = (Vector2)transform.position + new Vector2(0f, 1.6f);
                Services.Vfx.Embers(p, 40, Palette.Teal);
                Services.Vfx.FlashLight(p, Palette.Teal, 3f, 6f, 0.8f);
                Services.Vfx.DustPuff((Vector2)transform.position + new Vector2(-0.5f, 0f), 1.5f);
                Services.Vfx.DustPuff((Vector2)transform.position + new Vector2(0.5f, 0f), 1.5f);
                Services.Cam.Shake(0.3f);
            }
        }

        public void Close(bool silent = false)
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(0f, 0.6f));
            if (!silent)
            {
                Services.Audio.PlaySfx("gate_close");
                Services.Vfx.DustPuff((Vector2)transform.position + new Vector2(-0.5f, 0f), 1.5f);
                Services.Vfx.DustPuff((Vector2)transform.position + new Vector2(0.5f, 0f), 1.5f);
                Services.Cam.Shake(0.35f);
            }
        }

        IEnumerator Animate(float target, float seconds)
        {
            float start = 1f - door.color.a;
            float e = 0f;
            while (e < seconds)
            {
                e += Time.unscaledDeltaTime;
                ApplyState(Mathf.Lerp(start, target, Ease.InOutSine(e / seconds)));
                yield return null;
            }
            ApplyState(target);
        }

        void OnTriggerEnter2D(Collider2D other) { HandlePlayer(other); }
        void OnTriggerStay2D(Collider2D other) { if (IsOpen) HandlePlayer(other); }

        void HandlePlayer(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null || !p.IsAlive) return;
            if (IsOpen)
            {
                if (Mathf.Abs(p.Center.x - transform.position.x) > 0.6f) return;
                var ev = PlayerEntered; if (ev != null) ev();
            }
            else if (promptCooldown <= 0f)
            {
                promptCooldown = 3f;
                Services.Ui.ShowPrompt(SealedPrompt, 2.2f);
                Services.Audio.PlaySfx("ui_move", 0.6f);
                glyph.transform.localScale = Vector3.one * 1.3f;
            }
        }
    }

    // ------------------------------------------------------------------ EncounterZone
    public class EncounterZone : MonoBehaviour
    {
        public string Id;
        public Rect Area;
        public List<EnemyBase> Enemies = new List<EnemyBase>();
        public bool Cleared { get; private set; }
        public event Action<EncounterZone> OnCleared;

        public static EncounterZone Create(string id, Rect area, Transform parent)
        {
            var go = new GameObject("Zone_" + id);
            go.transform.SetParent(parent, false);
            var z = go.AddComponent<EncounterZone>();
            z.Id = id; z.Area = area;
            return z;
        }

        public void Add(EnemyBase e)
        {
            Enemies.Add(e);
            e.ZoneId = Id;
            e.Died += OnEnemyDied;
        }

        void OnDestroy() { foreach (var e in Enemies) if (e != null) e.Died -= OnEnemyDied; }

        void OnEnemyDied(EnemyBase e)
        {
            if (Cleared) return;
            for (int i = 0; i < Enemies.Count; i++) if (Enemies[i] != null && Enemies[i].IsAlive) return;
            Cleared = true;
            GameEvents.RaiseLog("zone cleared: " + Id);
            var c = OnCleared; if (c != null) c(this);
        }

        public void Rearm() { Cleared = false; }
    }

    // ------------------------------------------------------------------ Hazards
    public class SpikeHazard : MonoBehaviour
    {
        public int Damage = 15;
        void OnTriggerEnter2D(Collider2D other) { Hit(other); }
        void OnTriggerStay2D(Collider2D other) { Hit(other); }
        void Hit(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null && p.IsAlive) p.ApplyHazard(Damage);
        }
    }

    public class KillZone : MonoBehaviour
    {
        void OnTriggerEnter2D(Collider2D other) { Hit(other); }
        void OnTriggerStay2D(Collider2D other) { Hit(other); }
        void Hit(Collider2D other)
        {
            if (other.gameObject.layer != Layers.Player) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null && p.IsAlive) p.ApplyHazard(20);
        }
    }

    // ------------------------------------------------------------------ Tutorial sign
    public class TutorialSign : MonoBehaviour
    {
        public string Text;
        TextMesh mesh; MeshRenderer mr; Color baseColor;

        public static TutorialSign Create(Vector2 groundPos, string text, Transform parent)
        {
            var go = new GameObject("Sign");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            var s = go.AddComponent<TutorialSign>();
            s.Text = text;
            LevelDecor.Sprite(go.transform, "prop_sign", new Vector2(0f, 0.32f), SortOrder.Props - 1, Color.white);
            var tgo = new GameObject("text");
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            s.mesh = tgo.AddComponent<TextMesh>();
            s.mesh.font = UiFont.Get();
            s.mesh.text = text;
            s.mesh.fontSize = 56;
            s.mesh.characterSize = 0.055f;
            s.mesh.anchor = TextAnchor.MiddleCenter;
            s.mesh.alignment = TextAlignment.Center;
            s.mesh.color = Palette.Bone;
            s.mr = tgo.GetComponent<MeshRenderer>();
            s.mr.sortingOrder = SortOrder.Props + 2;
            if (s.mesh.font != null) s.mr.material = s.mesh.font.material;
            s.baseColor = Palette.Bone;
            return s;
        }

        void Update()
        {
            var p = PlayerController.Instance;
            float a = 0.15f;
            if (p != null)
            {
                float d = Vector2.Distance(p.Center, (Vector2)transform.position + new Vector2(0f, 1f));
                a = Mathf.Lerp(1f, 0.15f, Mathf.Clamp01((d - 4f) / 2.5f));
            }
            mesh.color = baseColor.WithAlpha(a);
        }
    }

    /// <summary>Shared font loader: Cinzel if present in Resources/Fonts, else the built-in runtime font.</summary>
    public static class UiFont
    {
        static Font font; static bool tried;
        public static Font Get()
        {
            if (font != null || tried) return font;
            tried = true;
            font = Resources.Load<Font>("Fonts/Cinzel-Regular");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    // ------------------------------------------------------------------ Parallax
    public class ParallaxLayer : MonoBehaviour
    {
        public float Factor = 0.5f;       // 1 = glued to camera, 0 = world locked, <0 = foreground
        public float FactorY = 0.3f;
        public bool RepeatX = true;
        public float ExtraScroll = 0f;    // units/second drift
        public float BaseY = 0f;
        float width; SpriteRenderer[] copies; float scroll; bool fitToView; float spriteW, spriteH;

        public static ParallaxLayer Create(string sprite, float factor, int sort, float baseY, float scale, Transform parent, bool repeatX, Color tint, float extraScroll = 0f, bool fitToView = false)
        {
            var go = new GameObject("Parallax_" + sprite);
            go.transform.SetParent(parent, false);
            var pl = go.AddComponent<ParallaxLayer>();
            pl.Factor = factor; pl.FactorY = factor * 0.45f; pl.RepeatX = repeatX; pl.ExtraScroll = extraScroll; pl.BaseY = baseY; pl.fitToView = fitToView;
            var sp = Res.Sprite(sprite);
            pl.spriteW = sp.bounds.size.x; pl.spriteH = sp.bounds.size.y;
            pl.width = pl.spriteW * scale;
            int n = repeatX ? 3 : 1;
            pl.copies = new SpriteRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var c = new GameObject("copy" + i);
                c.transform.SetParent(go.transform, false);
                var sr = c.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sortingOrder = sort;
                sr.color = tint;
                sr.material = MaterialLibrary.SpriteUnlit;
                c.transform.localScale = Vector3.one * scale;
                pl.copies[i] = sr;
            }
            return pl;
        }

        void LateUpdate()
        {
            if (Services.Cam == null || Services.Cam.Transform == null) return;
            Vector3 cam = Services.Cam.Transform.position;
            scroll += ExtraScroll * Time.unscaledDeltaTime;
            float x = cam.x * Factor;
            float y = BaseY + cam.y * FactorY;
            if (fitToView && Services.Cam.Camera != null)
            {
                float viewH = Services.Cam.Camera.orthographicSize * 2f, viewW = viewH * Services.Cam.Camera.aspect;
                float s = Mathf.Max(viewW / spriteW, viewH / spriteH) * 1.08f;
                for (int i = 0; i < copies.Length; i++) copies[i].transform.localScale = Vector3.one * s;
                width = spriteW * s;
                y = cam.y;
            }
            transform.position = new Vector3(x, y, 0f);
            if (RepeatX && width > 0.01f)
            {
                float rel = cam.x - x - scroll;
                int k = Mathf.FloorToInt(rel / width);
                for (int i = 0; i < copies.Length; i++)
                    copies[i].transform.localPosition = new Vector3((k - 1 + i) * width + scroll, 0f, 0f);
            }
        }
    }
}
