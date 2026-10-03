using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Skaner QR (CLIENT_UNITY.md §4.3): podgląd tylnej kamery, celownik, podpowiedź. Linków nigdy nie otwieramy.</summary>
    public class ScanScreen : AppScreen
    {
        const string DefaultHint = "Nakieruj aparat na kod QR komunikatu SYGNET";
        const float FinderSize = 740;
        static readonly Vector2 FinderOffset = new Vector2(0, 80);

        readonly RawImage preview;
        readonly RectTransform finder, scanLine;
        readonly TextMeshProUGUI hint;
        readonly Image hintCard;
        readonly Button retry;
        float errorUntil;

        public ScanScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Scan", Color.black)
        {
            var pv = Ui.Rect(Root, "Preview");
            pv.SetSiblingIndex(1);                                         // nad tłem, pod treścią
            preview = pv.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;

            // przyciemnienie poza celownikiem: 4 prostokąty wokół wycięcia
            var shade = Theme.WithAlpha(Theme.Bg, 0.62f);
            finder = Ui.Rect(Safe, "Finder");
            Ui.Center(finder, new Vector2(FinderSize, FinderSize), FinderOffset);
            foreach (var (min, max) in new[]
                     {
                         (new Vector2(-10, 1), new Vector2(11, 10)), (new Vector2(-10, -10), new Vector2(11, 0)),
                         (new Vector2(-10, 0), new Vector2(0, 1)), (new Vector2(1, 0), new Vector2(11, 1)),
                     })
            {
                var r = Ui.Image(finder, "Shade", shade).rectTransform;
                r.anchorMin = min;
                r.anchorMax = max;
                r.offsetMin = r.offsetMax = Vector2.zero;
            }

            // narożniki
            const float len = 120, th = 12;
            foreach (var (ax, ay) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f) })
            {
                foreach (var size in new[] { new Vector2(len, th), new Vector2(th, len) })
                {
                    var c = Ui.Card(finder, "Corner", Theme.Text, th / 2).rectTransform;
                    Ui.Pin(c, new Vector2(ax, ay), size);
                }
            }

            // linia skanowania
            scanLine = Ui.Card(finder, "ScanLine", Theme.WithAlpha(Theme.Text, 0.7f), 3).rectTransform;
            scanLine.anchorMin = new Vector2(0, 0.5f);
            scanLine.anchorMax = new Vector2(1, 0.5f);
            scanLine.sizeDelta = new Vector2(-80, 6);

            // górny pasek
            var back = Ui.Button(Safe, "", ButtonKind.Secondary, () => App.Show(App.Home), Icons.Back, 128);
            var brt = (RectTransform)back.transform;
            Ui.Pin(brt, new Vector2(0, 1), new Vector2(128, 128), new Vector2(Theme.Margin, -40));
            var title = Ui.Label(Safe, "Skanuj kod QR", TextStyle.Title, Theme.Text, TextAlignmentOptions.MidlineLeft);
            Ui.Top(title.rectTransform, 40, 128, 0);
            title.rectTransform.offsetMin = new Vector2(Theme.Margin + 128 + 36, title.rectTransform.offsetMin.y);

            // podpowiedź
            hintCard = Ui.Card(Safe, "Hint", Theme.WithAlpha(Theme.Surface, 0.92f));
            Ui.Bottom(hintCard.rectTransform, 220, 200, Theme.Margin);
            hint = Ui.Label(hintCard.transform, DefaultHint, TextStyle.Body, Theme.Text, TextAlignmentOptions.Center);
            Ui.Stretch(hint.rectTransform, 44, 20, 44, 20);

            retry = Ui.Button(Safe, "Spróbuj ponownie", ButtonKind.Primary, () => App.Scanner.StartCamera());
            Ui.Bottom((RectTransform)retry.transform, 48, Theme.ButtonHeight, Theme.Margin);
        }

        public override void OnShow()
        {
            errorUntil = 0;
            SetHint(DefaultHint, false);
            preview.texture = null;
            preview.color = Color.clear;
            App.Scanner.TextScanned += OnText;
            App.Scanner.StartCamera();
        }

        public override void OnHide()
        {
            App.Scanner.TextScanned -= OnText;
            App.Scanner.StopCamera();
            preview.texture = null;
        }

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }

        void OnText(string text)
        {
            if (App.HandleQrText(text)) return;                           // aplikacja przeszła do ekranu wyniku
            if (!text.TrimStart().StartsWith(Sygnet.Core.Frame.QrPrefix))
                ShowError("To nie jest komunikat SYGNET.\nLinków z kodów QR nie otwieramy.");
        }

        void ShowError(string text)
        {
            SetHint(text, true);
            errorUntil = Time.unscaledTime + 3f;
        }

        void SetHint(string text, bool error)
        {
            hint.text = text;
            hintCard.color = error ? Theme.WithAlpha(Theme.Danger, 0.95f) : Theme.WithAlpha(Theme.Surface, 0.92f);
        }

        public override void Tick()
        {
            // linia skanowania: płynnie góra–dół w obrębie celownika
            float phase = Mathf.PingPong(Time.unscaledTime * 0.55f, 1f);
            float eased = phase * phase * (3 - 2 * phase);
            scanLine.anchoredPosition = new Vector2(0, (eased - 0.5f) * (FinderSize - 80));

            var s = App.Scanner;
            retry.gameObject.SetActive(s.Status == QrScanner.State.PermissionDenied);
            if (s.Status == QrScanner.State.PermissionDenied)
                SetHint("Brak dostępu do aparatu. Zezwól na aparat, żeby skanować kody.", true);
            else if (s.Status == QrScanner.State.NoCamera)
                SetHint("Nie znaleziono aparatu.", true);
            else if (errorUntil > 0 && Time.unscaledTime > errorUntil)
            {
                errorUntil = 0;
                SetHint(DefaultHint, false);
            }

            var tex = s.Texture;
            if (tex == null || s.Status != QrScanner.State.Running) return;
            if (preview.texture != tex)
            {
                preview.texture = tex;
                preview.color = Color.white;
            }
            FitPreview(tex);
        }

        /// <summary>Wypełnia ekran podglądem z uwzględnieniem obrotu sensora (aspect fill).</summary>
        void FitPreview(WebCamTexture tex)
        {
            int rot = tex.videoRotationAngle;
            bool swap = rot % 180 != 0;
            float tw = swap ? tex.height : tex.width;
            float th = swap ? tex.width : tex.height;
            var area = Root.rect.size;
            float scale = Mathf.Max(area.x / tw, area.y / th);
            var rt = preview.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(tex.width * scale, tex.height * scale);
            rt.localEulerAngles = new Vector3(0, 0, -rot);
            preview.uvRect = tex.videoVerticallyMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        }
    }
}
