<?php

namespace App\Sygnet;

use RuntimeException;
use Throwable;

/**
 * Plik sygnet_trust_store.json (PROTOCOL.md §5.4) widziany tak jak przez aplikację: każdy certyfikat sprawdzany
 * kluczem ROOT, odrzucone są logowane, a nazwa i zakres pochodzą wyłącznie z bajtów certyfikatu.
 */
final class TrustStore
{
    /** @var array<int, Certificate> */
    private array $certificates = [];

    /** @var string[] */
    private array $rejected = [];

    private function __construct(
        private readonly string $rootPublicKey,
        private readonly array $json,
    ) {}

    public static function fromArray(array $json, string $rootPublicKey): self
    {
        if (($json['version'] ?? null) !== 1) {
            throw new RuntimeException('Nieobsługiwana wersja trust store.');
        }

        $store = new self($rootPublicKey, $json);

        foreach ($json['issuers'] ?? [] as $i => $entry) {
            try {
                $bytes = base64_decode($entry['cert_b64'] ?? '', true);
                $sig = base64_decode($entry['root_sig_b64'] ?? '', true);
                if ($bytes === false || $sig === false || strlen($sig) !== SODIUM_CRYPTO_SIGN_BYTES
                    || ! sodium_crypto_sign_verify_detached($sig, $bytes, $rootPublicKey)) {
                    $store->rejected[] = "wpis {$i}: zły podpis ROOT";

                    continue;
                }
                $cert = Certificate::fromBytes($bytes);
                $store->certificates[$cert->issuerId] = $cert;
            } catch (Throwable $e) {
                $store->rejected[] = "wpis {$i}: ".$e->getMessage();
            }
        }

        return $store;
    }

    public static function fromFile(string $path, string $rootPublicKey): self
    {
        if (! is_file($path)) {
            throw new RuntimeException("Brak trust store: {$path}. Uruchom: php artisan sygnet:init");
        }

        return self::fromArray(json_decode(file_get_contents($path), true, 512, JSON_THROW_ON_ERROR), $rootPublicKey);
    }

    public function rootPublicKey(): string
    {
        return $this->rootPublicKey;
    }

    public function rootFingerprint(): string
    {
        return KeyStore::fingerprintOf($this->rootPublicKey);
    }

    /** Odcisk zapisany w pliku – musi się zgadzać z faktycznym kluczem ROOT. */
    public function declaredRootFingerprint(): string
    {
        return (string) ($this->json['root_fingerprint'] ?? '');
    }

    public function hasIssuer(int $issuerId): bool
    {
        return isset($this->certificates[$issuerId]);
    }

    public function certificate(int $issuerId): ?Certificate
    {
        return $this->certificates[$issuerId] ?? null;
    }

    /** @return array<int, Certificate> */
    public function certificates(): array
    {
        ksort($this->certificates);

        return $this->certificates;
    }

    /** @return string[] */
    public function rejected(): array
    {
        return $this->rejected;
    }

    public function nameOf(int $issuerId): ?string
    {
        if ($issuerId === KeyStore::ROOT) {
            return config('sygnet.issuers.0.name');
        }

        return $this->certificates[$issuerId]->name ?? null;
    }

    public function toArray(): array
    {
        return $this->json;
    }
}
