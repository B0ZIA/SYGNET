<?php

namespace Tests\Unit;

use App\Sygnet\FrameVerifier;
use App\Sygnet\KeyStore;
use App\Sygnet\TrustStore;
use Tests\TestCase;

class VerificationTest extends TestCase
{
    private array $vectors;

    protected function setUp(): void
    {
        parent::setUp();

        $this->vectors = json_decode(
            file_get_contents(
                storage_path('app/keys/testvectors.json')
            ),
            true,
            512,
            JSON_THROW_ON_ERROR
        );
    }

    public function test_tv1_is_verified(): void
    {
        $result = $this->verifyVector(
            'TV1_air_raid_single',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('VERIFIED', $result['status']);
        $this->assertSame('OK', $result['reason']);
    }

    public function test_tv2_is_verified(): void
    {
        $result = $this->verifyVector(
            'TV2_evacuation_dual',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('VERIFIED', $result['status']);
        $this->assertSame('OK', $result['reason']);
    }

    public function test_tv3_is_forged(): void
    {
        $result = $this->verifyVector(
            'TV3_forged_hacker_as_1',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('FORGED', $result['status']);
        $this->assertSame('BAD_SIGNATURE:1', $result['reason']);
    }

    public function test_tv4_is_incomplete(): void
    {
        $result = $this->verifyVector(
            'TV4_evacuation_single',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('INCOMPLETE', $result['status']);
        $this->assertSame(
            'DUAL_SIGNATURE_REQUIRED',
            $result['reason']
        );
    }

    public function test_tv5_is_unauthorized(): void
    {
        $result = $this->verifyVector(
            'TV5_unauthorized_area',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('FORGED', $result['status']);
        $this->assertSame(
            'UNAUTHORIZED_AREA:6',
            $result['reason']
        );
    }

    public function test_tv6_is_tampered(): void
    {
        $result = $this->verifyVector(
            'TV6_tampered_note',
            $this->vectors['now_for_tests']
        );

        $this->assertSame('FORGED', $result['status']);
        $this->assertSame(
            'BAD_SIGNATURE:1',
            $result['reason']
        );
    }

    public function test_tv1_is_expired_three_days_later(): void
    {
        $result = $this->verifyVector(
            'TV1_air_raid_single',
            $this->vectors['now_expired']
        );

        $this->assertSame('EXPIRED', $result['status']);
        $this->assertSame(
            'MESSAGE_EXPIRED',
            $result['reason']
        );
    }

    public function test_tv2_is_expired_three_days_later(): void
    {
        $result = $this->verifyVector(
            'TV2_evacuation_dual',
            $this->vectors['now_expired']
        );

        $this->assertSame('EXPIRED', $result['status']);
        $this->assertSame(
            'MESSAGE_EXPIRED',
            $result['reason']
        );
    }

    private function verifyVector(
        string $name,
        int $now
    ): array {
        $vector = $this->vectors['vectors'][$name];

        $frame = hex2bin($vector['frame_hex']);

        $trustStore = TrustStore::fromFile(
            storage_path(
                'app/export/sygnet_trust_store.json'
            ),
            $this->rootPublicKey()
        );

        $verifier = new FrameVerifier($trustStore);

        return $verifier->verify(
            $frame,
            $now
        );
    }

    private function rootPublicKey(): string
    {
        return hex2bin(
            $this->vectors['root_pub_hex']
        );
    }
}
