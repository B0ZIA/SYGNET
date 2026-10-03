<?php

namespace Tests\Unit;

use App\Sygnet\Crc16;
use PHPUnit\Framework\TestCase;

final class Crc16Test extends TestCase
{
    public function test_known_crc_vector(): void
    {
        $this->assertSame(
            0x29B1,
            Crc16::calculate('123456789')
        );
    }

    public function test_crc_bytes_are_big_endian(): void
    {
        $this->assertSame(
            '29b1',
            bin2hex(Crc16::bytes('123456789'))
        );
    }
}
