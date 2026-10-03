<?php

namespace App\Sygnet;

/**
 * Typy komunikatów (PROTOCOL.md §4). Lista typów krytycznych jest „zaszyta” w aplikacji – tu tylko ją odwzorowujemy.
 */
final class AlertTypes
{
    public const KEY_REVOKE = 250;

    public const EVACUATION = 3;

    /** @return array<int, array<string, mixed>> */
    public static function all(): array
    {
        return config('sygnet.alert_types', []);
    }

    /** Typy, które może wydać zwykły wydawca (bez KEY_REVOKE). */
    public static function issuable(): array
    {
        return array_filter(self::all(), fn (array $t) => ! ($t['root_only'] ?? false));
    }

    public static function exists(int $type): bool
    {
        return isset(self::all()[$type]);
    }

    public static function isCritical(int $type): bool
    {
        return (bool) (self::all()[$type]['critical'] ?? false);
    }

    public static function name(int $type): string
    {
        return self::all()[$type]['name'] ?? "Komunikat (typ {$type})";
    }

    public static function instruction(int $type): string
    {
        return self::all()[$type]['instruction'] ?? 'Zapoznaj się z treścią komunikatu.';
    }
}
