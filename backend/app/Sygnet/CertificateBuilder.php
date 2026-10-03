<?php

namespace App\Sygnet;

use InvalidArgumentException;

final class CertificateBuilder
{
    public function __construct(
        private readonly KeyStore $keys
    ) {
    }

    /**
     * Builds a certificate signed by ROOT (issuer 0).
     *
     * @param int[] $areas
     * @return array{cert: Certificate, cert_b64: string, root_sig_b64: string}
     */
    public function build(
        int $issuerId,
        string $name,
        array $areas,
        int $validFrom,
        int $validUntil,
    ): array {
        if ($issuerId < 0 || $issuerId > 0xFFFF) {
            throw new InvalidArgumentException('Invalid issuer ID.');
        }

        if ($validUntil < $validFrom) {
            throw new InvalidArgumentException(
                'Certificate expiration precedes validity start.'
            );
        }

        $certificate = new Certificate(
            version: 1,
            issuerId: $issuerId,
            publicKey: $this->keys->publicKey($issuerId),
            validFrom: $validFrom,
            validUntil: $validUntil,
            areas: array_map('intval', $areas),
            name: $name,
        );

        $certBytes = $certificate->toBytes();

        $rootSignature = $this->keys->sign(
            0,
            $certBytes
        );

        return [
            'cert' => $certificate,
            'cert_b64' => base64_encode($certBytes),
            'root_sig_b64' => base64_encode($rootSignature),
        ];
    }
}
