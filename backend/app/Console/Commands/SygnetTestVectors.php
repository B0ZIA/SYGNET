<?php

namespace App\Console\Commands;

use App\Sygnet\KeyStore;
use App\Sygnet\TestVectors;
use Illuminate\Console\Command;
use Illuminate\Support\Str;

/**
 * Pierwszy kamień milowy (L2): z seedów testowych odtwarza TV1–TV6 i porównuje bajt w bajt z testvectors.json.
 * Działa na tymczasowym magazynie kluczy – nie rusza kluczy konsoli.
 */
class SygnetTestVectors extends Command
{
    protected $signature = 'sygnet:testvectors {--path= : Ścieżka do testvectors.json}';

    protected $description = 'Porównuje ramki budowane przez konsolę z wektorami testowymi (musi wypisać ALL OK)';

    public function handle(): int
    {
        $vectors = TestVectors::load($this->option('path') ?: null);
        $dir = sys_get_temp_dir().DIRECTORY_SEPARATOR.'sygnet_tv_'.Str::random(8);
        $keys = new KeyStore($dir);

        try {
            $vectors->installSeeds($keys);
            $rows = $vectors->compare($keys);
        } finally {
            array_map('unlink', glob($dir.DIRECTORY_SEPARATOR.'*') ?: []);
            @rmdir($dir);
        }

        $this->table(['Sprawdzenie', 'Wynik', ''], array_map(
            fn ($r) => [$r['check'], $r['ok'] ? '<info>OK</info>' : '<error>BŁĄD</error>', $r['detail']],
            $rows,
        ));

        $failed = array_filter($rows, fn ($r) => ! $r['ok']);
        if ($failed) {
            $this->error(count($failed).' niezgodności z wektorami testowymi.');

            return self::FAILURE;
        }

        $this->info('ALL OK');

        return self::SUCCESS;
    }
}
