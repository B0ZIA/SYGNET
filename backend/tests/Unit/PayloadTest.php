<?php

namespace Tests\Unit;

use App\Sygnet\Payload;
use PHPUnit\Framework\TestCase;

final class PayloadTest extends TestCase
{
    public function test_payload_serialization(): void
    {
        $payload = new Payload(
            issuerId: 3,
            type: 2,
            areaCode: 1465,
            timestamp: 1750000000,
            validMinutes: 120,
            sequence: 7,
            note: 'Schron: metro Świętokrzyska',
        );

        $bytes = $payload->toBytes();

        $this->assertSame(
            strlen($payload->note) + 16,
            strlen($bytes)
        );

        $this->assertSame(
            $payload->note,
            substr($bytes, -strlen($payload->note))
        );
    }

    public function test_note_is_limited_by_bytes(): void
    {
        $this->expectException(\InvalidArgumentException::class);

        new Payload(
            issuerId: 3,
            type: 2,
            areaCode: 1465,
            timestamp: 1750000000,
            validMinutes: 120,
            sequence: 1,
            note: str_repeat('A', 61),
        );
    }
}
