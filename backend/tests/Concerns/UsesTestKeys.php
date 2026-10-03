<?php

namespace Tests\Concerns;

use App\Sygnet\KeyStore;
use App\Sygnet\TestVectors;
use App\Sygnet\TrustStoreExporter;
use Illuminate\Support\Str;

/**
 * Klucze testowe (PROTOCOL.md §9) w katalogu tymczasowym + wyeksportowany trust store – testy nie ruszają kluczy konsoli.
 * Opcjonalnie dokłada klucze wydawców spoza wektorów (np. 4 i 7 dla Krakowa).
 */
trait UsesTestKeys
{
    protected string $keysDir;

    protected TestVectors $vectors;

    protected function installTestKeys(array $extraIssuers = []): KeyStore
    {
        $this->keysDir = sys_get_temp_dir().DIRECTORY_SEPARATOR.'sygnet_test_'.Str::random(8);
        config([
            'sygnet.paths.keys' => $this->keysDir.DIRECTORY_SEPARATOR.'keys',
            'sygnet.paths.export' => $this->keysDir.DIRECTORY_SEPARATOR.'export',
        ]);

        $keys = KeyStore::fromConfig();
        $this->vectors = TestVectors::load();
        $this->vectors->installSeeds($keys);
        foreach ($extraIssuers as $id) {
            $keys->saveSeed($id, str_repeat(chr(0x40 + $id), 32));
        }

        TrustStoreExporter::fromConfig()->export(TestVectors::CERT_FROM, TestVectors::CERT_UNTIL, 'test');

        return $keys;
    }

    protected function tearDown(): void
    {
        if (isset($this->keysDir) && is_dir($this->keysDir)) {
            $it = new \RecursiveIteratorIterator(
                new \RecursiveDirectoryIterator($this->keysDir, \FilesystemIterator::SKIP_DOTS),
                \RecursiveIteratorIterator::CHILD_FIRST,
            );
            foreach ($it as $f) {
                $f->isDir() ? rmdir($f->getPathname()) : unlink($f->getPathname());
            }
            rmdir($this->keysDir);
        }
        parent::tearDown();
    }
}
