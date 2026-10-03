<?php

namespace App\Sygnet;

use App\Models\Broadcast;
use InvalidArgumentException;

/**
 * Laboratorium ataków (CONSOLE_LARAVEL.md §6). Ramki budujemy tą samą klasą FrameBuilder co prawdziwe komunikaty –
 * atak różni się tylko kluczem albo zmienionymi bajtami. Każdy ma działać przeciw telefonowi w Krakowie (obszar demo).
 */
final class AttackFactory
{
    public const UNKNOWN_ISSUER = 42;

    public const HISTORY_AGE = 3 * 86400;

    public function __construct(
        private readonly Broadcaster $broadcaster,
        private readonly IssuerRegistry $issuers,
    ) {}

    /** Opisy do strony /attack. */
    public static function catalog(): array
    {
        return [
            'A1' => [
                'title' => 'Podszycie się pod nadawcę',
                'what' => 'Atakujący pisze własny komunikat i podaje się za Dowództwo Operacyjne. Nie ma jego klucza, więc podpisuje swoim.',
                'expect' => 'FORGED', 'reason' => 'BAD_SIGNATURE',
            ],
            'A2' => [
                'title' => 'Modyfikacja prawdziwego komunikatu',
                'what' => 'Bierze prawdziwą ramkę z historii, zmienia dopisek, zostawia oryginalny podpis i przelicza CRC.',
                'expect' => 'FORGED', 'reason' => 'BAD_SIGNATURE',
            ],
            'A3' => [
                'title' => 'Powtórka starego nagrania',
                'what' => 'Odtwarza bajt w bajt prawdziwy alarm sprzed 3 dni – podpis jest poprawny, ale komunikat wygasł.',
                'expect' => 'EXPIRED', 'reason' => 'EXPIRED',
            ],
            'A4' => [
                'title' => 'Przekroczenie uprawnień',
                'what' => 'Prawdziwy klucz Prezydenta Warszawy podpisuje alarm dla Krakowa – poza swoim zakresem.',
                'expect' => 'FORGED', 'reason' => 'UNAUTHORIZED_AREA',
            ],
            'A5' => [
                'title' => 'Ewakuacja bez drugiego podpisu',
                'what' => 'Jeden operator (np. przekupiony) wydaje ewakuację sam. Typ krytyczny wymaga dwóch niezależnych podpisów.',
                'expect' => 'INCOMPLETE', 'reason' => 'DUAL_SIGNATURE_REQUIRED',
            ],
            'A6' => [
                'title' => 'Nieznany nadawca',
                'what' => 'Wymyślony urząd (ID 42) z kluczem atakującego – nie ma go w trust store podpisanym przez ROOT.',
                'expect' => 'FORGED', 'reason' => 'UNKNOWN_ISSUER',
            ],
            'A7' => [
                'title' => 'Skradziony, unieważniony klucz',
                'what' => 'Atakujący ukradł prawdziwy klucz. Po unieważnieniu (strona Klucze → KEY_REVOKE od ROOT) telefon go odrzuca.',
                'expect' => 'FORGED', 'reason' => 'REVOKED_ISSUER',
            ],
        ];
    }

    public function make(string $attack, array $in): Broadcast
    {
        $area = (int) ($in['area_code'] ?? config('sygnet.demo_user_area'));
        $note = (string) ($in['note'] ?? '');
        $type = (int) ($in['type'] ?? 1);

        return match (strtoupper($attack)) {
            'A1' => $this->impersonate((int) ($in['victim_id'] ?? 1), $type, $area, $note ?: 'Mobilizacja: stawić się w jednostkach do 18:00'),
            'A2' => $this->tamper(isset($in['broadcast_id']) ? (int) $in['broadcast_id'] : null, $note ?: 'Schron: NIE schodźcie do metra'),
            'A3' => $this->replay(isset($in['broadcast_id']) ? (int) $in['broadcast_id'] : null),
            'A4' => $this->outOfScope(),
            'A5' => $this->singleSignature($area),
            'A6' => $this->unknownIssuer($area),
            'A7' => $this->revokedKey(isset($in['issuer_id']) ? (int) $in['issuer_id'] : null),
            default => throw new InvalidArgumentException('Nieznany atak.'),
        };
    }

    /** A1: issuer_id i signer_id = ofiara, podpis kluczem HAKERA. */
    private function impersonate(int $victim, int $type, int $area, string $note): Broadcast
    {
        $payload = new Payload($victim, $type, $area, time(), 120, $this->broadcaster->nextSequence($victim), $note);
        $frame = $this->broadcaster->builder()->build($payload, [[$victim, KeyStore::HACKER]]);

        return Broadcast::fromFrame($frame, 'attack', 'A1');
    }

    /** A2: prawdziwa ramka, zmieniony dopisek, stary podpis, nowe CRC. */
    private function tamper(?int $broadcastId, string $newNote): Broadcast
    {
        $original = $this->genuine($broadcastId, fn ($q) => $q->where('type', '!=', AlertTypes::KEY_REVOKE));
        $f = FrameBuilder::parse($original->frame());
        $p = $f['payload'];

        $note = $newNote;
        while (strlen($note) > Payload::NOTE_MAX) {
            $note = mb_substr($note, 0, -1);
        }
        $changed = new Payload($p->issuerId, $p->type, $p->areaCode, $p->timestamp, $p->validMinutes, $p->sequence, $note);

        return Broadcast::fromFrame(FrameBuilder::assemble($changed->toBytes(), $f['signatures']), 'attack', 'A2');
    }

    /** A3: bajt w bajt prawdziwa ramka sprzed 3 dni (z sygnet:seed-history; jeśli brak – tworzymy ją teraz). */
    private function replay(?int $broadcastId): Broadcast
    {
        $old = $broadcastId !== null
            ? $this->genuine($broadcastId)
            : Broadcast::genuine()->where('type', '!=', AlertTypes::KEY_REVOKE)->latest('id')->get()
                ->first(fn (Broadcast $b) => $b->validUntil() < time());

        $old ??= $this->seedHistory()[0];

        return Broadcast::fromFrame($old->frame(), 'attack', 'A3');
    }

    /** A4: Prezydent Warszawy (6) podpisuje dla Krakowa. */
    private function outOfScope(): Broadcast
    {
        $this->requireKey(6);

        return $this->attackSigned(6, [6], 1, 1261, 'Alarm! Natychmiast do schronu', 'A4');
    }

    /** A5: ewakuacja z jednym podpisem uprawnionego wydawcy. */
    private function singleSignature(int $area): Broadcast
    {
        $issuer = collect([4, 7, 1, 2, 3, 5, 6])
            ->first(fn ($id) => $this->broadcaster->keys()->has($id) && $this->issuers->canSign($id, $area))
            ?? throw new InvalidArgumentException('Brak klucza wydawcy uprawnionego do tego obszaru.');

        return $this->attackSigned($issuer, [$issuer], AlertTypes::EVACUATION, $area, 'Kierunek: Wieliczka', 'A5');
    }

    /** A6: issuer_id 42, klucz hakera. */
    private function unknownIssuer(int $area): Broadcast
    {
        $id = self::UNKNOWN_ISSUER;
        $payload = new Payload($id, 8, $area, time(), 120, 1, 'Urząd ds. Ewakuacji: zbiórka na Rynku');
        $frame = $this->broadcaster->builder()->build($payload, [[$id, KeyStore::HACKER]]);

        return Broadcast::fromFrame($frame, 'attack', 'A6');
    }

    /** A7: skradziony klucz – podpis prawdziwy, ale wydawca unieważniony przez ROOT. */
    private function revokedKey(?int $issuerId): Broadcast
    {
        $revoked = $this->broadcaster->revokedIssuers();
        $issuerId ??= $revoked[0] ?? 6;
        $this->requireKey($issuerId);
        $area = $this->issuers->areasFor($issuerId)[0] ?? 0;

        return $this->attackSigned($issuerId, [$issuerId], 8, $area, 'Komunikat ze skradzionego klucza', 'A7');
    }

    private function attackSigned(int $issuer, array $signers, int $type, int $area, string $note, string $attack): Broadcast
    {
        $payload = new Payload($issuer, $type, $area, time(), 120, $this->broadcaster->nextSequence($issuer), $note);

        return Broadcast::fromFrame($this->broadcaster->builder()->build($payload, $signers), 'attack', $attack);
    }

    private function genuine(?int $id, ?callable $filter = null): Broadcast
    {
        $q = Broadcast::genuine();
        if ($filter) {
            $filter($q);
        }

        return ($id !== null ? $q->find($id) : $q->latest('id')->first())
            ?? $this->seedHistory()[0];
    }

    private function requireKey(int $id): void
    {
        if (! $this->broadcaster->keys()->has($id)) {
            throw new InvalidArgumentException("Brak klucza wydawcy {$id} (seedy testowe mają tylko 1, 3, 5, 6).");
        }
    }

    /**
     * Prawdziwe komunikaty sprzed 3 dni – materiał do ataku „powtórka” (sygnet:seed-history).
     *
     * @return Broadcast[]
     */
    public function seedHistory(): array
    {
        $ts = time() - self::HISTORY_AGE;
        $out = [];
        $plan = [
            [1, [1], 1, 1261, 'Schron: piwnice i przejścia podziemne'],
            [1, [1], 1, 1465, 'Schron: metro Świętokrzyska'],
            [4, [4, 7], AlertTypes::EVACUATION, 1261, 'Kierunek: Wieliczka'],
            [1, [1], 2, 1261, ''],
        ];

        foreach ($plan as [$issuer, $signers, $type, $area, $note]) {
            if (array_filter($signers, fn ($id) => ! $this->broadcaster->keys()->has($id))) {
                continue;
            }
            $b = $this->broadcaster->sign($issuer, $signers, $type, $area, 120, $note, $ts);
            $b->created_at = $b->updated_at = now()->subSeconds(self::HISTORY_AGE);
            $b->save();
            $out[] = $b;
            $ts += 600;
        }

        return $out;
    }
}
