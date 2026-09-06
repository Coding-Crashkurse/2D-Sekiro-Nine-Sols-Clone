using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using AshenSol.Core;

namespace AshenSol.UI
{
    /// <summary>Keyboard / gamepad / mouse driven title menu: start, difficulty, options, quit.</summary>
    public class TitleMenu
    {
        class Row
        {
            public string Label;
            public Func<string> Value;          // null = plain action row
            public Action Activate;             // Enter
            public Action<int> Change;          // Left / Right (-1 / +1)
            public RectTransform Rt;
            public Text LabelText, ValueText;
            public Image Marker;
        }

        const float RowHeight = 46f;

        readonly List<Row> rows = new List<Row>();
        readonly RectTransform root;
        Text blurb;
        int index;
        float repeatTimer;
        float lastMouseMove = -99f;
        Vector2 lastMousePos;
        bool mouseSeen;
        float mouseTravel;          // the mouse only takes over after a deliberate movement

        public Action OnStart;

        public TitleMenu(Transform parent, Vector2 anchoredPos)
        {
            root = UiKit.Anchored(parent, "Menu", new Vector2(0.5f, 0.5f), anchoredPos, new Vector2(760f, 300f));
            Settings.Load();

            Add(new Row
            {
                Label = "START GAME",
                Activate = () => { if (OnStart != null) OnStart(); }
            });
            Add(new Row
            {
                Label = "DIFFICULTY",
                Value = () => Settings.DifficultyName,
                Change = d =>
                {
                    Settings.Difficulty = Settings.Difficulty == DifficultyLevel.Disciple ? DifficultyLevel.SolSlayer : DifficultyLevel.Disciple;
                    Settings.Save();
                },
                Activate = () =>
                {
                    Settings.Difficulty = Settings.Difficulty == DifficultyLevel.Disciple ? DifficultyLevel.SolSlayer : DifficultyLevel.Disciple;
                    Settings.Save();
                }
            });
            Add(new Row
            {
                Label = "MUSIC",
                Value = () => Bar(Settings.MusicVolume),
                Change = d => { Settings.MusicVolume = Mathf.Clamp01(Settings.MusicVolume + d * 0.1f); Settings.Save(); }
            });
            Add(new Row
            {
                Label = "SOUND",
                Value = () => Bar(Settings.SfxVolume),
                Change = d =>
                {
                    Settings.SfxVolume = Mathf.Clamp01(Settings.SfxVolume + d * 0.1f);
                    Settings.Save();
                    Services.Audio.PlaySfx("sword_swing_1", 0.8f);
                }
            });
            Add(new Row
            {
                Label = "SCREEN SHAKE",
                Value = () => Settings.ScreenShake ? "ON" : "OFF",
                Change = d => { Settings.ScreenShake = !Settings.ScreenShake; Settings.Save(); },
                Activate = () => { Settings.ScreenShake = !Settings.ScreenShake; Settings.Save(); }
            });
            Add(new Row
            {
                Label = "QUIT",
                Activate = () =>
                {
                    Settings.Save();
                    Application.Quit();
                }
            });

            blurb = UiKit.Text(root, "blurb", "", 17, Palette.Bone.WithAlpha(0.55f), new Vector2(0.5f, 1f),
                new Vector2(0f, -RowHeight * rows.Count - 16f), new Vector2(900f, 26f));
            Refresh();
        }

        static string Bar(float v)
        {
            int filled = Mathf.RoundToInt(v * 10f);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append(i < filled ? "▮" : "▯");
            return sb.ToString();
        }

        void Add(Row r)
        {
            float y = -RowHeight * rows.Count;
            r.Rt = UiKit.Anchored(root, "row_" + r.Label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(700f, RowHeight));
            r.Marker = UiKit.Image(r.Rt, "marker", Res.Sprite("fx_parry_glyph"), Palette.Teal.WithAlpha(0f),
                new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(26f, 26f));
            r.LabelText = UiKit.Text(r.Rt, "label", UiKit.Spaced(r.Label), 26, Palette.Bone,
                new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(420f, RowHeight), TextAnchor.MiddleLeft);
            if (r.Value != null)
                r.ValueText = UiKit.Text(r.Rt, "value", "", 24, Palette.Teal,
                    new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(340f, RowHeight), TextAnchor.MiddleRight);
            rows.Add(r);
        }

        public void SetVisible(bool v)
        {
            root.gameObject.SetActive(v);
            if (v) { index = 0; Refresh(); }
        }

        void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool sel = i == index;
                r.LabelText.color = sel ? Color.white : Palette.Bone.WithAlpha(0.62f);
                r.LabelText.fontSize = sel ? 28 : 26;
                r.Marker.color = Palette.Teal.WithAlpha(sel ? 0.9f : 0f);
                if (r.ValueText != null)
                {
                    r.ValueText.text = (sel ? "‹  " : "   ") + r.Value() + (sel ? "  ›" : "   ");
                    r.ValueText.color = sel ? Palette.Teal : Palette.Teal.WithAlpha(0.5f);
                }
            }
            blurb.text = rows[index].Label == "DIFFICULTY" ? Settings.DifficultyBlurb
                       : rows[index].Label == "START GAME" ? "Difficulty: " + Settings.DifficultyName
                       : "";
        }

        /// <summary>Drive the menu. Call once per frame while the title screen is visible.</summary>
        public void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            var inp = Services.Input;
            if (inp == null) return;

            // marker spin on the selected row
            var m = rows[index].Marker;
            m.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 40f);

            // ---- mouse: hover selects, click activates ----
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                // only hand control to the mouse once it has actually MOVED — otherwise a cursor that
                // happens to rest over a row would silently steal the selection
                if (!mouseSeen) { mouseSeen = true; lastMousePos = mp; }
                else
                {
                    float d = (mp - lastMousePos).magnitude;
                    lastMousePos = mp;
                    if (d > 1f)
                    {
                        mouseTravel += d;
                        if (mouseTravel > 80f) lastMouseMove = Time.unscaledTime;
                    }
                }
                if (mouseTravel > 80f && Time.unscaledTime - lastMouseMove < 3f)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(rows[i].Rt, mp, null) && i != index)
                        {
                            index = i;
                            Services.Audio.PlaySfx("ui_move", 0.5f);
                            Refresh();
                            break;
                        }
                    }
                }
                if (mouseTravel > 80f && mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(rows[index].Rt, mp, null))
                {
                    Activate();
                    return;
                }
            }

            // ---- keyboard / gamepad ----
            float v = inp.Vertical, h = inp.Horizontal;
            if (Mathf.Abs(v) < 0.4f && Mathf.Abs(h) < 0.4f) repeatTimer = 0f;
            else
            {
                repeatTimer -= dt;
                if (repeatTimer <= 0f)
                {
                    repeatTimer = 0.18f;
                    if (Mathf.Abs(v) >= 0.4f)
                    {
                        index = (index + (v < 0f ? 1 : -1) + rows.Count) % rows.Count;
                        Services.Audio.PlaySfx("ui_move", 0.6f);
                        Refresh();
                    }
                    else
                    {
                        var r = rows[index];
                        if (r.Change != null)
                        {
                            r.Change(h > 0f ? 1 : -1);
                            Services.Audio.PlaySfx("ui_move", 0.6f);
                            Refresh();
                        }
                    }
                }
            }

            if (inp.ConfirmPressed) Activate();
        }

        void Activate()
        {
            var r = rows[index];
            GameEvents.RaiseLog("menu activate: " + r.Label);
            Services.Audio.PlaySfx("ui_confirm", 0.9f);
            if (r.Activate != null) r.Activate();
            else if (r.Change != null) r.Change(1);
            Refresh();
        }
    }
}
