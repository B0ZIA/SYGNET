<?php

namespace App\Sygnet;

use RuntimeException;

/**
 * Wydawcy z config/sygnet.php (PROTOCOL.md §5.2). ROOT (0) podpisuje tylko certyfikaty i KEY_REVOKE.
 */
final class IssuerRegistry
{
    /** @return array<int, array{name: string, scopes: int[]}> */
    public function all(): array
    {
        return config('sygnet.issuers', []);
    }

    /** Wydawcy komunikatów (bez ROOT). */
    public function issuers(): array
    {
        return array_filter($this->all(), fn ($_, $id) => $id !== KeyStore::ROOT, ARRAY_FILTER_USE_BOTH);
    }

    public function exists(int $issuerId): bool
    {
        return isset($this->all()[$issuerId]);
    }

    public function get(int $issuerId): array
    {
        return $this->all()[$issuerId] ?? throw new RuntimeException("Nieznany wydawca {$issuerId}.");
    }

    public function name(int $issuerId): string
    {
        return $this->all()[$issuerId]['name'] ?? "Wydawca {$issuerId}";
    }

    /** @return int[] */
    public function scopes(int $issuerId): array
    {
        return array_map('intval', $this->get($issuerId)['scopes'] ?? []);
    }

    public function canSign(int $issuerId, int $area): bool
    {
        return $this->exists($issuerId) && Areas::anyCovers($this->scopes($issuerId), $area);
    }

    /** Obszary z listy demo, dla których wydawca może podpisać komunikat. */
    public function areasFor(int $issuerId): array
    {
        return array_values(array_filter(array_keys(Areas::all()), fn ($a) => $this->canSign($issuerId, $a)));
    }
}
