using System;
using Sygnet.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Sygnet.App
{
    /// <summary>
    /// Mikrofon → <see cref="StreamingDecoder"/> (CLIENT_UNITY.md §4.2). Microphone nagrywa w pętli do klipu 10 s;
    /// co klatkę czytamy nowe próbki od ostatniej pozycji (z obsługą zawinięcia bufora kołowego).
    /// Nasłuch jest wstrzymywany na czas „Przekaż dalej”, żeby telefon nie dekodował sam siebie.
    /// </summary>
    public class MicListener : MonoBehaviour
    {
        public enum State { Off, WaitingForPermission, PermissionDenied, NoMicrophone, Listening, Paused }

        const int ClipSeconds = 10;
        const int PreferredRate = 48000;
        const float ResumeDelay = 0.4f;          // ogon pogłosu po własnym nadawaniu

        public State Status { get; private set; } = State.Off;
        public int SampleRate { get; private set; }
        public string Device { get; private set; }
        public StreamingDecoder Decoder { get; private set; }

        /// <summary>Poprawna ramka z dźwięku (wątek główny).</summary>
        public event Action<byte[]> FrameReceived;

        AudioClip clip;
        int lastPos;
        float[] chunk = new float[0];
        bool suspended;              // „Przekaż dalej” lub pauza aplikacji
        float resumeAt;
        bool wantListening;

        public void StartListening()
        {
            wantListening = true;
            if (Status == State.Listening) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Status = State.WaitingForPermission;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => { if (wantListening) Open(); };
                cb.PermissionDenied += _ => Status = State.PermissionDenied;
                Permission.RequestUserPermission(Permission.Microphone, cb);
                return;
            }
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
                Decoder?.Reset();
                if (Status == State.Listening) Status = State.Paused;
            }
            else
            {
                resumeAt = Time.unscaledTime + ResumeDelay;
                suspended = false;
            }
        }

        void Open()
        {
            if (Microphone.devices.Length == 0)
            {
                Status = State.NoMicrophone;
                return;
            }
            Device = Microphone.devices[0];
            Microphone.GetDeviceCaps(Device, out int minRate, out int maxRate);
            SampleRate = maxRate == 0 ? PreferredRate : Mathf.Clamp(PreferredRate, Mathf.Max(minRate, 16000), maxRate);
            clip = Microphone.Start(Device, true, ClipSeconds, SampleRate);
            if (clip == null)
            {
                Status = State.NoMicrophone;
                return;
            }
            SampleRate = clip.frequency;                     // urządzenie może dać inną częstotliwość niż prosiliśmy
            if (Decoder == null || Decoder.SampleRate != SampleRate)
            {
                Decoder = new StreamingDecoder(SampleRate);
                Decoder.FrameDecoded += f => FrameReceived?.Invoke(f);
                Decoder.FrameFailed += DumpFailed;
            }
            else
            {
                Decoder.Reset();
            }
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

        void Update()
        {
            if (clip == null || (Status != State.Listening && Status != State.Paused)) return;
            int pos = Microphone.GetPosition(Device);
            if (pos < 0 || pos == lastPos) return;
            int total = clip.samples;
            int count = pos > lastPos ? pos - lastPos : total - lastPos + pos;   // zawinięcie bufora kołowego
            if (chunk.Length < count) chunk = new float[Mathf.NextPowerOfTwo(count)];
            ReadClip(lastPos, count, total);
            lastPos = pos;

            bool paused = suspended || Time.unscaledTime < resumeAt;
            Status = paused ? State.Paused : State.Listening;
            if (!paused) Decoder.Push(chunk, 0, count);
            LogDiagnostics();
        }

        /// <summary>Nagranie ramki z błędnym CRC → WAV w persistentDataPath (adb pull) do analizy w edytorze.</summary>
        void DumpFailed(float[] samples)
        {
            try
            {
                var path = System.IO.Path.Combine(Application.persistentDataPath,
                    "sygnet_fail_" + DateTime.Now.ToString("HHmmss") + ".wav");
                System.IO.File.WriteAllBytes(path, Wav.Write16(samples, SampleRate));
                Debug.LogWarning("[SYGNET] Ramka z błędnym CRC, nagranie: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SYGNET] Nie zapisano nagrania: " + e.Message);
            }
        }

        // ── diagnostyka do logcat (adb logcat -s Unity) – co 5 s albo przy wykryciu preambuły ──
        float diagAt, diagMaxRms;
        double diagMaxA, diagMaxB;
        int diagPreambles;

        void LogDiagnostics()
        {
            if (Decoder == null) return;
            diagMaxRms = Mathf.Max(diagMaxRms, Decoder.Rms);
            diagMaxA = Math.Max(diagMaxA, Decoder.PreambleA);
            diagMaxB = Math.Max(diagMaxB, Decoder.PreambleB);
            bool newPreamble = Decoder.PreamblesDetected != diagPreambles;
            if (!newPreamble && Time.unscaledTime < diagAt) return;
            Debug.Log($"[SYGNET] mic {Status}: rms max {diagMaxRms:0.0000}, P(1000) max {diagMaxA:0.00}, P(5200) max {diagMaxB:0.00}, " +
                      $"preambuły {Decoder.PreamblesDetected}, ramki {Decoder.FramesDecoded}, złe CRC {Decoder.FramesFailed}, " +
                      $"odbiór {Decoder.Progress:0.00}");
            diagPreambles = Decoder.PreamblesDetected;
            diagAt = Time.unscaledTime + 5f;
            diagMaxRms = 0;
            diagMaxA = diagMaxB = 0;
        }

        void ReadClip(int from, int count, int total)
        {
            if (from + count <= total)
            {
                ReadInto(from, 0, count);
                return;
            }
            int first = total - from;
            ReadInto(from, 0, first);
            ReadInto(0, first, count - first);
        }

        void ReadInto(int clipOffset, int chunkOffset, int count)
        {
            if (count > 0) clip.GetData(new Span<float>(chunk, chunkOffset, count), clipOffset);   // bez alokacji co klatkę
        }

        void OnApplicationPause(bool paused)
        {
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
    }
}
