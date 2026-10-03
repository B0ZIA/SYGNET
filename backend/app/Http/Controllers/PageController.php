<?php

namespace App\Http\Controllers;

use App\Sygnet\AlertTypes;
use App\Sygnet\Areas;
use App\Sygnet\AttackFactory;
use App\Sygnet\Broadcaster;
use App\Sygnet\FrameBuilder;
use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyStore;
use App\Sygnet\TrustStoreExporter;
use Illuminate\Http\Response;
use Illuminate\View\View;
use Symfony\Component\HttpFoundation\BinaryFileResponse;

/**
 * Strony konsoli: /console (nadawanie), /attack (laboratorium ataków), /keys (klucze i eksport dla aplikacji).
 */
class PageController extends Controller
{
    public function __construct(
        private readonly Broadcaster $broadcaster,
        private readonly IssuerRegistry $issuers,
        private readonly KeyStore $keys,
    ) {}

    public function console(): View
    {
        return view('console', ['boot' => $this->boot()]);
    }

    public function attack(): View
    {
        return view('attack', ['boot' => $this->boot() + ['attacks' => AttackFactory::catalog()]]);
    }

    public function keys(TrustStoreExporter $exporter): View
    {
        $trust = $this->broadcaster->trust();
        $revoked = $this->broadcaster->revokedIssuers();
        $rows = [];

        foreach ($this->issuers->issuers() as $id => $issuer) {
            $cert = $trust?->certificate($id);
            $rows[] = [
                'id' => $id,
                'name' => $issuer['name'],
                'scopes' => array_map(fn ($s) => Areas::name($s), $issuer['scopes']),
                'has_key' => $this->keys->has($id),
                'fingerprint' => $this->keys->has($id) ? $this->keys->fingerprint($id) : null,
                'in_trust_store' => $cert !== null,
                'cert_from' => $cert?->validFrom,
                'cert_until' => $cert?->validUntil,
                'revoked' => in_array($id, $revoked, true),
            ];
        }

        return view('keys', ['boot' => $this->boot() + [
            'issuerRows' => $rows,
            'rejected' => $trust?->rejected() ?? [],
            'hackerFingerprint' => $this->keys->has(KeyStore::HACKER) ? $this->keys->fingerprint(KeyStore::HACKER) : null,
            'exports' => [
                'trust_store' => is_file($exporter->path(TrustStoreExporter::TRUST_STORE_FILE)),
                'root_key' => is_file($exporter->path(TrustStoreExporter::ROOT_KEY_FILE)),
            ],
        ]]);
    }

    /** GET /keys/export/{file} – pliki dla aplikacji Unity (tylko klucze publiczne). */
    public function export(string $file, TrustStoreExporter $exporter): BinaryFileResponse|Response
    {
        abort_unless(in_array($file, [TrustStoreExporter::TRUST_STORE_FILE, TrustStoreExporter::ROOT_KEY_FILE], true), 404);
        $path = $exporter->path($file);
        abort_unless(is_file($path), 404, 'Brak eksportu. Uruchom: php artisan sygnet:init');

        return response()->download($path, $file);
    }

    /** Dane startowe dla Alpine.js – tylko publiczne informacje. */
    private function boot(): array
    {
        $revoked = $this->broadcaster->revokedIssuers();
        $issuers = [];

        foreach ($this->issuers->issuers() as $id => $issuer) {
            $issuers[] = [
                'id' => $id,
                'name' => $issuer['name'],
                'scopes' => $issuer['scopes'],
                'areas' => $this->issuers->areasFor($id),
                'has_key' => $this->keys->has($id),
                'revoked' => in_array($id, $revoked, true),
                'fingerprint' => $this->keys->has($id) ? $this->keys->fingerprint($id) : null,
            ];
        }

        $types = [];
        foreach (AlertTypes::issuable() as $id => $t) {
            $types[] = ['id' => $id] + $t;
        }

        $areas = [];
        foreach (Areas::all() as $code => $name) {
            $areas[] = ['code' => $code, 'name' => $name];
        }

        return [
            'issuers' => $issuers,
            'types' => $types,
            'areas' => $areas,
            'validity' => config('sygnet.validity_options'),
            'repeat' => (int) config('sygnet.repeat_default'),
            'userArea' => (int) config('sygnet.demo_user_area'),
            'rootFingerprint' => $this->keys->has(KeyStore::ROOT) ? $this->keys->fingerprint(KeyStore::ROOT) : null,
            'testKeys' => $this->keys->has(KeyStore::ROOT) && $this->keys->fingerprint(KeyStore::ROOT) === '6A38-03D5-F059-902A',
            'noteMax' => 60,
            'qrPrefix' => FrameBuilder::QR_PREFIX,
        ];
    }
}
