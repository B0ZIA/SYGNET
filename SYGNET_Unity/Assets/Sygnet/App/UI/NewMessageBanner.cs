using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Pasek „Nowy komunikat” nad bieżącym ekranem: komunikat odebrany, gdy użytkownik czyta inny wynik albo przegląda
    /// skrzynkę, nie zabiera mu ekranu – czeka w kolejce, a pasek (w kolorze statusu) pozwala go otworzyć jednym dotknięciem.
    /// </summary>
    public sealed class NewMessageBanner
    {
        public const float Height = 176;

        /// <summary>Ile miejsca zajmuje pasek nad treścią ekranu wyniku (odstęp od góry + wysokość + przerwa).</summary>
        public const float ResultInset = 24 + Height + 8;
        const float FadeSeconds = 0.22f;

        readonly RectTransform root;
        readonly RectTransform cardRect;
        readonly CanvasGroup group;
        readonly Image card;
        readonly Image circle;
        readonly Image icon;
        readonly TextMeshProUGUI overline;
        readonly TextMeshProUGUI title;
        float shown = 1;

        public bool Visible => root.gameObject.activeSelf;

        public NewMessageBanner(SygnetApp app, RectTransform canvas, UnityAction onOpen)
        {
            root = Ui.Rect(canvas, "NewMessage");
            group = root.gameObject.AddComponent<CanvasGroup>();
            var safe = Ui.Rect(root, "Safe");
            app.RegisterSafeArea(safe);

            // zawsze ciemny – odcina się od kolorowego ekranu wyniku; status mówi kółko i słowo w tytule
            card = Ui.Card(safe, "Card", Theme.Surface2, 44);
            card.raycastTarget = true;
            cardRect = card.rectTransform;
            Ui.Top(cardRect, 24, Height, Theme.Margin);
            Ui.HitArea(cardRect, onOpen);

            circle = Ui.Image(card.transform, "Circle", Theme.Verified, Icons.Circle);
            Ui.Pin(circle.rectTransform, new Vector2(0, 0.5f), new Vector2(88, 88), new Vector2(32, 0));
            icon = Ui.Image(circle.transform, "Icon", Color.white, Icons.Check);
            Ui.Center(icon.rectTransform, new Vector2(52, 52));

            overline = Ui.Label(card.transform, "", TextStyle.Overline, Theme.Muted, TextAlignmentOptions.BottomLeft);
            Ui.Stretch(overline.rectTransform, 144, 30, 216, Height / 2 + 2);
            overline.textWrappingMode = TextWrappingModes.NoWrap;
            overline.enableAutoSizing = true;
            overline.fontSizeMax = overline.fontSize;
            overline.fontSizeMin = 18;
            title = Ui.Label(card.transform, "", TextStyle.BodyStrong, Theme.Text, TextAlignmentOptions.TopLeft);
            Ui.Stretch(title.rectTransform, 144, Height / 2 + 4, 216, 18);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.enableAutoSizing = true;                                  // „FAŁSZYWKA · Alarm lotniczy” w jednej linii
            title.fontSizeMax = title.fontSize;
            title.fontSizeMin = 28;

            var open = Ui.Button(card.transform, "Pokaż", ButtonKind.Primary, onOpen, null, 96);
            Ui.Pin((RectTransform)open.transform, new Vector2(1, 0.5f), new Vector2(176, 96), new Vector2(-24, 0));

            root.gameObject.SetActive(false);
        }

        /// <param name="interrupted">komunikat, którego czytanie przerwano przejściem do nowego</param>
        /// <param name="waiting">ile komunikatów czeka (z tym włącznie)</param>
        /// <param name="top">odstęp od góry bezpiecznego obszaru (pod nagłówkiem podekranu)</param>
        public void Show(VerificationResult r, bool interrupted, int waiting, float top)
        {
            circle.color = Theme.ForStatus(r.Status);
            icon.sprite = r.Status == VerifyStatus.Verified || r.Status == VerifyStatus.VerifiedOtherArea ? Icons.Check
                : r.Status == VerifyStatus.Expired ? Icons.Warning : Icons.Cross;
            overline.text = interrupted
                ? "Dokończ czytanie" + (waiting > 1 ? " · +" + (waiting - 1) : "")
                : waiting > 1 ? "Nowe komunikaty: " + waiting : "Nowy komunikat";
            title.text = Messages.Title(r.Status) + " · " + AlertTypes.Get(r.Payload.Type).Name;
            cardRect.anchoredPosition = new Vector2(cardRect.anchoredPosition.x, -top);

            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                shown = AppScreen.SkipAnimations ? 1 : 0;
            }
            root.SetAsLastSibling();
            Animate();
        }

        public void Hide() => root.gameObject.SetActive(false);

        public void BringToFront()
        {
            if (Visible) root.SetAsLastSibling();
        }

        /// <summary>Wjazd z góry z przenikaniem (woła SygnetApp.Update).</summary>
        public void Tick(float dt)
        {
            if (!Visible || shown >= 1) return;
            shown = Mathf.Min(1, shown + dt / FadeSeconds);
            Animate();
        }

        void Animate()
        {
            float e = 1 - (1 - shown) * (1 - shown);
            group.alpha = e;
            root.anchoredPosition = new Vector2(0, 60 * (1 - e));
        }
    }
}
