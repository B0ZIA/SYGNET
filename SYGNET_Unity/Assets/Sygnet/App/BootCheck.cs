using System;
using System.Text;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App
{
    /// <summary>
    /// Kamień milowy U1: pusta aplikacja, która na telefonie dowodzi, że IL2CPP nie wyciął BouncyCastle
    /// (Ed25519 z seeda testowego ROOT daje odcisk 6A38-03D5-F059-902A z PROTOCOL.md §9).
    /// Zostanie zastąpiona przez SygnetApp w U4.
    /// </summary>
    public class BootCheck : MonoBehaviour
    {
        // Klucz publiczny ROOT z testvectors.json (seed 0x02×32). Tylko do testów.
        const string TestRootPubB64 = "gTl3Dqh9F19Wo1Rmw0x+zMuNipG07jeiXfYPW4/Js5Q=";
        const string TestRootFingerprint = "6A38-03D5-F059-902A";

        static readonly Color Bg = Hex("#0B0F14");
        static readonly Color Card = Hex("#151B23");
        static readonly Color Text = Hex("#E6EDF3");

        void Start()
        {
            var report = RunSelfTest(out bool ok);
            Debug.Log("[SYGNET] U1 self-test " + (ok ? "OK" : "FAIL") + "\n" + report);
            BuildUi(report, ok);
        }

        static string RunSelfTest(out bool ok)
        {
            var sb = new StringBuilder();
            ok = true;
            try
            {
                var seed = Fill(0x02, 32);
                var pub = Ed25519.PublicKeyFromSeed(seed);
                bool pubOk = Convert.ToBase64String(pub) == TestRootPubB64;
                var fp = Ed25519.Fingerprint(pub);
                bool fpOk = fp == TestRootFingerprint;
                Line(sb, pubOk && fpOk, "Klucz ROOT (testowy): " + fp);

                var msg = Encoding.UTF8.GetBytes("Schron: metro Świętokrzyska");
                var sig = Ed25519.Sign(seed, msg);
                bool verOk = Ed25519.Verify(pub, msg, sig);
                Line(sb, verOk, "Podpis Ed25519 i weryfikacja");

                msg[0] ^= 1;
                bool tamperOk = !Ed25519.Verify(pub, msg, sig);
                Line(sb, tamperOk, "Zmieniony bajt → podpis odrzucony");

                ok = pubOk && fpOk && verOk && tamperOk;
            }
            catch (Exception e)
            {
                ok = false;
                sb.AppendLine("<color=#FF6B6B>BŁĄD: " + e.GetType().Name + ": " + e.Message + "</color>");
            }

            sb.AppendLine();
            sb.AppendLine("<size=70%><color=#8B98A5>Unity " + Application.unityVersion + " · " + Application.platform +
                          " · " + IntPtr.Size * 8 + "-bit");
            sb.AppendLine("Wyjście audio: " + AudioSettings.outputSampleRate + " Hz</color></size>");
            return sb.ToString();
        }

        void BuildUi(string report, bool ok)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Bg;
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;

            var safe = Rect(canvasGo.transform, "Safe");
            var sa = Screen.safeArea;
            safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            var title = Label(safe, "SYGNET", 120, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.anchoredPosition = new Vector2(0, -120);
            title.rectTransform.sizeDelta = new Vector2(-96, 160);
            title.alignment = TextAlignmentOptions.Center;

            var sub = Label(safe, "Podpisane komunikaty kryzysowe · offline", 40, FontStyles.Normal);
            sub.color = Hex("#8B98A5");
            sub.rectTransform.anchorMin = new Vector2(0, 1);
            sub.rectTransform.anchorMax = new Vector2(1, 1);
            sub.rectTransform.pivot = new Vector2(0.5f, 1);
            sub.rectTransform.anchoredPosition = new Vector2(0, -290);
            sub.rectTransform.sizeDelta = new Vector2(-96, 60);
            sub.alignment = TextAlignmentOptions.Center;

            var card = Rect(safe, "Card");
            card.gameObject.AddComponent<Image>().color = Card;
            card.anchorMin = new Vector2(0, 0.25f);
            card.anchorMax = new Vector2(1, 0.75f);
            card.offsetMin = new Vector2(48, 0);
            card.offsetMax = new Vector2(-48, 0);

            var body = Label(card, report, 44, FontStyles.Normal);
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(48, 48);
            body.rectTransform.offsetMax = new Vector2(-48, -48);
            body.alignment = TextAlignmentOptions.TopLeft;

            var status = Label(safe, ok ? "ROZRUCH OK" : "BŁĄD ROZRUCHU", 64, FontStyles.Bold);
            status.color = ok ? Hex("#3FB950") : Hex("#FF6B6B");
            status.rectTransform.anchorMin = new Vector2(0, 0);
            status.rectTransform.anchorMax = new Vector2(1, 0);
            status.rectTransform.pivot = new Vector2(0.5f, 0);
            status.rectTransform.anchoredPosition = new Vector2(0, 160);
            status.rectTransform.sizeDelta = new Vector2(-96, 100);
            status.alignment = TextAlignmentOptions.Center;
        }

        static void Line(StringBuilder sb, bool ok, string text)
        {
            sb.Append(ok ? "<color=#3FB950>OK</color>   " : "<color=#FF6B6B>BŁĄD</color>   ");
            sb.AppendLine(text);
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static TextMeshProUGUI Label(Transform parent, string text, float size, FontStyles style)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Text;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        static byte[] Fill(byte b, int n)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = b;
            return a;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
