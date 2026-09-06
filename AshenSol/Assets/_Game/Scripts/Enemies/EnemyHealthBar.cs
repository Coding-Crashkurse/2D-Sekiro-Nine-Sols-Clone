using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>World-space enemy bars: red health on top, yellow posture underneath. When the posture
    /// bar fills the guard breaks — the bar flashes white and an execution prompt appears.</summary>
    public class EnemyHealthBar
    {
        const float Width = 1.15f, HpHeight = 0.09f, PostureHeight = 0.06f, Gap = 0.035f;

        EnemyBase owner;
        Transform holder;
        SpriteRenderer back, hpFill, postureBack, postureFill;
        TextMesh prompt; MeshRenderer promptRenderer;
        float alpha, targetAlpha, hideTimer;
        float heightAbove;
        float shownHp = 1f, shownPosture;

        public static EnemyHealthBar Create(EnemyBase owner, float heightAbove)
        {
            var b = new EnemyHealthBar { owner = owner, heightAbove = heightAbove };
            b.holder = new GameObject("HealthBar").transform;
            b.holder.SetParent(owner.transform, false);
            b.holder.localPosition = new Vector3(0f, heightAbove, 0f);

            b.back = b.Bar(SortOrder.Fx + 1, Palette.Ink.WithAlpha(0.8f), Width + 0.05f, HpHeight + 0.05f, 0f);
            b.hpFill = b.Bar(SortOrder.Fx + 2, Palette.Red, Width, HpHeight, 0f);
            float py = -(HpHeight * 0.5f + Gap + PostureHeight * 0.5f);
            b.postureBack = b.Bar(SortOrder.Fx + 1, Palette.Ink.WithAlpha(0.7f), Width + 0.04f, PostureHeight + 0.04f, py);
            b.postureFill = b.Bar(SortOrder.Fx + 3, Palette.Gold, 0f, PostureHeight, py);

            var pgo = new GameObject("prompt");
            pgo.transform.SetParent(b.holder, false);
            pgo.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            b.prompt = pgo.AddComponent<TextMesh>();
            b.prompt.font = AshenSol.Level.UiFont.Get();
            b.prompt.text = "I";
            b.prompt.fontSize = 64;
            b.prompt.characterSize = 0.055f;
            b.prompt.anchor = TextAnchor.MiddleCenter;
            b.prompt.alignment = TextAlignment.Center;
            b.prompt.fontStyle = FontStyle.Bold;
            b.prompt.color = Palette.Gold;
            b.promptRenderer = pgo.GetComponent<MeshRenderer>();
            b.promptRenderer.sortingOrder = SortOrder.FxFront + 4;
            if (b.prompt.font != null) b.promptRenderer.material = b.prompt.font.material;
            b.promptRenderer.enabled = false;

            b.SetAlpha(0f);
            return b;
        }

        SpriteRenderer Bar(int sort, Color c, float w, float h, float y)
        {
            var go = new GameObject("bar");
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.White;
            sr.material = MaterialLibrary.SpriteUnlit;
            sr.color = c;
            sr.sortingOrder = sort;
            go.transform.localScale = new Vector3(w / 0.04f, h / 0.04f, 1f);
            return sr;
        }

        public void Show() { targetAlpha = 1f; hideTimer = 3f; }

        public void Hide()
        {
            targetAlpha = 0f; hideTimer = 0f; alpha = 0f;
            SetAlpha(0f);
            promptRenderer.enabled = false;
        }

        public void Tick(float dt)
        {
            if (owner == null || holder == null) return;
            float udt = Time.unscaledDeltaTime;
            // a broken guard always shows its bar — that is the window the player is looking for
            if (owner.PostureBroken) Show();
            if (hideTimer > 0f) { hideTimer -= udt; if (hideTimer <= 0f) targetAlpha = 0f; }
            alpha = Mathf.MoveTowards(alpha, targetAlpha, udt * 4f);

            // follow the body's scale so a boss that grows keeps the bar above its head
            holder.position = (Vector2)owner.transform.position + new Vector2(0f, heightAbove * owner.transform.localScale.y);
            holder.rotation = Quaternion.identity;
            if (alpha <= 0.001f) { SetAlpha(0f); promptRenderer.enabled = false; return; }

            float hp01 = owner.MaxHp > 0 ? Mathf.Clamp01((float)owner.Hp / owner.MaxHp) : 0f;
            shownHp = Mathf.Lerp(shownHp, hp01, 1f - Mathf.Exp(-14f * udt));
            shownPosture = Mathf.Lerp(shownPosture, owner.PostureDisplay01, 1f - Mathf.Exp(-18f * udt));

            SetBar(hpFill, -Width * 0.5f, shownHp * Width, HpHeight);
            SetBar(postureFill, -Width * 0.5f, shownPosture * Width, PostureHeight);
            SetAlpha(alpha);

            bool broken = owner.PostureBroken;
            promptRenderer.enabled = broken;
            if (broken)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 9f);
                prompt.color = Color.Lerp(Palette.Gold, Color.white, pulse).WithAlpha(alpha);
                prompt.characterSize = 0.055f * (1f + 0.15f * pulse);
            }
        }

        void SetBar(SpriteRenderer sr, float left, float w, float h)
        {
            sr.transform.localScale = new Vector3(Mathf.Max(0f, w) / 0.04f, h / 0.04f, 1f);
            var p = sr.transform.localPosition;
            sr.transform.localPosition = new Vector3(left + w * 0.5f, p.y, 0f);
        }

        void SetAlpha(float a)
        {
            back.color = Palette.Ink.WithAlpha(0.8f * a);
            postureBack.color = Palette.Ink.WithAlpha(0.7f * a);
            hpFill.color = Palette.Red.WithAlpha(a);
            // full or broken posture reads as white-hot
            bool hot = owner != null && (owner.PostureBroken || owner.Posture01 > 0.995f);
            float pulse = hot ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f) : 0f;
            postureFill.color = Color.Lerp(Palette.Gold, Color.white, pulse).WithAlpha(a);
        }
    }
}
