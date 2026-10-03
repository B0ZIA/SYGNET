<?php

namespace App\Sygnet;

use RuntimeException;

final class TrustStore
{
    /**
     * @var array<int, array{certificate: Certificate, rootSignature: string}>
     */
    private array $issuers = [];

    private function __construct(
        private readonly string $rootFingerprint,
        private readonly string $rootPublicKey,
    ) {
    }

    public static function fromFile(
        string $path,
        string $rootPublicKey
    ): self {
        if (!is_file($path)) {
            throw new RuntimeException(
                "Trust store not found: {$path}"
            );
        }

        $data = json_decode(
            file_get_contents($path),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        if (($data['version'] ?? null) !== 1) {
            throw new RuntimeException('Unsupported trust-store version.');
        }

        $store = new self(
            rootFingerprint: (string) ($data['root_fingerprint'] ?? ''),
            rootPublicKey: $rootPublicKey,
        );

        foreach ($data['issuers'] ?? [] as $entry) {
            $certificate = Certificate::fromBase64(
                $entry['cert_b64']
            );

            $rootSignature = base64_decode(
                $entry['root_sig_b64'],
                true
            );

            if (
                $rootSignature === false ||
                strlen($rootSignature) !== SODIUM_CRYPTO_SIGN_BYTES
            ) {
                throw new RuntimeException(
                    "Invalid ROOT signature for issuer {$certificate->issuerId}."
                );
            }

            $store->issuers[$certificate->issuerId] = [
                'certificate' => $certificate,
                'rootSignature' => $rootSignature,
            ];
        }

        return $store;
    }

    public function rootFingerprint(): string
    {
        return $this->rootFingerprint;
    }

    public function hasIssuer(int $issuerId): bool
    {
        return isset($this->issuers[$issuerId]);
    }

    public function certificate(int $issuerId): ?Certificate
    {
        return $this->issuers[$issuerId]['certificate'] ?? null;
    }

    public function rootSignature(int $issuerId): ?string
    {
        return $this->issuers[$issuerId]['rootSignature'] ?? null;
    }

    public function verifyCertificate(int $issuerId): bool
    {
        $entry = $this->issuers[$issuerId] ?? null;

        if ($entry === null) {
            return false;
        }

        return sodium_crypto_sign_verify_detached(
            $entry['rootSignature'],
            $entry['certificate']->toBytes(),
            $this->rootPublicKey
        );
    }

    public function isCertificateValid(
        int $issuerId,
        int $timestamp
    ): bool {
        $certificate = $this->certificate($issuerId);

        if ($certificate === null) {
            return false;
        }

        return $timestamp >= $certificate->validFrom
            && $timestamp <= $certificate->validUntil;
    }
}
