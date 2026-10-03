<?php

namespace App\Sygnet;

use InvalidArgumentException;

/**
 * Treść komunikatu – to jest podpisywane (PROTOCOL.md §2). 16 B nagłówka + dopisek UTF-8 (≤ 60 B).
 */
final readonly class Payload
{
    public const VERSION = 1;

    public const HEADER_LENGTH = 16;

    public const NOTE_MAX = 60;

    public function __construct(
        public int $issuerId,
        public int $type,
        public int $areaCode,
        public int $timestamp,
        public int $validMinutes,
        public int $sequence,
        public string $note,
        public int $version = self::VERSION,
        public int $flags = 0,
    ) {
        self::range('issuer_id', $issuerId, 0xFFFF);
        self::range('type', $type, 0xFF);
        self::range('area_code', $areaCode, 0xFFFF);
        self::range('timestamp', $timestamp, 0xFFFFFFFF);
        self::range('valid_minutes', $validMinutes, 0xFFFF);
        self::range('sequence', $sequence, 0xFFFF);
        self::range('version', $version, 0xFF);
        self::range('flags', $flags, 0xFF);

        if (strlen($note) > self::NOTE_MAX) {
            throw new InvalidArgumentException('Dopisek może mieć najwyżej 60 bajtów.');
        }
    }

    public function toBytes(): string
    {
        return pack(
            'CnCnNnnCC',
            $this->version,
            $this->issuerId,
            $this->type,
            $this->areaCode,
            $this->timestamp,
            $this->validMinutes,
            $this->sequence,
            $this->flags,
            strlen($this->note),
        ).$this->note;
    }

    public static function fromBytes(string $bytes): self
    {
        if (strlen($bytes) < self::HEADER_LENGTH) {
            throw new InvalidArgumentException('Payload jest za krótki.');
        }

        $h = unpack('Cversion/nissuer/Ctype/narea/Nts/nvalid/nseq/Cflags/CnoteLen', $bytes);

        if (strlen($bytes) !== self::HEADER_LENGTH + $h['noteLen']) {
            throw new InvalidArgumentException('Długość dopisku nie zgadza się z payloadem.');
        }

        return new self(
            issuerId: $h['issuer'],
            type: $h['type'],
            areaCode: $h['area'],
            timestamp: $h['ts'],
            validMinutes: $h['valid'],
            sequence: $h['seq'],
            note: substr($bytes, self::HEADER_LENGTH),
            version: $h['version'],
            flags: $h['flags'],
        );
    }

    public function validUntil(): int
    {
        return $this->timestamp + $this->validMinutes * 60;
    }

    /** Dla KEY_REVOKE dopisek to u16 z ID odwoływanego wydawcy. */
    public function revokedIssuerId(): ?int
    {
        return $this->type === AlertTypes::KEY_REVOKE && strlen($this->note) === 2
            ? unpack('n', $this->note)[1]
            : null;
    }

    public function toHex(): string
    {
        return bin2hex($this->toBytes());
    }

    private static function range(string $field, int $value, int $max): void
    {
        if ($value < 0 || $value > $max) {
            throw new InvalidArgumentException("Pole {$field} poza zakresem.");
        }
    }
}
