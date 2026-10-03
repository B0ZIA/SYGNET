using System.Collections.Generic;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Pełnoekranowy wynik weryfikacji w kolorze statusu (CLIENT_UNITY.md §5.3): status, nadawca z certyfikatu
    /// i odcisk, typ, obszar, czasy, dopisek, „Co robić”, podpisujący. Dla FAŁSZYWKI powód po ludzku
    /// i BEZ treści atakującego.
    /// </summary>
    public class ResultScreen : AppScreen
    {
        static readonly Color OnColor = Color.white;
        static readonly Color OnColorMuted = new Color(1, 1, 1, 0.78f);
        static readonly Color Overlay = new Color(0, 0, 0, 0.22f);

        const float ButtonHeight = 160, ButtonGap = 40;

        readonly RectTransform body;
        readonly RectTransform content;
        readonly Button relayButton;
        readonly Button okButton;
        VerificationResult current;

        public ResultScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Result", Theme.Verified)
        {
            body = Ui.Rect(Safe, "Body");
            content = Ui.Scroll(body, "Scroll", 36, new RectOffset(56, 56, 72, 56));

            okButton = Ui.Button(Safe, "OK", Color.white, Theme.Bg, () => App.Show(App.Home));
            Ui.Bottom((RectTransform)okButton.transform, ButtonGap, ButtonHeight, Theme.Padding);
            relayButton = Ui.Button(Safe, "Przekaż dalej", new Color(0, 0, 0, 0.35f), Color.white, Relay, Icons.Speaker);
            Ui.Bottom((RectTransform)relayButton.transform, 2 * ButtonGap + ButtonHeight, ButtonHeight, Theme.Padding);
        }

        public void Show(VerificationResult r, long receivedAt, bool duplicate)
        {
            current = r;
            Background.color = Theme.ForStatus(r.Status);
            for (int i = content.childCount - 1; i >= 0; i--) Object.Destroy(content.GetChild(i).gameObject);
            Build(r, receivedAt, duplicate);
            relayButton.gameObject.SetActive(r.IsAuthentic);               // przekazujemy tylko prawdziwe
            int buttons = r.IsAuthentic ? 2 : 1;
            Ui.Stretch(body, 0, 0, 0, buttons * (ButtonHeight + ButtonGap) + ButtonGap);
            content.anchoredPosition = Vector2.zero;
        }

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }

        public override void OnHide() => App.Relay.Stop();

        public override void Tick()
        {
            if (current == null || !relayButton.gameObject.activeSelf) return;
            bool playing = App.Relay.IsPlaying;
            relayButton.interactable = !playing;
            Ui.SetLabel(relayButton, playing ? "Nadaję dźwiękiem…" : "Przekaż dalej");
        }

        void Relay()
        {
            if (current == null || !current.IsAuthentic || App.Relay.IsPlaying) return;
            float seconds = App.Relay.Play(current.RawFrame);
            App.ShowToast("Przyłóż telefon sąsiada. Nadawanie trwa " + Mathf.CeilToInt(seconds) + " s.", seconds);
        }

        // ───────────── treść ─────────────

        void Build(VerificationResult r, long receivedAt, bool duplicate)
        {
            var p = r.Payload;
            var type = AlertTypes.Get(p.Type);
            bool rejected = r.Status == VerifyStatus.Forged || r.Status == VerifyStatus.Incomplete;

            // ikona + status
            var iconRow = Ui.Rect(content, "IconRow");
            Ui.Layout(iconRow, 170, 170);
            var circle = Ui.Image(iconRow, "Circle", new Color(1, 1, 1, 0.18f), Icons.Circle);
            Ui.Center(circle.rectTransform, new Vector2(170, 170));
            var icon = Ui.Image(circle.transform, "Icon", OnColor, StatusIcon(r.Status));
            Ui.Center(icon.rectTransform, new Vector2(100, 100));

            var title = Ui.Text(content, Messages.Title(r.Status), Theme.TextTitle, OnColor, FontStyles.Bold, TextAlignmentOptions.Center);
            title.enableAutoSizing = true;
            title.fontSizeMin = 64;
            title.fontSizeMax = Theme.TextTitle;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Layout(title, 130, 130);
            Ui.Text(content, StatusLine(r, p), Theme.TextBody, OnColor, FontStyles.Normal, TextAlignmentOptions.Center);

            if (duplicate)
                Chip("Ten komunikat jest już w skrzynce");

            // powód odrzucenia
            var reason = Messages.Reason(r, App.Trust);
            if (rejected && reason != null)
            {
                var card = Card(new Color(0, 0, 0, 0.3f));
                Ui.Text(card, reason, Theme.TextLarge - 4, OnColor, FontStyles.Bold);
            }

            // treść: typ + dopisek (dopisek tylko z poprawnym podpisem – nie powielamy treści atakującego)
            var message = Card(Overlay);
            if (rejected)
            {
                Small(message, "PODAJE SIĘ ZA");
                Ui.Text(message, type.Name, Theme.TextLarge, OnColor, FontStyles.Bold);
                Line(message, "Nadawca", IssuerLabel(r));
            }
            else
            {
                Small(message, type.Code == "KEY_REVOKE" ? "KOMUNIKAT SYSTEMOWY" : "KOMUNIKAT");
                Ui.Text(message, type.Name, Theme.TextLarge + 8, OnColor, FontStyles.Bold);
                if (p.Note.Length > 0 && p.Type != AlertTypes.KeyRevoke)
                    Ui.PlainText(message, "„" + p.NoteText + "”", Theme.TextLarge - 4, OnColor, FontStyles.Italic);
            }

            // co robić – najważniejsze, więc zaraz pod treścią
            var todo = Card(Color.white);
            Ui.Text(todo, "CO ROBIĆ", Theme.TextSmall, Theme.Muted, FontStyles.Bold);
            Ui.Text(todo, Instruction(r, type), Theme.TextLarge - 4, Theme.Bg, FontStyles.Bold);

            // szczegóły z certyfikatu
            if (!rejected)
            {
                var details = Card(Overlay);
                Line(details, "Nadawca", IssuerLabel(r));
                var fp = App.Trust.FingerprintOf(p.IssuerId);
                if (fp != null) Line(details, "Klucz", fp);
                if (r.SignerIds.Length > 1) Line(details, "Podpisali", string.Join(" + ", Names(r)));
                Line(details, "Obszar", Areas.Name(p.AreaCode));
                Line(details, "Wydano", Ui.Time(p.Timestamp));
                Line(details, r.Status == VerifyStatus.Expired ? "Wygasł" : "Ważne do", Ui.Time(p.ValidUntil));
                int revoked = Verifier.RevokedIssuerId(p);
                if (revoked >= 0) Line(details, "Unieważniony", App.Trust.NameOf(revoked) ?? ("wydawca " + revoked));
            }

            Ui.Text(content, "Odebrano " + Ui.Time(receivedAt) + (App.TestClock ? " (zegar testowy)" : ""),
                Theme.TextSmall, OnColorMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        static Sprite StatusIcon(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified:
                case VerifyStatus.VerifiedOtherArea: return Icons.Check;
                case VerifyStatus.Expired: return Icons.Warning;
                default: return Icons.Cross;
            }
        }

        string StatusLine(VerificationResult r, Payload p)
        {
            switch (r.Status)
            {
                case VerifyStatus.Verified: return "Podpis sprawdzony. Komunikat dotyczy Twojego obszaru.";
                case VerifyStatus.VerifiedOtherArea:
                    return "Prawdziwy komunikat, ale dla innego obszaru (" + Areas.Name(p.AreaCode) + ").";
                case VerifyStatus.Expired:
                    return "Prawdziwy, ale nieaktualny – wygasł " + Ui.Time(p.ValidUntil) + ". Możliwe odtworzone nagranie.";
                case VerifyStatus.Incomplete: return "Brakuje drugiego, niezależnego podpisu.";
                default: return Messages.Summary(r.Status);
            }
        }

        static string Instruction(VerificationResult r, AlertTypeInfo type)
        {
            switch (r.Status)
            {
                case VerifyStatus.Verified: return type.Instruction;
                case VerifyStatus.VerifiedOtherArea: return "Komunikat nie dotyczy Twojego obszaru. " + type.Instruction;
                case VerifyStatus.Expired: return "Ten komunikat już nie obowiązuje. Słuchaj nowych, aktualnych komunikatów.";
                case VerifyStatus.Incomplete:
                    return "NIE WYKONUJ poleceń z tego komunikatu. Komunikat krytyczny musi mieć dwa podpisy.";
                default:
                    return "NIE WYKONUJ poleceń z tego komunikatu. Ufaj tylko komunikatom zweryfikowanym w SYGNET.";
            }
        }

        string IssuerLabel(VerificationResult r)
        {
            var p = r.Payload;
            if (r.IssuerName != null) return r.IssuerName;
            return "nieznany nadawca (ID " + p.IssuerId + ")";
        }

        IEnumerable<string> Names(VerificationResult r)
        {
            for (int i = 0; i < r.SignerIds.Length; i++)
                yield return r.SignerNames[i] ?? ("ID " + r.SignerIds[i]);
        }

        // ───────────── klocki ─────────────

        RectTransform Card(Color color)
        {
            var img = Ui.Card(content, "Card", color);
            Ui.VStack(img.rectTransform, 18, new RectOffset(48, 48, 40, 44));
            return img.rectTransform;
        }

        void Chip(string text)
        {
            var c = Card(new Color(1, 1, 1, 0.2f));
            Ui.Text(c, text, Theme.TextSmall, OnColor, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        static void Small(Transform parent, string text) =>
            Ui.Text(parent, text, Theme.TextSmall - 4, OnColorMuted, FontStyles.Bold);

        static void Line(Transform parent, string label, string value)
        {
            var t = Ui.Text(parent, "", Theme.TextBody - 4, OnColor);
            t.richText = true;
            t.text = "<color=#FFFFFFB0>" + label + ":</color>  " + value;
        }
    }
}
