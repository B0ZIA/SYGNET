<?php

namespace App\Sygnet;

use RuntimeException;

/**
 * Seedy Ed25519 (32 B, base64) w storage/app/keys/{id}.seed, uprawnienia 0600, katalog poza gitem.
 * Klucze prywatne nigdy nie opuszczają backendu. Produkcyjnie: HSM / karta kryptograficzna u każdego operatora.
 * Alias „hacker” to klucz spoza trust store – tylko dla laboratorium ataków.
 */
final class KeyStore
{
    public const ROOT = 0;

    public const HACKER = 'hacker';

    public function __construct(
        private readonly string $directory
    ) {}

    public static function fromConfig(): self
    {
        return new self(config('sygnet.paths.keys', storage_path('app/keys')));
    }

    public function directory(): string
    {
        return $this->directory;
    }

    public function seedPath(int|string $keyId): string
    {
        return $this->directory.DIRECTORY_SEPARATOR.$keyId.'.seed';
    }

    public function has(int|string $keyId): bool
    {
        return is_file($this->seedPath($keyId));
    }

    public function loadSeed(int|string $keyId): string
    {
        $path = $this->seedPath($keyId);

        if (! is_file($path)) {
            throw new RuntimeException("Brak klucza {$keyId}. Uruchom: php artisan sygnet:init");
        }

        $seed = base64_decode(trim((string) file_get_contents($path)), true);

        if ($seed === false || strlen($seed) !== SODIUM_CRYPTO_SIGN_SEEDBYTES) {
            throw new RuntimeException("Uszkodzony klucz {$keyId}.");
        }

        return $seed;
    }

    public function publicKey(int|string $keyId): string
    {
        return self::publicKeyFromSeed($this->loadSeed($keyId));
    }

    public function sign(int|string $keyId, string $message): string
    {
        $keypair = sodium_crypto_sign_seed_keypair($this->loadSeed($keyId));
        $signature = sodium_crypto_sign_detached($message, sodium_crypto_sign_secretkey($keypair));
        sodium_memzero($keypair);

        return $signature;
    }

    public function fingerprint(int|string $keyId): string
    {
        return self::fingerprintOf($this->publicKey($keyId));
    }

    public function saveSeed(int|string $keyId, string $seed): void
    {
        if (strlen($seed) !== SODIUM_CRYPTO_SIGN_SEEDBYTES) {
            throw new RuntimeException('Seed musi mieć dokładnie 32 bajty.');
        }

        if (! is_dir($this->directory)) {
            mkdir($this->directory, 0700, true);
        }

        $path = $this->seedPath($keyId);
        file_put_contents($path, base64_encode($seed).PHP_EOL, LOCK_EX);
        @chmod($path, 0600);
    }

    public static function publicKeyFromSeed(string $seed): string
    {
        return sodium_crypto_sign_publickey(sodium_crypto_sign_seed_keypair($seed));
    }

    /** Odcisk: pierwsze 8 B SHA-256(pubkey), hex wielkimi literami w grupach po 4 (PROTOCOL.md §1). */
    public static function fingerprintOf(string $publicKey): string
    {
        return implode('-', str_split(strtoupper(substr(hash('sha256', $publicKey), 0, 16)), 4));
    }
}
