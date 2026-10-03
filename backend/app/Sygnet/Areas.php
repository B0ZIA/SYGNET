<?php

namespace App\Sygnet;

final class Areas
{
    public static function covers(int $issuerId, int $areaCode): bool
    {
        $issuers = config('sygnet.issuers', []);

        if (!isset($issuers[$issuerId])) {
            return false;
        }

        $scopes = $issuers[$issuerId]['scopes'] ?? [];

        if (in_array(0, $scopes, true)) {
            return true;
        }

        if ($areaCode === 0) {
            return false;
        }

        foreach ($scopes as $scope) {
            if ($scope === $areaCode) {
                return true;
            }

            // Województwo obejmuje konkretne miasto.
            if ($scope === 14 && $areaCode === 1465) {
                return true;
            }

            if ($scope === 12 && $areaCode === 1261) {
                return true;
            }
        }

        return false;
    }

    public static function name(int $areaCode): ?string
    {
        return config("sygnet.areas.$areaCode");
    }
}
