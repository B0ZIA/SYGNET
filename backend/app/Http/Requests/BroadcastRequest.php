<?php

namespace App\Http\Requests;

use App\Sygnet\AlertTypes;
use App\Sygnet\Areas;
use App\Sygnet\Broadcaster;
use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyStore;
use App\Sygnet\Payload;
use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;
use Illuminate\Validation\Validator;

/**
 * Walidacja komunikatu z konsoli (CONSOLE_LARAVEL.md §5.3). W laboratorium ataków te reguły nie obowiązują.
 */
class BroadcastRequest extends FormRequest
{
    public function rules(): array
    {
        return [
            'issuer_id' => ['required', 'integer', Rule::in(array_keys((new IssuerRegistry)->issuers()))],
            'second_signer_id' => ['nullable', 'integer', Rule::in(array_keys((new IssuerRegistry)->issuers()))],
            'second_pin' => ['nullable', 'string'],
            'type' => ['required', 'integer', Rule::in(array_keys(AlertTypes::issuable()))],
            'area_code' => ['required', 'integer', Rule::in(array_keys(Areas::all()))],
            'valid_minutes' => ['required', 'integer', Rule::in(config('sygnet.validity_options'))],
            'note' => ['nullable', 'string'],
        ];
    }

    public function attributes(): array
    {
        return [
            'issuer_id' => 'nadawca', 'second_signer_id' => 'drugi podpis', 'type' => 'typ',
            'area_code' => 'obszar', 'valid_minutes' => 'ważność', 'note' => 'dopisek',
        ];
    }

    public function after(): array
    {
        return [function (Validator $v) {
            if ($v->errors()->isNotEmpty()) {
                return;
            }

            $issuers = new IssuerRegistry;
            $keys = KeyStore::fromConfig();
            $issuer = (int) $this->input('issuer_id');
            $area = (int) $this->input('area_code');
            $note = (string) $this->input('note', '');
            $revoked = Broadcaster::fromConfig()->revokedIssuers();

            if (strlen($note) > Payload::NOTE_MAX) {
                $v->errors()->add('note', 'Dopisek ma '.strlen($note).' B UTF-8, a może mieć najwyżej 60 B.');
            }
            if (preg_match('/[\x00-\x1F\x7F]/u', $note)) {
                $v->errors()->add('note', 'Dopisek nie może zawierać znaków sterujących (np. nowej linii).');
            }
            if (! $keys->has($issuer)) {
                $v->errors()->add('issuer_id', 'Konsola nie ma klucza tego nadawcy.');
            }
            if (in_array($issuer, $revoked, true)) {
                $v->errors()->add('issuer_id', 'Klucz tego nadawcy został unieważniony.');
            }
            if (! $issuers->canSign($issuer, $area)) {
                $v->errors()->add('area_code', $issuers->name($issuer).' nie ma uprawnień dla obszaru '.Areas::name($area).'.');
            }

            if (! AlertTypes::isCritical((int) $this->input('type'))) {
                return;
            }

            $second = $this->input('second_signer_id');
            if ($second === null) {
                $v->errors()->add('second_signer_id', 'Komunikat krytyczny wymaga drugiego, niezależnego podpisu.');

                return;
            }
            $second = (int) $second;
            if ($second === $issuer) {
                $v->errors()->add('second_signer_id', 'Drugi podpis musi pochodzić od innego wydawcy.');
            } elseif (! $issuers->canSign($second, $area)) {
                $v->errors()->add('second_signer_id', $issuers->name($second).' nie ma uprawnień dla tego obszaru.');
            } elseif (! $keys->has($second) || in_array($second, $revoked, true)) {
                $v->errors()->add('second_signer_id', 'Brak ważnego klucza drugiego wydawcy.');
            }
            if ((string) $this->input('second_pin') !== (string) config('sygnet.second_operator_pin')) {
                $v->errors()->add('second_pin', 'Drugi operator nie zatwierdził (zły PIN).');
            }
        }];
    }
}
