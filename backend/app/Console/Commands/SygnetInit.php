<?php

namespace App\Console\Commands;

use App\Sygnet\KeyStore;
use Illuminate\Console\Command;
use RuntimeException;

class SygnetInit extends Command
{
    protected $signature = 'sygnet:init
                            {--test-seeds : Install protocol test-vector seeds}
                            {--force : Overwrite existing seed files}';

    protected $description = 'Initialize SYGNET keys and trust-store material';

    public function handle(): int
    {
        $keyStore = KeyStore::fromConfig();

        if ($this->option('test-seeds')) {
            return $this->installTestSeeds($keyStore);
        }

        $this->error(
            'Normal production/demo initialization is not implemented yet.'
        );

        return self::FAILURE;
    }

    private function installTestSeeds(KeyStore $keyStore): int
    {
        $path = storage_path('app/keys/testvectors.json');

        if (!is_file($path)) {
            $this->error("Missing test vectors: {$path}");
            return self::FAILURE;
        }

        $vectors = json_decode(
            file_get_contents($path),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        $seeds = $vectors['seeds_hex'] ?? null;

        if (!is_array($seeds)) {
            $this->error('testvectors.json does not contain seeds_hex.');
            return self::FAILURE;
        }

        foreach ($seeds as $issuerId => $seedHex) {
            /*
             * "hacker" is intentionally stored separately under
             * hacker.seed. It is needed by the attack laboratory.
             */
            $filename = $issuerId === 'hacker'
                ? 'hacker.seed'
                : "{$issuerId}.seed";

            $seed = hex2bin($seedHex);

            if ($seed === false || strlen($seed) !== 32) {
                throw new RuntimeException(
                    "Invalid test seed for {$issuerId}."
                );
            }

            $target = storage_path("app/keys/{$filename}");

            if (is_file($target) && !$this->option('force')) {
                $this->line(
                    "<comment>SKIP</comment> {$filename} already exists"
                );

                continue;
            }

            $keyStore->saveSeed(
                $issuerId === 'hacker' ? 'hacker' : (int) $issuerId,
                $seed
            );

            $this->info("Installed {$filename}");
        }

        $this->newLine();
        $this->info('Test-vector seeds installed.');
        $this->line('These seeds are for protocol tests only.');

        return self::SUCCESS;
    }
}
