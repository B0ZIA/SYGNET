<?php

namespace App\Sygnet;

use InvalidArgumentException;

final readonly class Certificate
{
    public function __construct(
        public int $version,
        public int $issuerId,
        public string $publicKey,
        public int $validFrom,
        public int $validUntil,
        public array $areas,
        public string $name,
    ) {
        if ($version !== 1) {
            throw new InvalidArgumentException('Unsupported certificate version.');
        }

        if (strlen($publicKey) !== SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES) {
            throw new InvalidArgumentException('Invalid certificate public key.');
        }

        if ($issuerId < 0 || $issuerId > 0xFFFF) {
            throw new InvalidArgumentException('Invalid issuer ID.');
        }

        if ($validUntil < $validFrom) {
            throw new InvalidArgumentException(
                'Certificate expiration precedes validity start.'
            );
        }

        foreach ($areas as $area) {
            if ($area < 0 || $area > 0xFFFF) {
                throw new InvalidArgumentException(
                    "Invalid certificate area {$area}."
                );
            }
        }
    }

    public function toBytes(): string
    {
        if (count($this->areas) > 0xFFFF) {
            throw new InvalidArgumentException('Too many certificate areas.');
        }

        $nameLength = strlen($this->name);

        if ($nameLength > 0xFFFF) {
            throw new InvalidArgumentException('Certificate name is too long.');
        }

        $bytes =
            pack('C', $this->version) .
            pack('n', $this->issuerId) .
            $this->publicKey .
            pack('N', $this->validFrom) .
            pack('N', $this->validUntil) .
            pack('n', count($this->areas));

        foreach ($this->areas as $area) {
            $bytes .= pack('n', $area);
        }

        $bytes .= pack('n', $nameLength);
        $bytes .= $this->name;

        return $bytes;
    }

    public function toBase64(): string
    {
        return base64_encode($this->toBytes());
    }

    public static function fromBytes(string $bytes): self
    {
        $length = strlen($bytes);
        $offset = 0;

        // Fixed part before AREA_COUNT:
        // version 1
        // issuer_id 2
        // public_key 32
        // valid_from 4
        // valid_until 4
        // = 43 bytes
        if ($length < 45) {
            throw new InvalidArgumentException('Certificate is too short.');
        }

        $version = ord($bytes[$offset]);
        $offset += 1;

        $issuer = unpack('nissuer', substr($bytes, $offset, 2));

        if ($issuer === false) {
            throw new InvalidArgumentException('Invalid certificate issuer ID.');
        }

        $issuerId = $issuer['issuer'];
        $offset += 2;

        $publicKey = substr($bytes, $offset, 32);

        if (strlen($publicKey) !== 32) {
            throw new InvalidArgumentException('Certificate public key is truncated.');
        }

        $offset += 32;

        $validFromData = unpack('Nvalid_from', substr($bytes, $offset, 4));

        if ($validFromData === false) {
            throw new InvalidArgumentException('Invalid certificate valid_from.');
        }

        $validFrom = $validFromData['valid_from'];
        $offset += 4;

        $validUntilData = unpack('Nvalid_until', substr($bytes, $offset, 4));

        if ($validUntilData === false) {
            throw new InvalidArgumentException('Invalid certificate valid_until.');
        }

        $validUntil = $validUntilData['valid_until'];
        $offset += 4;

        // IMPORTANT:
        // AREA_COUNT is u16 BE, at offset 43.
        if ($length < $offset + 2) {
            throw new InvalidArgumentException(
                'Certificate is missing area count.'
            );
        }

        $areaCountData = unpack('narea_count', substr($bytes, $offset, 2));

        if ($areaCountData === false) {
            throw new InvalidArgumentException(
                'Invalid certificate area count.'
            );
        }

        $areaCount = $areaCountData['area_count'];
        $offset += 2;

        // Every area is u16 BE.
        $areas = [];

        for ($i = 0; $i < $areaCount; $i++) {
            if ($length < $offset + 2) {
                throw new InvalidArgumentException(
                    'Certificate area section is truncated.'
                );
            }

            $areaData = unpack('narea', substr($bytes, $offset, 2));

            if ($areaData === false) {
                throw new InvalidArgumentException(
                    'Invalid certificate area.'
                );
            }

            $areas[] = $areaData['area'];
            $offset += 2;
        }

        // NAME_LEN is u16 BE.
        if ($length < $offset + 2) {
            throw new InvalidArgumentException(
                'Certificate is missing name length.'
            );
        }

        $nameLengthData = unpack(
            'nname_length',
            substr($bytes, $offset, 2)
        );

        if ($nameLengthData === false) {
            throw new InvalidArgumentException(
                'Invalid certificate name length.'
            );
        }

        $nameLength = $nameLengthData['name_length'];
        $offset += 2;

        if ($length !== $offset + $nameLength) {
            throw new InvalidArgumentException(
                'Certificate name length mismatch.'
            );
        }

        $name = substr($bytes, $offset, $nameLength);

        return new self(
            version: $version,
            issuerId: $issuerId,
            publicKey: $publicKey,
            validFrom: $validFrom,
            validUntil: $validUntil,
            areas: $areas,
            name: $name,
        );
    }

    public static function fromBase64(string $value): self
    {
        $bytes = base64_decode($value, true);

        if ($bytes === false) {
            throw new InvalidArgumentException(
                'Invalid certificate Base64.'
            );
        }

        return self::fromBytes($bytes);
    }
}
