<?php

namespace App\Sygnet;

use RuntimeException;

final class IssuerRegistry
{
    /**
     * @return array<int, array<string, mixed>>
     */
    public function all(): array
    {
        return config('sygnet.issuers', []);
    }

    /**
     * @return array<string, mixed>
     */
    public function get(int $issuerId): array
    {
        $issuer = config("sygnet.issuers.{$issuerId}");

        if (!is_array($issuer)) {
            throw new RuntimeException(
                "Unknown issuer {$issuerId}."
            );
        }

        return $issuer;
    }

    public function exists(int $issuerId): bool
    {
        return is_array(config("sygnet.issuers.{$issuerId}"));
    }

    /**
     * @return array<int, int>
     */
    public function allowedAreas(int $issuerId): array
    {
        return array_map(
            'intval',
            $this->get($issuerId)['areas'] ?? []
        );
    }

    public function name(int $issuerId): string
    {
        return (string) (
            $this->get($issuerId)['name']
            ?? "Issuer {$issuerId}"
        );
    }
}
