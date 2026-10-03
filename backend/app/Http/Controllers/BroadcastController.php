<?php

namespace App\Http\Controllers;

use App\Http\Requests\BroadcastRequest;
use App\Models\Broadcast;
use App\Sygnet\AlertTypes;
use App\Sygnet\AttackFactory;
use App\Sygnet\Broadcaster;
use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyStore;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;
use InvalidArgumentException;

/**
 * API konsoli (CONSOLE_LARAVEL.md §5.3): JSON, sesja + CSRF. Klucze prywatne zostają na serwerze.
 */
class BroadcastController extends Controller
{
    public function __construct(
        private readonly Broadcaster $broadcaster,
    ) {}

    /** POST /api/broadcast */
    public function store(BroadcastRequest $request): JsonResponse
    {
        $issuer = (int) $request->input('issuer_id');
        $signers = AlertTypes::isCritical((int) $request->input('type'))
            ? [$issuer, (int) $request->input('second_signer_id')]
            : [$issuer];

        $b = $this->broadcaster->sign(
            $issuer,
            $signers,
            (int) $request->input('type'),
            (int) $request->input('area_code'),
            (int) $request->input('valid_minutes'),
            (string) $request->input('note', ''),
        );

        return response()->json($this->broadcaster->present($b), 201);
    }

    /** GET /api/broadcasts?limit=20&kind=genuine|attack */
    public function index(Request $request): JsonResponse
    {
        $limit = min(100, max(1, (int) $request->query('limit', 20)));
        $q = Broadcast::query()->latest('id')->limit($limit);
        if (in_array($request->query('kind'), ['genuine', 'attack'], true)) {
            $q->where('kind', $request->query('kind'));
        }

        return response()->json($q->get()->map(fn (Broadcast $b) => $this->broadcaster->present($b)));
    }

    /** POST /api/revoke – KEY_REVOKE podpisany przez ROOT. */
    public function revoke(Request $request, IssuerRegistry $issuers): JsonResponse
    {
        $data = $request->validate([
            'issuer_id' => ['required', 'integer', Rule::in(array_keys($issuers->issuers()))],
        ]);

        if (! $this->broadcaster->keys()->has(KeyStore::ROOT)) {
            return response()->json(['message' => 'Brak klucza ROOT. Uruchom: php artisan sygnet:init'], 422);
        }

        $b = $this->broadcaster->revoke((int) $data['issuer_id']);

        return response()->json($this->broadcaster->present($b), 201);
    }

    /** POST /api/attack/{type} */
    public function attack(Request $request, string $type, AttackFactory $attacks): JsonResponse
    {
        abort_unless(array_key_exists(strtoupper($type), AttackFactory::catalog()), 404);

        $data = $request->validate([
            'victim_id' => ['nullable', 'integer', 'min:1', 'max:65535'],
            'issuer_id' => ['nullable', 'integer', 'min:1', 'max:65535'],
            'broadcast_id' => ['nullable', 'integer'],
            'type' => ['nullable', 'integer', 'min:1', 'max:249'],
            'area_code' => ['nullable', 'integer', 'min:0', 'max:65535'],
            'note' => ['nullable', 'string', 'max:200'],
        ]);

        try {
            $b = $attacks->make($type, $data);
        } catch (InvalidArgumentException $e) {
            return response()->json(['message' => $e->getMessage()], 422);
        }

        // A7 zakłada telefon, który dostał już unieważnienie skradzionego klucza (KEY_REVOKE od ROOT)
        $out = $this->broadcaster->present($b, null, strtoupper($type) === 'A7' ? [$b->issuer_id] : []);
        $meta = AttackFactory::catalog()[strtoupper($type)];
        $out['expected'] = ['status' => $meta['expect'], 'reason' => $meta['reason']];
        $out['as_expected'] = $out['check'] !== null && $out['check']['status'] === $meta['expect']
            && str_starts_with($out['check']['reason'], $meta['reason']);

        return response()->json($out, 201);
    }
}
