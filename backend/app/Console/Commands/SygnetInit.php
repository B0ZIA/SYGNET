<?php

namespace App\Console\Commands;

use App\Sygnet\Areas;
use App\Sygnet\IssuerRegistry;
use App\Sygnet\KeyStore;
use App\Sygnet\TestVectors;
use App\Sygnet\TrustStoreExporter;
use Illuminate\Console\Command;

/**
 * Klucze ROOT, wydawców i HAKERA + eksport trust store i RootKey.cs dla aplikacji (CONSOLE_LARAVEL.md §5.1).
 */
class SygnetInit extends Command
{
    protected $signature = 'sygnet:init
                            {--test-seeds : Seedy testowe z PROTOCOL.md §9 (zgodne z wektorami i obecną aplikacją testową)}
                            {--force : Nadpisz istniejące klucze}';

    protected $description = 'Generuje klucze SYGNET i eksportuje trust store + RootKey.cs dla aplikacji';

    public function handle(IssuerRegistry $issuers): int
    {
        $keys = KeyStore::fromConfig();

        if ($keys->has(KeyStore::ROOT) && ! $this->option('force')) {
            $this->warn('Klucze już istnieją (ROOT '.$keys->fingerprint(KeyStore::ROOT).').');
            $this->line('Nowe klucze unieważnią zaufanie telefonów z obecnym ROOT. Jeśli na pewno: <comment>--force</comment>');

            return self::FAILURE;
        }

        foreach (glob($keys->directory().DIRECTORY_SEPARATOR.'*.seed') ?: [] as $old) {
            unlink($old);
        }

        if ($this->option('test-seeds')) {
            TestVectors::load()->installSeeds($keys);
            [$from, $until] = [TestVectors::CERT_FROM, TestVectors::CERT_UNTIL];
            $note = 'Klucze TESTOWE (seedy z PROTOCOL.md §9) – NIE do prawdziwego demo.';
        } else {
            foreach (array_keys($issuers->all()) as $id) {
                $keys->saveSeed($id, random_bytes(SODIUM_CRYPTO_SIGN_SEEDBYTES));
            }
            $keys->saveSeed(KeyStore::HACKER, random_bytes(SODIUM_CRYPTO_SIGN_SEEDBYTES));
            $from = strtotime('today UTC');
            $until = strtotime('+'.config('sygnet.cert_valid_years').' years', $from);
            $note = 'Klucze demo wygenerowane '.gmdate('Y-m-d H:i').' UTC (losowe seedy).';
        }

        $paths = TrustStoreExporter::fromConfig()->export($from, $until, $note);

        $rows = [];
        foreach ($issuers->issuers() as $id => $issuer) {
            $rows[] = [
                $id,
                $issuer['name'],
                implode(', ', array_map(fn ($s) => Areas::name($s), $issuer['scopes'])),
                $keys->has($id) ? $keys->fingerprint($id) : '– (brak klucza testowego)',
            ];
        }
        $this->table(['ID', 'Wydawca', 'Zakres', 'Odcisk klucza'], $rows);
        $this->line('Certyfikaty ważne: '.gmdate('Y-m-d', $from).' – '.gmdate('Y-m-d', $until));
        $this->line('Klucz HAKERA (poza trust store): '.$keys->fingerprint(KeyStore::HACKER));
        $this->newLine();
        $this->line('  <options=bold>ODCISK ROOT:  '.$keys->fingerprint(KeyStore::ROOT).'</>');
        $this->newLine();
        $this->info('Eksport dla Unity:');
        $this->line('  '.$paths['trust_store'].'  →  SYGNET_Unity/Assets/Resources/sygnet_trust_store.json');
        $this->line('  '.$paths['root_key'].'  →  SYGNET_Unity/Assets/Sygnet/App/RootKey.cs');

        return self::SUCCESS;
    }
}
