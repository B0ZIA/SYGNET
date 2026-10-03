using System;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Lista obszarów do wyboru (onboarding i zmiana z chipa na ekranie głównym).</summary>
    public sealed class AreaPicker
    {
        readonly RectTransform root;
        readonly Action<int> onPick;
        int selected;

        public AreaPicker(Transform parent, int selected, Action<int> onPick)
        {
            this.selected = selected;
            this.onPick = onPick;
            root = Ui.Rect(parent, "AreaPicker");
            Ui.VStack(root, 16);
            Rebuild();
        }

        public RectTransform Root => root;

        public void Select(int code)
        {
            selected = code;
            Rebuild();
        }

        void Rebuild()
        {
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            foreach (var kv in Areas.Demo)
            {
                int code = kv.Key;
                bool on = code == selected;
                var card = Ui.Card(root, "Area " + code, on ? Theme.Surface2 : Theme.Surface);
                card.raycastTarget = true;
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                btn.onClick.AddListener(() =>
                {
                    Select(code);
                    onPick?.Invoke(code);
                });
                Ui.Layout(card, 150, 150);

                var name = Ui.Label(card.transform, kv.Value, TextStyle.BodyStrong, Theme.Text, TextAlignmentOptions.BottomLeft);
                Ui.Stretch(name.rectTransform, 44, 22, 150, 78);
                var desc = Ui.Label(card.transform, Describe(code), TextStyle.Caption, Theme.Muted, TextAlignmentOptions.TopLeft);
                Ui.Stretch(desc.rectTransform, 44, 80, 150, 16);

                var mark = Ui.Image(card.transform, "Radio", on ? Theme.Text : Theme.Dim, on ? Icons.CheckCircle : Icons.Ring);
                Ui.Pin(mark.rectTransform, new Vector2(1, 0.5f), new Vector2(on ? 64 : 58, on ? 64 : 58), new Vector2(-44, 0));
            }
        }

        public static string Describe(int code)
        {
            if (code == 0) return "komunikaty ogólnokrajowe";
            if (code < 100) return "województwo · TERYT " + code.ToString("00");
            return "miasto na prawach powiatu · TERYT " + code.ToString("0000");
        }
    }

    /// <summary>Zmiana obszaru użytkownika (dotknięcie chipa obszaru na ekranie głównym).</summary>
    public class AreaScreen : AppScreen
    {
        readonly AreaPicker picker;

        public AreaScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Area", Theme.Bg)
        {
            Ui.Header(Safe, "Twój obszar", () => App.Show(App.Home));
            var body = Ui.Rect(Safe, "Body");
            Ui.Stretch(body, 0, Ui.BelowHeader, 0, 0);
            var content = Ui.Scroll(body, "Scroll", 28, new RectOffset(56, 56, 8, 56));
            var intro = Ui.Label(content, "Komunikaty dla Twojego obszaru pokażemy jako ZWERYFIKOWANO, " +
                                          "pozostałe prawdziwe – jako INNY OBSZAR.", TextStyle.Body, Theme.Muted);
            Ui.Layout(intro);
            picker = new AreaPicker(content, App.Store.UserArea, code =>
            {
                App.Store.UserArea = code;
                App.ShowToast("Obszar: " + Areas.Name(code), 2f);
            });
        }

        public override void OnShow() => picker.Select(App.Store.UserArea);

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }
    }
}
