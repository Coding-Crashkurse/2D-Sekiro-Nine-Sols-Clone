using UnityEngine;
using UnityEngine.UI;
using AshenSol.Core;

namespace AshenSol.UI
{
    /// <summary>Helpers to build uGUI elements from code.</summary>
    public static class UiKit
    {
        static Font font; static bool fontTried;

        public static Font Font
        {
            get
            {
                if (font == null && !fontTried)
                {
                    fontTried = true;
                    font = Resources.Load<Font>("Fonts/Cinzel-Regular");
                    if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return font;
            }
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        public static RectTransform Anchored(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, bool preserveAspect = true)
        {
            var rt = Anchored(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            // NOTE: frames that are stretched to a different aspect (the boss bar) must NOT preserve aspect,
            // otherwise Unity letterboxes them into a small centred box.
            img.preserveAspect = preserveAspect && sprite != null && sprite.name != "white";
            return img;
        }

        public static Image Fill(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Panel(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = null;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, string text, int size, Color color, Vector2 anchor, Vector2 pos, Vector2 boxSize, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Anchored(parent, name, anchor, pos, boxSize);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        /// <summary>Inserts thin spaces between characters for a letter-spaced heading look.</summary>
        /// <summary>Size a background image so it covers the whole canvas, keeping its 16:9 aspect —
        /// the fixed sizes left black bars down the sides of an ultrawide screen.</summary>
        public static void Cover(Image img, RectTransform canvas)
        {
            if (img == null || canvas == null) return;
            float w = canvas.rect.width, h = canvas.rect.height;
            if (w < 1f || h < 1f) return;
            float scale = Mathf.Max(w / 1920f, h / 1080f) * 1.02f;   // a hair over, so no seam shows
            img.rectTransform.sizeDelta = new Vector2(1920f * scale, 1080f * scale);
        }

        public static string Spaced(string s, int spaces = 1)
        {
            var sb = new System.Text.StringBuilder();
            string gap = new string(' ', spaces);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (i < s.Length - 1) sb.Append(s[i] == ' ' ? gap + gap : gap);
            }
            return sb.ToString();
        }

        public static void SetAlpha(CanvasGroup g, float a) { g.alpha = a; g.blocksRaycasts = false; g.interactable = false; }

        public static CanvasGroup Group(RectTransform rt) { var g = rt.gameObject.GetComponent<CanvasGroup>(); if (g == null) g = rt.gameObject.AddComponent<CanvasGroup>(); return g; }
    }
}
