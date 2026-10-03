<?php

namespace App\Sygnet;

use InvalidArgumentException;

/**
 * Certyfikat wydawcy – bajty podpisywane przez ROOT (PROTOCOL.md §5.3):
 * cert_version u8 | issuer_id u16 | pubkey 32 B | valid_from u32 | valid_until u32 | scope_count u8 | scopes u16[] | name_len u8 | name.
 */
final readonly class Certificate
{
    public const VERSION = 1;

    /** @param int[] $scopes */
    public function __construct(
        public int $issuerId,
        public string $publicKey,
        public int $validFrom,
        public int $validUntil,
        public array $scopes,
        public string $name,
        public int $version = self::VERSION,
    ) {
        if ($version !== self::VERSION) {
            throw new InvalidArgumentException('Nieobsługiwana wersja certyfikatu.');
        }
        if (strlen($publicKey) !== SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES) {
            throw new InvalidArgumentException('Zły klucz publiczny w certyfikacie.');
        }
        if ($issuerId < 0 || $issuerId > 0xFFFF) {
            throw new InvalidArgumentException('Złe ID wydawcy.');
        }
        if ($validUntil < $validFrom) {
            throw new InvalidArgumentException('Certyfikat wygasa przed datą ważności.');
        }
        if (count($scopes) > 0xFF || strlen($name) > 0xFF) {
            throw new InvalidArgumentException('Za dużo zakresów albo za długa nazwa.');
        }
        foreach ($scopes as $scope) {
            if ($scope < 0 || $scope > 0xFFFF) {
                throw new InvalidArgumentException("Zły zakres {$scope}.");
            }
        }
    }

    public function toBytes(): string
    {
        $bytes = pack('Cn', $this->version, $this->issuerId)
            .$this->publicKey
            .pack('NNC', $this->validFrom, $this->validUntil, count($this->scopes));

        foreach ($this->scopes as $scope) {
            $bytes .= pack('n', $scope);
        }

        return $bytes.pack('C', strlen($this->name)).$this->name;
    }

    public static function fromBytes(string $bytes): self
    {
        $n = strlen($bytes);

        if ($n < 45) {
            throw new InvalidArgumentException('Certyfikat jest za krótki.');
        }

        $h = unpack('Cversion/nissuer', $bytes);
        $t = unpack('Nfrom/Nuntil/Ccount', $bytes, 35);
        $p = 44;

        if ($p + 2 * $t['count'] + 1 > $n) {
            throw new InvalidArgumentException('Certyfikat ucięty.');
        }

        $scopes = [];
        for ($i = 0; $i < $t['count']; $i++, $p += 2) {
            $scopes[] = unpack('n', $bytes, $p)[1];
        }

        $nameLen = ord($bytes[$p++]);

        if ($p + $nameLen !== $n) {
            throw new InvalidArgumentException('Zła długość nazwy w certyfikacie.');
        }

        return new self(
            issuerId: $h['issuer'],
            publicKey: substr($bytes, 3, 32),
            validFrom: $t['from'],
            validUntil: $t['until'],
            scopes: $scopes,
            name: substr($bytes, $p, $nameLen),
            version: $h['version'],
        );
    }

    public static function fromBase64(string $value): self
    {
        $bytes = base64_decode($value, true);

        if ($bytes === false) {
            throw new InvalidArgumentException('Zły base64 certyfikatu.');
        }

        return self::fromBytes($bytes);
    }

    public function fingerprint(): string
    {
        return KeyStore::fingerprintOf($this->publicKey);
    }
}
