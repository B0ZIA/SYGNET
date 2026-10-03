using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Skrzynka (CLIENT_UNITY.md §5.4): odebrane komunikaty, najnowsze na górze, z paskiem w kolorze statusu
    /// z chwili odbioru. Dotknięcie otwiera ekran wyniku (ponowna weryfikacja na bieżący czas).
    /// </summary>
    public class InboxScreen : AppScreen
    {
        readonly RectTransform list;
        readonly RectTransform empty;
        readonly TextMeshProUGUI title;

        public InboxScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Inbox", Theme.Bg)
        {
            title = Ui.Header(Safe, "Skrzynka", () => App.Show(App.Home));
            var body = Ui.Rect(Safe, "Body");
            Ui.Stretch(body, 0, Ui.BelowHeader, 0, 0);
            list = Ui.Scroll(body, "List", 20, new RectOffset(56, 56, 8, 56));

            empty = Ui.Rect(body, "Empty");
            Ui.VStack(empty, 24, new RectOffset(80, 80, 360, 0), TextAnchor.UpperCenter).childForceExpandWidth = false;
            Ui.Icon(empty, Icons.Inbox, Theme.Dim, 140);
            Ui.Label(empty, "Skrzynka jest pusta", TextStyle.Headline, Theme.Text, TextAlignmentOptions.Center);
            var hint = Ui.Label(empty, "Odebrane komunikaty pojawią się tutaj. Wszystko zostaje w telefonie.",
                TextStyle.Caption, Theme.Muted, TextAlignmentOptions.Center);
            Ui.Layout(hint, -1, -1, 800);
        }

        public override void OnShow()
        {
            for (int i = list.childCount - 1; i >= 0; i--) Object.Destroy(list.GetChild(i).gameObject);
            var inbox = App.Store.Inbox;
            title.text = inbox.Count > 0 ? "Skrzynka · " + inbox.Count : "Skrzynka";
            empty.gameObject.SetActive(inbox.Count == 0);
            foreach (var e in inbox) Row(e);
            list.anchoredPosition = Vector2.zero;
        }

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }

        void Row(InboxEntry e)
        {
            if (!Frame.TryParse(Bytes.FromHex(e.frameHex), out var f, out _)) return;
            var p = f.Payload;
            var status = Verifier.ParseStatus(e.status);
            var color = Theme.ForStatus(status);
            bool rejected = status == VerifyStatus.Forged || status == VerifyStatus.Incomplete;

            var card = Ui.Card(list, "Entry", Theme.Surface);
            card.raycastTarget = true;
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            var cb = btn.colors;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            btn.colors = cb;
            btn.onClick.AddListener(() => App.OpenInboxEntry(e, App.Inbox));
            Ui.Layout(card, 176, 176);

            // pasek statusu po lewej
            var stripe = Ui.Card(card.transform, "Stripe", color, 6);
            stripe.rectTransform.anchorMin = new Vector2(0, 0);
            stripe.rectTransform.anchorMax = new Vector2(0, 1);
            stripe.rectTransform.pivot = new Vector2(0, 0.5f);
            stripe.rectTransform.offsetMin = new Vector2(20, 26);
            stripe.rectTransform.offsetMax = new Vector2(32, -26);

            // ikona typu w kółku
            var circle = Ui.Image(card.transform, "TypeCircle", Theme.WithAlpha(color, rejected ? 0.35f : 0.28f), Icons.Circle);
            Ui.Pin(circle.rectTransform, new Vector2(0, 0.5f), new Vector2(96, 96), new Vector2(56, 0));
            var icon = Ui.Image(circle.transform, "Icon", Theme.Text, Icons.ForAlert(p.Type));
            Ui.Center(icon.rectTransform, new Vector2(52, 52));

            // tytuł + nadawca/obszar
            var type = AlertTypes.Get(p.Type);
            var name = Ui.Label(card.transform, (status == VerifyStatus.Forged ? "Fałszywy: " : "") + type.Name, TextStyle.BodyStrong, Theme.Text,
                TextAlignmentOptions.BottomLeft);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Stretch(name.rectTransform, 176, 30, 330, 88);
            var issuer = App.Trust.NameOf(p.IssuerId) ?? "nieznany nadawca";
            var meta = Ui.Label(card.transform, issuer + " · " + Areas.Name(p.AreaCode), TextStyle.Caption, Theme.Muted,
                TextAlignmentOptions.TopLeft);
            meta.textWrappingMode = TextWrappingModes.NoWrap;
            meta.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Stretch(meta.rectTransform, 176, 98, 200, 20);

            // prawa kolumna: status z chwili odbioru + godzina
            var statusColor = status == VerifyStatus.Verified ? Theme.Hex("#3FB950") : Color.Lerp(color, Color.white, 0.35f);
            var st = Ui.Label(card.transform, Messages.Title(status), TextStyle.Overline, statusColor, TextAlignmentOptions.TopRight);
            st.textWrappingMode = TextWrappingModes.NoWrap;
            st.enableAutoSizing = true;
            st.fontSizeMin = 22;
            st.fontSizeMax = 30;
            Ui.Pin(st.rectTransform, new Vector2(1, 1), new Vector2(290, 46), new Vector2(-40, -40));
            var time = Ui.Label(card.transform, Ui.Clock(e.receivedAt), TextStyle.Mono, Theme.Muted, TextAlignmentOptions.TopRight);
            Ui.Pin(time.rectTransform, new Vector2(1, 1), new Vector2(150, 50), new Vector2(-40, -98));
        }
    }
}
