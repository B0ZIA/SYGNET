using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sygnet.Core;
using UnityEditor;
using UnityEngine;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Sygnet.Editor
{
    /// <summary>
    /// Nadajnik testowy w edytorze: świeże komunikaty podpisane SEEDAMI TESTOWYMI z testvectors.json albo wektory TV1–TV6,
    /// pokazane jako QR na ekranie laptopa i zapisywane jako WAV. Zastępuje konsolę, dopóki ta nie jest gotowa.
    /// </summary>
    public class TestTransmitterWindow : EditorWindow
    {
        const string VectorsPath = "Assets/Sygnet/Tests/EditMode/Data/testvectors.json";
        const string SeqPref = "Sygnet.TestTransmitter.Seq";

        sealed class Scenario
        {
            public string Label;
            public string Expect;
            public Func<long, int, byte[]> Build;   // (now, seq) → ramka
        }

        Dictionary<string, object> vectors;
        List<Scenario> scenarios;
        string[] labels;
        int selected;
        byte[] frame;
        string qrText;
        string expect;
        Texture2D qr;
        Vector2 scroll;

        [MenuItem("SYGNET/Nadajnik testowy (QR + WAV)")]
        static void Open() => GetWindow<TestTransmitterWindow>("Nadajnik SYGNET").Show();

        void OnEnable()
        {
            try
            {
                vectors = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(VectorsPath));
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Brak " + VectorsPath + ": " + e.Message);
                return;
            }
            scenarios = BuildScenarios();
            labels = scenarios.Select(s => s.Label).ToArray();
            if (frame == null) Generate();
        }

        void OnDisable()
        {
            if (qr != null) DestroyImmediate(qr);
        }

        void OnGUI()
        {
            if (scenarios == null)
            {
                EditorGUILayout.HelpBox("Nie wczytano " + VectorsPath, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox("Klucze TESTOWE z testvectors.json – działa z aplikacją, dopóki ma testowy RootKey.", MessageType.Info);
            selected = EditorGUILayout.Popup("Scenariusz", selected, labels);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generuj", GUILayout.Height(32))) Generate();
                GUI.enabled = frame != null;
                if (GUILayout.Button("Zapisz WAV", GUILayout.Height(32))) SaveWav();
                if (GUILayout.Button("Kopiuj tekst QR", GUILayout.Height(32))) EditorGUIUtility.systemCopyBuffer = qrText;
                GUI.enabled = true;
            }
            if (frame == null) return;

            EditorGUILayout.LabelField("Oczekiwany wynik na telefonie:", expect, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Ramka: " + frame.Length + " B, dźwięk ≈ " +
                                       ModemConstants.DurationSeconds(frame.Length).ToString("0.0") + " s (2 powtórzenia)");
            float size = Mathf.Min(position.width - 20, position.height - 190);
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            rect.x = (position.width - size) / 2;
            if (qr != null) GUI.DrawTexture(rect, qr, ScaleMode.ScaleToFit);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(60));
            EditorGUILayout.SelectableLabel(qrText, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(50));
            EditorGUILayout.EndScrollView();
        }

        void Generate()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int seq = EditorPrefs.GetInt(SeqPref, 100) + 1;
            if (seq > 65000) seq = 100;
            EditorPrefs.SetInt(SeqPref, seq);

            var s = scenarios[selected];
            frame = s.Build(now, seq);
            expect = s.Expect;
            qrText = Frame.ToQrText(frame);
            if (qr != null) DestroyImmediate(qr);
            qr = RenderQr(qrText);
            Debug.Log("[SYGNET] Nadajnik: " + s.Label + "\n" + qrText + "\n" + Bytes.ToHex(frame));
            Repaint();
        }

        void SaveWav()
        {
            var path = EditorUtility.SaveFilePanel("Zapisz WAV", "", "sygnet_" + DateTime.Now.ToString("HHmmss") + ".wav", "wav");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllBytes(path, Wav.Write16(ModemEncoder.Encode(frame, 48000), 48000));
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>QR poziom M (PROTOCOL.md §3), wiersze odwrócone, bo PixelData idzie od góry, a Texture2D od dołu.</summary>
        static Texture2D RenderQr(string text)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions { ErrorCorrection = ErrorCorrectionLevel.M, Margin = 4, Width = 720, Height = 720 },
            };
            var pd = writer.Write(text);
            int stride = pd.Width * 4;
            var flipped = new byte[pd.Pixels.Length];
            for (int y = 0; y < pd.Height; y++)
                Buffer.BlockCopy(pd.Pixels, y * stride, flipped, (pd.Height - 1 - y) * stride, stride);
            var tex = new Texture2D(pd.Width, pd.Height, TextureFormat.BGRA32, false) { filterMode = FilterMode.Point };
            tex.LoadRawTextureData(flipped);
            tex.Apply();
            return tex;
        }

        // ───────────── scenariusze (jak demo z README §7) ─────────────

        byte[] Seed(string key) => Bytes.FromHex((string)((Dictionary<string, object>)vectors["seeds_hex"])[key]);

        byte[] Signed(int issuer, int type, int area, long ts, int validMin, int seq, string note, params (int id, string seed)[] signers) =>
            SignedRaw(issuer, type, area, ts, validMin, seq, Encoding.UTF8.GetBytes(note ?? ""), signers);

        byte[] SignedRaw(int issuer, int type, int area, long ts, int validMin, int seq, byte[] note, params (int id, string seed)[] signers)
        {
            var p = new Payload
            {
                IssuerId = issuer, Type = type, AreaCode = area, Timestamp = ts, ValidMinutes = validMin, Sequence = seq, Note = note,
            };
            var pb = p.ToBytes();
            return Frame.Build(pb, signers.Select(s => new SignatureEntry(s.id, Ed25519.Sign(Seed(s.seed), pb))).ToList());
        }

        List<Scenario> BuildScenarios()
        {
            var list = new List<Scenario>
            {
                new Scenario { Label = "Alarm lotniczy – Wojewoda Mazowiecki, Warszawa", Expect = "ZWERYFIKOWANO",
                    Build = (now, seq) => Signed(3, AlertTypes.AirRaid, 1465, now, 60, seq, "Schron: metro Świętokrzyska", (3, "3")) },
                new Scenario { Label = "Ewakuacja – 2 podpisy (Wojewoda + PSP)", Expect = "ZWERYFIKOWANO",
                    Build = (now, seq) => Signed(3, AlertTypes.Evacuation, 1465, now, 240, seq, "Kierunek: Grodzisk Maz.", (3, "3"), (5, "5")) },
                new Scenario { Label = "Ewakuacja – tylko 1 podpis", Expect = "NIEPEŁNY PODPIS",
                    Build = (now, seq) => Signed(3, AlertTypes.Evacuation, 1465, now, 240, seq, "Kierunek: Grodzisk Maz.", (3, "3")) },
                new Scenario { Label = "ATAK A1: haker podszywa się pod Dowództwo Operacyjne", Expect = "FAŁSZYWKA (podpis nie pasuje)",
                    Build = (now, seq) => Signed(1, AlertTypes.General, 0, now, 120, seq, "Mobilizacja 200 tys. rezerwistów", (1, "hacker")) },
                new Scenario { Label = "ATAK: Prezydent Warszawy wydaje dla Krakowa", Expect = "FAŁSZYWKA (brak uprawnień)",
                    Build = (now, seq) => Signed(6, AlertTypes.AirRaid, 1261, now, 60, seq, "", (6, "6")) },
                new Scenario { Label = "ATAK A3: prawdziwy, ale sprzed 3 dni (powtórka)", Expect = "NIEAKTUALNY",
                    Build = (now, seq) => Signed(1, AlertTypes.AirRaid, 1465, now - 3 * 86400, 120, seq, "Schron: metro Świętokrzyska", (1, "1")) },
                new Scenario { Label = "Komunikat dla Krakowa (Dowództwo Operacyjne)", Expect = "INNY OBSZAR (dla użytkownika z Warszawy)",
                    Build = (now, seq) => Signed(1, AlertTypes.WaterContamination, 1261, now, 600, seq, "Nie pij wody z kranu", (1, "1")) },
                new Scenario { Label = "Skażenie chemiczne – PSP Mazowsze, woj. mazowieckie", Expect = "ZWERYFIKOWANO",
                    Build = (now, seq) => Signed(5, AlertTypes.Chemical, 14, now, 180, seq, "Pożar zakładu w Płocku", (5, "5")) },
                new Scenario { Label = "ROOT unieważnia klucz Prezydenta Warszawy (6)", Expect = "ZWERYFIKOWANO, potem klucz 6 odrzucany",
                    // dopisek KEY_REVOKE = u16 ID odwoływanego wydawcy (PROTOCOL.md §2)
                    Build = (now, seq) => SignedRaw(0, AlertTypes.KeyRevoke, 0, now, 60 * 24 * 365, seq, new byte[] { 0, 6 }, (0, "0")) },
            };

            foreach (var kv in (Dictionary<string, object>)vectors["vectors"])
            {
                var v = (Dictionary<string, object>)kv.Value;
                var hex = (string)v["frame_hex"];
                list.Add(new Scenario
                {
                    Label = "Wektor " + kv.Key + " (włącz zegar testowy: 5× logo)",
                    Expect = (string)v["expected_status"] + " " + v["reason"],
                    Build = (now, seq) => Bytes.FromHex(hex),
                });
            }
            return list;
        }
    }
}
