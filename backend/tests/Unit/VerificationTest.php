<?php

namespace Tests\Unit;

use App\Sygnet\Areas;
use App\Sygnet\Certificate;
use App\Sygnet\FrameBuilder;
use App\Sygnet\FrameVerifier;
use App\Sygnet\KeyStore;
use App\Sygnet\Payload;
use App\Sygnet\TrustStore;
use App\Sygnet\TrustStoreExporter;
use Tests\Concerns\UsesTestKeys;
use Tests\TestCase;

class VerificationTest extends TestCase
{
    use UsesTestKeys;

    private FrameVerifier $verifier;

    protected function setUp(): void
    {
        parent::setUp();
        $this->installTestKeys();
        $this->verifier = new FrameVerifier(TrustStoreExporter::fromConfig()->load());
    }

    public function test_vectors_give_expected_status_now_and_three_days_later(): void
    {
        $json = $this->vectors->json;

        foreach ($json['vectors'] as $name => $tv) {
            $frame = hex2bin($tv['frame_hex']);

            $now = $this->verifier->verify($frame, $json['now_for_tests'], $json['user_area']);
            $this->assertSame($tv['expected_status'], $now['status'], $name);
            $this->assertSame($tv['reason'], $now['reason'], $name);

            $later = $this->verifier->verify($frame, $json['now_expired'], $json['user_area']);
            $this->assertSame($tv['expected_status_3_days_later'], $later['status'], $name);
        }
    }

    public function test_exported_trust_store_equals_vectors(): void
    {
        $exported = json_decode(file_get_contents(TrustStoreExporter::fromConfig()->path('sygnet_trust_store.json')), true);
        $this->assertSame($this->vectors->json['trust_store'], $exported);
    }

    public function test_certificate_uses_u8_counts(): void
    {
        $b64 = $this->vectors->json['trust_store']['issuers'][0]['cert_b64'];
        $cert = Certificate::fromBase64($b64);

        $this->assertSame(1, $cert->issuerId);
        $this->assertSame([0], $cert->scopes);
        $this->assertSame('Dowództwo Operacyjne RSZ', $cert->name);
        $this->assertSame($b64, base64_encode($cert->toBytes()));
    }

    public function test_tampered_trust_store_entry_is_rejected(): void
    {
        $json = $this->vectors->json['trust_store'];
        $json['issuers'][0]['root_sig_b64'] = $json['issuers'][1]['root_sig_b64'];
        $trust = TrustStore::fromArray($json, hex2bin($this->vectors->json['root_pub_hex']));

        $this->assertFalse($trust->hasIssuer(1));
        $this->assertCount(1, $trust->rejected());
    }

    public function test_other_area_duplicate_and_revoked(): void
    {
        $json = $this->vectors->json;
        $tv1 = hex2bin($json['vectors']['TV1_air_raid_single']['frame_hex']);
        $now = $json['now_for_tests'];

        $this->assertSame('VERIFIED_OTHER_AREA', $this->verifier->verify($tv1, $now, 1261)['status']);
        $this->assertSame('DUPLICATE', $this->verifier->verify($tv1, $now, 1465, [], ['1:1' => true])['status']);
        $this->assertSame('REVOKED_ISSUER:1', $this->verifier->verify($tv1, $now, 1465, [1])['reason']);
        $this->assertSame('MALFORMED', $this->verifier->verify(substr($tv1, 0, -1), $now, 1465)['status']);
    }

    public function test_root_rules(): void
    {
        $keys = KeyStore::fromConfig();
        $b = new FrameBuilder($keys);
        $now = $this->vectors->json['now_for_tests'];

        $revoke = $b->build(new Payload(0, 250, 0, $now, 60, 1, pack('n', 6)), [0]);
        $this->assertSame('VERIFIED', $this->verifier->verify($revoke, $now, 1261)['status']);

        $rootAlarm = $b->build(new Payload(0, 1, 0, $now, 60, 2, ''), [0]);
        $this->assertSame('ROOT_ONLY_REVOKE', $this->verifier->verify($rootAlarm, $now, 1261)['reason']);

        $fakeRevoke = $b->build(new Payload(1, 250, 0, $now, 60, 3, pack('n', 6)), [1]);
        $this->assertSame('REVOKE_NOT_ROOT', $this->verifier->verify($fakeRevoke, $now, 1261)['reason']);
    }

    public function test_area_coverage_rule(): void
    {
        $this->assertTrue(Areas::covers(0, 1261));
        $this->assertTrue(Areas::covers(12, 1261));
        $this->assertTrue(Areas::covers(14, 1465));
        $this->assertFalse(Areas::covers(14, 1261));
        $this->assertFalse(Areas::covers(1465, 14));
        $this->assertFalse(Areas::covers(1261, 0));
    }
}
