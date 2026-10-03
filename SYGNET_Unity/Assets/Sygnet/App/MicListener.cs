using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Sygnet.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using Debug = UnityEngine.Debug;

namespace Sygnet.App
{
    /// <summary>
    /// Mikrofon → <see cref="StreamingDecoder"/> (CLIENT_UNITY.md §4.2).
    ///
    /// Android: nagrywa usługa pierwszoplanowa <c>SygnetListenService</c> (Plugins/Android/SygnetListen.androidlib),
    /// więc nasłuch trwa także w tle i przy zgaszonym ekranie. Osobny wątek C# zbiera z niej próbki i karmi dekoder.
    /// Ramka odebrana, gdy aplikacja jest na ekranie, trafia do wątku głównego (<see cref="FrameReceived"/>);
    /// w tle – od razu do <see cref="FrameReceivedInBackground"/> (wątek nasłuchu), bo pętla Unity wtedy stoi.
    ///
    /// Edytor i inne platformy: Unity Microphone (klip 10 s w pętli), tylko gdy aplikacja jest aktywna.
    /// Nasłuch jest wstrzymywany na czas „Przekaż dalej”, żeby telefon nie dekodował sam siebie.
    /// </summary>
    public class MicListener : MonoBehaviour
    {
        public enum State { Off, WaitingForPermission, PermissionDenied, NoMicrophone, Listening, Paused }

        const int PreferredRate = 48000;
        const long ResumeDelayMs = 400;          // ogon pogłosu po własnym nadawaniu

        public State Status { get; private set; } = State.Off;
        public int SampleRate { get; private set; }
        public string Device { get; private set; }
        public StreamingDecoder Decoder { get; private set; }

        /// <summary>Czy nasłuch działa także w tle (usługa Androida).</summary>
        public bool Background { get; private set; }

        /// <summary>Poprawna ramka z dźwięku (wątek główny).</summary>
        public event Action<byte[]> FrameReceived;

        /// <summary>Poprawna ramka odebrana, gdy aplikacja jest w tle (wątek nasłuchu, nie główny!).</summary>
        public event Action<byte[]> FrameReceivedInBackground;

        readonly ConcurrentQueue<byte[]> received = new ConcurrentQueue<byte[]>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool appPaused;
        volatile bool suspended;                 // „Przekaż dalej”
        volatile bool resetRequested;
        long resumeAtMs;
        bool wantListening;
        string dumpDir;

        void Awake() => dumpDir = Application.persistentDataPath;     // API Unity – tylko w wątku głównym

        public void StartListening()
        {
            wantListening = true;
            if (Status == State.Listening) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Status = State.WaitingForPermission;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += p => { if (p == Permission.Microphone && wantListening) Open(); };
                cb.PermissionDenied += p => { if (p == Permission.Microphone) Status = State.PermissionDenied; };
                Permission.RequestUserPermissions(new[] { Permission.Microphone, NotificationPermission }, cb);
                return;
            }
            if (!Permission.HasUserAuthorizedPermission(NotificationPermission))
                Permission.RequestUserPermission(NotificationPermission);      // bez tego wynik w tle byłby niewidoczny
#endif
            Open();
        }

        public void StopListening()
        {
            wantListening = false;
            Close();
            Status = State.Off;
        }

        /// <summary>Wstrzymuje przekazywanie próbek do dekodera (np. podczas własnego nadawania) i porzuca odbierane ramki.</summary>
        public void Suspend(bool on)
        {
            if (on)
            {
                suspended = true;
                resetRequested = true;
                if (Status == State.Listening) Status = State.Paused;
            }
            else
            {
                Interlocked.Exchange(ref resumeAtMs, clock.ElapsedMilliseconds + ResumeDelayMs);
                suspended = false;
            }
        }

        // ───────────── wspólne: dekoder ─────────────

        void EnsureDecoder(int rate)
        {
            SampleRate = rate;
            if (Decoder != null && Decoder.SampleRate == rate)
            {
                Decoder.Reset();
                return;
            }
            var d = new StreamingDecoder(rate);
            d.FrameDecoded += OnFrameDecoded;
            d.FrameFailed += DumpFailed;
            Decoder = d;
        }

        /// <summary>Próbki z mikrofonu (wątek nasłuchu na Androidzie, główny w edytorze).</summary>
        void Feed(float[] samples, int count)
        {
            if (resetRequested)
            {
                resetRequested = false;
                Decoder.Reset();
            }
            bool paused = suspended || clock.ElapsedMilliseconds < Interlocked.Read(ref resumeAtMs);
            Status = paused ? State.Paused : State.Listening;
            if (!paused) Decoder.Push(samples, 0, count);
            LogDiagnostics();
        }

        void OnFrameDecoded(byte[] frame)
        {
            var bg = FrameReceivedInBackground;
            if (appPaused && bg != null) bg(frame);
            else received.Enqueue(frame);
        }

        void Update()
        {
            while (received.TryDequeue(out var f)) FrameReceived?.Invoke(f);
#if !(UNITY_ANDROID && !UNITY_EDITOR)
            ReadMicrophone();
#endif
        }

        /// <summary>Nagranie ramki z błędnym CRC → WAV w persistentDataPath (adb pull) do analizy w edytorze.</summary>
        void DumpFailed(float[] samples)
        {
            try
            {
                var path = System.IO.Path.Combine(dumpDir, "sygnet_fail_" + DateTime.Now.ToString("HHmmss") + ".wav");
                System.IO.File.WriteAllBytes(path, Wav.Write16(samples, SampleRate));
                Debug.LogWarning("[SYGNET] Ramka z błędnym CRC, nagranie: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SYGNET] Nie zapisano nagrania: " + e.Message);
            }
        }

        // ── diagnostyka do logcat (adb logcat -s Unity) – co 5 s albo przy wykryciu preambuły ──
        long diagAtMs;
        float diagMaxRms;
        double diagMaxA, diagMaxB;
        int diagPreambles;

        void LogDiagnostics()
        {
            diagMaxRms = Math.Max(diagMaxRms, Decoder.Rms);
            diagMaxA = Math.Max(diagMaxA, Decoder.PreambleA);
            diagMaxB = Math.Max(diagMaxB, Decoder.PreambleB);
            bool newPreamble = Decoder.PreamblesDetected != diagPreambles;
            long now = clock.ElapsedMilliseconds;
            if (!newPreamble && now < diagAtMs) return;
            Debug.Log($"[SYGNET] mic {Status}{(appPaused ? " (w tle)" : "")}: rms max {diagMaxRms:0.0000}, " +
                      $"P(1000) max {diagMaxA:0.00}, P(5200) max {diagMaxB:0.00}, preambuły {Decoder.PreamblesDetected}, " +
                      $"ramki {Decoder.FramesDecoded}, złe CRC {Decoder.FramesFailed}, odbiór {Decoder.Progress:0.00}");
            diagPreambles = Decoder.PreamblesDetected;
            diagAtMs = now + 5000;
            diagMaxRms = 0;
            diagMaxA = diagMaxB = 0;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // ───────────── Android: usługa pierwszoplanowa + wątek nasłuchu ─────────────

        const string NotificationPermission = "android.permission.POST_NOTIFICATIONS";
        const string ServiceClass = "pl.hackyeah.sygnet.SygnetListenService";

        Thread worker;
        volatile bool workerRun;

        void Open()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var service = new AndroidJavaClass(ServiceClass))
                    service.CallStatic("start", activity);        // tylko z pierwszego planu (Android 14: mikrofon)
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Nie wystartowała usługa nasłuchu: " + e);
                Status = State.NoMicrophone;
                return;
            }
            Background = true;
            if (Status != State.Listening && Status != State.Paused) Status = State.WaitingForPermission;
            if (worker != null && worker.IsAlive) return;
            workerRun = true;
            worker = new Thread(WorkerLoop) { Name = "sygnet-listen", IsBackground = true };
            worker.Start();
        }

        void Close()
        {
            workerRun = false;
            worker?.Join(1000);
            worker = null;
            Background = false;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var service = new AndroidJavaClass(ServiceClass))
                    service.CallStatic("stop", activity);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SYGNET] Zatrzymanie usługi nasłuchu: " + e.Message);
            }
        }

        void WorkerLoop()
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var service = new AndroidJavaClass(ServiceClass))
                {
                    var buffer = new float[PreferredRate];
                    long startedAt = clock.ElapsedMilliseconds;
                    while (workerRun)
                    {
                        if (!service.CallStatic<bool>("isRunning"))
                        {
                            if (clock.ElapsedMilliseconds - startedAt > 3000)
                                Status = State.NoMicrophone;           // usługa nie ruszyła albo system ją zatrzymał
                            Thread.Sleep(100);
                            continue;
                        }
                        int rate = service.CallStatic<int>("getSampleRate");
                        if (Decoder == null || Decoder.SampleRate != rate)
                        {
                            EnsureDecoder(rate);
                            Device = "AudioRecord " + service.CallStatic<string>("getSource");
                            Debug.Log("[SYGNET] Nasłuch (usługa): " + Device + " @ " + rate + " Hz");
                        }

                        AndroidJNI.PushLocalFrame(16);
                        short[] pcm;
                        try
                        {
                            pcm = service.CallStatic<short[]>("drain");
                        }
                        finally
                        {
                            AndroidJNI.PopLocalFrame(IntPtr.Zero);
                        }
                        if (pcm == null || pcm.Length == 0)
                        {
                            if (Status == State.WaitingForPermission) Status = State.Listening;
                            Thread.Sleep(40);
                            continue;
                        }
                        if (buffer.Length < pcm.Length) buffer = new float[Mathf.NextPowerOfTwo(pcm.Length)];
                        for (int i = 0; i < pcm.Length; i++) buffer[i] = pcm[i] / 32768f;
                        Feed(buffer, pcm.Length);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Wątek nasłuchu: " + e);
                Status = State.NoMicrophone;
            }
            finally
            {
                AndroidJNI.DetachCurrentThread();
            }
        }

        void OnApplicationPause(bool paused)
        {
            appPaused = paused;
            if (!paused && wantListening && (worker == null || !worker.IsAlive || Status == State.NoMicrophone))
                StartListening();                                  // np. system zatrzymał usługę, gdy byliśmy w tle
        }

        void OnApplicationQuit() => Close();
#else
        // ───────────── edytor / inne: Unity Microphone ─────────────

        const int ClipSeconds = 10;

        AudioClip clip;
        int lastPos;
        float[] chunk = new float[0];

        void Open()
        {
            if (Microphone.devices.Length == 0)
            {
                Status = State.NoMicrophone;
                return;
            }
            Device = Microphone.devices[0];
            Microphone.GetDeviceCaps(Device, out int minRate, out int maxRate);
            int rate = maxRate == 0 ? PreferredRate : Mathf.Clamp(PreferredRate, Mathf.Max(minRate, 16000), maxRate);
            clip = Microphone.Start(Device, true, ClipSeconds, rate);
            if (clip == null)
            {
                Status = State.NoMicrophone;
                return;
            }
            EnsureDecoder(clip.frequency);                      // urządzenie może dać inną częstotliwość niż prosiliśmy
            lastPos = 0;
            Status = State.Listening;
            Debug.Log("[SYGNET] Mikrofon: " + Device + " @ " + SampleRate + " Hz (caps " + minRate + "–" + maxRate + ")");
        }

        void Close()
        {
            if (clip != null && Microphone.IsRecording(Device)) Microphone.End(Device);
            if (clip != null) Destroy(clip);
            clip = null;
        }

        void ReadMicrophone()
        {
            if (clip == null || (Status != State.Listening && Status != State.Paused)) return;
            int pos = Microphone.GetPosition(Device);
            if (pos < 0 || pos == lastPos) return;
            int total = clip.samples;
            int count = pos > lastPos ? pos - lastPos : total - lastPos + pos;   // zawinięcie bufora kołowego
            if (chunk.Length < count) chunk = new float[Mathf.NextPowerOfTwo(count)];
            if (lastPos + count <= total)
            {
                ReadInto(lastPos, 0, count);
            }
            else
            {
                int first = total - lastPos;
                ReadInto(lastPos, 0, first);
                ReadInto(0, first, count - first);
            }
            lastPos = pos;
            Feed(chunk, count);
        }

        void ReadInto(int clipOffset, int chunkOffset, int count)
        {
            if (count > 0) clip.GetData(new Span<float>(chunk, chunkOffset, count), clipOffset);   // bez alokacji co klatkę
        }

        void OnApplicationPause(bool paused)
        {
            appPaused = paused;
            if (paused)
            {
                Close();
                if (Status != State.PermissionDenied) Status = State.Off;
            }
            else if (wantListening)
            {
                StartListening();
            }
        }

        void OnDestroy() => Close();
#endif
    }
}
