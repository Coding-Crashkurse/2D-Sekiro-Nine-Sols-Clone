using UnityEngine;
using AshenSol.Core;

namespace AshenSol.Enemies
{
    /// <summary>Tiny world-space bar above an enemy: HP (bone) with internal damage (red) drawn from the right edge.</summary>
    public class EnemyHealthBar
    {
        const float Width = 1.1f, Height = 0.09f;
        EnemyBase owner;
        Transform holder;
        SpriteRenderer back, fill, internalFill;
        float alpha, targetAlpha, hideTimer;
        float heightAbove;
        float shownHp = 1f;

        public static EnemyHealthBar Create(EnemyBase owner, float heightAbove)
        {
            var b = new EnemyHealthBar { owner = owner, heightAbove = heightAbove };
            b.holder = new GameObject("HealthBar").transform;
            b.holder.SetParent(owner.transform, false);
            b.holder.localPosition = new Vector3(0f, heightAbove, 0f);
            b.back = b.Bar(SortOrder.Fx + 1, Palette.Ink.WithAlpha(0.8f), Width + 0.04f, Height + 0.04f);
            b.fill = b.Bar(SortOrder.Fx + 2, Palette.Bone, Width, Height);
            b.internalFill = b.Bar(SortOrder.Fx + 3, Palette.InternalDamage, 0f, Height);
            b.SetAlpha(0f);
            return b;
        }

        SpriteRenderer Bar(int sort, Color c, float w, float h)
        {
            var go = new GameObject("bar");
            go.transform.SetParent(holder, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Res.White;
            sr.material = MaterialLibrary.SpriteUnlit;
            sr.color = c;
            sr.sortingOrder = sort;
            go.transform.localScale = new Vector3(w / 0.04f, h / 0.04f, 1f);
            return sr;
        }

        public void Show() { targetAlpha = 1f; hideTimer = 3f; }
        public void Hide() { targetAlpha = 0f; hideTimer = 0f; alpha = 0f; SetAlpha(0f); }

        public void Tick(float dt)
        {
            if (owner == null || holder == null) return;
            float udt = Time.unscaledDeltaTime;
            if (hideTimer > 0f) { hideTimer -= udt; if (hideTimer <= 0f) targetAlpha = 0f; }
            alpha = Mathf.MoveTowards(alpha, targetAlpha, udt * 4f);
            holder.position = owner.Center + new Vector2(0f, heightAbove - (owner.Center.y - owner.transform.position.y));
            holder.rotation = Quaternion.identity;
            if (alpha <= 0.001f) { SetAlpha(0f); return; }

            float hp01 = owner.MaxHp > 0 ? Mathf.Clamp01((float)owner.Hp / owner.MaxHp) : 0f;
            float int01 = owner.MaxHp > 0 ? Mathf.Clamp01((float)owner.InternalDamage / owner.MaxHp) : 0f;
            shownHp = Mathf.Lerp(shownHp, hp01, 1f - Mathf.Exp(-14f * udt));
            SetBar(fill, -Width * 0.5f, shownHp * Width);
            float intW = Mathf.Min(int01, shownHp) * Width;
            SetBar(internalFill, -Width * 0.5f + shownHp * Width - intW, intW);
            SetAlpha(alpha);
        }

        void SetBar(SpriteRenderer sr, float left, float w)
        {
            sr.transform.localScale = new Vector3(Mathf.Max(0f, w) / 0.04f, Height / 0.04f, 1f);
            sr.transform.localPosition = new Vector3(left + w * 0.5f, 0f, 0f);
        }

        void SetAlpha(float a)
        {
            back.color = Palette.Ink.WithAlpha(0.8f * a);
            fill.color = Palette.Bone.WithAlpha(a);
            internalFill.color = Palette.InternalDamage.WithAlpha(a);
        }
    }
}
