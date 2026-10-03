using System;
using System.Text;
using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App
{
    /// <summary>
    /// Ekran rozruchowy U1–U2: na telefonie dowodzi, że IL2CPP nie wyciął BouncyCastle i że Sygnet.Core
    /// (TrustStore z Resources + Verifier) daje te same wyniki co testy EditMode.
    /// Zostanie zastąpiony przez SygnetApp w U4.
    /// </summary>
    public class BootCheck : MonoBehaviour
    {
        // QR wektorów TV1 (VERIFIED) i TV3 (FORGED BAD_SIGNATURE:1) z testvectors.json; now_for_tests, user_area 1465.
        const string Tv1Qr = "SYG1:U0cAcgEAAQEFuWrBp-AAeAABAB1TY2hyb246IG1ldHJvIMWad2nEmXRva3J6eXNrYQEAAWlOyixH4xDKCe3jR-e4a4reIZcc6iB1viusomY6oW-4mGmvg5bbDme4SEksA9ed5dkZCxNeiDkMMtGhIi2YjQbucw";
        const string Tv3Qr = "SYG1:U0cAcgEAAQEFuWrBp-AAeAABAB1TY2hyb246IG1ldHJvIMWad2nEmXRva3J6eXNrYQEAAdHBYoZyeVSsMYW_7OCwdUrPmqnGNRLp6vPK_vD4RpZDuwEgJAr3bWXiomv_v9fW3qKlhdM8NwNYelEZgUFZyg0Zug";
        const long NowForTests = 1791076920;

        static readonly Color Bg = Hex("#0B0F14");
        static readonly Color Card = Hex("#151B23");
        static readonly Color Text = Hex("#E6EDF3");

        void Start()
        {
            var report = RunSelfTest(out bool ok);
            Debug.Log("[SYGNET] self-test " + (ok ? "OK" : "FAIL") + "\n" + report);
            BuildUi(report, ok);
        }

        static string RunSelfTest(out bool ok)
        {
            var sb = new StringBuilder();
            ok = true;
            try
            {
                var rootPub = RootKey.PublicKey;
                var fp = Ed25519.Fingerprint(rootPub);
                bool fpOk = fp == RootKey.Fingerprint;
                Line(sb, fpOk, "Klucz ROOT" + (RootKey.IsTestKey ? " (testowy)" : "") + ": " + fp);

                var json = Resources.Load<TextAsset>("sygnet_trust_store");
                var trust = TrustStore.FromJson(json.text, rootPub);
                foreach (var why in trust.Rejected) Debug.LogWarning("[SYGNET] Odrzucony certyfikat: " + why);
                bool trustOk = trust.Issuers.Count > 0 && trust.Rejected.Count == 0;
                Line(sb, trustOk, "Zaufani wydawcy: " + trust.Issuers.Count + ", odrzuceni: " + trust.Rejected.Count);

                bool tv1Ok = CheckQr(sb, trust, Tv1Qr, "TV1", VerifyStatus.Verified, "OK");
                bool tv3Ok = CheckQr(sb, trust, Tv3Qr, "TV3", VerifyStatus.Forged, "BAD_SIGNATURE:1");

                ok = fpOk && trustOk && tv1Ok && tv3Ok;
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

        static bool CheckQr(StringBuilder sb, TrustStore trust, string qr, string label, VerifyStatus expected, string reason)
        {
            if (!Frame.TryFromQrText(qr, out var frame))
            {
                Line(sb, false, label + ": nieczytelny QR");
                return false;
            }
            var r = Verifier.Verify(frame, trust, NowForTests, 1465, null, null);
            bool ok = r.Status == expected && r.ReasonCode == reason;
            var what = r.Payload != null ? AlertTypes.Get(r.Payload.Type).Name + ", " + r.IssuerName : "";
            Line(sb, ok, label + " → " + Messages.Title(r.Status) + " <size=80%>(" + r.ReasonCode + ")</size>\n      <size=80%>" + what + "</size>");
            return ok;
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

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
