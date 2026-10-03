using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Ukryty panel diagnostyczny (CLIENT_UNITY.md §5.6, 5× tap w logo): mikrofon na żywo, ostatnia surowa ramka,
    /// zegar testowy, autotest WAV-ów TV1–TV6 ze StreamingAssets, reset danych przed próbą demo.
    /// </summary>
    public class DebugScreen : AppScreen
    {
        static readonly (string name, string expected)[] Vectors =
        {
            ("TV1_air_raid_single", "VERIFIED"), ("TV2_evacuation_dual", "VERIFIED"),
            ("TV3_forged_hacker_as_1", "FORGED"), ("TV4_evacuation_single", "INCOMPLETE"),
            ("TV5_unauthorized_area", "FORGED"), ("TV6_tampered_note", "FORGED"),
        };

        readonly TextMeshProUGUI mic, clock, frame, selftest, info;
        readonly UnityEngine.UI.Button clockButton, testButton;
        float nextRefresh;
        bool testing;

        public DebugScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Debug", Theme.Bg)
        {
            Ui.Header(Safe, "Panel diagnostyczny", () => App.Show(App.Home));
            var body = Ui.Rect(Safe, "Body");
            Ui.Stretch(body, 0, Ui.BelowHeader, 0, 0);
            var c = Ui.Scroll(body, "Scroll", 24, new RectOffset(56, 56, 8, 64));

            mic = Section(c, "Mikrofon i dekoder");
            Ui.Button(mic.transform.parent, "Uruchom mikrofon ponownie", ButtonKind.Secondary, () =>
            {
                App.Mic.StopListening();
                App.Mic.StartListening();
            }, Icons.Mic, 128);

            clock = Section(c, "Czas");
            clockButton = Ui.Button(clock.transform.parent, "Zegar testowy", ButtonKind.Secondary, () =>
            {
                App.ToggleTestClock();
                Refresh();
            }, null, 128);

            frame = Section(c, "Ostatnia ramka");
            frame.richText = false;

            selftest = Section(c, "Autotest modemu i weryfikacji");
            selftest.text = "Dekoduje WAV-y TV1–TV6 zapisane w aplikacji i weryfikuje kluczami testowymi z wektorów.";
            testButton = Ui.Button(selftest.transform.parent, "Dekoduj testowe WAV", ButtonKind.Primary, () =>
            {
                if (!testing) App.StartCoroutine(SelfTest());
            }, Icons.Speaker, 128);

            var data = Card(c);
            Ui.Label(data, "Dane", TextStyle.Overline, Theme.Muted);
            Ui.Button(data, "Wyczyść skrzynkę i pamięć odbioru", ButtonKind.Secondary, () =>
            {
                App.Store.ClearMessages();
                App.ShowToast("Wyczyszczono skrzynkę, seen i unieważnienia", 2f);
                Refresh();
            }, Icons.Inbox, 128);
            Ui.Button(data, "Pokaż onboarding", ButtonKind.Secondary, () =>
            {
                App.Store.Onboarded = false;
                App.Show(App.Onboarding);
            }, null, 128);

            info = Section(c, "Aplikacja");
        }

        public override void OnShow() => Refresh();

        public override bool OnBack()
        {
            App.Show(App.Home);
            return true;
        }

        public override void Tick()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void Refresh()
        {
            var m = App.Mic;
            var d = m.Decoder;
            var sb = new StringBuilder();
            sb.AppendLine("Stan:        " + m.Status);
            sb.AppendLine("Urządzenie:  " + (m.Device ?? "–"));
            sb.AppendLine("Sample rate: " + m.SampleRate + " Hz (wyjście " + AudioSettings.outputSampleRate + " Hz)");
            if (d != null)
            {
                sb.AppendLine("RMS:         " + d.Rms.ToString("0.0000") + "  " + Bar(d.Rms * 8));
                sb.AppendLine("P(1000 Hz):  " + d.PreambleA.ToString("0.00") + "  " + Bar((float)d.PreambleA));
                sb.AppendLine("P(5200 Hz):  " + d.PreambleB.ToString("0.00") + "  " + Bar((float)d.PreambleB));
                sb.AppendLine("Preambuły " + d.PreamblesDetected + " · ramki " + d.FramesDecoded + " · złe CRC " + d.FramesFailed +
                              " · połączone " + d.FramesCombined);
                sb.Append(d.Progress >= 0 ? "Odbiór:      " + (d.Progress * 100).ToString("0") + "% z " + d.ReceivingBytes + " B" : "Odbiór:      –");
            }
            mic.text = sb.ToString();

            clock.text = "UTC      " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                         "lokalnie " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                         "aplikacja " + Ui.Time(App.Now) + (App.TestClock ? "  (ZEGAR TESTOWY)" : "");
            Ui.SetLabel(clockButton, App.TestClock ? "Wyłącz zegar testowy" : "Włącz zegar testowy (czas wektorów)");

            if (App.LastFrame == null)
                frame.text = "Brak – nic jeszcze nie odebrano.";
            else
                frame.text = App.LastAt.ToString("HH:mm:ss") + " · " + App.LastSource + " · " + App.LastResult + "\n" +
                             App.LastFrame.Length + " B\n" + Bytes.ToHex(App.LastFrame);

            info.text = "SYGNET " + Application.version + " · Unity " + Application.unityVersion + "\n" +
                        "ROOT " + App.Trust.RootFingerprint + (SygnetApp.RootIsTestKey ? " (testowy)" : "") + "\n" +
                        "Zaufani nadawcy: " + App.Trust.Issuers.Count + " · odrzuceni: " + App.Trust.Rejected.Count + "\n" +
                        "Obszar: " + Areas.Name(App.Store.UserArea) + " · skrzynka: " + App.Store.Inbox.Count +
                        " · seen: " + App.Store.Seen.Count + " · unieważnieni: " + App.Store.Revoked.Count + "\n" +
                        "Wejście: " + InputDevices();
        }

        static string InputDevices()
        {
            var names = new List<string>();
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices) names.Add(d.layout);
            return string.Join(", ", names);
        }

        static string Bar(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 20), 0, 20);
            return new string('=', n) + new string('·', 20 - n);
        }

        /// <summary>Dekoduje WAV-y wektorów (StreamingAssets) i weryfikuje kluczami testowymi z testvectors.json.</summary>
        IEnumerator SelfTest()
        {
            testing = true;
            testButton.interactable = false;
            var dir = Application.streamingAssetsPath + "/testvectors/";
            selftest.text = "Wczytuję testvectors.json…";
            string json = null;
            yield return Fetch(dir + "testvectors.json", req => json = req.downloadHandler.text);
            if (json == null)
            {
                selftest.text = "Nie udało się wczytać testvectors.json";
                testing = false;
                testButton.interactable = true;
                yield break;
            }
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var trustJson = TrustStoreJson((Dictionary<string, object>)root["trust_store"]);
            var trust = TrustStore.FromJson(trustJson, Bytes.FromHex((string)root["root_pub_hex"]));
            long now = (long)root["now_for_tests"];
            var vectors = (Dictionary<string, object>)root["vectors"];

            var sb = new StringBuilder();
            int ok = 0;
            foreach (var (name, expected) in Vectors)
            {
                selftest.text = sb + "→ " + name + "…";
                byte[] wav = null;
                yield return Fetch(dir + name + ".wav", req => wav = req.downloadHandler.data);
                if (wav == null)
                {
                    sb.AppendLine("BŁĄD " + name + ": brak pliku");
                    continue;
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var x = Wav.Read(wav, out int sr);
                var frames = ModemDecoder.Decode(x, sr);
                sw.Stop();
                var expectedFrame = (string)((Dictionary<string, object>)vectors[name])["frame_hex"];
                bool bytesOk = frames.Count == 1 && Bytes.ToHex(frames[0]) == expectedFrame;
                var r = frames.Count > 0 ? Verifier.Verify(frames[0], trust, now, 1465, null, null) : null;
                bool pass = bytesOk && r != null && r.StatusName == expected;
                if (pass) ok++;
                sb.AppendLine((pass ? "OK  " : "BŁĄD ") + name.Substring(0, 3) + "  " + (r?.ToString() ?? "brak ramki") +
                              "  · " + sw.ElapsedMilliseconds + " ms");
            }
            sb.Append(ok == Vectors.Length ? "Wszystko OK (" + ok + "/" + Vectors.Length + ")" : "Błędy: " + (Vectors.Length - ok));
            selftest.text = sb.ToString();
            Debug.Log("[SYGNET] Autotest:\n" + sb);
            testing = false;
        }

        static IEnumerator Fetch(string url, Action<UnityWebRequest> onOk)
        {
            // na Androidzie StreamingAssets to jar:file://… – czytany lokalnie, bez sieci
            if (!url.Contains("://")) url = "file://" + url;
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) onOk(req);
                else Debug.LogWarning("[SYGNET] Autotest: " + url + " → " + req.error);
            }
        }

        static string TrustStoreJson(Dictionary<string, object> ts)
        {
            var sb = new StringBuilder("{\"version\":1,\"issuers\":[");
            var items = (List<object>)ts["issuers"];
            for (int i = 0; i < items.Count; i++)
            {
                var e = (Dictionary<string, object>)items[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"cert_b64\":\"").Append(e["cert_b64"]).Append("\",\"root_sig_b64\":\"").Append(e["root_sig_b64"]).Append("\"}");
            }
            return sb.Append("]}").ToString();
        }

        // ── klocki ──

        RectTransform Card(RectTransform content)
        {
            var img = Ui.Card(content, "Card", Theme.Surface);
            Ui.VStack(img.rectTransform, 14, new RectOffset(40, 40, 32, 36));
            return img.rectTransform;
        }

        TextMeshProUGUI Section(RectTransform content, string title)
        {
            var card = Card(content);
            Ui.Label(card, title, TextStyle.Overline, Theme.Muted);
            var t = Ui.Label(card, "", TextStyle.Mono, Theme.Text);
            t.fontSize = 32;
            return t;
        }
    }
}
