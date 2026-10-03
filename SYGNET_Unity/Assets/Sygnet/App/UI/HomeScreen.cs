using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Ekran główny (CLIENT_UNITY.md §5.2). W U4: gotowość + skan QR. W U5 dochodzi nasłuch mikrofonu
    /// (pulsujący okrąg, widmo, „Odbieram… 34%”).
    /// </summary>
    public class HomeScreen : AppScreen
    {
        readonly TextMeshProUGUI areaLabel;
        readonly RectTransform clockChip;
        readonly TextMeshProUGUI clockText;
        readonly Image ring1, ring2, core;
        readonly Image coreIcon;
        readonly TextMeshProUGUI status, hint;
        readonly Button lastButton;

        int logoTaps;
        float lastLogoTap;
        float nextClockRefresh;

        public HomeScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Home", Theme.Bg)
        {
            // ── górny pasek: logo (5× tap = zegar testowy) + obszar ──
            var top = Ui.Rect(Safe, "TopBar");
            Ui.Top(top, 48, 120, Theme.Padding);
            var logo = Ui.Text(top, "SYGNET", 76, Theme.Text, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            logo.characterSpacing = 12;
            logo.rectTransform.anchorMax = new Vector2(0.6f, 1);
            Ui.HitArea(logo.rectTransform, OnLogoTap);

            var chip = Ui.Card(top, "AreaChip", Theme.Card, 48);
            chip.rectTransform.anchorMin = new Vector2(0.55f, 0.1f);
            chip.rectTransform.anchorMax = new Vector2(1, 0.9f);
            chip.rectTransform.offsetMin = chip.rectTransform.offsetMax = Vector2.zero;
            areaLabel = Ui.Text(chip.transform, "", Theme.TextSmall, Theme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            Ui.Stretch(areaLabel.rectTransform, 24, 0, 24, 0);

            // ── „działa bez internetu” ──
            var offline = Ui.Rect(Safe, "Offline");
            Ui.Top(offline, 196, 64, Theme.Padding);
            var plane = Ui.Image(offline, "Plane", Theme.Accent, Icons.Plane);
            plane.rectTransform.anchorMin = new Vector2(0, 0.5f);
            plane.rectTransform.anchorMax = new Vector2(0, 0.5f);
            plane.rectTransform.sizeDelta = new Vector2(56, 56);
            plane.rectTransform.anchoredPosition = new Vector2(28, 0);
            var offText = Ui.Text(offline, "Działa bez internetu", Theme.TextSmall, Theme.Accent, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            Ui.Stretch(offText.rectTransform, 80, 0, 0, 0);

            // ── zegar testowy (widoczny tylko gdy włączony) ──
            var cc = Ui.Card(Safe, "TestClock", Theme.Expired, 42);
            clockChip = cc.rectTransform;
            Ui.Top(clockChip, 290, 84, Theme.Padding);
            clockText = Ui.Text(clockChip, "", Theme.TextSmall, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);

            // ── środek: okrąg ──
            var center = Ui.Rect(Safe, "Center");
            Ui.Center(center, new Vector2(900, 900), new Vector2(0, 160));
            ring1 = Ui.Image(center, "Ring1", Theme.Accent, Icons.Ring);
            ring2 = Ui.Image(center, "Ring2", Theme.Accent, Icons.Ring);
            foreach (var r in new[] { ring1, ring2 }) Ui.Center(r.rectTransform, new Vector2(460, 460));
            core = Ui.Image(center, "Core", Theme.Card, Icons.Circle);
            Ui.Center(core.rectTransform, new Vector2(460, 460));
            coreIcon = Ui.Image(core.transform, "Icon", Theme.Text, Icons.Qr);
            Ui.Center(coreIcon.rectTransform, new Vector2(180, 180));

            status = Ui.Text(Safe, "", Theme.TextLarge, Theme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            Ui.Center(status.rectTransform, new Vector2(1000, 90), new Vector2(0, -190));
            hint = Ui.Text(Safe, "", Theme.TextSmall + 4, Theme.Muted, FontStyles.Normal, TextAlignmentOptions.Top);
            Ui.Center(hint.rectTransform, new Vector2(900, 160), new Vector2(0, -320));

            // ── przyciski ──
            var scan = Ui.Button(Safe, "Skanuj kod QR", Theme.Primary, Color.white, () => App.Show(App.Scan), Icons.Qr);
            Ui.Bottom((RectTransform)scan.transform, 230, 170, Theme.Padding);
            lastButton = Ui.Button(Safe, "Ostatni komunikat", Theme.Card, Theme.Text, OpenLast, Icons.Inbox, Theme.TextBody - 4);
            Ui.Bottom((RectTransform)lastButton.transform, 48, 150, Theme.Padding);
        }

        public override void OnShow() => Refresh();

        public void Refresh()
        {
            areaLabel.text = Areas.Name(App.Store.UserArea);
            clockChip.gameObject.SetActive(App.TestClock);
            lastButton.gameObject.SetActive(App.Store.Inbox.Count > 0);
            status.text = "Gotowy do odbioru";
            hint.text = "Zeskanuj kod QR komunikatu\nz plakatu lub ekranu";
            UpdateClock();
        }

        public override void Tick()
        {
            // spokojny „oddech” pierścieni
            float t = Time.unscaledTime;
            Pulse(ring1, t % 2.4f / 2.4f);
            Pulse(ring2, (t + 1.2f) % 2.4f / 2.4f);
            if (App.TestClock && t > nextClockRefresh) UpdateClock();
        }

        static void Pulse(Image ring, float phase)
        {
            float s = 1f + 0.55f * phase;
            ring.rectTransform.localScale = new Vector3(s, s, 1);
            ring.color = Theme.WithAlpha(Theme.Accent, 0.45f * (1f - phase));
        }

        void UpdateClock()
        {
            nextClockRefresh = Time.unscaledTime + 1f;
            clockText.text = "ZEGAR TESTOWY · " + Ui.Time(App.Now);
        }

        void OnLogoTap()
        {
            if (Time.unscaledTime - lastLogoTap > 1.5f) logoTaps = 0;
            lastLogoTap = Time.unscaledTime;
            if (++logoTaps >= 5)
            {
                logoTaps = 0;
                App.ToggleTestClock();
            }
        }

        void OpenLast()
        {
            if (App.Store.Inbox.Count > 0) App.OpenInboxEntry(App.Store.Inbox[0]);
        }
    }
}
