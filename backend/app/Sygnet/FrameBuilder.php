<?php

namespace App\Sygnet;

use InvalidArgumentException;

final class FrameBuilder
{
    private const MAGIC = 'SG';

    public function __construct(
        private readonly KeyStore $keys
    ) {
    }

    /**
     * @param int[] $signers
     */
    public function build(Payload $payload, array $signers): string
    {
        if ($signers === []) {
            throw new InvalidArgumentException(
                'At least one signer is required.'
            );
        }

        if (count($signers) > 2) {
            throw new InvalidArgumentException(
                'Maximum two signers are supported.'
            );
        }

        $payloadBytes = $payload->toBytes();

        $signerBlock = pack('C', count($signers));

        foreach ($signers as $issuerId) {
            if ($issuerId < 0 || $issuerId > 0xFFFF) {
                throw new InvalidArgumentException(
                    "Invalid signer id {$issuerId}."
                );
            }

            $signature = $this->keys->sign(
                $issuerId,
                $payloadBytes
            );

            if (strlen($signature) !== SODIUM_CRYPTO_SIGN_BYTES) {
                throw new InvalidArgumentException(
                    "Invalid signature generated for issuer {$issuerId}."
                );
            }

            $signerBlock .= pack('n', $issuerId);
            $signerBlock .= $signature;
        }

        /*
         * Length field is the complete frame length
         * minus the first four bytes:
         *
         * SG + LEN:u16
         */
        $bodyWithoutCrc =
            self::MAGIC .
            pack('n', 0) .
            $payloadBytes .
            $signerBlock;

        $frameLength = strlen($bodyWithoutCrc) + 2;

        $bodyWithoutCrc =
            self::MAGIC .
            pack('n', $frameLength - 4) .
            substr($bodyWithoutCrc, 4);

        $crc = Crc16::bytes($bodyWithoutCrc);

        return $bodyWithoutCrc . $crc;
    }

    public function qrText(string $frame): string
    {
        return 'SYG1:' . rtrim(
                strtr(base64_encode($frame), '+/', '-_'),
                '='
            );
    }

    public function frameHex(string $frame): string
    {
        return bin2hex($frame);
    }
}
