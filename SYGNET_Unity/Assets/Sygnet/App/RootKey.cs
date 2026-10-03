// WBUDOWANY klucz publiczny ROOT (pinning, PROTOCOL.md §5.1). W aplikacji nie ma żadnego klucza prywatnego.
// Format dokładnie jak eksport `php artisan sygnet:init` (CONSOLE_LARAVEL.md §5.1): plik z konsoli
// (storage/app/export/RootKey.cs) podmienia ten plik 1:1, razem z Resources/sygnet_trust_store.json.
// Klucze demo wygenerowane 2026-10-03 17:54 UTC (losowe seedy).
public static class RootKey {
    public static readonly byte[] Public = System.Convert.FromBase64String("vsWT1/uVRIYTS1U6EosjWjR0jrDJEXVlOUmmQCiop60=");
    public const string Fingerprint = "82B7-E5B3-7129-166D";
}
