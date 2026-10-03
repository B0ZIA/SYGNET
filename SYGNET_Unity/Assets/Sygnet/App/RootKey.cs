// WBUDOWANY klucz publiczny ROOT (pinning, PROTOCOL.md §5.1). W aplikacji nie ma żadnego klucza prywatnego.
// Format dokładnie jak eksport `php artisan sygnet:init` (CONSOLE_LARAVEL.md §5.1): plik z konsoli
// (storage/app/export/RootKey.cs) podmienia ten plik 1:1, razem z Resources/sygnet_trust_store.json.
// Obecnie: ROOT z testvectors.json (seed 0x02×32) – NIE produkcyjny.
public static class RootKey {
    public static readonly byte[] Public = System.Convert.FromBase64String("gTl3Dqh9F19Wo1Rmw0x+zMuNipG07jeiXfYPW4/Js5Q=");
    public const string Fingerprint = "6A38-03D5-F059-902A";
}
