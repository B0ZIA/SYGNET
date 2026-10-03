using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sygnet.App.UI;
using Sygnet.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Sygnet.App
{
    public enum FrameSource { Qr, Audio }

    /// <summary>
    /// Bootstrap i stan aplikacji: trust store (zweryfikowany wbudowanym ROOT), Storage, nawigacja ekranów
    /// i jedna ścieżka odbioru ramki: Verify → Commit → skrzynka → alarm → ekran wyniku.
    /// Nowy komunikat nie zabiera ekranu, gdy użytkownik coś czyta: trafia do kolejki z paskiem „Nowy komunikat”
    /// (ważne alarmy na początek), a „OK” na ekranie wyniku otwiera kolejny. W tle (Android) – powiadomienie,
    /// a po powrocie do aplikacji wszystkie odebrane w tle komunikaty są w tej samej kolejce.
    /// </summary>
    public class SygnetApp : MonoBehaviour
    {
        /// <summary>now_for_tests z testvectors.json – „zegar testowy” pozwala pokazać wektory TV1–TV6 jako aktualne.</summary>
        public const long TestVectorsNow = 1791076920;

        /// <summary>Odcisk ROOT z wektorów testowych (seed 0x02×32) – wtedy aplikacja oznacza klucz jako TESTOWY.</summary>
        public const string TestRootFingerprint = "6A38-03D5-F059-902A";

        public static bool RootIsTestKey => RootKey.Fingerprint == TestRootFingerprint;

        public TrustStore Trust { get; private set; }
        public Storage Store { get; private set; }
        public RelayPlayer Relay { get; private set; }
        public QrScanner Scanner { get; private set; }
        public MicListener Mic { get; private set; }

        public HomeScreen Home { get; private set; }
        public ScanScreen Scan { get; private set; }
        public ResultScreen Result { get; private set; }
        public InboxScreen Inbox { get; private set; }
        public AboutScreen About { get; private set; }
        public AreaScreen Area { get; private set; }
        public OnboardingScreen Onboarding { get; private set; }
        public DebugScreen DebugPanel { get; private set; }

        /// <summary>Ostatnia odebrana ramka (QR lub dźwięk) – dla panelu diagnostycznego.</summary>
        public byte[] LastFrame { get; private set; }
        public FrameSource LastSource { get; private set; }
        public VerificationResult LastResult { get; private set; }
        public DateTime LastAt { get; private set; }

        long testClockOffset;
        public bool TestClock { get; private set; }

        readonly object storeLock = new object();      // Store używa też wątek nasłuchu w tle
        readonly ConcurrentQueue<InboxEntry> fromBackground = new ConcurrentQueue<InboxEntry>();   // do kolejki po powrocie
        int notificationId = 100;                      // 1 = stałe powiadomienie usługi nasłuchu

        /// <summary>Komunikat czekający na przeczytanie (odebrany w trakcie czytania innego albo przerwany).</summary>
        sealed class Pending
        {
            public VerificationResult Result;
            public long ReceivedAt;
            public bool Interrupted;
            public long Order;
        }

        readonly List<Pending> pending = new List<Pending>();
        long pendingOrder;
        NewMessageBanner banner;

        /// <summary>Ile komunikatów czeka na przeczytanie (pasek „Nowy komunikat”).</summary>
        public int PendingCount => pending.Count;

        /// <summary>Bieżący czas UTC (unix s); przy zegarze testowym przesunięty do czasu wektorów.</summary>
        public long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (TestClock ? testClockOffset : 0);

        readonly List<AppScreen> screens = new List<AppScreen>();
        readonly List<RectTransform> safeAreas = new List<RectTransform>();
        Rect lastSafeArea;
        AppScreen current;
        RectTransform canvasRoot;
        RectTransform toast;
        TMPro.TextMeshProUGUI toastText;
        float toastUntil;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;     // na ekranie widać odbiór; zgaszony ekran = nasłuch w tle

            Store = new Storage(Application.persistentDataPath);
            Store.Load();
            LoadTrustStore();

            Relay = new GameObject("Relay", typeof(AudioSource), typeof(RelayPlayer)).GetComponent<RelayPlayer>();
            Relay.transform.SetParent(transform);
            Scanner = new GameObject("QrScanner", typeof(QrScanner)).GetComponent<QrScanner>();
            Scanner.transform.SetParent(transform);
            Mic = new GameObject("MicListener", typeof(MicListener)).GetComponent<MicListener>();
            Mic.transform.SetParent(transform);
            Mic.FrameReceived += f => HandleFrame(f, FrameSource.Audio);
            Mic.FrameReceivedInBackground += HandleFrameInBackground;
            Relay.PlayingChanged += playing => Mic.Suspend(playing);    // telefon nie dekoduje sam siebie

            BuildCanvas();
            Home = Add(new HomeScreen(this, canvasRoot));
            Scan = Add(new ScanScreen(this, canvasRoot));
            Result = Add(new ResultScreen(this, canvasRoot));
            Inbox = Add(new InboxScreen(this, canvasRoot));
            About = Add(new AboutScreen(this, canvasRoot));
            Area = Add(new AreaScreen(this, canvasRoot));
            Onboarding = Add(new OnboardingScreen(this, canvasRoot));
            DebugPanel = Add(new DebugScreen(this, canvasRoot));
            BuildToast();
            banner = new NewMessageBanner(this, canvasRoot, OpenPending);
            if (Store.Onboarded)
            {
                Show(Home);
                Mic.StartListening();
            }
            else
            {
                Show(Onboarding);                                   // mikrofon dopiero po onboardingu
            }
        }

        void LoadTrustStore()
        {
            var rootPub = (byte[])RootKey.Public.Clone();                 // kopia: pole statyczne jest modyfikowalne
            if (Ed25519.Fingerprint(rootPub) != RootKey.Fingerprint)
                Debug.LogError("[SYGNET] Odcisk wbudowanego klucza ROOT nie zgadza się ze stałą Fingerprint!");
            var json = Resources.Load<TextAsset>("sygnet_trust_store");
            try
            {
                Trust = TrustStore.FromJson(json != null ? json.text : "{}", rootPub);
            }
            catch (FormatException e)
            {
                Debug.LogError("[SYGNET] Uszkodzony trust store: " + e.Message);
                Trust = new TrustStore(rootPub);
            }
            foreach (var why in Trust.Rejected) Debug.LogWarning("[SYGNET] Odrzucony certyfikat: " + why);
            Debug.Log("[SYGNET] Zaufani wydawcy: " + Trust.Issuers.Count + ", ROOT " + Trust.RootFingerprint +
                      (RootIsTestKey ? " (TESTOWY)" : ""));
        }

        T Add<T>(T s) where T : AppScreen
        {
            screens.Add(s);
            return s;
        }

        // ───────────── odbiór komunikatu ─────────────

        /// <summary>Tekst z kodu QR. Zwraca true, jeśli był to komunikat SYGNET (skaner może się wtedy zamknąć).</summary>
        public bool HandleQrText(string text)
        {
            if (!Frame.TryFromQrText(text, out var frame))
            {
                Debug.Log("[SYGNET] QR spoza SYGNET zignorowany (nie otwieramy linków).");
                return false;
            }
            var r = HandleFrame(frame, FrameSource.Qr);
            if (r.Status == VerifyStatus.Malformed)
            {
                ShowToast("Uszkodzony kod SYGNET. Spróbuj ponownie.");
                return false;
            }
            return true;
        }

        /// <summary>Jedyna ścieżka odbioru ramki (QR i dźwięk) na ekranie. PROTOCOL.md §7.</summary>
        public VerificationResult HandleFrame(byte[] frame, FrameSource source)
        {
            var r = Receive(frame, source, out var entry);
            if (entry == null)
            {
                // MALFORMED: ignoruj po cichu. DUPLICATE z dźwięku (np. powtórka w TV): bez alarmu i bez zabierania ekranu,
                // ale z krótką informacją – inaczej odbiór dochodzi do 99% i „nic się nie dzieje”
                if (r.Status == VerifyStatus.Duplicate && source == FrameSource.Qr) ShowDuplicate(r);
                else if (r.Status == VerifyStatus.Duplicate) ShowToast("Już w skrzynce: " + AlertTypes.Get(r.Payload.Type).Name);
                return r;
            }
            Alarm(r);
            Present(r, entry.receivedAt, source == FrameSource.Qr);
            return r;
        }

        // ───────────── kolejka komunikatów ─────────────

        /// <summary>
        /// Nowy wynik. Na ekranie nasłuchu (i po skanie QR, który użytkownik sam zrobił) – od razu. W trakcie czytania
        /// innego wyniku albo przeglądania skrzynki – do kolejki z paskiem; nic nie znika użytkownikowi sprzed oczu.
        /// </summary>
        void Present(VerificationResult r, long receivedAt, bool userInitiated)
        {
            if (userInitiated || (current == Home && pending.Count == 0))
            {
                OpenResult(r, receivedAt, null);
                return;
            }
            Enqueue(r, receivedAt, false);
            if (current == Home) OpenNextPending();
            else if (!Vibrates(r)) StartCoroutine(Vibrate(1));            // sam pasek mógłby umknąć
        }

        void Enqueue(VerificationResult r, long receivedAt, bool interrupted)
        {
            pending.Add(new Pending { Result = r, ReceivedAt = receivedAt, Interrupted = interrupted, Order = pendingOrder++ });
            UpdateBanner();
        }

        /// <summary>Prawdziwy alarm lotniczy, ewakuacja i zagrożenie chemiczne – przed resztą; dalej kolejność odbioru.</summary>
        static int Priority(Pending p) => p.Result.Status == VerifyStatus.Verified && AlertNotification.IsUrgent(p.Result) ? 0 : 1;

        Pending First()
        {
            Pending best = null;
            foreach (var p in pending)
                if (best == null || Priority(p) < Priority(best) || (Priority(p) == Priority(best) && p.Order < best.Order))
                    best = p;
            return best;
        }

        /// <summary>Dotknięcie paska: otwórz czekający komunikat; czytany właśnie wynik wraca do kolejki („Dokończ czytanie”).</summary>
        public void OpenPending()
        {
            if (pending.Count == 0) return;
            var next = First();
            pending.Remove(next);
            // po przeczytaniu wracamy tam, skąd użytkownik przyszedł (np. do skrzynki), a przerwany wynik czeka w kolejce
            var back = current == Result ? Result.ReturnTo : current;
            if (current == Result && Result.Current != null) Enqueue(Result.Current, Result.ReceivedAt, true);
            OpenResult(next.Result, next.ReceivedAt, null, back);
        }

        /// <summary>„OK” albo „wstecz” na ekranie wyniku: kolejny czekający komunikat albo powrót.</summary>
        public void CloseResult()
        {
            if (pending.Count > 0) OpenNextPending(Result.ReturnTo);
            else Show(Result.ReturnTo ?? Home);
        }

        void OpenNextPending(AppScreen returnTo = null)
        {
            var next = First();
            pending.Remove(next);
            OpenResult(next.Result, next.ReceivedAt, null, returnTo);
        }

        void OpenResult(VerificationResult r, long receivedAt, string notice, AppScreen returnTo = null)
        {
            Result.ReturnTo = returnTo ?? Home;
            Result.Show(r, receivedAt, notice);
            if (current == Result) Result.BeginEnter();                  // nowa treść na tym samym ekranie – z animacją
            else Show(Result);
            UpdateBanner();
        }

        void UpdateBanner()
        {
            if (banner == null) return;
            var first = First();
            bool visible = first != null && current != Onboarding;
            Result.SetTopInset(visible && current == Result ? NewMessageBanner.ResultInset : 0);
            if (!visible)
            {
                banner.Hide();
                return;
            }
            float top = current == Result ? 24 : Ui.BelowHeader;              // pod nagłówkiem podekranu, nie na „wstecz”
            banner.Show(first.Result, first.Interrupted, pending.Count, top);
        }

        /// <summary>
        /// Ramka z dźwięku, gdy aplikacja jest w tle (wątek nasłuchu): ta sama weryfikacja, wynik jako powiadomienie.
        /// MALFORMED i DUPLICATE (np. powtórka w telewizji) – bez powiadomienia.
        /// </summary>
        void HandleFrameInBackground(byte[] frame)
        {
            var r = Receive(frame, FrameSource.Audio, out var entry);
            if (entry == null) return;
            fromBackground.Enqueue(entry);
            AlertNotification.Post(r, Trust, System.Threading.Interlocked.Increment(ref notificationId));
        }

        /// <summary>Verify → Commit → skrzynka. Zwraca wpis skrzynki albo null (MALFORMED, DUPLICATE).</summary>
        VerificationResult Receive(byte[] frame, FrameSource source, out InboxEntry entry)
        {
            lock (storeLock)
            {
                long now = Now;
                var r = Verifier.Verify(frame, Trust, now, Store.UserArea, Store.Revoked, Store.Seen);
                Debug.Log("[SYGNET] " + source + ": " + r + " " + Bytes.ToHex(frame));
                LastFrame = frame;
                LastSource = source;
                LastResult = r;
                LastAt = DateTime.Now;
                entry = null;
                if (r.Status == VerifyStatus.Malformed || r.Status == VerifyStatus.Duplicate) return r;

                bool revokedChanged = Verifier.Commit(r, Store.Revoked, Store.Seen);
                if (revokedChanged) Debug.LogWarning("[SYGNET] Unieważniono klucz wydawcy " + Verifier.RevokedIssuerId(r.Payload));
                entry = Store.AddToInbox(r, source == FrameSource.Qr ? "qr" : "audio", now);
                Store.Save();
                return r;
            }
        }

        /// <summary>
        /// Powrót z tła: komunikaty odebrane w tle trafiają do kolejki (wszystkie, nie tylko ostatni). Na ekranie nasłuchu
        /// od razu otwiera się pierwszy; jeśli użytkownik coś czytał – zostaje pasek „Nowy komunikat”.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused || current == Onboarding) return;
            bool any = false;
            while (fromBackground.TryDequeue(out var e))
            {
                var r = Verifier.Verify(Bytes.FromHex(e.frameHex), Trust, Now, Store.UserArea, Store.Revoked, null);
                Enqueue(r, e.receivedAt, false);
                any = true;
            }
            if (!any) return;
            Home.Refresh();
            if (current == Home) OpenNextPending();
        }

        /// <summary>Komunikat już otrzymany: pokaż istniejący wpis, bez alarmu.</summary>
        void ShowDuplicate(VerificationResult dup)
        {
            var entry = Store.FindAuthentic(dup.Payload.IssuerId, dup.Payload.Sequence);
            var frame = entry != null ? Bytes.FromHex(entry.frameHex) : dup.RawFrame;
            var r = Verifier.Verify(frame, Trust, Now, Store.UserArea, Store.Revoked, null);
            OpenResult(r, entry?.receivedAt ?? Now, "Ten komunikat jest już w skrzynce");
        }

        /// <summary>Wpis skrzynki: ponowna weryfikacja na bieżący czas (np. teraz już NIEAKTUALNY).</summary>
        public void OpenInboxEntry(InboxEntry e, AppScreen returnTo = null)
        {
            var r = Verifier.Verify(Bytes.FromHex(e.frameHex), Trust, Now, Store.UserArea, Store.Revoked, null);
            OpenResult(r, e.receivedAt, null, returnTo);
        }

        /// <summary>Czy <see cref="Alarm"/> sam wibruje (zweryfikowany alarm/ewakuacja/chemia albo odrzucenie).</summary>
        static bool Vibrates(VerificationResult r) => AlertNotification.IsUrgent(r);

        void Alarm(VerificationResult r)
        {
            if (r.Status == VerifyStatus.Verified)
            {
                int t = r.Payload.Type;
                if (t == AlertTypes.AirRaid || t == AlertTypes.Evacuation || t == AlertTypes.Chemical)
                    StartCoroutine(Vibrate(3));
            }
            else if (r.Status == VerifyStatus.Forged || r.Status == VerifyStatus.Incomplete)
            {
                StartCoroutine(Vibrate(1));
            }
        }

        static IEnumerator Vibrate(int times)
        {
            for (int i = 0; i < times; i++)
            {
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
                yield return new WaitForSecondsRealtime(0.8f);
            }
        }

        // ───────────── zegar testowy ─────────────

        public void ToggleTestClock()
        {
            TestClock = !TestClock;
            testClockOffset = TestVectorsNow - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ShowToast(TestClock ? "Zegar testowy: czas wektorów TV1–TV6" : "Zegar systemowy");
            Home.Refresh();
        }

        // ───────────── nawigacja i UI ─────────────

        public void Show(AppScreen s)
        {
            if (current == s) return;
            if (current != null)
            {
                current.OnHide();
                current.Root.gameObject.SetActive(false);
            }
            current = s;
            s.Root.gameObject.SetActive(true);
            s.Root.SetAsLastSibling();
            banner?.BringToFront();
            toast.SetAsLastSibling();
            s.BeginEnter();
            s.OnShow();
            UpdateBanner();                                   // pod nagłówkiem nowego ekranu (albo schowany)
        }

        public void ShowToast(string text, float seconds = 3f)
        {
            toastText.text = text;
            toast.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + seconds;
        }

        public void RegisterSafeArea(RectTransform rt)
        {
            safeAreas.Add(rt);
            ApplySafeArea(rt, Screen.safeArea);
        }

        void Update()
        {
            if (Screen.safeArea != lastSafeArea)
            {
                lastSafeArea = Screen.safeArea;
                foreach (var rt in safeAreas) ApplySafeArea(rt, lastSafeArea);
            }
            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
            banner.Tick(Time.unscaledDeltaTime);

            if (BackPressed() && current != null && !current.OnBack() && current != Home && current != Onboarding)
                Show(Home);

            foreach (var s in screens)
            {
                if (!s.Visible) continue;
                s.AnimateEnter(Time.unscaledDeltaTime);
                s.Tick();
            }
        }

        /// <summary>
        /// Przycisk „wstecz” Androida. Input System zgłasza go jako Escape klawiatury, ale przy GameActivity
        /// nie zawsze na Keyboard.current – sprawdzamy więc każdą klawiaturę.
        /// </summary>
        static bool BackPressed()
        {
            foreach (var d in InputSystem.devices)
                if (d is Keyboard k && k.escapeKey.wasPressedThisFrame) return true;
            return false;
        }

        static void ApplySafeArea(RectTransform rt, Rect sa)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void BuildCanvas()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Theme.Bg;
            }
            if (FindAnyObjectByType<AudioListener>() == null)            // bez niego „Przekaż dalej” byłoby nieme
                (cam != null ? cam.gameObject : gameObject).AddComponent<AudioListener>();

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;
            canvasRoot = (RectTransform)go.transform;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform);
            }
        }

        void BuildToast()
        {
            toast = Ui.Rect(canvasRoot, "Toast");
            var safe = Ui.Rect(toast, "Safe");
            RegisterSafeArea(safe);
            var card = Ui.Card(safe, "Card", Theme.WithAlpha(Theme.Surface2, 0.97f), 80);
            Ui.Bottom(card.rectTransform, 440, 160, Theme.Margin);            // nad przyciskami ekranów
            toastText = Ui.Label(card.transform, "", TextStyle.BodyStrong, Theme.Text, TMPro.TextAlignmentOptions.Center);
            Ui.Stretch(toastText.rectTransform, 48, 16, 48, 16);
            toast.gameObject.SetActive(false);
        }
    }
}
