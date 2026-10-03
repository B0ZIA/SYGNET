# SYGNET – Konsola nadawcza i Laboratorium ataków (Laravel)

> **Instrukcja dla Claude Code.** Budujesz konsolę operatora SYGNET w Laravel.
> Zanim napiszesz jakikolwiek kod, przeczytaj **`docs/PROTOCOL.md`**. To jedyne źródło prawdy o formacie bajtów, kryptografii i modemie dźwiękowym.
> Implementacja referencyjna: `tools/sygnet_ref.py`. Wektory testowe: `testvectors/testvectors.json` + `*.wav`.
> Bajty generowane przez konsolę muszą być **identyczne** z wektorami testowymi (Ed25519 jest deterministyczny).

> **Stan (2026-10-03): L1–L8 zrobione** – szczegóły uruchomienia w `backend/README.md`.
> `sygnet:testvectors` → ALL OK (także trust store bajt w bajt), modem JS = WAV-y z `testvectors/` (±1 LSB, `npm test`),
> PHPUnit 27 testów. Sprawdzone na Pixelu przez głośnik laptopa: alarm z konsoli → ✅, A1 → 🟥, A3 → ⚠️, A5 → 🟥 NIEPEŁNY.
> Poza specyfikacją: atak **A7** (skradziony, unieważniony klucz → `REVOKED_ISSUER`) i „kontrolna weryfikacja” –
> konsola sprawdza każdą ramkę tą samą logiką co telefon (§7) i pokazuje wynik w podglądzie.
> Certyfikat: `scope_count` i `name_len` to **u8** (PROTOCOL.md §5.3) – wcześniejsza wersja PHP miała u16.

## 1. Cel

Aplikacja webowa uruchamiana **lokalnie na laptopie, w 100% offline**, z trzema częściami:

1. **Konsola nadawcza (`/console`):** operator (wojsko, RCB, wojewoda) tworzy komunikat, serwer go podpisuje, przeglądarka nadaje go **dźwiękiem** przez głośnik, pokazuje **kod QR** i pozwala pobrać **WAV**.
2. **Laboratorium ataków (`/attack`):** na demo pokazujemy, że ataki NIE działają: podszycie, modyfikacja, powtórka, przekroczenie uprawnień, brak drugiego podpisu.
3. **Zarządzanie kluczami (`/keys` + komendy artisan):** generowanie kluczy, eksport trust store i klucza ROOT dla aplikacji Unity, unieważnianie kluczy.

## 2. Technologia

| Element | Wybór |
|---|---|
| Framework | Laravel 11+ (PHP 8.2+), SQLite |
| Kryptografia | wbudowane **ext-sodium** (żadnych innych bibliotek krypto) |
| Frontend | Blade + **Alpine.js** + **Tailwind**, budowane lokalnie przez **Vite** (`npm run build`) |
| QR | npm `qrcode` (generowanie w przeglądarce, poziom korekcji **M**) |
| Audio | **Web Audio API**, czysty JS (własny moduł modemu) |
| Offline | **zero CDN, zero zewnętrznych fontów i API.** Wszystko z `public/build` |

Bezpieczeństwo (to też część oceny):

- **Klucze prywatne nigdy nie trafiają do przeglądarki.** Podpisuje wyłącznie backend.
- Seedy w `storage/app/keys/{issuer_id}.seed` (base64), uprawnienia pliku 0600, katalog w `.gitignore`.
- W README uczciwie opisujemy: „na demo klucze są w plikach; produkcyjnie: HSM / karta kryptograficzna u każdego operatora”.

## 3. Struktura

```
app/
├── Sygnet/
│   ├── Crc16.php
│   ├── Payload.php            ← DTO + toBytes() (pack('CnCnNnnCC') + note)
│   ├── FrameBuilder.php       ← build(payload, signers[]) → bytes; toQrText(); parse() (do testów/ataków)
│   ├── KeyStore.php           ← load seed, sign(issuerId, bytes), publicKey(), fingerprint()
│   ├── IssuerRegistry.php     ← wydawcy z config/sygnet.php
│   ├── CertificateBuilder.php ← certyfikat wydawcy wg PROTOCOL §5.3 + podpis ROOT
│   ├── Areas.php              ← covers(c, a), nazwy
│   └── AttackFactory.php      ← generowanie ramek ataków (§6)
├── Console/Commands/
│   ├── SygnetInit.php         ← sygnet:init [--test-seeds]
│   ├── SygnetTestVectors.php  ← sygnet:testvectors (porównanie z testvectors.json)
│   └── SygnetSeedHistory.php  ← sygnet:seed-history (stare, prawdziwe komunikaty do ataku „powtórka”)
├── Http/Controllers/
│   ├── ConsoleController.php
│   ├── AttackController.php
│   └── KeysController.php
config/sygnet.php
database/migrations/xxxx_create_broadcasts_table.php
resources/js/
├── sygnet-modem.js            ← encode(frameBytes, sr, repeat) → Float32Array; play(); toWav()
├── waveform.js                ← wizualizacja (canvas + AnalyserNode)
└── app.js
resources/views/ console.blade.php, attack.blade.php, keys.blade.php, layout.blade.php
tests/Unit/ ProtocolTest.php
```

### Payload w PHP

```php
$bin = pack('CnCnNnnCC', 1, $issuerId, $type, $areaCode, $timestamp, $validMinutes, $sequence, 0, strlen($note)) . $note;
// C = u8, n = u16 BE, N = u32 BE
```

### Podpis

```php
$kp  = sodium_crypto_sign_seed_keypair($seed32);
$sig = sodium_crypto_sign_detached($payloadBytes, sodium_crypto_sign_secretkey($kp));   // 64 B
$pub = sodium_crypto_sign_publickey($kp);                                               // 32 B
```

## 4. Konfiguracja `config/sygnet.php`

Dokładnie wg `PROTOCOL.md` §4–§6:

- `issuers`: id → `name`, `scopes` (1 Dowództwo Operacyjne RSZ `[0]`, 2 RCB `[0]`, 3 Wojewoda Mazowiecki `[14]`, 4 Wojewoda Małopolski `[12]`, 5 Komendant Woj. PSP Mazowsze `[14]`, 6 Prezydent m.st. Warszawy `[1465]`, 7 Prezydent Miasta Krakowa `[1261]`).
- `alert_types`: id → `code`, `name`, `icon`, `critical` (3 EVACUATION = critical), plus 250 KEY_REVOKE (tylko ROOT).
- `areas`: 0 Cała Polska, 14 woj. mazowieckie, 12 woj. małopolskie, 1465 Warszawa, 1261 Kraków.
- `validity_options`: 30, 120, 1440 minut.
- `repeat_default`: 2.

## 5. Funkcje

### 5.1 Komendy artisan

- **`sygnet:init`**: generuje losowe seedy dla ROOT (id 0), wszystkich wydawców i klucza HAKERA (`hacker.seed`, **poza trust store**). Następnie:
  - buduje certyfikaty (ważne od dziś do +2 lata) podpisane przez ROOT,
  - zapisuje `storage/app/export/sygnet_trust_store.json` (format `PROTOCOL.md` §5.4),
  - zapisuje `storage/app/export/RootKey.cs`, gotową klasę C# z kluczem ROOT:
    ```csharp
    public static class RootKey {
        public static readonly byte[] Public = System.Convert.FromBase64String("...");
        public const string Fingerprint = "XXXX-XXXX-XXXX-XXXX";
    }
    ```
  - wypisuje odcisk ROOT na ekranie.
  - Flaga `--test-seeds` używa seedów testowych z `PROTOCOL.md` §9 (do synchronizacji z Unity w pierwszych godzinach). **Bez flagi seedy są losowe** (na finalne demo).
- **`sygnet:testvectors`**: z seedów testowych odtwarza TV1–TV6 i porównuje `payload_hex`/`frame_hex` z `testvectors/testvectors.json`. Musi wypisać `ALL OK`. To pierwszy kamień milowy.
- **`sygnet:seed-history`**: dodaje do bazy kilka prawdziwych komunikatów sprzed 3 dni (np. alarm lotniczy Warszawa), do ataku „powtórka”.

### 5.2 Tabela `broadcasts`

`id, issuer_id, signer_ids (json), type, area_code, timestamp, valid_minutes, sequence, note, frame_hex, qr_text, kind ('genuine'|'attack'), attack_type (nullable), created_at`.
`sequence` liczony osobno dla każdego `issuer_id` (max + 1).

### 5.3 Endpointy (JSON, sesja + CSRF, bez auth)

- `POST /api/broadcast`: `{issuer_id, second_signer_id?, type, area_code, valid_minutes, note}` → walidacja (note ≤ 60 **bajtów** UTF-8, bez znaków sterujących; obszar musi być w zakresie wydawcy; typ krytyczny wymaga `second_signer_id` ≠ issuer i uprawnionego do obszaru) → podpis → zapis → `{frame_hex, frame_b64, qr_text, bytes, audio_seconds, sequence}`.
- `POST /api/attack/{type}`: patrz §6.
- `POST /api/revoke`: `{issuer_id}` → komunikat `KEY_REVOKE` podpisany przez ROOT (issuer 0, area 0, note = u16 id).
- `GET /api/broadcasts?limit=20`: historia.

## 6. Laboratorium ataków (`/attack`)

Czerwony motyw, nagłówek „🕵️ TRYB ATAKUJĄCEGO: klucze nieautoryzowane”. Każdy atak to karta z opisem „co robi atakujący”, przyciskami `▶ Nadaj` / `▦ QR` oraz **„Oczekiwany wynik na telefonie”**:

| Atak | Jak generujemy ramkę | Oczekiwany wynik |
|---|---|---|
| **A1 Podszycie** | Operator wpisuje treść, wybiera „ofiarę” (np. Dowództwo Op.). `issuer_id` = ofiara, `signer_id` = ofiara, **podpis kluczem HAKERA** | 🟥 FORGED `BAD_SIGNATURE` |
| **A2 Modyfikacja** | Bierze prawdziwą ramkę z historii, zmienia dopisek/obszar/typ, **zostawia stary podpis**, przelicza CRC | 🟥 FORGED `BAD_SIGNATURE` |
| **A3 Powtórka** | Odtwarza bajt w bajt prawdziwą ramkę sprzed 3 dni (`sygnet:seed-history`) | ⚠️ EXPIRED |
| **A4 Przekroczenie uprawnień** | Prawdziwy klucz Prezydenta Warszawy podpisuje komunikat dla Krakowa | 🟥 FORGED `UNAUTHORIZED_AREA` |
| **A5 Brak drugiego podpisu** | Ewakuacja podpisana tylko przez Wojewodę | 🟥 INCOMPLETE |
| **A6 Nieznany nadawca** | `issuer_id = 42`, podpis kluczem hakera | 🟥 FORGED `UNKNOWN_ISSUER` |
| **A7 Skradziony klucz** | Prawdziwy klucz wydawcy unieważnionego wcześniej przez ROOT (`/keys` → KEY_REVOKE) | 🟥 FORGED `REVOKED_ISSUER` |

Ramki ataków budujemy **tą samą klasą `FrameBuilder`**. Atak różni się tylko kluczem lub zmodyfikowanymi bajtami. W bazie zapisujemy je jako `kind = 'attack'`.

## 7. UI konsoli (`/console`)

Styl: ciemny „centrum dowodzenia” (tło `#0B0F14`, panele `#151B23`, akcent zielony `#2EA043`, alarm czerwony `#B42318`, font monospace dla danych technicznych). Wszystko na jednym ekranie 1920×1080 (pod projektor).

```
┌──────────────────────────────────────────────────────────────────────────┐
│ SYGNET · KONSOLA NADAWCZA     🔐 Wojewoda Mazowiecki · 9F2C-…  ● OFFLINE │
├────────────────────────────────┬─────────────────────────────────────────┤
│ NADAWCA     [▼ Wojewoda Maz. ] │  PODGLĄD NA TELEFONIE (ramka smartfona) │
│ TYP         [▼ ⚠ Alarm lotn. ] │  ✅ ZWERYFIKOWANO                        │
│ OBSZAR      [▼ Warszawa      ] │  Wojewoda Mazowiecki · 03:12            │
│ WAŻNOŚĆ     (•)2h ( )30m ( )24h│  ⚠ ALARM LOTNICZY · Warszawa            │
│ DOPISEK     [Schron: metro   ] │  „Schron: metro Świętokrzyska”          │
│             27/60 B            │  Co robić: Natychmiast udaj się…        │
│ POWTÓRZENIA (•)2 ( )1          │                                         │
│ 📦 116 B · 🔊 ~13,5 s           │                                         │
├────────────────────────────────┴─────────────────────────────────────────┤
│ [▶ NADAJ DŹWIĘKIEM]  [▦ POKAŻ QR]  [⬇ WAV]                               │
│ ▁▃▇▅▂▇▃▁▅▇▂▃▇▅▁▃▇▂  ████████░░░░ 64%                                     │
├──────────────────────────────────────────────────────────────────────────┤
│ RAMKA HEX: 5347 0072 0100 0101 05b9 … (podpis podświetlony innym kolorem)│
│ HISTORIA: #12 03:12 Alarm lotniczy Warszawa ✔ · #11 02:40 Odwołanie …    │
└──────────────────────────────────────────────────────────────────────────┘
```

- **Obszar** filtrowany do zakresu wybranego nadawcy (`covers`).
- **Typ krytyczny** (Ewakuacja) pokazuje pole „Drugi podpis” (wybór drugiego wydawcy uprawnionego do obszaru) i modal „Zatwierdzenie drugiego operatora”. Na demo wystarczy PIN `1234`, ale **podpis jest naprawdę podwójny** (2 klucze).
- **Licznik bajtów dopisku** liczy bajty UTF-8 (`new TextEncoder().encode(s).length`), nie znaki.
- **Szacowany czas** wg `PROTOCOL.md` §8.1.
- **Podczas nadawania:** animowana fala (AnalyserNode) i pasek postępu. Przyciski zablokowane.
- **Podgląd ramki hex** z kolorami: nagłówek / payload / podpisy / CRC. Na pitchu dobrze pokazuje, że „to tylko 116 bajtów”.
- **Historia:** ponowne nadanie, QR i WAV dla każdej pozycji.
- Strona `/keys`: lista wydawców (nazwa, zakres, odcisk, ważność certu, status unieważnienia), przycisk „Unieważnij” (wysyła `KEY_REVOKE`), linki do pobrania `sygnet_trust_store.json` i `RootKey.cs`, odcisk ROOT dużą czcionką.

## 8. Modem JS (`resources/js/sygnet-modem.js`)

Implementuj **dokładnie** `PROTOCOL.md` §8.1–8.2. Porównaj z funkcjami `_tone`, `encode_frame_once`, `encode` w `tools/sygnet_ref.py`.

```js
export const MODEM = { A0: 1500, B0: 3200, STEP: 100, SYMBOL_MS: 40, GAP_MS: 10,
  PRE_A: 1000, PRE_B: 5200, PRE_TONE_MS: 200, PRE_GAP_MS: 50, END_TONE_MS: 200,
  REPEAT_GAP_MS: 500, TONE_AMP: 0.4, MARKER_AMP: 0.6, FADE_MS: 5 };

export function encode(frame /* Uint8Array */, sr, repeat = 2) { /* → Float32Array */ }
export async function play(samples, sr, { onProgress }) { /* AudioContext + AudioBufferSourceNode + AnalyserNode */ }
export function toWav(samples, sr) { /* → Blob 16-bit PCM mono */ }
export const durationSeconds = (frameLen, repeat) => repeat * (0.7 + frameLen * 0.05) + (repeat - 1) * 0.5;
```

- `n = Math.round(sr * ms / 1000)`, raised-cosine fade 5 ms na obu końcach każdego tonu.
- Użyj `AudioContext.sampleRate` urządzenia. WAV generuj w 48000 Hz.
- **Test zgodności:** pobierz WAV z konsoli dla TV1 (seedy testowe) i uruchom `python3 tools/sygnet_ref.py decode plik.wav`. Musi zwrócić ramkę i `VERIFIED`.

## 9. Testy (PHPUnit)

- CRC16 `"123456789"` = `0x29B1`,
- `sygnet:testvectors` przechodzi (TV1–TV6 bajt w bajt),
- `sodium_crypto_sign_verify_detached` przechodzi dla każdej ramki genuine, nie przechodzi dla A1/A2,
- walidacja: dopisek 61 B daje błąd 422, obszar poza zakresem wydawcy daje 422, ewakuacja bez drugiego podpisu daje 422 (w konsoli, w laboratorium wolno).

## 10. Kolejność pracy (kamienie milowe)

| # | Cel | Kryterium „gotowe” |
|---|---|---|
| L1 | Laravel + SQLite + Vite/Tailwind/Alpine offline | strona się ładuje bez internetu |
| L2 | `Crc16`, `Payload`, `FrameBuilder`, `KeyStore`, `sygnet:testvectors` | **ALL OK** (bajty = wektory) |
| L3 | `sygnet:init` + eksport trust store i `RootKey.cs` | pliki przekazane osobie od Unity |
| L4 | `/api/broadcast` + formularz + podgląd + QR | QR z konsoli skanowany w Unity daje ✅ |
| L5 | Modem JS + odtwarzanie + WAV + wizualizacja | WAV dekodowany przez `sygnet_ref.py decode`, potem telefon słyszy ✅ |
| L6 | Laboratorium ataków A1–A6 + `seed-history` | każdy atak daje oczekiwany wynik na telefonie |
| L7 | Podwójny podpis, `/keys`, unieważnianie, dopieszczenie UI | pełne demo przechodzi |
| L8 | README (instalacja offline, uruchomienie), zrzuty ekranu do prezentacji | – |

Priorytety: L1–L6 obowiązkowe. L7 bardzo ważne dla pytań jury. Unieważnianie kluczy może zostać na slajdzie, jeśli zabraknie czasu.

## 11. Czego NIE robić

- Nie wysyłaj kluczy prywatnych do frontendu. Nie loguj seedów.
- Nie używaj CDN ani zewnętrznych fontów.
- Nie zmieniaj formatu ramki ani stałych modemu bez uzgodnienia z osobą od Unity (i aktualizacji `sygnet_ref.py`).
- Nie pisz własnej kryptografii, tylko ext-sodium.
