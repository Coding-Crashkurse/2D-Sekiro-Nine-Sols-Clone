using System;
using UnityEngine;
using UnityEngine.UI;
using AshenSol.Core;

namespace AshenSol.UI
{
    /// <summary>
    /// The end credits: a slow roll over the night valley with the credits theme under it, ending on a
    /// card that holds for a few seconds. Any key skips. Everything runs on unscaled time.
    /// </summary>
    public class CreditsRoll
    {
        enum Style { Title, Tagline, Role, Name, Cast, Rule, Gap, End }

        struct Line
        {
            public Style Style; public string Text;
            public Line(Style s, string t) { Style = s; Text = t; }
        }

        static readonly Line[] Script =
        {
            new Line(Style.Gap, "1.0"),
            new Line(Style.Title, "ASHEN SOL"),
            new Line(Style.Tagline, "a game about holding a blade steady"),
            new Line(Style.Rule, ""),

            new Line(Style.Role, "Producer"),
            new Line(Style.Name, "Markus Lang"),
            new Line(Style.Role, "Lead Engineer"),
            new Line(Style.Name, "Claude Code"),
            new Line(Style.Role, "Test Engineer"),
            new Line(Style.Name, "Sibylle Piechaczek"),
            new Line(Style.Gap, "0.6"),

            new Line(Style.Role, "Design & Direction"),
            new Line(Style.Name, "Markus Lang"),
            new Line(Style.Role, "Programming, Art & Animation"),
            new Line(Style.Name, "Claude Code"),
            new Line(Style.Role, "Testing & Quality"),
            new Line(Style.Name, "Sibylle Piechaczek"),
            new Line(Style.Role, "Music, Voice & Sound Design"),
            new Line(Style.Name, "ElevenLabs"),
            new Line(Style.Role, "Voice of the Warden"),
            new Line(Style.Name, "Cornelius — Wise Sage"),
            new Line(Style.Role, "Built With"),
            new Line(Style.Name, "Unity 6  ·  Universal Render Pipeline"),
            new Line(Style.Rule, ""),

            new Line(Style.Role, "The Sealed Valley"),
            new Line(Style.Cast, "The Last Student"),
            new Line(Style.Cast, "The Seventh Artisan  —  Smith of the Dead Sun"),
            new Line(Style.Cast, "The Forsaken Warden  —  Keeper of the Sealed Gate"),
            new Line(Style.Cast, "Ash Unbound  —  What the Ninth Sun Left Behind"),
            new Line(Style.Rule, ""),

            new Line(Style.Role, "Nine suns burned over this valley."),
            new Line(Style.Name, "One of them was your master."),
            new Line(Style.Gap, "0.8"),
            new Line(Style.Role, "Thank you for parrying instead of running."),
            new Line(Style.Rule, ""),
            new Line(Style.End, "THE END"),
            new Line(Style.Gap, "1.2"),
        };

        const float RollSeconds = 56f;   // first line rising to the last line resting on the centre
        const float HoldSeconds = 5f;    // how long THE END sits there before the fade

        readonly RectTransform root, content;
        readonly CanvasGroup group;
        readonly Image glow;
        Image valley;
        readonly Text skipHint;

        float startY, endY, elapsed, armTime, total, lastLineY;
        bool running, finishing;
        Action onDone;

        public bool IsRunning { get { return running; } }

        public CreditsRoll(RectTransform parent)
        {
            root = UiKit.Panel(parent, "Credits", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            group = UiKit.Group(root);
            UiKit.Fill(root, "black", new Color(Palette.Ink.r, Palette.Ink.g, Palette.Ink.b, 1f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // the valley you came through, sunk back into the night
            valley = UiKit.Image(root, "valley", Res.Sprite("bg_title"), new Color(0.34f, 0.32f, 0.36f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2240f, 1260f), false);
            valley.preserveAspect = false;
            UiKit.Fill(root, "veil", Palette.Ink.WithAlpha(0.55f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // a low ember wash along the bottom
            glow = UiKit.Image(root, "wash", Res.Sprite("fx_glow"), Palette.Amber.WithAlpha(0.1f),
                new Vector2(0.5f, 0f), new Vector2(0f, -180f), new Vector2(2400f, 900f), false);
            glow.material = MaterialLibrary.Additive;

            content = UiKit.Anchored(root, "content", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1500f, 100f));

            float cursor = 0f, lastLine = 0f;
            foreach (var entry in Script)
            {
                float h; int size; Color color; string text = entry.Text;
                switch (entry.Style)
                {
                    case Style.Gap:
                        cursor += 120f * float.Parse(entry.Text, System.Globalization.CultureInfo.InvariantCulture);
                        continue;
                    case Style.Rule:
                        UiKit.Image(content, "rule", null, Palette.Bone.WithAlpha(0.16f),
                            new Vector2(0.5f, 1f), new Vector2(0f, -cursor - 60f), new Vector2(420f, 1f), false);
                        cursor += 150f;
                        continue;
                    case Style.Title: h = 150f; size = 80; color = Palette.Gold; break;
                    case Style.Tagline: h = 90f; size = 26; color = Palette.Bone.WithAlpha(0.55f); break;
                    case Style.Role: h = 54f; size = 24; color = Palette.Bone.WithAlpha(0.45f); break;
                    case Style.Name: h = 92f; size = 40; color = Palette.Bone; break;
                    case Style.Cast: h = 60f; size = 25; color = Palette.Bone.WithAlpha(0.72f); break;
                    default: h = 170f; size = 60; color = Palette.Gold; break;
                }
                if (entry.Style == Style.Title || entry.Style == Style.End) text = UiKit.Spaced(text, 2);
                else if (entry.Style == Style.Role) text = UiKit.Spaced(text, 1);
                lastLine = cursor + h * 0.5f;
                UiKit.Text(content, "line", text, size, color, new Vector2(0.5f, 1f),
                    new Vector2(0f, -cursor), new Vector2(1500f, h));
                cursor += h;
            }

            total = cursor; lastLineY = lastLine;
            content.sizeDelta = new Vector2(1500f, total);
            Measure();
            content.anchoredPosition = new Vector2(0f, startY);

            skipHint = UiKit.Text(root, "skip", UiKit.Spaced("PRESS ANY KEY TO SKIP"), 17,
                Palette.Bone.WithAlpha(0.3f), new Vector2(1f, 0f), new Vector2(-60f, 48f), new Vector2(460f, 30f),
                TextAnchor.MiddleRight);

            group.alpha = 0f;
            root.gameObject.SetActive(false);
        }

        /// <summary>Recompute the travel against the live canvas — it is only laid out once shown.</summary>
        void Measure()
        {
            float screenH = root.rect.height > 1f ? root.rect.height : 1080f;
            startY = -total;                                  // the whole roll parked below the screen
            endY = screenH * 0.5f - total + lastLineY;        // last line resting on the centre line
        }

        public void Play(Action done)
        {
            Measure();
            UiKit.Cover(valley, root);
            if (running) return;
            running = true; finishing = false; elapsed = 0f;
            onDone = done;
            armTime = Time.unscaledTime + 1.5f;   // a key still held from the victory screen must not skip it
            content.anchoredPosition = new Vector2(0f, startY);
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            group.alpha = 0f;
            Services.Audio.PlayMusic("music_credits", 2f);
            GameEvents.RaiseLog("credits roll");
        }

        public void Tick()
        {
            if (!running) return;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;

            group.alpha = finishing
                ? Mathf.MoveTowards(group.alpha, 0f, dt * 0.8f)
                : Mathf.MoveTowards(group.alpha, 1f, dt * 0.7f);

            float k = Mathf.Clamp01(elapsed / RollSeconds);
            content.anchoredPosition = new Vector2(0f, Mathf.Lerp(startY, endY, k));
            glow.color = Palette.Amber.WithAlpha(0.07f + 0.05f * Mathf.Sin(Time.unscaledTime * 0.6f));
            if (skipHint != null)
                skipHint.color = Palette.Bone.WithAlpha(finishing ? 0f : 0.3f * Mathf.Clamp01((Time.unscaledTime - armTime) * 2f));

            var inp = Services.Input;
            bool skip = !finishing && Time.unscaledTime > armTime && inp != null
                        && (inp.AnyPressed || inp.ConfirmPressed || inp.PausePressed || inp.InteractPressed);
            if (skip)
            {
                finishing = true;
                Services.Audio.StopMusic(1f);
                GameEvents.RaiseLog("credits skipped");
            }
            else if (!finishing && elapsed >= RollSeconds + HoldSeconds)
            {
                finishing = true;
                Services.Audio.StopMusic(2.5f);
            }

            if (finishing && group.alpha <= 0.001f)
            {
                running = false;
                root.gameObject.SetActive(false);
                var cb = onDone; onDone = null;
                if (cb != null) cb();
            }
        }
    }
}
