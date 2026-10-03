<?php

namespace Tests\Unit;

use App\Sygnet\FrameBuilder;
use App\Sygnet\KeyStore;
use App\Sygnet\Payload;
use Tests\TestCase;

class FrameBuilderTest extends TestCase
{
    public function test_tv1_frame_is_byte_for_byte_identical(): void
    {
        $vectors = json_decode(
            file_get_contents(storage_path('app/keys/testvectors.json')),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        $payloadHex = $vectors['vectors']['TV1_air_raid_single']['payload_hex'];
        $expectedFrameHex = $vectors['vectors']['TV1_air_raid_single']['frame_hex'];

        $payloadBytes = hex2bin($payloadHex);

        $payload = $this->payloadFromBytes($payloadBytes);

        $builder = new FrameBuilder(
            KeyStore::fromConfig()
        );

        $frame = $builder->build($payload, [1]);

        $this->assertSame(
            $expectedFrameHex,
            bin2hex($frame)
        );
    }

    public function test_tv2_frame_is_byte_for_byte_identical(): void
    {
        $vectors = json_decode(
            file_get_contents(storage_path('app/keys/testvectors.json')),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        $payloadHex = $vectors['vectors']['TV2_evacuation_dual']['payload_hex'];
        $expectedFrameHex = $vectors['vectors']['TV2_evacuation_dual']['frame_hex'];

        $payloadBytes = hex2bin($payloadHex);

        $payload = $this->payloadFromBytes($payloadBytes);

        $builder = new FrameBuilder(
            KeyStore::fromConfig()
        );

        $frame = $builder->build($payload, [3, 5]);

        $this->assertSame(
            $expectedFrameHex,
            bin2hex($frame)
        );
    }

    public function test_tv1_qr_is_byte_for_byte_identical(): void
    {
        $vectors = json_decode(
            file_get_contents(storage_path('app/keys/testvectors.json')),
            true,
            512,
            JSON_THROW_ON_ERROR
        );

        $payload = $this->payloadFromBytes(
            hex2bin(
                $vectors['vectors']['TV1_air_raid_single']['payload_hex']
            )
        );

        $builder = new FrameBuilder(
            KeyStore::fromConfig()
        );

        $frame = $builder->build($payload, [1]);

        $this->assertSame(
            $vectors['vectors']['TV1_air_raid_single']['qr_text'],
            $builder->qrText($frame)
        );
    }

    private function payloadFromBytes(string $bytes): Payload
    {
        $offset = 0;

        $version = ord($bytes[$offset++]);
        $issuerId = unpack('n', substr($bytes, $offset, 2))[1];
        $offset += 2;

        $type = ord($bytes[$offset++]);

        $areaCode = unpack('n', substr($bytes, $offset, 2))[1];
        $offset += 2;

        $timestamp = unpack('N', substr($bytes, $offset, 4))[1];
        $offset += 4;

        $validMinutes = unpack('n', substr($bytes, $offset, 2))[1];
        $offset += 2;

        $sequence = unpack('n', substr($bytes, $offset, 2))[1];
        $offset += 2;

        $reserved = ord($bytes[$offset++]);

        $noteLength = ord($bytes[$offset++]);

        $note = substr($bytes, $offset, $noteLength);

        $this->assertSame(1, $version);
        $this->assertSame(0, $reserved);

        return new Payload(
            issuerId: $issuerId,
            type: $type,
            areaCode: $areaCode,
            timestamp: $timestamp,
            validMinutes: $validMinutes,
            sequence: $sequence,
            note: $note,
        );
    }
}
