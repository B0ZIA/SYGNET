using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Ekran główny (CLIENT_UNITY.md §5.2): nasłuch mikrofonu (pulsujący okrąg, widmo wokół niego,
    /// „Odbieram… 34%” wg frame_len), skan QR i ostatni komunikat.
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
        readonly RectTransform progressTrack, progressFill;

        const int BarCount = 48;
        readonly RectTransform[] bars;
        readonly float[] barLevels = new float[BarCount];
        float smoothLevel;

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

            // widmo: słupki promieniście wokół okręgu (pasma 800–5400 Hz z dekodera)
            bars = new RectTransform[BarCount];
            for (int i = 0; i < BarCount; i++)
            {
                var pivot = Ui.Rect(center, "BarPivot");
                Ui.Center(pivot, Vector2.zero);
                pivot.localEulerAngles = new Vector3(0, 0, -360f * i / BarCount);
                var bar = Ui.Card(pivot, "Bar", Theme.WithAlpha(Theme.Accent, 0.85f), 8);
                var rt = bar.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(0, 250);
                rt.sizeDelta = new Vector2(16, 0);
                bars[i] = rt;
            }

            core = Ui.Image(center, "Core", Theme.Card, Icons.Circle);
            Ui.Center(core.rectTransform, new Vector2(460, 460));
            coreIcon = Ui.Image(core.transform, "Icon", Theme.Text, Icons.Mic);
            Ui.Center(coreIcon.rectTransform, new Vector2(180, 180));
            Ui.HitArea(core.rectTransform, () => App.Mic.StartListening());     // ponowna prośba o mikrofon

            status = Ui.Text(Safe, "", Theme.TextLarge, Theme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            Ui.Center(status.rectTransform, new Vector2(1000, 90), new Vector2(0, -190));

            var track = Ui.Card(Safe, "ProgressTrack", Theme.Card, 12);
            progressTrack = track.rectTransform;
            Ui.Center(progressTrack, new Vector2(760, 24), new Vector2(0, -262));
            progressFill = Ui.Card(progressTrack, "Fill", Theme.Accent, 12).rectTransform;
            progressFill.anchorMax = new Vector2(0, 1);

            hint = Ui.Text(Safe, "", Theme.TextSmall + 4, Theme.Muted, FontStyles.Normal, TextAlignmentOptions.Top);
            Ui.Center(hint.rectTransform, new Vector2(900, 160), new Vector2(0, -330));

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
            UpdateClock();
        }

        public override void Tick()
        {
            float t = Time.unscaledTime;
            var mic = App.Mic;
            var dec = mic.Decoder;
            bool listening = mic.Status == MicListener.State.Listening;
            bool receiving = listening && dec != null && dec.Progress >= 0;

            // pierścienie: spokojny „oddech”, mocniejszy przy głośnym dźwięku
            float level = listening && dec != null ? Mathf.Clamp01(dec.Rms * 12f) : 0f;
            smoothLevel = Mathf.Lerp(smoothLevel, level, 0.25f);
            Color ringColor = mic.Status == MicListener.State.PermissionDenied ? Theme.Danger : Theme.Accent;
            Pulse(ring1, t % 2.4f / 2.4f, ringColor, listening ? 0.35f + 0.4f * smoothLevel : 0.15f);
            Pulse(ring2, (t + 1.2f) % 2.4f / 2.4f, ringColor, listening ? 0.35f + 0.4f * smoothLevel : 0.15f);

            // widmo
            for (int i = 0; i < BarCount; i++)
            {
                float target = 0;
                if (listening && dec != null && dec.Rms > 0.002f)
                    target = Mathf.Clamp01(Mathf.Sqrt((float)dec.Spectrum[i % dec.Spectrum.Length]) * 1.6f) * (0.3f + 0.7f * level);
                barLevels[i] = Mathf.Lerp(barLevels[i], target, target > barLevels[i] ? 0.6f : 0.15f);
                bars[i].sizeDelta = new Vector2(16, 8 + 130 * barLevels[i]);
            }

            core.color = receiving ? Theme.WithAlpha(Theme.Accent, 0.35f) : Theme.Card;
            progressTrack.gameObject.SetActive(receiving);
            if (receiving) progressFill.anchorMax = new Vector2(Mathf.Clamp01((float)dec.Progress), 1);
            UpdateStatus(mic, receiving);

            if (App.TestClock && t > nextClockRefresh) UpdateClock();
        }

        void UpdateStatus(MicListener mic, bool receiving)
        {
            switch (mic.Status)
            {
                case MicListener.State.Listening when receiving:
                    int pct = Mathf.RoundToInt((float)mic.Decoder.Progress * 100);
                    status.text = "Odbieram… " + pct + "%";
                    hint.text = "Komunikat dźwiękowy (" + mic.Decoder.ReceivingBytes + " B).\nNie zasłaniaj mikrofonu.";
                    break;
                case MicListener.State.Listening:
                    status.text = "Nasłuchuję komunikatów…";
                    hint.text = "Radio, megafon albo telefon sąsiada.\nMożesz też zeskanować kod QR.";
                    break;
                case MicListener.State.Paused:
                    status.text = "Nadaję dźwiękiem…";
                    hint.text = "Nasłuch wstrzymany na czas przekazywania.";
                    break;
                case MicListener.State.PermissionDenied:
                    status.text = "Brak dostępu do mikrofonu";
                    hint.text = "Dotknij okręgu i zezwól na mikrofon,\nżeby odbierać komunikaty dźwiękowe.";
                    break;
                case MicListener.State.NoMicrophone:
                    status.text = "Brak mikrofonu";
                    hint.text = "Komunikaty możesz odbierać z kodów QR.";
                    break;
                default:
                    status.text = "Gotowy do odbioru";
                    hint.text = "Zeskanuj kod QR komunikatu\nz plakatu lub ekranu";
                    break;
            }
        }

        static void Pulse(Image ring, float phase, Color color, float strength)
        {
            float s = 1f + 0.55f * phase;
            ring.rectTransform.localScale = new Vector3(s, s, 1);
            ring.color = Theme.WithAlpha(color, strength * (1f - phase));
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
