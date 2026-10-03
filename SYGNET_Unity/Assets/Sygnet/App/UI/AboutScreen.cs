using System.Collections.Generic;
using Sygnet.Core;
using TMPro;
using UnityEngine;

namespace Sygnet.App.UI
{
    /// <summary>
    /// „Klucze i nadawcy” (CLIENT_UNITY.md §5.5): wbudowany klucz ROOT z odciskiem do porównania z wydrukiem
    /// oraz zaufani wydawcy z trust store (nazwa i zakres z certyfikatu, odcisk, ważność, unieważnienie).
    /// </summary>
    public class AboutScreen : AppScreen
    {
        readonly RectTransform content;

        public AboutScreen(SygnetApp app, Transform canvas) : base(app, canvas, "About", Theme.Bg)
        {
            Ui.Header(Safe, "Klucze i nadawcy", () => App.Show(App.Home));
            var body = Ui.Rect(Safe, "Body");
            Ui.Stretch(body, 0, Ui.BelowHeader, 0, 0);
            content = Ui.Scroll(body, "Scroll", 24, new RectOffset(56, 56, 8, 64));
        }

        public override void OnShow()
        {
            for (int i = content.childCount - 1; i >= 0; i--) Object.Destroy(content.GetChild(i).gameObject);
            var trust = App.Trust;

            // klucz główny
            var root = Card(Theme.Surface2);
            Ui.Label(root, "Klucz główny (ROOT)", TextStyle.Overline, Theme.Muted);
            var fp = Ui.Label(root, trust.RootFingerprint, TextStyle.Mono, Theme.Text);
            fp.font = Theme.MonoBold;
            fp.fontSize = 58;
            fp.textWrappingMode = TextWrappingModes.NoWrap;
            fp.enableAutoSizing = true;
            fp.fontSizeMin = 36;
            fp.fontSizeMax = 58;
            Ui.Label(root, "Porównaj z wydrukiem w urzędzie. Ten klucz jest wbudowany w aplikację – nikt nie podmieni " +
                           "go przez radio ani kod QR.", TextStyle.Caption, Theme.Muted);
            if (SygnetApp.RootIsTestKey)
                Ui.Chip(root, Icons.Warning, "Klucz testowy z wektorów – nie produkcyjny", Theme.Expired, Color.white,
                    TextStyle.Caption, 68);

            // zaufani wydawcy
            Ui.Label(content, "Zaufani nadawcy · " + trust.Issuers.Count, TextStyle.Overline, Theme.Muted);
            var ids = new List<int>(trust.Issuers.Keys);
            ids.Sort();
            long now = App.Now;
            foreach (var id in ids)
            {
                var c = trust.Issuers[id];
                bool revoked = App.Store.Revoked.Contains(id);
                bool valid = c.IsValidAt(now);
                var card = Card(Theme.Surface);
                Ui.Label(card, c.Name, TextStyle.BodyStrong, Theme.Text);
                var scopes = new List<string>();
                foreach (var s in c.Scopes) scopes.Add(Areas.Name(s));
                Ui.Label(card, "Zakres: " + string.Join(", ", scopes), TextStyle.Caption, Theme.Muted);
                Ui.Label(card, c.Fingerprint + "  ·  ID " + id, TextStyle.Mono, Theme.Muted);
                if (revoked)
                    Ui.Chip(card, Icons.Cross, "Klucz unieważniony", Theme.Danger, Color.white, TextStyle.Caption, 64);
                else if (!valid)
                    Ui.Chip(card, Icons.Warning, "Certyfikat nieważny", Theme.Expired, Color.white, TextStyle.Caption, 64);
                else
                    Ui.Label(card, "Ważny do " + Ui.Time(c.ValidUntil).Substring(0, 10), TextStyle.Caption, Theme.Dim);
            }
            foreach (var why in trust.Rejected)
                Ui.Label(content, "Odrzucony certyfikat: " + why, TextStyle.Caption, Theme.Danger);

            var foot = Ui.Label(content, "SYGNET " + Application.version + " · działa w 100% offline · " +
                                          "bez uprawnienia INTERNET · podpisy Ed25519", TextStyle.Caption, Theme.Dim,
                TextAlignmentOptions.Center);
            Ui.Layout(foot);
            content.anchoredPosition = Vector2.zero;
        }

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }

        RectTransform Card(Color color)
        {
            var img = Ui.Card(content, "Card", color);
            Ui.VStack(img.rectTransform, 12, new RectOffset(44, 44, 36, 40));
            return img.rectTransform;
        }
    }
}
