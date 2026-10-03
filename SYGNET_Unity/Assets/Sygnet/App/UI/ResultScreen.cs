using System.Collections.Generic;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Wynik weryfikacji (CLIENT_UNITY.md §5.3) na ciemnym tle jak cała aplikacja. Kolor statusu tylko w karcie
    /// u góry (ramka z przyciemnionym tłem, ikona, ZWERYFIKOWANO / FAŁSZYWKA). Dalej typ z ikoną, dopisek,
    /// „Co robić”, nadawca z certyfikatu i odcisk, podpisujący, czasy. Dla FAŁSZYWKI powód po ludzku
    /// i BEZ treści atakującego.
    /// </summary>
    public class ResultScreen : AppScreen
    {
        static readonly Color On = Theme.Text;
        static readonly Color OnMuted = Theme.Muted;

        readonly RectTransform body;
        readonly RectTransform content;
        readonly Button relayButton;
        VerificationResult current;

        /// <summary>Dokąd wraca „OK” i przycisk wstecz (ekran główny albo skrzynka).</summary>
        public AppScreen ReturnTo;

        Button ok;
        float topInset;

        /// <summary>Miejsce na pasek „Nowy komunikat” – treść wyniku zsuwa się pod niego zamiast pod nim znikać.</summary>
        public void SetTopInset(float inset)
        {
            if (Mathf.Approximately(inset, topInset)) return;
            topInset = inset;
            body.offsetMax = new Vector2(body.offsetMax.x, -inset);
        }

        /// <summary>Wyświetlany wynik i czas odbioru – do kolejki, gdy użytkownik przejdzie do nowego komunikatu.</summary>
        public VerificationResult Current => current;
        public long ReceivedAt { get; private set; }

        public ResultScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Result", Theme.Bg)
        {
            body = Ui.Rect(Safe, "Body");
            content = Ui.Scroll(body, "Scroll", 28, new RectOffset(56, 56, 40, 48));

            ok = Ui.Button(Safe, "OK", ButtonKind.Primary, () => App.CloseResult());
            Ui.Bottom((RectTransform)ok.transform, 40, Theme.ButtonHeight, Theme.Margin);
            relayButton = Ui.Button(Safe, "Przekaż dalej", ButtonKind.Secondary, Relay, Icons.Speaker);
            Ui.Bottom((RectTransform)relayButton.transform, 40 + Theme.ButtonHeight + 24, Theme.ButtonHeight, Theme.Margin);
        }

        /// <summary><paramref name="notice"/>: opcjonalna informacja nad treścią (np. „już w skrzynce”).</summary>
        public void Show(VerificationResult r, long receivedAt, string notice)
        {
            current = r;
            ReceivedAt = receivedAt;
            for (int i = content.childCount - 1; i >= 0; i--) Object.Destroy(content.GetChild(i).gameObject);
            Build(r, receivedAt, notice);
            relayButton.gameObject.SetActive(r.IsAuthentic);                // przekazujemy tylko prawdziwe
            int buttons = r.IsAuthentic ? 2 : 1;
            Ui.Stretch(body, 0, topInset, 0, 40 + buttons * Theme.ButtonHeight + (buttons - 1) * 24 + 24);
            content.anchoredPosition = Vector2.zero;
        }

        public override bool OnBack()
        {
            App.CloseResult();
            return true;
        }

        public override void OnHide() => App.Relay.Stop();

        public override void Tick()
        {
            int waiting = App.PendingCount;
            Ui.SetLabel(ok, waiting > 0 ? "OK · następny (" + waiting + ")" : "OK");    // „OK” prowadzi do kolejki
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

        void Build(VerificationResult r, long receivedAt, string notice)
        {
            var p = r.Payload;
            var type = AlertTypes.Get(p.Type);
            bool rejected = r.Status == VerifyStatus.Forged || r.Status == VerifyStatus.Incomplete;

            // sygnatura marki + godzina odbioru
            var top = Row(content, 20);
            Ui.Icon(top, Resources.Load<Sprite>("sygnet_logo"), OnMuted, 44);
            Ui.Label(top, "Sygnet", TextStyle.Overline, OnMuted, TextAlignmentOptions.MidlineLeft);
            Ui.Layout(Ui.Rect(top, "Spacer"), -1, -1, -1, 1);
            Ui.Label(top, Ui.Clock(receivedAt) + (App.TestClock ? " test" : ""), TextStyle.Mono, OnMuted, TextAlignmentOptions.MidlineRight);

            // status: jedyne miejsce z kolorem – ramka z lekko zabarwionym tłem, ikona, tytuł, jedno zdanie
            // (przy fałszywce od razu powód po ludzku)
            var accent = Theme.AccentFor(r.Status);
            var reason = rejected ? Messages.Reason(r, App.Trust) : null;
            var frame = Ui.Card(content, "Status", Color.Lerp(Theme.Bg, accent, 0.42f), 40);
            Ui.VStack(frame.rectTransform, 0, new RectOffset(3, 3, 3, 3));
            var head = Ui.Card(frame.transform, "Fill", Color.Lerp(Theme.Bg, accent, 0.09f), 37);
            Ui.VStack(head.rectTransform, 12, new RectOffset(40, 40, 40, 40), TextAnchor.UpperCenter).childForceExpandWidth = false;
            var iconBg = Ui.Image(head.transform, "Circle", accent, Icons.Circle);
            Ui.Layout(iconBg, 112, 112, 112);
            var icon = Ui.Image(iconBg.transform, "Icon", Theme.Bg, StatusIcon(r.Status));
            Ui.Center(icon.rectTransform, new Vector2(60, 60));
            var title = Ui.Label(head.transform, Messages.Title(r.Status), TextStyle.Display, accent, TextAlignmentOptions.Center);
            title.enableAutoSizing = true;
            title.fontSizeMin = 52;
            title.fontSizeMax = 84;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Layout(title, 100, 100, 880);
            var line = Ui.Label(head.transform, reason ?? StatusLine(r, p), TextStyle.Body, On, TextAlignmentOptions.Center);
            Ui.Layout(line, -1, -1, 880);

            if (notice != null)
            {
                var c = Card(Theme.Surface2, 22);
                Ui.Label(c, notice, TextStyle.BodyStrong, On, TextAlignmentOptions.Center);
            }

            // treść: typ + dopisek (dopisek tylko z poprawnym podpisem – nie powielamy treści atakującego)
            var msg = Card(Theme.Surface);
            Ui.Label(msg, rejected ? "Podaje się za" : type.Code == "KEY_REVOKE" ? "Komunikat systemowy" : "Komunikat",
                TextStyle.Overline, OnMuted);
            var typeRow = Row(msg, 22);
            Ui.Icon(typeRow, Icons.ForAlert(p.Type), On, 64);
            Ui.Label(typeRow, type.Name, TextStyle.Title, On, TextAlignmentOptions.MidlineLeft);
            if (!rejected && p.Note.Length > 0 && p.Type != AlertTypes.KeyRevoke)
                Ui.PlainLabel(msg, "„" + p.NoteText + "”", TextStyle.Headline, On);
            if (rejected) Detail(msg, "Nadawca", IssuerLabel(r));

            // co robić
            var todo = Card(Theme.Surface);
            Ui.Label(todo, "Co robić", TextStyle.Overline, accent);
            Ui.Label(todo, Instruction(r, type), TextStyle.Headline, On);

            // szczegóły z certyfikatu
            if (!rejected)
            {
                var d = Card(Theme.Surface);
                Detail(d, "Nadawca", IssuerLabel(r));
                var fp = App.Trust.FingerprintOf(p.IssuerId);
                if (fp != null) Detail(d, "Klucz", fp, mono: true);
                if (r.SignerIds.Length > 1) Detail(d, "Podpisali", string.Join(" + ", Names(r)));
                Detail(d, "Obszar", Areas.Name(p.AreaCode));
                Detail(d, "Wydano", Ui.Time(p.Timestamp), mono: true);
                Detail(d, r.Status == VerifyStatus.Expired ? "Wygasł" : "Ważne do", Ui.Time(p.ValidUntil), mono: true);
                int revoked = Verifier.RevokedIssuerId(p);
                if (revoked >= 0) Detail(d, "Unieważniony", App.Trust.NameOf(revoked) ?? ("wydawca " + revoked));
            }
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
                case VerifyStatus.Verified: return "Podpis sprawdzony. Dotyczy Twojego obszaru.";
                case VerifyStatus.VerifiedOtherArea: return "Prawdziwy komunikat dla innego obszaru: " + Areas.Name(p.AreaCode) + ".";
                case VerifyStatus.Expired: return "Prawdziwy, ale wygasł " + Ui.Time(p.ValidUntil) + ". Możliwe odtworzone nagranie.";
                case VerifyStatus.Incomplete: return "Brakuje drugiego, niezależnego podpisu.";
                default: return Messages.Summary(r.Status);
            }
        }

        static string Instruction(VerificationResult r, AlertTypeInfo type)
        {
            switch (r.Status)
            {
                case VerifyStatus.Verified: return type.Instruction;
                case VerifyStatus.VerifiedOtherArea: return "Nie dotyczy Twojego obszaru. " + type.Instruction;
                case VerifyStatus.Expired: return "Ten komunikat już nie obowiązuje. Słuchaj nowych, aktualnych komunikatów.";
                case VerifyStatus.Incomplete: return "Nie wykonuj poleceń z tego komunikatu. Komunikat krytyczny musi mieć dwa podpisy.";
                default: return "Nie wykonuj poleceń z tego komunikatu. Ufaj tylko komunikatom zweryfikowanym w SYGNET.";
            }
        }

        string IssuerLabel(VerificationResult r) => r.IssuerName ?? "nieznany nadawca (ID " + r.Payload.IssuerId + ")";

        IEnumerable<string> Names(VerificationResult r)
        {
            for (int i = 0; i < r.SignerIds.Length; i++)
                yield return r.SignerNames[i] ?? ("ID " + r.SignerIds[i]);
        }

        // ───────────── klocki ─────────────

        RectTransform Card(Color color, int padY = 36)
        {
            var img = Ui.Card(content, "Card", color);
            Ui.VStack(img.rectTransform, 14, new RectOffset(44, 44, padY, padY + 4));
            return img.rectTransform;
        }

        static RectTransform Row(Transform parent, float spacing)
        {
            var row = Ui.Rect(parent, "Row");
            Ui.HStack(row, spacing, TextAnchor.MiddleLeft);
            return row;
        }

        /// <summary>Wiersz „etykieta – wartość”; wartości techniczne (klucz, czas) monospace.</summary>
        static void Detail(Transform parent, string label, string value, bool mono = false)
        {
            var row = Row(parent, 20);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            var l = Ui.Label(row, label, TextStyle.Caption, OnMuted);
            Ui.Layout(l, -1, -1, 230);
            var v = Ui.Label(row, value, mono ? TextStyle.Mono : TextStyle.BodyStrong, On);
            Ui.Layout(v, -1, -1, -1, 1);
        }
    }
}
