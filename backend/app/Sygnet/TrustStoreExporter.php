<?php

namespace App\Sygnet;

use RuntimeException;

/**
 * Eksport dla aplikacji Unity (CONSOLE_LARAVEL.md §5.1): storage/app/export/sygnet_trust_store.json (PROTOCOL.md §5.4)
 * i RootKey.cs z wbudowanym kluczem publicznym ROOT. Oba pliki podmienia się w Unity 1:1.
 */
final class TrustStoreExporter
{
    public const TRUST_STORE_FILE = 'sygnet_trust_store.json';

    public const ROOT_KEY_FILE = 'RootKey.cs';

    public function __construct(
        private readonly KeyStore $keys,
        private readonly IssuerRegistry $issuers,
    ) {}

    public static function fromConfig(): self
    {
        return new self(KeyStore::fromConfig(), new IssuerRegistry);
    }

    public function directory(): string
    {
        return config('sygnet.paths.export', storage_path('app/export'));
    }

    public function path(string $file): string
    {
        return $this->directory().DIRECTORY_SEPARATOR.$file;
    }

    /**
     * Buduje trust store z certyfikatami wydawców, którzy mają klucz (w kolejności ID).
     */
    public function build(int $validFrom, int $validUntil): array
    {
        $builder = new CertificateBuilder($this->keys);
        $entries = [];

        foreach ($this->issuers->issuers() as $id => $issuer) {
            if (! $this->keys->has($id)) {
                continue;
            }
            $c = $builder->build($id, $issuer['name'], $issuer['scopes'], $validFrom, $validUntil);
            $entries[] = ['cert_b64' => $c['cert_b64'], 'root_sig_b64' => $c['root_sig_b64']];
        }

        return [
            'version' => 1,
            'root_fingerprint' => $this->keys->fingerprint(KeyStore::ROOT),
            'issuers' => $entries,
        ];
    }

    /** @return array{trust_store: string, root_key: string} ścieżki zapisanych plików */
    public function export(int $validFrom, int $validUntil, string $note): array
    {
        $json = $this->build($validFrom, $validUntil);

        if (! is_dir($this->directory())) {
            mkdir($this->directory(), 0700, true);
        }

        file_put_contents(
            $this->path(self::TRUST_STORE_FILE),
            json_encode($json, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES).PHP_EOL,
            LOCK_EX,
        );
        file_put_contents($this->path(self::ROOT_KEY_FILE), $this->rootKeyCs($note), LOCK_EX);

        return ['trust_store' => $this->path(self::TRUST_STORE_FILE), 'root_key' => $this->path(self::ROOT_KEY_FILE)];
    }

    /** Klasa C# z kluczem publicznym ROOT – ten sam format co Assets/Sygnet/App/RootKey.cs w Unity. */
    public function rootKeyCs(string $note): string
    {
        $pub = base64_encode($this->keys->publicKey(KeyStore::ROOT));
        $fp = $this->keys->fingerprint(KeyStore::ROOT);

        return <<<CS
        // WBUDOWANY klucz publiczny ROOT (pinning, PROTOCOL.md §5.1). W aplikacji nie ma żadnego klucza prywatnego.
        // Format dokładnie jak eksport `php artisan sygnet:init` (CONSOLE_LARAVEL.md §5.1): plik z konsoli
        // (storage/app/export/RootKey.cs) podmienia ten plik 1:1, razem z Resources/sygnet_trust_store.json.
        // {$note}
        public static class RootKey {
            public static readonly byte[] Public = System.Convert.FromBase64String("{$pub}");
            public const string Fingerprint = "{$fp}";
        }

        CS;
    }

    /** Trust store, który dostała aplikacja – do kontrolnej weryfikacji ramek w konsoli. */
    public function load(): TrustStore
    {
        if (! $this->keys->has(KeyStore::ROOT)) {
            throw new RuntimeException('Brak kluczy. Uruchom: php artisan sygnet:init');
        }

        return TrustStore::fromFile($this->path(self::TRUST_STORE_FILE), $this->keys->publicKey(KeyStore::ROOT));
    }
}
