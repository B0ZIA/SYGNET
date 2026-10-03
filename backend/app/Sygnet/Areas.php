<?php

namespace App\Sygnet;

/**
 * Kody obszarów TERYT (PROTOCOL.md §6): 0 = Polska, 1..99 = województwo, 100..9999 = powiat.
 */
final class Areas
{
    /** Czy obszar $c obejmuje obszar $a: covers(c, a) = c == 0 || c == a || (c < 100 && a / 100 == c). */
    public static function covers(int $c, int $a): bool
    {
        return $c === 0 || $c === $a || ($c < 100 && intdiv($a, 100) === $c);
    }

    /** Czy któryś z zakresów wydawcy obejmuje obszar (uprawnienie do podpisu). */
    public static function anyCovers(array $scopes, int $area): bool
    {
        foreach ($scopes as $scope) {
            if (self::covers((int) $scope, $area)) {
                return true;
            }
        }

        return false;
    }

    /** @return array<int, string> */
    public static function all(): array
    {
        return config('sygnet.areas', []);
    }

    public static function name(int $code): string
    {
        $areas = self::all();

        if (isset($areas[$code])) {
            return $areas[$code];
        }

        return $code < 100 ? sprintf('województwo %02d', $code) : sprintf('powiat %04d', $code);
    }
}
