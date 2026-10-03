using System;
using System.Collections.Generic;
using Sygnet.Core;
using UnityEditor;
using UnityEngine;

namespace Sygnet.Editor
{
    /// <summary>
    /// Odbiornik w edytorze: mikrofon komputera → StreamingDecoder → Verifier (ten sam trust store i RootKey co aplikacja).
    /// Do testów „Przekaż dalej” z telefonu i dźwięku z konsoli – działa poza trybem Play.
    /// </summary>
    public class TestReceiverWindow : EditorWindow
    {
        const int Rate = 48000;

        TrustStore trust;
        string device;
        AudioClip clip;
        int lastPos;
        float[] chunk = new float[0];
        StreamingDecoder decoder;
        bool testClock;
        int userArea = 1465;
        double nextRepaint;
        readonly List<(VerificationResult r, DateTime at)> results = new List<(VerificationResult, DateTime)>();

        [MenuItem("SYGNET/Odbiornik (mikrofon komputera)")]
        static void Open() => GetWindow<TestReceiverWindow>("Odbiornik SYGNET").Show();

        /// <summary>Do automatyzacji testów: otwiera okno i zaczyna słuchać.</summary>
        public static void OpenAndListen()
        {
            var w = GetWindow<TestReceiverWindow>("Odbiornik SYGNET");
            w.Show();
            w.StartListening();
        }

        /// <summary>Ostatni wynik jako tekst (do automatyzacji testów) albo null.</summary>
        public static string LastResult()
        {
            var w = HasOpenInstances<TestReceiverWindow>() ? GetWindow<TestReceiverWindow>() : null;
            return w == null || w.results.Count == 0 ? null : w.results[0].r + " " + Bytes.ToHex(w.results[0].r.RawFrame);
        }

        void OnEnable()
        {
            var json = Resources.Load<TextAsset>("sygnet_trust_store");
            trust = TrustStore.FromJson(json.text, (byte[])RootKey.Public.Clone());
        }

        void OnDisable() => StopListening();

        void StartListening()
        {
            if (clip != null) return;
            if (Microphone.devices.Length == 0)
            {
                ShowNotification(new GUIContent("Brak mikrofonu"));
                return;
            }
            if (string.IsNullOrEmpty(device) || Array.IndexOf(Microphone.devices, device) < 0) device = Microphone.devices[0];
            clip = Microphone.Start(device, true, 10, Rate);
            lastPos = 0;
            decoder = new StreamingDecoder(clip.frequency);
            decoder.FrameDecoded += OnFrame;
            EditorApplication.update += Poll;
        }

        void StopListening()
        {
            EditorApplication.update -= Poll;
            if (clip != null && Microphone.IsRecording(device)) Microphone.End(device);
            clip = null;
        }

        void Poll()
        {
            if (clip == null) return;
            int pos = Microphone.GetPosition(device);
            if (pos >= 0 && pos != lastPos)
            {
                int total = clip.samples;
                int count = pos > lastPos ? pos - lastPos : total - lastPos + pos;
                if (chunk.Length < count) chunk = new float[Mathf.NextPowerOfTwo(count)];
                int first = Math.Min(count, total - lastPos);
                clip.GetData(new Span<float>(chunk, 0, first), lastPos);
                if (count > first) clip.GetData(new Span<float>(chunk, first, count - first), 0);
                lastPos = pos;
                decoder.Push(chunk, 0, count);
            }
            if (EditorApplication.timeSinceStartup > nextRepaint)
            {
                nextRepaint = EditorApplication.timeSinceStartup + 0.1;
                Repaint();
            }
        }

        void OnFrame(byte[] frame)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (testClock) now = 1791076920;                      // now_for_tests z testvectors.json
            var r = Verifier.Verify(frame, trust, now, userArea, null, null);
            results.Insert(0, (r, DateTime.Now));
            Debug.Log("[SYGNET] Odbiornik (komputer): " + r + " " + Bytes.ToHex(frame));
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var devices = Microphone.devices;
                int idx = Math.Max(0, Array.IndexOf(devices, device));
                GUI.enabled = clip == null && devices.Length > 0;
                idx = EditorGUILayout.Popup(idx, devices);
                if (devices.Length > 0) device = devices[idx];
                GUI.enabled = true;
                if (GUILayout.Button(clip == null ? "▶ Słuchaj" : "■ Stop", GUILayout.Width(100)))
                {
                    if (clip == null) StartListening();
                    else StopListening();
                }
            }
            testClock = EditorGUILayout.ToggleLeft("Zegar testowy (wektory TV1–TV6)", testClock);
            userArea = EditorGUILayout.IntField("Obszar użytkownika (TERYT)", userArea);

            if (decoder != null)
            {
                EditorGUILayout.LabelField($"{device} @ {decoder.SampleRate} Hz");
                var r = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(r, Mathf.Clamp01(decoder.Rms * 8), "poziom " + decoder.Rms.ToString("0.000"));
                EditorGUILayout.LabelField($"P(1000) {decoder.PreambleA:0.00}   P(5200) {decoder.PreambleB:0.00}   " +
                                           $"preambuły {decoder.PreamblesDetected}   ramki {decoder.FramesDecoded}   złe CRC {decoder.FramesFailed}");
                if (decoder.Progress >= 0)
                {
                    var p = EditorGUILayout.GetControlRect(false, 18);
                    EditorGUI.ProgressBar(p, (float)decoder.Progress, "Odbieram " + decoder.ReceivingBytes + " B…");
                }
            }

            EditorGUILayout.Space();
            foreach (var (res, at) in results)
            {
                var color = res.Status == VerifyStatus.Verified ? new Color(0.05f, 0.48f, 0.24f)
                    : res.Status == VerifyStatus.VerifiedOtherArea ? new Color(0.12f, 0.31f, 0.55f)
                    : res.Status == VerifyStatus.Expired ? new Color(0.72f, 0.47f, 0.12f) : new Color(0.71f, 0.14f, 0.09f);
                var rect = EditorGUILayout.GetControlRect(false, 54);
                EditorGUI.DrawRect(rect, color);
                var style = new GUIStyle(EditorStyles.whiteLargeLabel) { fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(rect.x + 8, rect.y + 4, rect.width, 22), Messages.Title(res.Status) + "  (" + res.ReasonCode + ")", style);
                var p = res.Payload;
                var desc = p == null ? "" : AlertTypes.Get(p.Type).Name + " · " + (res.IssuerName ?? "nieznany") + " · " +
                                            Areas.Name(p.AreaCode) + (p.Note.Length > 0 ? " · „" + p.NoteText + "”" : "");
                GUI.Label(new Rect(rect.x + 8, rect.y + 28, rect.width, 22), at.ToString("HH:mm:ss") + "  " + desc, EditorStyles.whiteLabel);
            }
        }
    }
}
