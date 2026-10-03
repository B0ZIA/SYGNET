<?php

namespace App\Sygnet;

use App\Models\Broadcast;
use Throwable;

/**
 * Podpisuje i zapisuje komunikaty konsoli. Kontrolna weryfikacja (check) pokazuje, co zobaczy telefon odbiorcy
 * z wyeksportowanym trust store – ta sama logika co w aplikacji (PROTOCOL.md §7).
 */
final class Broadcaster
{
    public const REVOKE_VALID_MINUTES = 0xFFFF;     // ~45 dni – maksimum u16

    public function __construct(
        private readonly KeyStore $keys,
        private readonly IssuerRegistry $issuers,
        private readonly TrustStoreExporter $exporter,
    ) {}

    public static function fromConfig(): self
    {
        $keys = KeyStore::fromConfig();

        return new self($keys, new IssuerRegistry, new TrustStoreExporter($keys, new IssuerRegistry));
    }

    public function keys(): KeyStore
    {
        return $this->keys;
    }

    public function builder(): FrameBuilder
    {
        return new FrameBuilder($this->keys);
    }

    /**
     * Licznik osobno dla każdego wydawcy (max + 1), tylko prawdziwe komunikaty. Nie mniej niż sygnet.sequence_start:
     * nowa baza (np. na serwerze) nie może zacząć od numerów, które telefony już widziały – uznałyby je za DUPLICATE.
     */
    public function nextSequence(int $issuerId): int
    {
        $start = min(0xFFFF, max(1, (int) config('sygnet.sequence_start', 1)));
        $max = (int) Broadcast::genuine()->where('issuer_id', $issuerId)->max('sequence');

        return $max >= 0xFFFF ? $start : max($start, $max + 1);
    }

    /** @param int[] $signers */
    public function sign(int $issuerId, array $signers, int $type, int $area, int $validMinutes, string $note,
        ?int $timestamp = null, string $kind = 'genuine', ?string $attackType = null): Broadcast
    {
        $payload = new Payload(
            issuerId: $issuerId,
            type: $type,
            areaCode: $area,
            timestamp: $timestamp ?? time(),
            validMinutes: $validMinutes,
            sequence: $this->nextSequence($issuerId),
            note: $note,
        );

        return Broadcast::fromFrame($this->builder()->build($payload, $signers), $kind, $attackType);
    }

    /** KEY_REVOKE podpisany przez ROOT: wydawca 0, obszar 0, dopisek = u16 ID odwoływanego wydawcy. */
    public function revoke(int $issuerId): Broadcast
    {
        return $this->sign(KeyStore::ROOT, [KeyStore::ROOT], AlertTypes::KEY_REVOKE, 0,
            self::REVOKE_VALID_MINUTES, pack('n', $issuerId));
    }

    /** Wydawcy unieważnieni komunikatem KEY_REVOKE z tej konsoli. @return int[] */
    public function revokedIssuers(): array
    {
        return Broadcast::genuine()->where('type', AlertTypes::KEY_REVOKE)->get()
            ->map(fn (Broadcast $b) => FrameBuilder::parse($b->frame())['payload']->revokedIssuerId())
            ->filter(fn ($id) => $id !== null)->unique()->values()->all();
    }

    public function trust(): ?TrustStore
    {
        try {
            return $this->exporter->load();
        } catch (Throwable) {
            return null;
        }
    }

    /** Wynik weryfikacji na telefonie z naszym trust store (zakładamy, że dotarły do niego nasze unieważnienia). */
    public function check(string $frame, ?int $userArea = null, ?int $now = null): ?array
    {
        $trust = $this->trust();
        if ($trust === null) {
            return null;
        }

        $verifier = new FrameVerifier($trust);
        $userArea ??= (int) config('sygnet.demo_user_area');
        $r = $verifier->verify($frame, $now ?? time(), $userArea, $this->revokedIssuers());

        return [
            'status' => $r['status'],
            'reason' => $r['reason'],
            'explanation' => $verifier->explain($r),
            'user_area' => $userArea,
            'user_area_name' => Areas::name($userArea),
            'signer_names' => array_map(fn ($id) => $trust->nameOf($id), $r['signers']),
        ];
    }

    /** Odpowiedź API dla ramki z historii. */
    public function present(Broadcast $b, ?int $userArea = null): array
    {
        $frame = $b->frame();
        $p = FrameBuilder::parse($frame)['payload'];
        $repeat = (int) config('sygnet.repeat_default');

        return [
            'id' => $b->id,
            'kind' => $b->kind,
            'attack_type' => $b->attack_type,
            'issuer_id' => $p->issuerId,
            'issuer_name' => $this->issuers->exists($p->issuerId) ? $this->issuers->name($p->issuerId) : "nieznany wydawca {$p->issuerId}",
            'signer_ids' => $b->signer_ids,
            'type' => $p->type,
            'type_name' => AlertTypes::name($p->type),
            'type_icon' => AlertTypes::all()[$p->type]['icon'] ?? 'info',
            'critical' => AlertTypes::isCritical($p->type),
            'instruction' => AlertTypes::instruction($p->type),
            'area_code' => $p->areaCode,
            'area_name' => Areas::name($p->areaCode),
            'timestamp' => $p->timestamp,
            'valid_minutes' => $p->validMinutes,
            'valid_until' => $p->validUntil(),
            'sequence' => $p->sequence,
            'note' => $b->note,
            'note_bytes' => strlen($p->note),
            'frame_hex' => $b->frame_hex,
            'frame_b64' => base64_encode($frame),
            'qr_text' => $b->qr_text,
            'bytes' => strlen($frame),
            'audio_seconds' => FrameBuilder::audioSeconds(strlen($frame), $repeat),
            'created_at' => $b->created_at?->toIso8601String(),
            'check' => $this->check($frame, $userArea),
        ];
    }
}
