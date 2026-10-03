using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Skaner QR (CLIENT_UNITY.md §4.3): podgląd tylnej kamery, celownik, podpowiedź. Linków nigdy nie otwieramy.</summary>
    public class ScanScreen : AppScreen
    {
        const string DefaultHint = "Nakieruj aparat na kod QR komunikatu SYGNET";

        readonly RawImage preview;
        readonly TextMeshProUGUI hint;
        readonly Image hintCard;
        readonly Button retry;
        float errorUntil;

        public ScanScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Scan", Color.black)
        {
            var pv = Ui.Rect(Root, "Preview");
            pv.SetSiblingIndex(1);                                         // nad tłem, pod obszarem bezpiecznym
            preview = pv.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            preview.color = Color.white;

            // celownik: 4 narożniki
            var finder = Ui.Rect(Safe, "Finder");
            Ui.Center(finder, new Vector2(760, 760), new Vector2(0, 60));
            const float len = 130, th = 14;
            foreach (var (ax, ay) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f) })
            {
                var h = Ui.Image(finder, "H", Color.white);
                var v = Ui.Image(finder, "V", Color.white);
                foreach (var img in new[] { h, v })
                {
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, ay);
                    rt.anchoredPosition = Vector2.zero;
                }
                h.rectTransform.sizeDelta = new Vector2(len, th);
                v.rectTransform.sizeDelta = new Vector2(th, len);
            }

            // górny pasek
            var back = Ui.Button(Safe, "", new Color(0, 0, 0, 0.55f), Color.white, () => App.Show(App.Home), Icons.Back,
                Theme.TextBody, radius: 66);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
            brt.sizeDelta = new Vector2(132, 132);
            brt.anchoredPosition = new Vector2(Theme.Padding, -48);
            var title = Ui.Text(Safe, "Skanuj kod QR", Theme.TextLarge, Color.white, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Ui.Top(title.rectTransform, 48, 132, 0);
            title.rectTransform.offsetMin = new Vector2(Theme.Padding + 132 + 40, title.rectTransform.offsetMin.y);

            // podpowiedź na dole
            hintCard = Ui.Card(Safe, "Hint", new Color(0, 0, 0, 0.7f));
            Ui.Bottom(hintCard.rectTransform, 220, 230, Theme.Padding);
            hint = Ui.Text(hintCard.transform, DefaultHint, Theme.TextBody, Color.white, FontStyles.Normal, TextAlignmentOptions.Center);
            Ui.Stretch(hint.rectTransform, 40, 20, 40, 20);

            retry = Ui.Button(Safe, "Spróbuj ponownie", Theme.Primary, Color.white, () => App.Scanner.StartCamera());
            Ui.Bottom((RectTransform)retry.transform, 48, 150, Theme.Padding);
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
            hintCard.color = error ? Theme.WithAlpha(Theme.Danger, 0.92f) : new Color(0, 0, 0, 0.7f);
        }

        public override void Tick()
        {
            var s = App.Scanner;
            retry.gameObject.SetActive(s.Status == QrScanner.State.PermissionDenied);
            if (s.Status == QrScanner.State.PermissionDenied)
                SetHint("Brak dostępu do aparatu. Zezwól na użycie aparatu, aby skanować kody.", true);
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
