using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    public enum ButtonKind
    {
        Primary,     // biała pigułka, ciemny tekst – jedna na ekran
        Secondary,   // ciemna pigułka
        OnColor,     // półprzezroczysta czerń na tle statusu
    }

    /// <summary>Fabryka kontrolek uGUI + TextMeshPro. Cały interfejs jest budowany w kodzie, wg Theme.</summary>
    public static class Ui
    {
        // ───────────── układ ─────────────

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Przypina do góry (y od góry) na pełną szerokość z marginesami.</summary>
        public static void Top(RectTransform rt, float y, float height, float margin = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -y);
            rt.sizeDelta = new Vector2(-2 * margin, height);
        }

        /// <summary>Przypina do dołu (y od dołu) na pełną szerokość z marginesami.</summary>
        public static void Bottom(RectTransform rt, float y, float height, float margin = 0)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(-2 * margin, height);
        }

        public static void Center(RectTransform rt, Vector2 size, Vector2 offset = default)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        /// <summary>Kotwica w punkcie (0..1) rodzica, pivot = ten sam punkt.</summary>
        public static void Pin(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset = default)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
        }

        public static LayoutElement Layout(Component c, float minHeight = -1, float preferredHeight = -1, float preferredWidth = -1,
            float flexibleWidth = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minHeight;
            le.preferredHeight = preferredHeight;
            le.preferredWidth = preferredWidth;
            le.minWidth = preferredWidth;
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset();
            v.childAlignment = align;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>Pionowo przewijana zawartość; zwraca kontener z VerticalLayoutGroup i ContentSizeFitter.</summary>
        public static RectTransform Scroll(Transform parent, string name, float spacing, RectOffset padding)
        {
            var view = Rect(parent, name);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = Color.clear;                                   // łapie przeciąganie
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 40;

            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VStack(content, spacing, padding);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content;
            sr.viewport = view;
            return content;
        }

        // ───────────── grafika ─────────────

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        public static Image Icon(Transform parent, Sprite sprite, Color color, float size)
        {
            var img = Image(parent, "Icon", color, sprite);
            img.preserveAspect = true;
            Layout(img, size, size, size);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        /// <summary>Prostokąt z zaokrąglonymi rogami (sprite 9-slice). radius ≥ połowa wysokości = pigułka.</summary>
        public static Image Card(Transform parent, string name, Color color, float radius = Theme.RadiusCard)
        {
            var img = Image(parent, name, color, Icons.RoundedRect);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            SetRadius(img, radius);
            return img;
        }

        public static void SetRadius(Image img, float radius) =>
            img.pixelsPerUnitMultiplier = Icons.RoundedBorder / Mathf.Max(1f, radius);

        // ───────────── tekst ─────────────

        public static TextMeshProUGUI Label(Transform parent, string text, TextStyle style, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            ApplyStyle(t, style);
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static void ApplyStyle(TextMeshProUGUI t, TextStyle style)
        {
            var (font, size, tracking, upper, leading) = Theme.Style(style);
            t.font = font;
            t.fontSize = size;
            t.characterSpacing = tracking;
            t.lineSpacing = leading;
            t.fontStyle = upper ? FontStyles.UpperCase : FontStyles.Normal;
        }

        /// <summary>Tekst z ramki (dopisek) to dane atakującego: bez rich text, żeby &lt;color&gt; itp. nie udawały UI.</summary>
        public static TextMeshProUGUI PlainLabel(Transform parent, string text, TextStyle style, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Label(parent, "", style, color, align);
            t.richText = false;
            t.text = text;
            return t;
        }

        // ───────────── kontrolki ─────────────

        public static Button Button(Transform parent, string label, ButtonKind kind, UnityAction onClick, Sprite icon = null,
            float height = Theme.ButtonHeight)
        {
            Color bg, fg;
            switch (kind)
            {
                case ButtonKind.Primary: bg = Theme.Text; fg = Theme.Bg; break;
                case ButtonKind.OnColor: bg = new Color(0, 0, 0, 0.28f); fg = Color.white; break;
                default: bg = Theme.Surface2; fg = Theme.Text; break;
            }
            var img = Card(parent, "Button " + label, bg, height / 2);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.disabledColor = new Color(1, 1, 1, 0.45f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var row = Rect(img.transform, "Row");
            HStack(row, 22, TextAnchor.MiddleCenter);
            if (icon != null) Icon(row, icon, fg, 52);
            var t = Label(row, label, TextStyle.Button, fg, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return btn;
        }

        public static void SetLabel(Button b, string label) => b.GetComponentInChildren<TextMeshProUGUI>().text = label;

        /// <summary>Pigułka z ikoną i tekstem (stan, obszar, klucz). Szerokość dopasowuje się do treści.</summary>
        public static (Image bg, TextMeshProUGUI text) Chip(Transform parent, Sprite icon, string text, Color bg, Color fg,
            TextStyle style = TextStyle.Caption, float height = 76)
        {
            var img = Card(parent, "Chip", bg, height / 2);
            var row = img.rectTransform;
            var h = HStack(row, 14, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(28, 30, 0, 0);
            var fit = img.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            img.rectTransform.sizeDelta = new Vector2(img.rectTransform.sizeDelta.x, height);
            if (icon != null) Icon(row, icon, fg, height * 0.46f);
            var t = Label(row, text, style, fg, TextAlignmentOptions.MidlineLeft);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Layout(img, height, height);
            return (img, t);
        }

        /// <summary>Niewidoczny przycisk na całym prostokącie (np. 5× tap w logo).</summary>
        public static Button HitArea(RectTransform rt, UnityAction onClick)
        {
            if (!rt.TryGetComponent<Graphic>(out var g))
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.color = Color.clear;
                g = img;
            }
            g.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = g;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            return b;
        }

        // ───────────── formaty ─────────────

        public static string Time(long unix) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        public static string Clock(long unix) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm");
    }
}
