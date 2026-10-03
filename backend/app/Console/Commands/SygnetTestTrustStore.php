<?php

namespace App\Console\Commands;

use App\Sygnet\KeyStore;
use Illuminate\Console\Command;
use RuntimeException;

class SygnetTestTrustStore extends Command
{
    protected $signature = 'sygnet:test-trust-store
                            {--force : Overwrite existing trust store}';

    protected $description = 'Create the SYGNET test trust store from test vectors';

    public function handle(): int
    {
        $source = storage_path('app/keys/testvectors.json');
        $target = storage_path('app/export/sygnet_trust_store.json');

        if (!is_file($source)) {
            $this->error("Missing {$source}");
            return self::FAILURE;
        }

        if (is_file($target) && !$this->option('force')) {
            $this->error(
                "Trust store already exists. Use --force to overwrite."
            );

            return self::FAILURE;
        }

        $data = json_decode(
            file_get_contents($source),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        $trustStore = $data['trust_store'] ?? null;

        if (!is_array($trustStore)) {
            throw new RuntimeException(
                'testvectors.json does not contain trust_store.'
            );
        }

        $directory = dirname($target);

        if (!is_dir($directory)) {
            mkdir($directory, 0700, true);
        }

        file_put_contents(
            $target,
            json_encode(
                $trustStore,
                JSON_PRETTY_PRINT |
                JSON_UNESCAPED_UNICODE |
                JSON_UNESCAPED_SLASHES
            ) . PHP_EOL,
            LOCK_EX
        );

        @chmod($target, 0600);

        $this->info("Created {$target}");

        return self::SUCCESS;
    }
}
