<?php

namespace App\Sygnet;

use Throwable;

/**
 * Czy klucze konsoli i wyeksportowany trust store do siebie pasują. Typowy błąd po wdrożeniu: na serwerze są prawdziwe
 * klucze, ale stary trust store (np. testowy) – wtedy telefon i kontrolna weryfikacja widzą „nieznanego nadawcę”.
 * Nie ujawnia niczego tajnego (tylko odciski kluczy publicznych).
 */
final class KeyHealth
{
    /**
     * @return array{ok: bool, problems: string[], root: ?string, store_root: ?string, app_root: ?string, accepted: int[], issuers: array}
     */
    public static function check(KeyStore $keys, TrustStoreExporter $exporter, IssuerRegistry $issuers): array
    {
        $problems = [];
        $root = $keys->has(KeyStore::ROOT) ? $keys->fingerprint(KeyStore::ROOT) : null;
        $storeRoot = null;
        $trust = null;
        $rows = [];

        $appRoot = self::appRoot();

        if ($root === null) {
            $problems[] = 'Brak klucza ROOT w '.$keys->directory().' – skopiuj storage/app/keys z laptopa (nie uruchamiaj sygnet:init).';
        } elseif ($appRoot !== null && $appRoot !== $root) {
            $problems[] = "Klucze konsoli (ROOT {$root}) to nie te, które zna aplikacja (ROOT {$appRoot}) – telefony odrzucą "
                .'każdy komunikat. Skopiuj storage/app/keys i storage/app/export z laptopa (nie uruchamiaj sygnet:init).';
        }

        $path = $exporter->path(TrustStoreExporter::TRUST_STORE_FILE);
        if (! is_file($path)) {
            $problems[] = "Brak {$path} – skopiuj storage/app/export z laptopa albo uruchom: php artisan sygnet:export";
        } elseif ($root !== null) {
            try {
                $json = json_decode(file_get_contents($path), true, 512, JSON_THROW_ON_ERROR);
                $storeRoot = $json['root_fingerprint'] ?? null;
                $trust = TrustStore::fromArray($json, $keys->publicKey(KeyStore::ROOT));
            } catch (Throwable $e) {
                $problems[] = 'Uszkodzony trust store: '.$e->getMessage();
            }
            if ($storeRoot !== null && $storeRoot !== $root) {
                $problems[] = "Trust store jest od innego ROOT ({$storeRoot}) niż klucze konsoli ({$root}) – "
                    .'stary eksport. Skopiuj storage/app/export z laptopa albo uruchom: php artisan sygnet:export';
            } elseif ($trust && $trust->rejected()) {
                $problems[] = count($trust->rejected()).' certyfikat(y) z trust store nie przechodzą weryfikacji kluczem ROOT – uruchom: php artisan sygnet:export';
            }
        }

        foreach ($issuers->issuers() as $id => $issuer) {
            $cert = $trust?->certificate($id);
            $hasKey = $keys->has($id);
            $match = $cert && $hasKey && hash_equals($cert->publicKey, $keys->publicKey($id));
            if ($hasKey && $trust && ! $cert && ! $trust->rejected()) {
                $problems[] = "Wydawca {$id} ({$issuer['name']}) ma klucz, ale nie ma go w trust store – uruchom: php artisan sygnet:export";
            }
            if ($hasKey && $cert && ! $match) {
                $problems[] = "Klucz wydawcy {$id} nie pasuje do jego certyfikatu – klucze i eksport pochodzą z różnych sygnet:init.";
            }
            $rows[] = ['id' => $id, 'name' => $issuer['name'], 'has_key' => $hasKey, 'in_store' => (bool) $cert, 'match' => $match];
        }

        return [
            'ok' => $problems === [],
            'problems' => array_values(array_unique($problems)),
            'root' => $root,
            'store_root' => $storeRoot,
            'app_root' => $appRoot,
            'accepted' => $trust ? array_keys($trust->certificates()) : [],
            'issuers' => $rows,
        ];
    }

    /** Odcisk ROOT wbudowany w aplikację (SYGNET_Unity/.../RootKey.cs w repo); null, gdy pliku nie ma. */
    public static function appRoot(): ?string
    {
        $path = config('sygnet.paths.app_root_key');
        if (! $path || ! is_file($path)) {
            return null;
        }

        return preg_match('/Fingerprint\s*=\s*"([0-9A-F]{4}(?:-[0-9A-F]{4}){3})"/', file_get_contents($path), $m) ? $m[1] : null;
    }
}
