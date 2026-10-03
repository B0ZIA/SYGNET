<?php

namespace Tests\Feature;

use App\Sygnet\FrameBuilder;
use Illuminate\Foundation\Http\Middleware\ValidateCsrfToken;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Tests\Concerns\UsesTestKeys;
use Tests\TestCase;

/**
 * API konsoli i laboratorium (CONSOLE_LARAVEL.md §5.3, §6, §9). Telefon odbiorcy: Kraków (1261).
 * Klucze testowe + 4 (Wojewoda Małopolski) i 7 (Prezydent Krakowa), żeby w Krakowie dało się podpisać ewakuację.
 */
class ConsoleApiTest extends TestCase
{
    use RefreshDatabase;
    use UsesTestKeys;

    protected function setUp(): void
    {
        parent::setUp();
        $this->installTestKeys([4, 7]);
        $this->withoutMiddleware(ValidateCsrfToken::class);
    }

    private function broadcast(array $over = [])
    {
        return $this->postJson('/api/broadcast', $over + [
            'issuer_id' => 1, 'type' => 1, 'area_code' => 1261, 'valid_minutes' => 120,
            'note' => 'Schron: piwnice i przejścia podziemne',
        ]);
    }

    public function test_pages_render_offline(): void
    {
        $this->get('/')->assertRedirect('/console');
        foreach (['/console', '/attack', '/keys'] as $url) {
            $this->get($url)->assertOk()->assertDontSee('fonts.bunny.net')->assertDontSee('cdn.');
        }
    }

    public function test_broadcast_is_signed_and_verified(): void
    {
        $r = $this->broadcast()->assertCreated();

        $r->assertJsonPath('check.status', 'VERIFIED')
            ->assertJsonPath('sequence', 1)
            ->assertJsonPath('area_name', 'Kraków')
            ->assertJsonPath('bytes', 89 + strlen('Schron: piwnice i przejścia podziemne'));

        $frame = hex2bin($r->json('frame_hex'));
        $this->assertSame($r->json('qr_text'), FrameBuilder::qrText($frame));
        $this->assertSame(base64_encode($frame), $r->json('frame_b64'));
        $this->assertSame(2, $this->broadcast()->json('sequence'));            // licznik osobno dla wydawcy
        $this->assertSame(1, $this->broadcast(['issuer_id' => 7])->json('sequence'));
    }

    public function test_console_password_protects_pages_and_api(): void
    {
        config(['sygnet.console_password' => 'tajne-haslo']);

        $this->get('/console')->assertRedirect('/login');
        $this->postJson('/api/broadcast', [])->assertUnauthorized();
        $this->get('/keys/export/RootKey.cs')->assertRedirect('/login');

        $this->post('/login', ['password' => 'zle'])->assertSessionHasErrors('password');
        $this->get('/console')->assertRedirect('/login');

        $this->post('/login', ['password' => 'tajne-haslo'])->assertRedirect('/console');
        $this->get('/console')->assertOk()->assertSee('Wyloguj');
        $this->broadcast()->assertCreated();

        config(['sygnet.console_password' => 'nowe-haslo']);               // zmiana hasła wylogowuje
        $this->get('/console')->assertRedirect('/login');
    }

    public function test_jury_info_shows_password_and_pin_only_when_enabled(): void
    {
        config(['sygnet.console_password' => 'haslo-dla-jury', 'sygnet.second_operator_pin' => '2468', 'sygnet.jury_info' => true]);
        $this->get('/login')->assertOk()->assertSee('Dla oceniających')->assertSee('haslo-dla-jury')->assertSee('2468');
        $this->post('/login', ['password' => 'haslo-dla-jury']);
        $this->get('/console')->assertOk()->assertSee('Dla oceniających')->assertSee('2468');

        config(['sygnet.jury_info' => false]);
        $this->get('/console')->assertOk()->assertDontSee('Dla oceniających')->assertDontSee('2468');
        $this->post('/logout');
        $this->get('/login')->assertOk()->assertDontSee('haslo-dla-jury');
    }

    public function test_login_page_without_password_goes_to_console(): void
    {
        $this->get('/login')->assertRedirect('/console');
        $this->get('/console')->assertOk()->assertDontSee('Wyloguj');
    }

    public function test_poster_with_seven_day_validity(): void
    {
        $b = $this->broadcast(['type' => 7, 'issuer_id' => 7, 'valid_minutes' => 10080, 'note' => 'Punkt informacyjny: Rynek Główny 1'])
            ->assertCreated()->json();
        $this->assertSame($b['timestamp'] + 7 * 86400, $b['valid_until']);

        $this->get("/poster/{$b['id']}")->assertOk()
            ->assertSee($b['qr_text'], false)
            ->assertSee('Ostrzeżenie przed dezinformacją')
            ->assertSee('Prezydent Miasta Krakowa')
            ->assertSee('(7 dni)');
        $this->get('/poster/999999')->assertNotFound();
        $this->broadcast(['valid_minutes' => 99999])->assertStatus(422);
    }

    public function test_sequence_starts_from_configured_number(): void
    {
        config(['sygnet.sequence_start' => 1000]);

        $this->assertSame(1000, $this->broadcast()->json('sequence'));
        $this->assertSame(1001, $this->broadcast()->json('sequence'));
    }

    public function test_replay_uses_expired_genuine_broadcast(): void
    {
        $this->artisan('sygnet:seed-history')->assertSuccessful();
        $this->broadcast()->assertCreated();                       // świeży – nie nadaje się do powtórki

        $this->postJson('/api/attack/A3')->assertCreated()->assertJsonPath('check.status', 'EXPIRED');
    }

    public function test_note_limited_to_60_bytes_of_utf8(): void
    {
        $this->broadcast(['note' => str_repeat('A', 61)])->assertStatus(422)->assertJsonValidationErrors('note');
        $this->broadcast(['note' => str_repeat('ś', 31)])->assertStatus(422)->assertJsonValidationErrors('note');   // 62 B
        $this->broadcast(['note' => str_repeat('ś', 30)])->assertCreated();                                         // 60 B
        $this->broadcast(['note' => "Linia 1\nLinia 2"])->assertStatus(422)->assertJsonValidationErrors('note');
    }

    public function test_area_must_be_in_issuer_scope(): void
    {
        $this->broadcast(['issuer_id' => 6])->assertStatus(422)->assertJsonValidationErrors('area_code');
        $this->broadcast(['issuer_id' => 4, 'area_code' => 12])->assertCreated();
        $this->broadcast(['issuer_id' => 2])->assertStatus(422)->assertJsonValidationErrors('issuer_id');   // brak klucza
    }

    public function test_evacuation_requires_second_signature_and_operator_pin(): void
    {
        $evac = ['type' => 3, 'issuer_id' => 4, 'note' => 'Kierunek: Wieliczka'];

        $this->broadcast($evac)->assertStatus(422)->assertJsonValidationErrors('second_signer_id');
        $this->broadcast($evac + ['second_signer_id' => 4, 'second_pin' => '1234'])->assertStatus(422);
        $this->broadcast($evac + ['second_signer_id' => 6, 'second_pin' => '1234'])->assertStatus(422);       // poza zakresem
        $this->broadcast($evac + ['second_signer_id' => 7, 'second_pin' => '0000'])->assertStatus(422)->assertJsonValidationErrors('second_pin');

        $r = $this->broadcast($evac + ['second_signer_id' => 7, 'second_pin' => '1234'])->assertCreated();
        $r->assertJsonPath('signer_ids', [4, 7])->assertJsonPath('check.status', 'VERIFIED');
        $this->assertSame(155 + strlen('Kierunek: Wieliczka'), $r->json('bytes'));
    }

    public function test_attacks_give_expected_result_on_the_phone(): void
    {
        $this->broadcast()->assertCreated();                          // materiał do A2

        foreach (['A1', 'A2', 'A3', 'A4', 'A5', 'A6'] as $attack) {
            $r = $this->postJson("/api/attack/{$attack}", [])->assertCreated();
            $this->assertTrue($r->json('as_expected'), $attack.': '.$r->json('check.status').' '.$r->json('check.reason'));
            $this->assertSame('attack', $r->json('kind'));
        }

        $a1 = $this->postJson('/api/attack/A1', ['victim_id' => 1])->json();
        $this->assertSame('BAD_SIGNATURE:1', $a1['check']['reason']);
        $this->assertSame('UNKNOWN_ISSUER:42', $this->postJson('/api/attack/A6')->json('check.reason'));
        $this->assertSame('UNAUTHORIZED_AREA:6', $this->postJson('/api/attack/A4')->json('check.reason'));
    }

    public function test_tampered_frame_keeps_original_signature(): void
    {
        $original = $this->broadcast()->json();
        $a2 = $this->postJson('/api/attack/A2', ['broadcast_id' => $original['id'], 'note' => 'Schron: NIE schodźcie do metra'])->json();

        $o = FrameBuilder::parse(hex2bin($original['frame_hex']));
        $t = FrameBuilder::parse(hex2bin($a2['frame_hex']));
        $this->assertSame($o['signatures'], $t['signatures']);
        $this->assertNotSame($o['payload']->note, $t['payload']->note);
        $this->assertSame('BAD_SIGNATURE:1', $a2['check']['reason']);
    }

    public function test_revoke_blocks_issuer(): void
    {
        $r = $this->postJson('/api/revoke', ['issuer_id' => 7])->assertCreated();
        $r->assertJsonPath('type', 250)->assertJsonPath('issuer_id', 0)->assertJsonPath('check.status', 'VERIFIED');
        $this->assertSame(pack('n', 7), FrameBuilder::parse(hex2bin($r->json('frame_hex')))['payload']->note);

        $this->broadcast(['issuer_id' => 7])->assertStatus(422)->assertJsonValidationErrors('issuer_id');
        $a7 = $this->postJson('/api/attack/A7', ['issuer_id' => 7])->assertCreated();
        $a7->assertJsonPath('check.reason', 'REVOKED_ISSUER:7')->assertJsonPath('as_expected', true);
    }

    public function test_history_lists_broadcasts(): void
    {
        $this->broadcast();
        $this->postJson('/api/attack/A6');

        $this->getJson('/api/broadcasts?kind=genuine')->assertOk()->assertJsonCount(1);
        $this->getJson('/api/broadcasts')->assertOk()->assertJsonCount(2);
    }

    public function test_export_downloads_only_public_files(): void
    {
        $this->get('/keys/export/sygnet_trust_store.json')->assertOk();
        $this->get('/keys/export/RootKey.cs')->assertOk();
        $this->get('/keys/export/0.seed')->assertNotFound();
        $this->get('/keys/export/..%2Fkeys%2F0.seed')->assertNotFound();
    }
}
