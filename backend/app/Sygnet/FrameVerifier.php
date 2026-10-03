<?php

namespace App\Sygnet;

use Throwable;

/**
 * Weryfikacja ramki dokładnie jak w aplikacji (PROTOCOL.md §7, kolejność obowiązkowa).
 * Konsola używa jej do „kontrolnej weryfikacji”: pokazuje, co zobaczy telefon z wyeksportowanym trust store.
 */
final class FrameVerifier
{
    public const CLOCK_SKEW = 300;

    public function __construct(
        private readonly TrustStore $trust,
    ) {}

    /**
     * @param  int[]  $revoked  unieważnieni wydawcy
     * @param  array<string, true>  $seen  klucze "issuer:sequence"
     * @return array{status: string, reason: string, payload: ?Payload, signers: int[]}
     */
    public function verify(string $frame, int $now, int $userArea, array $revoked = [], array $seen = []): array
    {
        try {
            $f = FrameBuilder::parse($frame);
        } catch (Throwable $e) {
            return self::result('MALFORMED', $e->getMessage());
        }

        $p = $f['payload'];
        $signers = array_map(fn ($s) => $s[0], $f['signatures']);
        $r = fn (string $status, string $reason) => self::result($status, $reason, $p, $signers);

        if ($p->version !== Payload::VERSION) {
            return $r('MALFORMED', 'BAD_VERSION');
        }
        if ($f['signatures'] === []) {
            return $r('FORGED', 'NO_SIGNATURE');
        }
        if ($f['signatures'][0][0] !== $p->issuerId) {
            return $r('FORGED', 'PRIMARY_SIGNER_MISMATCH');
        }

        $valid = [];
        foreach ($f['signatures'] as [$sid, $sig]) {
            if ($sid === KeyStore::ROOT) {
                [$pub, $scopes] = [$this->trust->rootPublicKey(), [0]];
            } else {
                $cert = $this->trust->certificate($sid);
                if ($cert === null) {
                    return $r('FORGED', "UNKNOWN_ISSUER:{$sid}");
                }
                if (in_array($sid, $revoked, true)) {
                    return $r('FORGED', "REVOKED_ISSUER:{$sid}");
                }
                if ($now < $cert->validFrom || $now > $cert->validUntil) {
                    return $r('FORGED', "CERT_EXPIRED:{$sid}");
                }
                [$pub, $scopes] = [$cert->publicKey, $cert->scopes];
            }
            if (! sodium_crypto_sign_verify_detached($sig, $f['payload_bytes'], $pub)) {
                return $r('FORGED', "BAD_SIGNATURE:{$sid}");
            }
            if (! Areas::anyCovers($scopes, $p->areaCode)) {
                return $r('FORGED', "UNAUTHORIZED_AREA:{$sid}");
            }
            $valid[$sid] = true;
        }

        if ($p->type === AlertTypes::KEY_REVOKE && $p->issuerId !== KeyStore::ROOT) {
            return $r('FORGED', 'REVOKE_NOT_ROOT');
        }
        if ($p->issuerId === KeyStore::ROOT && $p->type !== AlertTypes::KEY_REVOKE) {
            return $r('FORGED', 'ROOT_ONLY_REVOKE');
        }
        if (AlertTypes::isCritical($p->type) && count($valid) < 2) {
            return $r('INCOMPLETE', 'DUAL_SIGNATURE_REQUIRED');
        }
        if ($p->timestamp > $now + self::CLOCK_SKEW) {
            return $r('FORGED', 'TIMESTAMP_IN_FUTURE');
        }
        if ($now > $p->validUntil()) {
            return $r('EXPIRED', 'EXPIRED');
        }
        if (isset($seen["{$p->issuerId}:{$p->sequence}"])) {
            return $r('DUPLICATE', 'ALREADY_RECEIVED');
        }
        if (! Areas::covers($p->areaCode, $userArea)) {
            return $r('VERIFIED_OTHER_AREA', 'OTHER_AREA');
        }

        return $r('VERIFIED', 'OK');
    }

    /** Powód po ludzku – te same zdania co w aplikacji (Sygnet.Core.Messages). */
    public function explain(array $result): ?string
    {
        $reason = $result['reason'];
        [$code, $id] = array_pad(explode(':', $reason, 2), 2, null);
        $name = $id !== null
            ? ($this->trust->nameOf((int) $id) ?? "nadawca {$id}")
            : ($result['payload'] ? ($this->trust->nameOf($result['payload']->issuerId) ?? 'nadawca') : 'nadawca');

        return match ($code) {
            'BAD_SIGNATURE' => "Podpis nie pasuje do: {$name}. Ktoś się podszywa lub zmienił treść.",
            'UNKNOWN_ISSUER' => 'Nieznany nadawca.',
            'UNAUTHORIZED_AREA' => "{$name} nie ma uprawnień dla tego obszaru.",
            'REVOKED_ISSUER' => 'Klucz tego nadawcy został unieważniony.',
            'CERT_EXPIRED' => "Certyfikat nadawcy ({$name}) jest nieważny.",
            'TIMESTAMP_IN_FUTURE' => 'Podejrzany czas wydania.',
            'NO_SIGNATURE' => 'Komunikat nie ma podpisu.',
            'PRIMARY_SIGNER_MISMATCH' => 'Podpis nie należy do nadawcy wskazanego w komunikacie.',
            'REVOKE_NOT_ROOT' => 'Tylko klucz główny może unieważniać klucze.',
            'ROOT_ONLY_REVOKE' => 'Klucz główny nie wydaje komunikatów tego typu.',
            'DUAL_SIGNATURE_REQUIRED' => 'Komunikat krytyczny wymaga dwóch podpisów. NIE WYKONUJ.',
            'EXPIRED' => 'Prawdziwy, ale nieaktualny. Możliwe odtworzone nagranie.',
            'OTHER_AREA' => 'Prawdziwy komunikat, ale dla innego obszaru.',
            default => null,
        };
    }

    private static function result(string $status, string $reason, ?Payload $payload = null, array $signers = []): array
    {
        return ['status' => $status, 'reason' => $reason, 'payload' => $payload, 'signers' => $signers];
    }
}
