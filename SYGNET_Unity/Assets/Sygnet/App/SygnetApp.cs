using System;
using System.Collections;
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

        long testClockOffset;
        public bool TestClock { get; private set; }

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
            Screen.sleepTimeout = SleepTimeout.NeverSleep;     // nasłuch działa tylko przy aktywnej aplikacji

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
            Relay.PlayingChanged += playing => Mic.Suspend(playing);    // telefon nie dekoduje sam siebie

            BuildCanvas();
            Home = Add(new HomeScreen(this, canvasRoot));
            Scan = Add(new ScanScreen(this, canvasRoot));
            Result = Add(new ResultScreen(this, canvasRoot));
            BuildToast();
            Show(Home);
            Mic.StartListening();
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

        /// <summary>Jedyna ścieżka odbioru ramki (QR i dźwięk). PROTOCOL.md §7.</summary>
        public VerificationResult HandleFrame(byte[] frame, FrameSource source)
        {
            long now = Now;
            var r = Verifier.Verify(frame, Trust, now, Store.UserArea, Store.Revoked, Store.Seen);
            Debug.Log("[SYGNET] " + source + ": " + r + " " + Bytes.ToHex(frame));

            switch (r.Status)
            {
                case VerifyStatus.Malformed:
                    return r;                                     // ignoruj po cichu
                case VerifyStatus.Duplicate:
                    if (source == FrameSource.Qr) ShowDuplicate(r);   // z dźwięku: bez alarmu
                    return r;
            }

            bool revokedChanged = Verifier.Commit(r, Store.Revoked, Store.Seen);
            if (revokedChanged) Debug.LogWarning("[SYGNET] Unieważniono klucz wydawcy " + Verifier.RevokedIssuerId(r.Payload));
            var entry = Store.AddToInbox(r, source == FrameSource.Qr ? "qr" : "audio", now);
            Store.Save();

            Alarm(r);
            Result.Show(r, entry.receivedAt, duplicate: false);
            Show(Result);
            return r;
        }

        /// <summary>Komunikat już otrzymany: pokaż istniejący wpis, bez alarmu.</summary>
        void ShowDuplicate(VerificationResult dup)
        {
            var entry = Store.FindAuthentic(dup.Payload.IssuerId, dup.Payload.Sequence);
            var frame = entry != null ? Bytes.FromHex(entry.frameHex) : dup.RawFrame;
            var r = Verifier.Verify(frame, Trust, Now, Store.UserArea, Store.Revoked, null);
            Result.Show(r, entry?.receivedAt ?? Now, duplicate: true);
            Show(Result);
        }

        public void OpenInboxEntry(InboxEntry e)
        {
            var r = Verifier.Verify(Bytes.FromHex(e.frameHex), Trust, Now, Store.UserArea, Store.Revoked, null);
            Result.Show(r, e.receivedAt, duplicate: true);
            Show(Result);
        }

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
            toast.SetAsLastSibling();
            s.BeginEnter();
            s.OnShow();
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

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && current != null && !current.OnBack() && current != Home)
                Show(Home);

            foreach (var s in screens)
            {
                if (!s.Visible) continue;
                s.AnimateEnter(Time.unscaledDeltaTime);
                s.Tick();
            }
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
