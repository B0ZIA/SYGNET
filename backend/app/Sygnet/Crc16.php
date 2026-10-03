<?php

namespace App\Sygnet;

final class Crc16
{
    public static function calculate(string $data): int
    {
        $crc = 0xFFFF;

        $length = strlen($data);

        for ($i = 0; $i < $length; $i++) {
            $crc ^= ord($data[$i]) << 8;

            for ($bit = 0; $bit < 8; $bit++) {
                if (($crc & 0x8000) !== 0) {
                    $crc = (($crc << 1) ^ 0x1021) & 0xFFFF;
                } else {
                    $crc = ($crc << 1) & 0xFFFF;
                }
            }
        }

        return $crc;
    }

    public static function bytes(string $data): string
    {
        return pack('n', self::calculate($data));
    }
}
