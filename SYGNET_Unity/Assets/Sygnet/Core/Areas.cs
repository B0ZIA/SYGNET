using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>Kody obszarów oparte na TERYT (PROTOCOL.md §6).</summary>
    public static class Areas
    {
        public const int WholeCountry = 0;

        /// <summary>Czy obszar <paramref name="c"/> obejmuje obszar <paramref name="a"/>.</summary>
        public static bool Covers(int c, int a) => c == 0 || c == a || (c < 100 && a / 100 == c);

        /// <summary>Lista obszarów na demo (kolejność do wyboru w onboardingu).</summary>
        public static readonly IReadOnlyList<KeyValuePair<int, string>> Demo = new[]
        {
            new KeyValuePair<int, string>(1261, "Kraków"),
            new KeyValuePair<int, string>(1465, "Warszawa"),
            new KeyValuePair<int, string>(12, "woj. małopolskie"),
            new KeyValuePair<int, string>(14, "woj. mazowieckie"),
            new KeyValuePair<int, string>(0, "Cała Polska"),
        };

        public static string Name(int code)
        {
            foreach (var kv in Demo)
                if (kv.Key == code) return kv.Value;
            if (code < 100) return "województwo " + code.ToString("00");
            return "powiat " + code.ToString("0000");
        }
    }
}
