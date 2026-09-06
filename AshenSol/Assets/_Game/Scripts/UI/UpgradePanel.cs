using System;
using UnityEngine;
using UnityEngine.UI;
using AshenSol.Core;

namespace AshenSol.UI
{
    /// <summary>The shrine menu: pick one of three permanent upgrades with a spent level.
    /// Runs on unscaled time because the game is paused underneath it.</summary>
    public class UpgradePanel
    {
        class Card
        {
            public UpgradeKind Kind;
            public RectTransform Rt;
            public Image Frame, Glow;
            public Text Title, Detail;
        }

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Card[] cards = new Card[3];
        Text header, footer;
        int index;
        float repeat, armTime;
        Action<UpgradeKind> onPick;

        public bool IsOpen { get; private set; }

        public UpgradePanel(Transform parent)
        {
            root = UiKit.Panel(parent, "UpgradePanel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            group = UiKit.Group(root);
            UiKit.Fill(root, "bg", Palette.Ink.WithAlpha(0.82f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var c = new Vector2(0.5f, 0.5f);
            header = UiKit.Text(root, "header", "", 44, Palette.Gold, c, new Vector2(0f, 250f), new Vector2(1400f, 70f));
            UiKit.Text(root, "sub", "Spend ash at the shrine. Choose what it tempers.", 20, Palette.Bone.WithAlpha(0.65f),
                c, new Vector2(0f, 196f), new Vector2(1200f, 30f));

            for (int i = 0; i < 3; i++)
            {
                var card = new Card { Kind = (UpgradeKind)i };
                float x = (i - 1) * 330f;
                card.Rt = UiKit.Anchored(root, "card" + i, c, new Vector2(x, 20f), new Vector2(300f, 250f));
                card.Glow = UiKit.Image(card.Rt, "glow", Res.Sprite("fx_glow"), Palette.Gold.WithAlpha(0f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f, 380f));
                card.Glow.material = MaterialLibrary.Additive;
                UiKit.Image(card.Rt, "plate", null, Palette.Night.WithAlpha(0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 250f));
                card.Frame = UiKit.Image(card.Rt, "frame", Res.Sprite("ui_bar_frame"), Palette.Bone.WithAlpha(0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 250f), false);
                card.Title = UiKit.Text(card.Rt, "title", "", 30, Palette.Bone, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(280f, 40f));
                card.Detail = UiKit.Text(card.Rt, "detail", "", 19, Palette.Bone.WithAlpha(0.7f), c, new Vector2(0f, -14f), new Vector2(280f, 100f));
                card.Detail.lineSpacing = 1.3f;
                cards[i] = card;
            }

            footer = UiKit.Text(root, "footer", UiKit.Spaced("A / D  choose      ENTER  temper"), 18,
                Palette.Bone.WithAlpha(0.5f), c, new Vector2(0f, -190f), new Vector2(1200f, 30f));

            group.alpha = 0f;
            root.gameObject.SetActive(false);
        }

        public void Open(Action<UpgradeKind> pick)
        {
            if (IsOpen) return;
            IsOpen = true;
            onPick = pick;
            index = 0;
            armTime = Time.unscaledTime + 0.35f;
            root.gameObject.SetActive(true);
            group.alpha = 0f;
            if (TimeController.Instance != null) TimeController.Instance.SetPaused(true);
            Services.Audio.PlaySfx("checkpoint", 0.7f);
            Refresh();
        }

        void Dismiss()
        {
            Close();
            group.alpha = 0f;
            root.gameObject.SetActive(false);
        }

        void Close()
        {
            IsOpen = false;
            onPick = null;
            if (TimeController.Instance != null) TimeController.Instance.SetPaused(false);
        }

        void Refresh()
        {
            header.text = UiKit.Spaced("LEVEL " + Progression.Level + "  →  " + (Progression.Level + 1), 2);
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                bool sel = i == index;
                bool ok = Progression.Available(card.Kind);
                card.Title.text = UiKit.Spaced(Progression.Title(card.Kind));
                card.Detail.text = Progression.Detail(card.Kind);
                card.Title.color = !ok ? Palette.Bone.WithAlpha(0.25f) : sel ? Palette.Gold : Palette.Bone.WithAlpha(0.75f);
                card.Detail.color = Palette.Bone.WithAlpha(ok ? (sel ? 0.85f : 0.5f) : 0.2f);
                card.Frame.color = Palette.Bone.WithAlpha(sel ? 0.9f : 0.28f);
                card.Glow.color = Palette.Gold.WithAlpha(sel && ok ? 0.22f : 0f);
                card.Rt.localScale = Vector3.one * (sel ? 1.06f : 0.96f);
            }
            footer.text = UiKit.Spaced("COST " + Progression.LevelCost + " ASH      you carry " + Progression.Ash
                                       + "      A / D  choose      ENTER  temper      ESC  leave");
        }

        public void Tick()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            group.alpha = Mathf.MoveTowards(group.alpha, 1f, dt * 5f);
            for (int i = 0; i < cards.Length; i++)
                if (i == index) cards[i].Glow.color = Palette.Gold.WithAlpha(0.16f + 0.1f * Mathf.Sin(Time.unscaledTime * 3f));

            var inp = Services.Input;
            if (inp == null || Time.unscaledTime < armTime) return;

            float h = inp.Horizontal;
            if (Mathf.Abs(h) < 0.4f) repeat = 0f;
            else
            {
                repeat -= dt;
                if (repeat <= 0f)
                {
                    repeat = 0.2f;
                    index = (index + (h > 0f ? 1 : -1) + cards.Length) % cards.Length;
                    Services.Audio.PlaySfx("ui_move", 0.6f);
                    Refresh();
                }
            }

            if (inp.PausePressed)
            {
                Services.Audio.PlaySfx("ui_move", 0.5f);
                Dismiss();
                return;
            }
            if (inp.ConfirmPressed)
            {
                var kind = cards[index].Kind;
                if (!Progression.Available(kind))
                {
                    Services.Audio.PlaySfx("ui_move", 0.4f);
                    return;
                }
                var cb = onPick;
                Services.Audio.PlaySfx("ui_confirm", 1f);
                if (cb != null) cb(kind);
                // stay open while there is still ash to spend
                if (Progression.CanAffordLevel) { armTime = Time.unscaledTime + 0.25f; Refresh(); }
                else Dismiss();
            }
        }
    }
}
