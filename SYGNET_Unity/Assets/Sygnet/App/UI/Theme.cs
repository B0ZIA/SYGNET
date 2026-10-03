using Sygnet.Core;
using TMPro;
using UnityEngine;

namespace Sygnet.App.UI
{
    /// <summary>Style tekstu – jedyna skala typografii w aplikacji.</summary>
    public enum TextStyle
    {
        Display,     // tytuł statusu: ZWERYFIKOWANO
        Title,       // nazwa typu komunikatu, tytuł ekranu
        Headline,    // instrukcja „Co robić”, status nasłuchu
        Body,
        BodyStrong,
        Caption,     // podpowiedzi, metadane
        Overline,    // etykiety sekcji: CO ROBIĆ
        Mono,        // odciski kluczy, godziny, bajty
        Button,
        Wordmark,    // SYGNET w nagłówku
    }

    /// <summary>
    /// System wizualny SYGNET: monochrom jak logo (biel na prawie czerni), kolor wyłącznie dla statusów.
    /// Inter (tekst) + JetBrains Mono (dane techniczne), OFL – Assets/Sygnet/Fonts.
    /// Rozmiary w jednostkach kanwy (szerokość referencyjna 1080).
    /// </summary>
    public static class Theme
    {
        // ── kolory ──
        public static readonly Color Bg = Hex("#0A0C0F");          // = tło logo
        public static readonly Color Surface = Hex("#14181E");     // karty
        public static readonly Color Surface2 = Hex("#1D232B");    // przyciski drugorzędne, chipy
        public static readonly Color Line = Hex("#2A313B");        // obrysy, tor postępu
        public static readonly Color Text = Hex("#ECF0F4");
        public static readonly Color Muted = Hex("#8C97A3");
        public static readonly Color Dim = Hex("#5B6570");

        public static readonly Color Verified = Hex("#0E7A3E");
        public static readonly Color OtherArea = Hex("#1F4E8C");
        public static readonly Color Expired = Hex("#B7791F");
        public static readonly Color Danger = Hex("#B42318");

        // ── odstępy i kształty ──
        public const float Margin = 56;
        public const float Gap = 32;
        public const float RadiusCard = 32;
        public const float ButtonHeight = 156;

        // ── fonty ──
        static TMP_FontAsset regular, semiBold, bold, extraBold, mono, monoBold;
        public static TMP_FontAsset Regular => regular ??= Load("Inter-Regular");
        public static TMP_FontAsset SemiBold => semiBold ??= Load("Inter-SemiBold");
        public static TMP_FontAsset Bold => bold ??= Load("Inter-Bold");
        public static TMP_FontAsset ExtraBold => extraBold ??= Load("Inter-ExtraBold");
        public static TMP_FontAsset Mono => mono ??= Load("JetBrainsMono-Medium");
        public static TMP_FontAsset MonoBold => monoBold ??= Load("JetBrainsMono-Bold");

        static TMP_FontAsset Load(string name) =>
            Resources.Load<TMP_FontAsset>("Fonts/" + name + " SDF") ?? TMP_Settings.defaultFontAsset;

        /// <summary>Krój, rozmiar, rozstrzelenie (em/100), wielkie litery, interlinia (em/100).</summary>
        public static (TMP_FontAsset font, float size, float tracking, bool upper, float leading) Style(TextStyle s)
        {
            switch (s)
            {
                case TextStyle.Display: return (ExtraBold, 96, 2, true, 0);
                case TextStyle.Title: return (Bold, 62, -1, false, 0);
                case TextStyle.Headline: return (SemiBold, 50, -0.5f, false, 4);
                case TextStyle.Body: return (Regular, 44, 0, false, 8);
                case TextStyle.BodyStrong: return (SemiBold, 44, 0, false, 8);
                case TextStyle.Caption: return (Regular, 38, 0, false, 6);
                case TextStyle.Overline: return (Bold, 30, 14, true, 0);
                case TextStyle.Mono: return (Mono, 38, 0, false, 0);
                case TextStyle.Button: return (SemiBold, 46, 0, false, 0);
                case TextStyle.Wordmark: return (Bold, 52, 34, true, 0);
                default: return (Regular, 44, 0, false, 0);
            }
        }

        public static Color ForStatus(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified: return Verified;
                case VerifyStatus.VerifiedOtherArea: return OtherArea;
                case VerifyStatus.Expired: return Expired;
                case VerifyStatus.Incomplete:
                case VerifyStatus.Forged: return Danger;
                default: return Surface;
            }
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
