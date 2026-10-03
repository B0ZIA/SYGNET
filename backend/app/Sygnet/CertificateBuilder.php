<?php

namespace App\Sygnet;

/**
 * Certyfikat wydawcy podpisany kluczem ROOT (PROTOCOL.md §5.3) → wpis trust store (§5.4).
 */
final class CertificateBuilder
{
    public function __construct(
        private readonly KeyStore $keys
    ) {}

    /**
     * @param  int[]  $scopes
     * @return array{cert: Certificate, cert_b64: string, root_sig_b64: string}
     */
    public function build(int $issuerId, string $name, array $scopes, int $validFrom, int $validUntil): array
    {
        $certificate = new Certificate(
            issuerId: $issuerId,
            publicKey: $this->keys->publicKey($issuerId),
            validFrom: $validFrom,
            validUntil: $validUntil,
            scopes: array_map('intval', $scopes),
            name: $name,
        );

        $bytes = $certificate->toBytes();

        return [
            'cert' => $certificate,
            'cert_b64' => base64_encode($bytes),
            'root_sig_b64' => base64_encode($this->keys->sign(KeyStore::ROOT, $bytes)),
        ];
    }
}
