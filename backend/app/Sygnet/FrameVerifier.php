<?php

namespace App\Sygnet;

use Throwable;

final class FrameVerifier
{
    public function __construct(
        private readonly TrustStore $trustStore,
    ) {
    }

    /**
     * @return array{
     *     status: string,
     *     reason: string,
     *     issuer_id?: int,
     *     type?: int,
     *     area_code?: int,
     *     timestamp?: int,
     *     valid_minutes?: int,
     *     sequence?: int,
     *     note?: string,
     *     signers?: int[]
     * }
     */
    public function verify(
        string $frame,
        ?int $now = null,
    ): array {
        $now ??= time();

        try {
            return $this->doVerify($frame, $now);
        } catch (Throwable $e) {
            return [
                'status' => 'FORGED',
                'reason' => 'INVALID_FRAME',
            ];
        }
    }

    private function doVerify(
        string $frame,
        int $now,
    ): array {
        $length = strlen($frame);

        /*
         * SG + u16 length + payload + count +
         * at least one signer (u16 + 64) + CRC16
         */
        if ($length < 4 + 1 + 2 + 64 + 2) {
            return $this->forged('INVALID_FRAME_LENGTH');
        }

        if (substr($frame, 0, 2) !== 'SG') {
            return $this->forged('BAD_MAGIC');
        }

        $declaredLength = unpack(
            'n',
            substr($frame, 2, 2)
        )[1];

        if ($declaredLength !== $length - 4) {
            return $this->forged('FRAME_LENGTH_MISMATCH');
        }

        $withoutCrc = substr($frame, 0, -2);

        $expectedCrc = Crc16::bytes($withoutCrc);
        $actualCrc = substr($frame, -2);

        if (!hash_equals($expectedCrc, $actualCrc)) {
            return $this->forged('BAD_CRC');
        }

        /*
         * Parse payload.
         *
         * Fixed payload prefix:
         * version 1
         * issuer 2
         * type 1
         * area 2
         * timestamp 4
         * valid_minutes 2
         * sequence 2
         * reserved 1
         * note_length 1
         */
        $payloadOffset = 4;

        if ($length < $payloadOffset + 16 + 1 + 2 + 64 + 2) {
            return $this->forged('INVALID_PAYLOAD');
        }

        $version = ord($frame[$payloadOffset]);

        if ($version !== 1) {
            return $this->forged('UNSUPPORTED_VERSION');
        }

        $issuerId = unpack(
            'n',
            substr($frame, $payloadOffset + 1, 2)
        )[1];

        $type = ord($frame[$payloadOffset + 3]);

        $areaCode = unpack(
            'n',
            substr($frame, $payloadOffset + 4, 2)
        )[1];

        $timestamp = unpack(
            'N',
            substr($frame, $payloadOffset + 6, 4)
        )[1];

        $validMinutes = unpack(
            'n',
            substr($frame, $payloadOffset + 10, 2)
        )[1];

        $sequence = unpack(
            'n',
            substr($frame, $payloadOffset + 12, 2)
        )[1];

        $reserved = ord($frame[$payloadOffset + 14]);
        $noteLength = ord($frame[$payloadOffset + 15]);

        if ($reserved !== 0) {
            return $this->forged('INVALID_RESERVED');
        }

        $payloadLength = 16 + $noteLength;

        if (
            $length <
            $payloadOffset +
            $payloadLength +
            1 +
            2 +
            64 +
            2
        ) {
            return $this->forged('INVALID_PAYLOAD');
        }

        $payloadBytes = substr(
            $frame,
            $payloadOffset,
            $payloadLength
        );

        $note = substr(
            $payloadBytes,
            16,
            $noteLength
        );

        $signerOffset = $payloadOffset + $payloadLength;

        $signerCount = ord($frame[$signerOffset]);
        $signerOffset++;

        if ($signerCount < 1 || $signerCount > 2) {
            return $this->forged('INVALID_SIGNER_COUNT');
        }

        $expectedLength =
            4 +
            $payloadLength +
            1 +
            ($signerCount * (2 + 64)) +
            2;

        if ($expectedLength !== $length) {
            return $this->forged('FRAME_STRUCTURE_MISMATCH');
        }

        $signers = [];

        for ($i = 0; $i < $signerCount; $i++) {
            $signerId = unpack(
                'n',
                substr($frame, $signerOffset, 2)
            )[1];

            $signerOffset += 2;

            $signature = substr(
                $frame,
                $signerOffset,
                SODIUM_CRYPTO_SIGN_BYTES
            );

            $signerOffset += SODIUM_CRYPTO_SIGN_BYTES;

            $signers[] = [
                'id' => $signerId,
                'signature' => $signature,
            ];
        }

        /*
         * Unknown issuer.
         */
        foreach ($signers as $signer) {
            if (!$this->trustStore->hasIssuer($signer['id'])) {
                return $this->forged(
                    'UNKNOWN_ISSUER:' . $signer['id']
                );
            }

            /*
             * The certificate itself must be ROOT-signed.
             */
            if (!$this->trustStore->verifyCertificate($signer['id'])) {
                return $this->forged(
                    'BAD_CERTIFICATE:' . $signer['id']
                );
            }
        }

        /*
         * Check that the payload issuer is represented by
         * one of the actual signers.
         */
        $payloadIssuerIsSigner = false;

        foreach ($signers as $signer) {
            if ($signer['id'] === $issuerId) {
                $payloadIssuerIsSigner = true;
                break;
            }
        }

        if (!$payloadIssuerIsSigner) {
            return $this->forged(
                'ISSUER_SIGNER_MISMATCH:' . $issuerId
            );
        }

        /*
         * Verify every signature against the SAME payload.
         */
        foreach ($signers as $signer) {
            $certificate = $this->trustStore->certificate(
                $signer['id']
            );

            if ($certificate === null) {
                return $this->forged(
                    'UNKNOWN_ISSUER:' . $signer['id']
                );
            }

            $valid = sodium_crypto_sign_verify_detached(
                $signer['signature'],
                $payloadBytes,
                $certificate->publicKey
            );

            if (!$valid) {
                return $this->forged(
                    'BAD_SIGNATURE:' . $signer['id']
                );
            }
        }

        /*
         * Certificate validity.
         */
        foreach ($signers as $signer) {
            if (
                !$this->trustStore->isCertificateValid(
                    $signer['id'],
                    $timestamp
                )
            ) {
                return $this->forged(
                    'CERTIFICATE_EXPIRED:' . $signer['id']
                );
            }
        }

        /*
         * Message validity.
         *
         * timestamp <= now <= timestamp + validMinutes*60
         */
        $expiresAt = $timestamp + ($validMinutes * 60);

        if ($now > $expiresAt) {
            return [
                'status' => 'EXPIRED',
                'reason' => 'MESSAGE_EXPIRED',
                'issuer_id' => $issuerId,
                'type' => $type,
                'area_code' => $areaCode,
                'timestamp' => $timestamp,
                'valid_minutes' => $validMinutes,
                'sequence' => $sequence,
                'note' => $note,
                'signers' => array_column($signers, 'id'),
            ];
        }

        if ($now < $timestamp) {
            return $this->forged('MESSAGE_NOT_YET_VALID');
        }

        /*
         * Area authorization.
         *
         * Every signer must be authorized for the target area.
         */
        foreach ($signers as $signer) {
            $certificate = $this->trustStore->certificate(
                $signer['id']
            );

            if (
                !Areas::covers(
                    $certificate->areas,
                    $areaCode
                )
            ) {
                return $this->forged(
                    'UNAUTHORIZED_AREA:' . $signer['id']
                );
            }
        }

        /*
         * Critical alerts require two independent signers.
         */
        if ($this->requiresDualSignature($type)) {
            if ($signerCount < 2) {
                return [
                    'status' => 'INCOMPLETE',
                    'reason' => 'DUAL_SIGNATURE_REQUIRED',
                    'issuer_id' => $issuerId,
                    'type' => $type,
                    'area_code' => $areaCode,
                    'timestamp' => $timestamp,
                    'valid_minutes' => $validMinutes,
                    'sequence' => $sequence,
                    'note' => $note,
                    'signers' => array_column($signers, 'id'),
                ];
            }

            if ($signers[0]['id'] === $signers[1]['id']) {
                return [
                    'status' => 'INCOMPLETE',
                    'reason' => 'DUAL_SIGNATURE_REQUIRED',
                    'issuer_id' => $issuerId,
                    'type' => $type,
                    'area_code' => $areaCode,
                    'timestamp' => $timestamp,
                    'valid_minutes' => $validMinutes,
                    'sequence' => $sequence,
                    'note' => $note,
                    'signers' => array_column($signers, 'id'),
                ];
            }
        }

        return [
            'status' => 'VERIFIED',
            'reason' => 'OK',
            'issuer_id' => $issuerId,
            'type' => $type,
            'area_code' => $areaCode,
            'timestamp' => $timestamp,
            'valid_minutes' => $validMinutes,
            'sequence' => $sequence,
            'note' => $note,
            'signers' => array_column($signers, 'id'),
        ];
    }

    private function requiresDualSignature(int $type): bool
    {
        $definition = config("sygnet.alert_types.{$type}");

        return is_array($definition)
            && (bool) ($definition['critical'] ?? false);
    }

    /**
     * @return array{status:string,reason:string}
     */
    private function forged(string $reason): array
    {
        return [
            'status' => 'FORGED',
            'reason' => $reason,
        ];
    }
}
