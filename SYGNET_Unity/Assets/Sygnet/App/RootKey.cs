using System;

namespace Sygnet.App
{
    /// <summary>
    /// WBUDOWANY klucz publiczny ROOT (pinning, PROTOCOL.md §5.1). W aplikacji nie ma żadnego klucza prywatnego.
    /// </summary>
    public static class RootKey
    {
        // TODO U6: podmienić na klucz z `php artisan sygnet:init` (razem z Resources/sygnet_trust_store.json).
        // Na razie ROOT z testvectors.json (seed 0x02×32) – NIE produkcyjny.
        public const string PublicKeyB64 = "gTl3Dqh9F19Wo1Rmw0x+zMuNipG07jeiXfYPW4/Js5Q=";
        public const bool IsTestKey = true;

        /// <summary>Odcisk do pokazania na ekranie „O aplikacji” i porównania z wydrukiem w urzędzie.</summary>
        public const string Fingerprint = "6A38-03D5-F059-902A";

        public static byte[] PublicKey => Convert.FromBase64String(PublicKeyB64);
    }
}
