<?php

namespace App\Sygnet;

use RuntimeException;

/**
 * Odtwarza wektory TV1–TV6 z seedów testowych (PROTOCOL.md §9) i porównuje bajt w bajt z testvectors/testvectors.json.
 * Te same dane, co w tools/sygnet_ref.py make_vectors().
 */
final class TestVectors
{
    public const TS = 1791076320;              // 2026-10-04 01:12:00 UTC

    public const CERT_FROM = 1767225600;       // 2026-01-01

    public const CERT_UNTIL = 1830297600;      // 2028-01-01

    public const USER_AREA = 1465;

    public function __construct(
        public readonly array $json,
    ) {}

    public static function load(?string $path = null): self
    {
        $path ??= config('sygnet.paths.testvectors');

        if (! is_file($path)) {
            throw new RuntimeException("Brak wektorów testowych: {$path}");
        }

        return new self(json_decode(file_get_contents($path), true, 512, JSON_THROW_ON_ERROR));
    }

    /** @return array<string, string> alias klucza → seed (32 B) */
    public function seeds(): array
    {
        $out = [];
        foreach ($this->json['seeds_hex'] as $id => $hex) {
            $out[$id] = hex2bin($hex);
        }

        return $out;
    }

    /** Zapisuje seedy testowe do magazynu kluczy. */
    public function installSeeds(KeyStore $keys): void
    {
        foreach ($this->seeds() as $id => $seed) {
            $keys->saveSeed($id, $seed);
        }
    }

    /**
     * Buduje wektory tak jak sygnet_ref.py.
     *
     * @return array<string, array{payload: string, frame: string}>
     */
    public function rebuild(KeyStore $keys): array
    {
        $b = new FrameBuilder($keys);
        $p1 = new Payload(1, 1, 1465, self::TS, 120, 1, 'Schron: metro Świętokrzyska');
        $p2 = new Payload(3, AlertTypes::EVACUATION, 1465, self::TS, 240, 7, 'Kierunek: Grodzisk Maz.');
        $p5 = new Payload(6, 1, 1261, self::TS, 120, 1, '');

        $v = [
            'TV1_air_raid_single' => [$p1->toBytes(), $b->build($p1, [1])],
            'TV2_evacuation_dual' => [$p2->toBytes(), $b->build($p2, [3, 5])],
            'TV3_forged_hacker_as_1' => [$p1->toBytes(), $b->build($p1, [[1, KeyStore::HACKER]])],
            'TV4_evacuation_single' => [$p2->toBytes(), $b->build($p2, [3])],
            'TV5_unauthorized_area' => [$p5->toBytes(), $b->build($p5, [6])],
        ];

        $tampered = $v['TV1_air_raid_single'][1];
        $tampered[4 + 16] = chr(ord($tampered[4 + 16]) ^ 0x01);         // 1. bajt dopisku
        $tampered = FrameBuilder::withFixedCrc($tampered);
        $v['TV6_tampered_note'] = [substr($tampered, 4, strlen($p1->toBytes())), $tampered];

        return array_map(fn ($x) => ['payload' => $x[0], 'frame' => $x[1]], $v);
    }

    /**
     * Pełne porównanie: CRC, odcisk ROOT, trust store, payload/frame/QR, statusy weryfikacji (teraz i po 3 dniach).
     *
     * @return array<array{check: string, ok: bool, detail: string}>
     */
    public function compare(KeyStore $keys): array
    {
        $rows = [];
        $row = function (string $check, bool $ok, string $detail = '') use (&$rows) {
            $rows[] = compact('check', 'ok', 'detail');
        };

        $crc = sprintf('%04X', Crc16::calculate('123456789'));
        $row('CRC16("123456789")', $crc === $this->json['crc16_check']['crc_hex'], $crc);

        $fp = $keys->fingerprint(KeyStore::ROOT);
        $row('Odcisk ROOT', $fp === $this->json['root_fingerprint'], $fp);

        $exporter = new TrustStoreExporter($keys, new IssuerRegistry);
        $store = $exporter->build(self::CERT_FROM, self::CERT_UNTIL);
        $row('Trust store (certyfikaty + podpisy ROOT)', $store === $this->json['trust_store'], count($store['issuers']).' wydawców');

        $trust = TrustStore::fromArray($store, $keys->publicKey(KeyStore::ROOT));
        $verifier = new FrameVerifier($trust);
        $now = $this->json['now_for_tests'];
        $later = $this->json['now_expired'];

        foreach ($this->rebuild($keys) as $name => $tv) {
            $expected = $this->json['vectors'][$name];
            $row("{$name} payload", bin2hex($tv['payload']) === $expected['payload_hex']);
            $row("{$name} frame + QR", bin2hex($tv['frame']) === $expected['frame_hex']
                && FrameBuilder::qrText($tv['frame']) === $expected['qr_text'], strlen($tv['frame']).' B');

            $r = $verifier->verify($tv['frame'], $now, self::USER_AREA);
            $row("{$name} weryfikacja", $r['status'] === $expected['expected_status'] && $r['reason'] === $expected['reason'],
                $r['status'].' '.$r['reason']);

            $r = $verifier->verify($tv['frame'], $later, self::USER_AREA);
            $row("{$name} po 3 dniach", $r['status'] === $expected['expected_status_3_days_later'], $r['status']);
        }

        return $rows;
    }
}
