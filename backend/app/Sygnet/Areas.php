<?php

namespace App\Sygnet;

final class Areas
{
    /**
     * @param int[] $allowedAreas
     */
    public static function covers(
        array $allowedAreas,
        int $targetArea
    ): bool {
        foreach ($allowedAreas as $allowed) {
            $allowed = (int) $allowed;

            /*
             * 0 = entire Poland
             */
            if ($allowed === 0) {
                return true;
            }

            /*
             * Exact area.
             */
            if ($allowed === $targetArea) {
                return true;
            }

            /*
             * Province -> city.
             *
             * Mazowieckie 14 covers Warsaw 1465.
             * Małopolskie 12 covers Kraków 1261.
             */
            if ($allowed === 14 && $targetArea === 1465) {
                return true;
            }

            if ($allowed === 12 && $targetArea === 1261) {
                return true;
            }
        }

        return false;
    }

    public static function name(int $areaCode): string
    {
        return match ($areaCode) {
            0 => 'Polska',
            14 => 'woj. mazowieckie',
            12 => 'woj. małopolskie',
            1465 => 'Warszawa',
            1261 => 'Kraków',
            default => "Obszar {$areaCode}",
        };
    }
}
