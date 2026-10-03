using Sygnet.Core;
using UnityEngine;

namespace Sygnet.App.UI
{
    /// <summary>Kolory i rozmiary wg CLIENT_UNITY.md §5: ciemny, wysoki kontrast, duże fonty.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("#0B0F14");
        public static readonly Color Card = Hex("#151B23");
        public static readonly Color CardBorder = Hex("#243040");
        public static readonly Color Text = Hex("#E6EDF3");
        public static readonly Color Muted = Hex("#8B98A5");
        public static readonly Color Accent = Hex("#3FB950");
        public static readonly Color Primary = Hex("#2F81F7");

        public static readonly Color Verified = Hex("#0E7A3E");
        public static readonly Color OtherArea = Hex("#1F4E8C");
        public static readonly Color Expired = Hex("#B7791F");
        public static readonly Color Danger = Hex("#B42318");

        // Rozmiary w jednostkach kanwy (szerokość referencyjna 1080). 18 sp ≈ 46 j. na typowym telefonie.
        public const float TextSmall = 40;
        public const float TextBody = 48;
        public const float TextLarge = 60;
        public const float TextTitle = 108;
        public const float Padding = 56;
        public const float Radius = 36;

        public static Color ForStatus(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified: return Verified;
                case VerifyStatus.VerifiedOtherArea: return OtherArea;
                case VerifyStatus.Expired: return Expired;
                case VerifyStatus.Incomplete:
                case VerifyStatus.Forged: return Danger;
                default: return Card;
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
