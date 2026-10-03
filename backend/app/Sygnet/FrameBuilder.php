<?php

namespace App\Sygnet;

use InvalidArgumentException;

/**
 * Ramka: "SG" | frame_len u16 | payload | sig_count u8 | sig_count × (signer_id u16 | sig 64 B) | crc16 (PROTOCOL.md §3).
 * Ataki z laboratorium budujemy tą samą klasą – różnią się tylko kluczem albo zmienionymi bajtami.
 */
final class FrameBuilder
{
    public const MAGIC = 'SG';

    public const QR_PREFIX = 'SYG1:';

    public function __construct(
        private readonly KeyStore $keys
    ) {}

    /**
     * Podpisuje payload kluczami wydawców. $signers to ID wydawców albo pary [signer_id, alias klucza],
     * np. [1, 'hacker'] = ramka podaje wydawcę 1, a podpisuje klucz hakera.
     *
     * @param  array<int|array{0:int,1:int|string}>  $signers
     */
    public function build(Payload $payload, array $signers): string
    {
        $bytes = $payload->toBytes();
        $signatures = [];

        foreach ($signers as $signer) {
            [$id, $key] = is_array($signer) ? $signer : [$signer, $signer];
            $signatures[] = [(int) $id, $this->keys->sign($key, $bytes)];
        }

        return self::assemble($bytes, $signatures);
    }

    /**
     * Składa ramkę z gotowych bajtów payloadu i podpisów (bez podpisywania) – m.in. dla ataku A2.
     *
     * @param  array<array{0:int,1:string}>  $signatures
     */
    public static function assemble(string $payloadBytes, array $signatures): string
    {
        if (count($signatures) > 2) {
            throw new InvalidArgumentException('Ramka może mieć najwyżej 2 podpisy.');
        }

        $body = $payloadBytes.pack('C', count($signatures));

        foreach ($signatures as [$id, $sig]) {
            if ($id < 0 || $id > 0xFFFF || strlen($sig) !== SODIUM_CRYPTO_SIGN_BYTES) {
                throw new InvalidArgumentException('Niepoprawny podpis.');
            }
            $body .= pack('n', $id).$sig;
        }

        $head = self::MAGIC.pack('n', strlen($body) + 2).$body;      // frame_len = bajty po polu frame_len, z CRC

        return $head.Crc16::bytes($head);
    }

    /** Przelicza CRC (atakujący może to zrobić zawsze – CRC nie chroni przed niczym poza szumem). */
    public static function withFixedCrc(string $frame): string
    {
        $head = substr($frame, 0, -2);

        return $head.Crc16::bytes($head);
    }

    /**
     * @return array{payload: Payload, payload_bytes: string, signatures: array<array{0:int,1:string}>}
     */
    public static function parse(string $frame): array
    {
        $n = strlen($frame);

        if ($n < 4 + Payload::HEADER_LENGTH + 1 + 2 || substr($frame, 0, 2) !== self::MAGIC) {
            throw new InvalidArgumentException('To nie jest ramka SYGNET.');
        }

        if (unpack('n', substr($frame, 2, 2))[1] !== $n - 4) {
            throw new InvalidArgumentException('Zła długość ramki.');
        }

        if (Crc16::bytes(substr($frame, 0, -2)) !== substr($frame, -2)) {
            throw new InvalidArgumentException('Złe CRC.');
        }

        $noteLen = ord($frame[4 + 15]);
        $payloadLen = Payload::HEADER_LENGTH + $noteLen;
        $p = 4 + $payloadLen;

        if ($p + 1 > $n - 2) {
            throw new InvalidArgumentException('Ramka ucięta.');
        }

        $payloadBytes = substr($frame, 4, $payloadLen);
        $count = ord($frame[$p++]);

        if ($p + $count * 66 !== $n - 2) {
            throw new InvalidArgumentException('Zła liczba podpisów.');
        }

        $signatures = [];
        for ($i = 0; $i < $count; $i++, $p += 66) {
            $signatures[] = [unpack('n', substr($frame, $p, 2))[1], substr($frame, $p + 2, 64)];
        }

        return [
            'payload' => Payload::fromBytes($payloadBytes),
            'payload_bytes' => $payloadBytes,
            'signatures' => $signatures,
        ];
    }

    public static function qrText(string $frame): string
    {
        return self::QR_PREFIX.rtrim(strtr(base64_encode($frame), '+/', '-_'), '=');
    }

    public static function fromQrText(string $text): string
    {
        if (! str_starts_with($text, self::QR_PREFIX)) {
            throw new InvalidArgumentException('To nie jest kod SYGNET.');
        }

        $b64 = strtr(substr($text, strlen(self::QR_PREFIX)), '-_', '+/');
        $frame = base64_decode($b64.str_repeat('=', (4 - strlen($b64) % 4) % 4), true);

        if ($frame === false) {
            throw new InvalidArgumentException('Zły base64url.');
        }

        return $frame;
    }

    /** Czas nadawania (PROTOCOL.md §8.1): R × (0,7 s + 0,05 s/B) + (R − 1) × 0,5 s. */
    public static function audioSeconds(int $frameBytes, int $repeat): float
    {
        return round($repeat * (0.7 + 0.05 * $frameBytes) + ($repeat - 1) * 0.5, 2);
    }
}
