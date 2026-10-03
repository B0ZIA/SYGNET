<?php

namespace App\Models;

use App\Sygnet\AlertTypes;
use App\Sygnet\Areas;
use App\Sygnet\FrameBuilder;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Model;

/**
 * Nadana ramka. Źródłem prawdy są bajty (frame_hex) – pozostałe kolumny to kopia do historii i filtrów.
 *
 * @property int $id
 * @property int $issuer_id
 * @property int[] $signer_ids
 * @property int $type
 * @property int $area_code
 * @property int $timestamp
 * @property int $valid_minutes
 * @property int $sequence
 * @property string $note
 * @property string $frame_hex
 * @property string $qr_text
 * @property string $kind
 * @property ?string $attack_type
 */
class Broadcast extends Model
{
    protected $fillable = [
        'issuer_id', 'signer_ids', 'type', 'area_code', 'timestamp', 'valid_minutes', 'sequence',
        'note', 'frame_hex', 'qr_text', 'kind', 'attack_type',
    ];

    protected function casts(): array
    {
        return [
            'signer_ids' => 'array',
            'issuer_id' => 'integer',
            'type' => 'integer',
            'area_code' => 'integer',
            'timestamp' => 'integer',
            'valid_minutes' => 'integer',
            'sequence' => 'integer',
        ];
    }

    public function scopeGenuine(Builder $q): Builder
    {
        return $q->where('kind', 'genuine');
    }

    public function frame(): string
    {
        return hex2bin($this->frame_hex);
    }

    public function validUntil(): int
    {
        return $this->timestamp + $this->valid_minutes * 60;
    }

    public function summary(): string
    {
        return AlertTypes::name($this->type).' · '.Areas::name($this->area_code);
    }

    public static function fromFrame(string $frame, string $kind = 'genuine', ?string $attackType = null): self
    {
        $f = FrameBuilder::parse($frame);
        $p = $f['payload'];

        return self::create([
            'issuer_id' => $p->issuerId,
            'signer_ids' => array_map(fn ($s) => $s[0], $f['signatures']),
            'type' => $p->type,
            'area_code' => $p->areaCode,
            'timestamp' => $p->timestamp,
            'valid_minutes' => $p->validMinutes,
            'sequence' => $p->sequence,
            'note' => $p->revokedIssuerId() !== null ? '→ wydawca '.$p->revokedIssuerId() : mb_scrub($p->note, 'UTF-8'),
            'frame_hex' => bin2hex($frame),
            'qr_text' => FrameBuilder::qrText($frame),
            'kind' => $kind,
            'attack_type' => $attackType,
        ]);
    }
}
