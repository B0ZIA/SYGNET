<?php

namespace App\Sygnet;

use InvalidArgumentException;

final readonly class Payload
{
    public function __construct(
        public int $issuerId,
        public int $type,
        public int $areaCode,
        public int $timestamp,
        public int $validMinutes,
        public int $sequence,
        public string $note,
    ) {
        if ($this->issuerId < 0 || $this->issuerId > 0xFFFF) {
            throw new InvalidArgumentException('Invalid issuer_id.');
        }

        if ($this->type < 0 || $this->type > 0xFF) {
            throw new InvalidArgumentException('Invalid type.');
        }

        if ($this->areaCode < 0 || $this->areaCode > 0xFFFF) {
            throw new InvalidArgumentException('Invalid area_code.');
        }

        if ($this->timestamp < 0 || $this->timestamp > 0xFFFFFFFF) {
            throw new InvalidArgumentException('Invalid timestamp.');
        }

        if ($this->validMinutes < 0 || $this->validMinutes > 0xFFFF) {
            throw new InvalidArgumentException('Invalid valid_minutes.');
        }

        if ($this->sequence < 0 || $this->sequence > 0xFFFF) {
            throw new InvalidArgumentException('Invalid sequence.');
        }

        if (strlen($this->note) > 60) {
            throw new InvalidArgumentException('Note cannot exceed 60 bytes.');
        }
    }

    public function toBytes(): string
    {
        return pack(
                'CnCnNnnCC',
                1,
                $this->issuerId,
                $this->type,
                $this->areaCode,
                $this->timestamp,
                $this->validMinutes,
                $this->sequence,
                0,
                strlen($this->note),
            ) . $this->note;
    }

    public function toHex(): string
    {
        return bin2hex($this->toBytes());
    }
}
