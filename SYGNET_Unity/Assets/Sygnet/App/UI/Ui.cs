using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Fabryka kontrolek uGUI + TextMeshPro. Cały interfejs jest budowany w kodzie.</summary>
    public static class Ui
    {
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

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Panel z zaokrąglonymi rogami (sprite 9-slice).</summary>
        public static Image Card(Transform parent, string name, Color color, float radius = Theme.Radius)
        {
            var img = Image(parent, name, color, Icons.RoundedRect);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = Icons.RoundedBorder / Mathf.Max(1f, radius);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Przycisk: zaokrąglone tło, opcjonalna ikona po lewej, etykieta.</summary>
        public static Button Button(Transform parent, string label, Color bg, Color fg, UnityAction onClick,
            Sprite icon = null, float textSize = Theme.TextBody, float radius = 64)
        {
            var img = Card(parent, "Button " + label, bg, radius);
            img.raycastTarget = true;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(1, 1, 1, 0.35f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var row = Rect(img.transform, "Row");
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = 24;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (icon != null)
            {
                var ic = Image(row, "Icon", fg, icon);
                ic.preserveAspect = true;
                Layout(ic, textSize * 1.1f, textSize * 1.1f);
            }
            var t = Text(row, label, textSize, fg, FontStyles.Bold, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return btn;
        }

        public static void SetLabel(Button b, string label) => b.GetComponentInChildren<TextMeshProUGUI>().text = label;

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

        public static LayoutElement Layout(Component c, float minHeight = -1, float preferredHeight = -1, float minWidth = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minHeight;
            le.preferredHeight = preferredHeight;
            if (minWidth >= 0)
            {
                le.minWidth = minWidth;
                le.preferredWidth = minWidth;
            }
            return le;
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

        /// <summary>Tekst z ramki (dopisek) jest danymi atakującego: bez rich text, żeby &lt;color&gt; itp. nie udawały UI.</summary>
        public static TextMeshProUGUI PlainText(Transform parent, string text, float size, Color color,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var t = Text(parent, "", size, color, style, align);
            t.richText = false;
            t.text = text;
            return t;
        }

        public static string Time(long unix) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        public static string Clock(long unix) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm");
    }
}
