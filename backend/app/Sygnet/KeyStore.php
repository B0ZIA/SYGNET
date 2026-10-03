<?php

namespace App\Sygnet;

use RuntimeException;

final class KeyStore
{
    public function __construct(
        private readonly string $directory
    ) {
    }

    public static function fromConfig(): self
    {
        return new self(
            storage_path('app/keys')
        );
    }

    public function seedPath(int|string $issuerId): string
    {
        return $this->directory . '/' . $issuerId . '.seed';
    }

    public function loadSeed(int|string $issuerId): string
    {
        $path = $this->seedPath($issuerId);

        if (!is_file($path)) {
            throw new RuntimeException("Missing seed for issuer {$issuerId}.");
        }

        $encoded = trim((string) file_get_contents($path));

        $seed = base64_decode($encoded, true);

        if ($seed === false || strlen($seed) !== SODIUM_CRYPTO_SIGN_SEEDBYTES) {
            throw new RuntimeException("Invalid seed for issuer {$issuerId}.");
        }

        return $seed;
    }

    public function keypair(int|string $issuerId): string
    {
        return sodium_crypto_sign_seed_keypair(
            $this->loadSeed($issuerId)
        );
    }

    public function publicKey(int|string $issuerId): string
    {
        return sodium_crypto_sign_publickey(
            $this->keypair($issuerId)
        );
    }

    public function sign(int|string $issuerId, string $message): string
    {
        return sodium_crypto_sign_detached(
            $message,
            sodium_crypto_sign_secretkey(
                $this->keypair($issuerId)
            )
        );
    }

    public function fingerprint(int|string $issuerId): string
    {
        $hex = strtoupper(
            substr(
                hash('sha256', $this->publicKey($issuerId)),
                0,
                16
            )
        );

        return implode('-', str_split($hex, 4));
    }

    public function saveSeed(int|string $issuerId, string $seed): void
    {
        if (strlen($seed) !== SODIUM_CRYPTO_SIGN_SEEDBYTES) {
            throw new RuntimeException('Seed must contain exactly 32 bytes.');
        }

        if (!is_dir($this->directory)) {
            mkdir($this->directory, 0700, true);
        }

        $path = $this->seedPath($issuerId);

        file_put_contents(
            $path,
            base64_encode($seed) . PHP_EOL,
            LOCK_EX
        );

        @chmod($path, 0600);
    }
}
