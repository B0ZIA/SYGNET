# SYGNET – konsola nadawcza i laboratorium ataków

Aplikacja Laravel uruchamiana **lokalnie na laptopie, w 100% offline** (`docs/CONSOLE_LARAVEL.md`).
Operator tworzy komunikat, serwer podpisuje go Ed25519 (ext-sodium), a przeglądarka nadaje go **dźwiękiem**
(modem JS, Web Audio), pokazuje **kod QR** albo zapisuje **WAV**. Format bajtów: `docs/PROTOCOL.md`.

| Strona | Co robi |
|---|---|
| `/console` | formularz (nadawca, typ, obszar w zakresie nadawcy, ważność, dopisek ≤ 60 B UTF-8), podgląd telefonu na żywo, nadawanie dźwiękiem z wizualizacją tonów, QR, WAV, ramka hex w kolorach, historia |
| `/attack` | laboratorium A1–A7: podszycie, modyfikacja, powtórka, przekroczenie uprawnień, brak 2. podpisu, nieznany nadawca, skradziony unieważniony klucz – każdy z oczekiwanym wynikiem i kontrolną weryfikacją |
| `/keys` | odcisk ROOT, wydawcy i certyfikaty, unieważnianie (KEY_REVOKE od ROOT), pobranie `sygnet_trust_store.json` i `RootKey.cs` dla aplikacji |

„Kontrolna weryfikacja” to ta sama logika co w telefonie (`FrameVerifier`, PROTOCOL.md §7) na wyeksportowanym
trust store – konsola pokazuje, co zobaczy telefon odbiorcy (domyślnie w Krakowie, `SYGNET_USER_AREA`).

## Instalacja (raz, z internetem)

Wymagania: PHP ≥ 8.3 z rozszerzeniami `sodium`, `pdo_sqlite`, `mbstring`, `openssl`, `fileinfo`; Composer; Node ≥ 20.

```bash
composer install
cp .env.example .env
php artisan key:generate
touch database/database.sqlite
php artisan migrate
npm install
npm run build
```

Windows: PHP z `winget install PHP.PHP.8.4` nie ma `php.ini` – skopiuj `php.ini-development` do `php.ini`
i włącz `extension_dir = "ext"` oraz `extension=sodium`, `pdo_sqlite`, `sqlite3`, `mbstring`, `openssl`, `fileinfo`, `curl`, `zip`.

Po `npm run build` wszystko (fonty, JS, CSS) jest w `public/build` – konsola nie potrzebuje już sieci, zero CDN.

## Klucze

```bash
php artisan sygnet:testvectors          # TV1–TV6 bajt w bajt jak testvectors.json → ALL OK
php artisan sygnet:init --test-seeds    # klucze testowe z PROTOCOL.md §9 (wydawcy 1, 3, 5, 6 + HAKER)
php artisan sygnet:init --force         # NOWE losowe klucze na demo: ROOT, wydawcy 1–7, HAKER
php artisan sygnet:seed-history         # prawdziwe komunikaty sprzed 3 dni (materiał do ataku A3)
```

`sygnet:init` zapisuje seedy do `storage/app/keys/*.seed` (0600, poza gitem) i eksportuje dla Unity:

- `storage/app/export/sygnet_trust_store.json` → `SYGNET_Unity/Assets/Resources/sygnet_trust_store.json`
- `storage/app/export/RootKey.cs` → `SYGNET_Unity/Assets/Sygnet/App/RootKey.cs`

Po podmianie trzeba zbudować APK od nowa. Nowe klucze = telefony ze starym ROOT odrzucą wszystko (tak ma być).

## Uruchomienie

Wdrożenie na serwer (VPS, nginx, HTTPS, hasło, przeniesienie kluczy): **[DEPLOY.md](DEPLOY.md)**.

```bash
php artisan serve        # http://127.0.0.1:8000 → /console
```

Na demo: projektor 1920×1080, głośność laptopa ~70%, telefon 0,5–2 m od głośnika. Przeglądarka wymaga
kliknięcia, zanim zagra dźwięk – przycisk „Nadaj dźwiękiem” to załatwia.

## API (JSON, sesja + CSRF)

| Metoda | Ścieżka | Treść |
|---|---|---|
| `POST` | `/api/broadcast` | `{issuer_id, type, area_code, valid_minutes, note, second_signer_id?, second_pin?}` → ramka, QR, check |
| `POST` | `/api/attack/{A1..A7}` | parametry ataku (opcjonalne) → ramka + `expected` + `as_expected` |
| `POST` | `/api/revoke` | `{issuer_id}` → KEY_REVOKE podpisany przez ROOT |
| `GET` | `/api/broadcasts?limit=20&kind=genuine` | historia |

Walidacja (422): dopisek > 60 B lub ze znakami sterującymi, obszar poza zakresem nadawcy, brak klucza,
klucz unieważniony, ewakuacja bez drugiego, uprawnionego wydawcy albo bez PIN-u drugiego operatora (`1234` na demo).
W laboratorium ataków te reguły nie obowiązują – tam weryfikuje dopiero telefon.

## Testy

```bash
php artisan test     # PHPUnit: CRC, wektory TV1–TV6, certyfikaty, weryfikacja §7, API, walidacja, ataki A1–A7
npm test             # modem JS = pliki testvectors/*.wav (±1 LSB), WAV, zaokrąglanie przy 44,1 kHz
```

## Bezpieczeństwo

- Klucze prywatne nigdy nie trafiają do przeglądarki – podpisuje wyłącznie backend; przeglądarka dostaje gotowe bajty.
- Seedy nie są logowane; eksport zawiera tylko klucze publiczne.
- Na demo klucze leżą w plikach. **Produkcyjnie:** HSM / karta kryptograficzna u każdego operatora, ROOT offline.
- Typy krytyczne wymagają dwóch podpisów różnych wydawców – ta reguła jest zaszyta w aplikacji, nie w ramce.

## Struktura

```
app/Sygnet/          Crc16, Payload, FrameBuilder, KeyStore, Certificate(Builder), TrustStore(Exporter),
                     FrameVerifier, Areas, AlertTypes, IssuerRegistry, Broadcaster, AttackFactory, TestVectors
app/Console/Commands sygnet:init, sygnet:testvectors, sygnet:seed-history
resources/js/        sygnet-modem.js (encode/play/toWav), waveform.js, phone.js, frame.js, app.js (Alpine)
resources/views/     console, attack, keys + components/layout, transmit, partials/phone, hex, qr-modal
```
