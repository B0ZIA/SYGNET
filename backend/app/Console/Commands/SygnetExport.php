<?php

namespace App\Console\Commands;

use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyHealth;
use App\Sygnet\KeyStore;
use App\Sygnet\TrustStoreExporter;
use Illuminate\Console\Command;

/**
 * Odtwarza eksport (trust store + RootKey.cs) z kluczy, które już są w storage/app/keys – NIE generuje nowych kluczy.
 * Przydatne na serwerze, gdy skopiowano same klucze albo został stary eksport. Telefony dalej działają: klucze te same.
 */
class SygnetExport extends Command
{
    protected $signature = 'sygnet:export';

    protected $description = 'Odtwarza sygnet_trust_store.json i RootKey.cs z istniejących kluczy (bez nowych kluczy)';

    public function handle(IssuerRegistry $issuers): int
    {
        $keys = KeyStore::fromConfig();

        if (! $keys->has(KeyStore::ROOT)) {
            $this->error('Brak klucza ROOT w '.$keys->directory().' – najpierw skopiuj storage/app/keys z laptopa.');

            return self::FAILURE;
        }

        $appRoot = KeyHealth::appRoot();
        if ($appRoot !== null && $appRoot !== $keys->fingerprint(KeyStore::ROOT)) {
            $this->error('Klucze konsoli (ROOT '.$keys->fingerprint(KeyStore::ROOT).") to nie te, które zna aplikacja (ROOT {$appRoot}).");
            $this->line('Eksport z tych kluczy nic nie da – telefony i tak odrzucą komunikaty. Skopiuj storage/app/keys z laptopa.');

            return self::FAILURE;
        }

        $from = strtotime('today UTC');
        $until = strtotime('+'.config('sygnet.cert_valid_years').' years', $from);
        $exporter = TrustStoreExporter::fromConfig();
        $paths = $exporter->export($from, $until, 'Eksport odtworzony z istniejących kluczy '.gmdate('Y-m-d H:i').' UTC.');

        $this->info('ROOT '.$keys->fingerprint(KeyStore::ROOT).' – zapisano:');
        $this->line('  '.$paths['trust_store']);
        $this->line('  '.$paths['root_key']);

        $h = KeyHealth::check($keys, $exporter, $issuers);
        $this->line('Wydawcy w trust store: '.implode(', ', $h['accepted']));

        return $h['ok'] ? self::SUCCESS : self::FAILURE;
    }
}
