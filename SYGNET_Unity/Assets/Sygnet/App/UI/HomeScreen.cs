using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Ekran główny (CLIENT_UNITY.md §5.2): nasłuch mikrofonu – okrąg, którego obwód jest paskiem postępu odbioru,
    /// widmo dookoła, „Odbieram… 34%” – oraz skan QR i ostatni komunikat. Monochrom: kolor tylko dla wyników.
    /// </summary>
    public class HomeScreen : AppScreen
    {
        const int BarCount = 56;
        const float HeroSize = 520;

        readonly TextMeshProUGUI areaLabel;
        readonly RectTransform clockChip;
        readonly TextMeshProUGUI clockText;
        readonly Image pulse1, pulse2, track, progress, core, coreIcon;
        readonly TextMeshProUGUI percent;
        readonly TextMeshProUGUI status, hint;
        readonly Button lastButton;
        readonly RectTransform[] bars = new RectTransform[BarCount];
        readonly Image[] barImages = new Image[BarCount];
        readonly float[] barLevels = new float[BarCount];

        float smoothLevel, shownProgress;
        float noiseFloorDb;                 // szum tła; poziom liczymy względem niego
        bool floorKnown;
        float floorWait;                    // pomiar szumu dopiero po chwili nasłuchu (wygładzona głośność rośnie od zera)
        float[] bandAvg;                    // średnia moc każdego pasma z ostatnich sekund (stały szum = brak ruchu)
        int logoTaps;
        float lastLogoTap, nextClockRefresh;

        public HomeScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Home", Theme.Bg)
        {
            // ── nagłówek: sygnet + napis (5× tap = panel diagnostyczny), obszar ──
            var header = Ui.Rect(Safe, "Header");
            Ui.Top(header, 40, 104, Theme.Margin);
            var brand = Ui.Rect(header, "Brand");
            brand.anchorMax = new Vector2(0.62f, 1);
            Ui.HStack(brand, 26, TextAnchor.MiddleLeft);
            Ui.Icon(brand, Resources.Load<Sprite>("sygnet_logo"), Theme.Text, 92);
            Ui.Label(brand, "Sygnet", TextStyle.Wordmark, Theme.Text, TextAlignmentOptions.MidlineLeft)
                .textWrappingMode = TextWrappingModes.NoWrap;
            Ui.HitArea(brand, OnLogoTap);

            var (areaChip, areaText) = Ui.Chip(header, Icons.Pin, "", Theme.Surface2, Theme.Text, TextStyle.BodyStrong, 88);
            Ui.Pin(areaChip.rectTransform, new Vector2(1, 0.5f), new Vector2(0, 88));
            Ui.HitArea(areaChip.rectTransform, () => App.Show(App.Area));
            areaLabel = areaText;

            // ── stan zaufania: offline + odcisk ROOT ──
            var chips = Ui.Rect(Safe, "Chips");
            Ui.Top(chips, 172, 72, Theme.Margin);
            Ui.HStack(chips, 16, TextAnchor.MiddleLeft);
            Ui.Chip(chips, Icons.Plane, "Offline", Theme.Surface, Theme.Muted, TextStyle.Caption, 72);
            var (rootChip, _) = Ui.Chip(chips, Icons.Key,
                "ROOT " + App.Trust.RootFingerprint.Substring(0, 9) + (SygnetApp.RootIsTestKey ? " · test" : ""),
                Theme.Surface, Theme.Muted, TextStyle.Mono, 72);
            Ui.HitArea(rootChip.rectTransform, () => App.Show(App.About));

            var (cc, ct) = Ui.Chip(Safe, null, "", Theme.Expired, Color.white, TextStyle.Overline, 64);
            clockChip = cc.rectTransform;
            Ui.Pin(clockChip, new Vector2(0, 1), new Vector2(0, 64), new Vector2(Theme.Margin, -264));
            clockText = ct;

            // ── okrąg: puls, widmo, tor i postęp odbioru, rdzeń ──
            var hero = Ui.Rect(Safe, "Hero");
            Ui.Center(hero, new Vector2(900, 900), new Vector2(0, 150));
            pulse1 = Ui.Image(hero, "Pulse1", Theme.Text, Icons.Ring);
            pulse2 = Ui.Image(hero, "Pulse2", Theme.Text, Icons.Ring);
            foreach (var p in new[] { pulse1, pulse2 }) Ui.Center(p.rectTransform, new Vector2(HeroSize, HeroSize));
            for (int i = 0; i < BarCount; i++)
            {
                var pivot = Ui.Rect(hero, "BarPivot");
                Ui.Center(pivot, Vector2.zero);
                pivot.localEulerAngles = new Vector3(0, 0, -360f * i / BarCount);
                var bar = Ui.Card(pivot, "Bar", Theme.Text, 6);
                var rt = bar.rectTransform;
                Ui.Pin(rt, new Vector2(0.5f, 0.5f), new Vector2(10, 0));
                rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(0, HeroSize / 2 + 26);
                bars[i] = rt;
                barImages[i] = bar;
            }
            track = Ui.Image(hero, "Track", Theme.Line, Icons.RingThick);
            Ui.Center(track.rectTransform, new Vector2(HeroSize, HeroSize));
            progress = Ui.Image(hero, "Progress", Theme.Text, Icons.RingThick);
            Ui.Center(progress.rectTransform, new Vector2(HeroSize, HeroSize));
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = (int)Image.Origin360.Top;
            progress.fillClockwise = true;
            progress.fillAmount = 0;
            core = Ui.Image(hero, "Core", Theme.Surface, Icons.Circle);
            Ui.Center(core.rectTransform, new Vector2(HeroSize - 92, HeroSize - 92));
            coreIcon = Ui.Image(core.transform, "Mic", Theme.Text, Icons.Mic);
            Ui.Center(coreIcon.rectTransform, new Vector2(150, 150));
            percent = Ui.Label(core.transform, "", TextStyle.Display, Theme.Text, TextAlignmentOptions.Center);
            Ui.Center(percent.rectTransform, new Vector2(400, 140));
            Ui.HitArea(core.rectTransform, () => App.Mic.StartListening());       // ponowna prośba o mikrofon

            status = Ui.Label(Safe, "", TextStyle.Headline, Theme.Text, TextAlignmentOptions.Center);
            Ui.Center(status.rectTransform, new Vector2(980, 80), new Vector2(0, -300));      // niżej – słupki widma mają miejsce
            hint = Ui.Label(Safe, "", TextStyle.Caption, Theme.Muted, TextAlignmentOptions.Top);
            Ui.Center(hint.rectTransform, new Vector2(900, 130), new Vector2(0, -392));

            // ── akcje ──
            var scan = Ui.Button(Safe, "Skanuj kod QR", ButtonKind.Primary, () => App.Show(App.Scan), Icons.Qr);
            Ui.Bottom((RectTransform)scan.transform, 210, Theme.ButtonHeight, Theme.Margin);
            lastButton = Ui.Button(Safe, "Skrzynka", ButtonKind.Secondary, () => App.Show(App.Inbox), Icons.Inbox, 136);
            Ui.Bottom((RectTransform)lastButton.transform, 48, 136, Theme.Margin);
        }

        public override void OnShow()
        {
            floorKnown = false;                                         // szum tła mierzymy od nowa
            floorWait = 0;
            Refresh();
        }

        public void Refresh()
        {
            areaLabel.text = Areas.Name(App.Store.UserArea);
            clockChip.gameObject.SetActive(App.TestClock);
            lastButton.gameObject.SetActive(App.Store.Inbox.Count > 0);
            Ui.SetLabel(lastButton, "Skrzynka · " + App.Store.Inbox.Count);
            UpdateClock();
        }

        public override void Tick()
        {
            float t = Time.unscaledTime;
            var mic = App.Mic;
            var dec = mic.Decoder;
            bool listening = mic.Status == MicListener.State.Listening;
            bool receiving = listening && dec != null && dec.Progress >= 0;
            bool denied = mic.Status == MicListener.State.PermissionDenied;

            // poziom dźwięku w dB ponad szum tła: mikrofon UNPROCESSED daje małe wartości, a liczy się,
            // o ile głośniej niż cisza w pokoju – wtedy okrąg reaguje na zwykłą mowę
            float level = 0f;
            if (listening && dec != null)
            {
                float db = 20f * Mathf.Log10(Mathf.Max(dec.Loudness, 1e-6f));
                if (!floorKnown && (floorWait += Time.unscaledDeltaTime) > 0.4f)
                {
                    noiseFloorDb = db;
                    floorKnown = true;
                }
                if (floorKnown)
                {
                    float gap = db - noiseFloorDb;
                    noiseFloorDb = gap < 0
                        ? Mathf.Lerp(noiseFloorDb, db, 0.08f)                                      // cisza: szybko w dół
                        : noiseFloorDb + Time.unscaledDeltaTime * (3f + Mathf.Max(0, gap - 30f)); // w górę powoli – mowa ma przerwy
                    level = Mathf.Clamp01((gap - 9f) / 20f);                // wahania szumu pokoju (±6 dB) dają zero
                }
            }
            smoothLevel = Mathf.Lerp(smoothLevel, level, level > smoothLevel ? 0.16f : 0.08f);   // wolniejsze narastanie: stuknięcia nie, mowa tak

            // puls: spokojny oddech, mocniejszy przy głośnym dźwięku
            Color pulseColor = denied ? Theme.Danger : Theme.Text;
            float strength = listening ? 0.10f + 0.25f * smoothLevel : 0.05f;
            Pulse(pulse1, t % 2.6f / 2.6f, pulseColor, strength);
            Pulse(pulse2, (t + 1.3f) % 2.6f / 2.6f, pulseColor, strength);

            // widmo dookoła okręgu (150–5000 Hz, symetrycznie, niskie na dole). Każde pasmo względem własnej średniej
            // z ostatnich sekund – stały szum (buczenie, wentylator) nie rusza słupków, ruszają się tylko zmiany (mowa)
            var spec = listening ? dec?.Spectrum : null;
            if (spec != null)
            {
                if (bandAvg == null || bandAvg.Length != spec.Length)
                {
                    bandAvg = new float[spec.Length];
                    for (int k = 0; k < spec.Length; k++) bandAvg[k] = (float)spec[k];
                }
                float follow = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 3f);
                for (int k = 0; k < spec.Length; k++) bandAvg[k] = Mathf.Lerp(bandAvg[k], (float)spec[k], follow);
            }
            for (int i = 0; i < BarCount; i++)
            {
                float target = 0;
                if (spec != null)
                {
                    float pos = (i + 0.5f) / BarCount;                              // 0 = góra, zgodnie z zegarem
                    int band = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(2f * pos - 1f) * (spec.Length - 1)), 0, spec.Length - 1);
                    float shape = Mathf.Clamp01(((float)spec[band] / (bandAvg[band] + 1e-9f) - 1f) / 3f);
                    float idle = 0.04f + 0.02f * Mathf.Sin(t * 1.2f + i * 0.45f);   // spokojny ruch w ciszy
                    target = Mathf.Max(idle, smoothLevel * (0.3f + 0.7f * shape));
                }
                barLevels[i] = Mathf.Lerp(barLevels[i], target, target > barLevels[i] ? 0.35f : 0.1f);
                bars[i].sizeDelta = new Vector2(10, 6 + 110 * barLevels[i]);
                barImages[i].color = Theme.WithAlpha(Theme.Text, 0.22f + 0.78f * barLevels[i]);
            }

            // rdzeń „oddycha” z głosem
            float breathe = 1f + 0.06f * smoothLevel;
            core.rectTransform.localScale = new Vector3(breathe, breathe, 1);

            // postęp odbioru na obwodzie okręgu
            float target01 = receiving ? Mathf.Clamp01((float)dec.Progress) : 0f;
            shownProgress = receiving ? Mathf.Lerp(shownProgress, target01, 0.3f) : Mathf.MoveTowards(shownProgress, 0, Time.unscaledDeltaTime * 2);
            progress.fillAmount = shownProgress;
            coreIcon.gameObject.SetActive(!receiving);
            percent.gameObject.SetActive(receiving);
            if (receiving) percent.text = Mathf.RoundToInt(target01 * 100) + "%";
            coreIcon.color = denied ? Theme.Danger : Color.Lerp(Theme.Text, Theme.AccentFor(VerifyStatus.Verified), smoothLevel);

            UpdateStatus(mic, receiving);
            if (App.TestClock && t > nextClockRefresh) UpdateClock();
        }

        void UpdateStatus(MicListener mic, bool receiving)
        {
            switch (mic.Status)
            {
                case MicListener.State.Listening when receiving:
                    status.text = "Odbieram komunikat…";
                    hint.text = mic.Decoder.ReceivingBytes + " B · nie zasłaniaj mikrofonu";
                    break;
                case MicListener.State.Listening:
                    status.text = "Nasłuchuję komunikatów";
                    hint.text = mic.Background
                        ? "Radio, telewizja, telefon sąsiada – także w tle.\nMożesz też zeskanować kod QR."
                        : "Radio, megafon albo telefon sąsiada.\nMożesz też zeskanować kod QR.";
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
                    hint.text = "Zeskanuj kod QR komunikatu\nz plakatu lub ekranu.";
                    break;
            }
        }

        static void Pulse(Image ring, float phase, Color color, float strength)
        {
            float s = 1f + 0.5f * phase;
            ring.rectTransform.localScale = new Vector3(s, s, 1);
            ring.color = Theme.WithAlpha(color, strength * (1f - phase));
        }

        void UpdateClock()
        {
            nextClockRefresh = Time.unscaledTime + 1f;
            clockText.text = "Zegar testowy · " + Ui.Time(App.Now);
        }

        void OnLogoTap()
        {
            if (Time.unscaledTime - lastLogoTap > 1.5f) logoTaps = 0;
            lastLogoTap = Time.unscaledTime;
            if (++logoTaps >= 5)
            {
                logoTaps = 0;
                App.Show(App.DebugPanel);                           // ukryty panel diagnostyczny (CLIENT_UNITY.md §5.6)
            }
        }
    }
}
