<?php

namespace Tests\Unit;

use App\Sygnet\FrameBuilder;
use App\Sygnet\Payload;
use Tests\Concerns\UsesTestKeys;
use Tests\TestCase;

class FrameBuilderTest extends TestCase
{
    use UsesTestKeys;

    public function test_tv1_tv6_are_byte_for_byte_identical(): void
    {
        $keys = $this->installTestKeys();

        foreach ($this->vectors->rebuild($keys) as $name => $tv) {
            $expected = $this->vectors->json['vectors'][$name];
            $this->assertSame($expected['payload_hex'], bin2hex($tv['payload']), $name);
            $this->assertSame($expected['frame_hex'], bin2hex($tv['frame']), $name);
            $this->assertSame($expected['qr_text'], FrameBuilder::qrText($tv['frame']), $name);
        }
    }

    public function test_frame_parses_back(): void
    {
        $keys = $this->installTestKeys();
        $payload = new Payload(3, 3, 1465, 1791076320, 240, 7, 'Kierunek: Grodzisk Maz.');
        $frame = (new FrameBuilder($keys))->build($payload, [3, 5]);

        $parsed = FrameBuilder::parse($frame);
        $this->assertEquals($payload, $parsed['payload']);
        $this->assertSame([3, 5], array_column($parsed['signatures'], 0));
        $this->assertSame(155 + strlen($payload->note), strlen($frame));          // PROTOCOL.md §3
        $this->assertSame($frame, FrameBuilder::fromQrText(FrameBuilder::qrText($frame)));
    }

    public function test_genuine_signatures_verify_and_hacker_ones_do_not(): void
    {
        $keys = $this->installTestKeys();
        $tvs = $this->vectors->rebuild($keys);

        foreach (['TV1_air_raid_single' => true, 'TV3_forged_hacker_as_1' => false, 'TV6_tampered_note' => false] as $name => $ok) {
            $f = FrameBuilder::parse($tvs[$name]['frame']);
            [$signer, $sig] = $f['signatures'][0];
            $this->assertSame($ok, sodium_crypto_sign_verify_detached($sig, $f['payload_bytes'], $keys->publicKey($signer)), $name);
        }
    }

    public function test_audio_seconds_match_vectors(): void
    {
        $this->installTestKeys();
        foreach ($this->vectors->json['vectors'] as $name => $tv) {
            $this->assertEqualsWithDelta($tv['audio_seconds'], FrameBuilder::audioSeconds($tv['frame_len'], 2), 0.01, $name);
        }
    }
}
