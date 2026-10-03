<?php

namespace App\Console\Commands;

use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyHealth;
use App\Sygnet\KeyStore;
use App\Sygnet\TrustStoreExporter;
use Illuminate\Console\Command;

/**
 * Diagnostyka po wdrożeniu: czy klucze i trust store pasują do siebie (np. przy „UNKNOWN_ISSUER” w kontrolnej weryfikacji).
 */
class SygnetCheck extends Command
{
    protected $signature = 'sygnet:check';

    protected $description = 'Sprawdza, czy klucze konsoli i wyeksportowany trust store do siebie pasują';

    public function handle(IssuerRegistry $issuers): int
    {
        $keys = KeyStore::fromConfig();
        $h = KeyHealth::check($keys, TrustStoreExporter::fromConfig(), $issuers);

        $this->line('Katalog kluczy:   '.$keys->directory());
        $this->line('ROOT z kluczy:    '.($h['root'] ?? '–'));
        $this->line('ROOT w eksporcie: '.($h['store_root'] ?? '–'));
        $this->line('ROOT aplikacji:   '.($h['app_root'] ?? '– (brak RootKey.cs w repo)'));
        $this->table(['ID', 'Wydawca', 'Klucz', 'W trust store', 'Pasuje'], array_map(fn ($r) => [
            $r['id'], $r['name'], $r['has_key'] ? 'tak' : '–', $r['in_store'] ? 'tak' : '–', $r['match'] ? 'tak' : '–',
        ], $h['issuers']));

        if ($h['ok']) {
            $this->info('OK – klucze i trust store pasują do siebie.');

            return self::SUCCESS;
        }

        foreach ($h['problems'] as $p) {
            $this->error($p);
        }

        return self::FAILURE;
    }
}
