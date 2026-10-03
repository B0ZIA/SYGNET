# SYGNET – Specyfikacja protokołu v1

> **To jest jedyne źródło prawdy.** Konsola (Laravel/PHP/JS) i klient (Unity/C#) muszą implementować ten dokument 1:1.
> Nie zmieniajcie niczego bez uzgodnienia obu osób. Każdą zmianę wprowadzacie też w `tools/sygnet_ref.py` i generujecie od nowa wektory testowe.
>
> Implementacja referencyjna: `tools/sygnet_ref.py` (przetestowana: szum do 6 dB SNR, pogłos, 44.1/48 kHz, dryf zegara 0,1%).
> Wektory testowe: `testvectors/testvectors.json` oraz pliki `testvectors/*.wav`.

---

## 1. Kryptografia

| Element | Wybór |
|---|---|
| Algorytm podpisu | **Ed25519** (RFC 8032), podpis 64 B, klucz publiczny 32 B |
| Klucz prywatny | **32-bajtowy seed** (ten sam format w PHP sodium i BouncyCastle) |
| PHP | `sodium_crypto_sign_seed_keypair($seed)` → `sodium_crypto_sign_secretkey()` → `sodium_crypto_sign_detached()` |
| C# | `new Ed25519PrivateKeyParameters(seed, 0)` → `Ed25519Signer` |
| Odcisk klucza (fingerprint) | pierwsze 8 bajtów `SHA-256(pubkey)`, hex WIELKIMI literami, w grupach po 4: `6A38-03D5-F059-902A` |

Podpis Ed25519 jest deterministyczny: ten sam seed i te same bajty dają zawsze identyczny podpis. Dzięki temu wektory testowe porównujecie bajt w bajt.

Wszystkie liczby wielobajtowe zapisujemy w **big-endian**. Tekst w **UTF-8**.

---

## 2. Payload (treść komunikatu, to jest podpisywane)

| Offset | Pole | Typ | Opis |
|---|---|---|---|
| 0 | `version` | u8 | zawsze `1` |
| 1 | `issuer_id` | u16 | wydawca (§5) |
| 3 | `type` | u8 | typ komunikatu (§4) |
| 4 | `area_code` | u16 | obszar (§6) |
| 6 | `timestamp` | u32 | czas wydania, unix seconds UTC |
| 10 | `valid_minutes` | u16 | ważność w minutach od `timestamp` |
| 12 | `sequence` | u16 | numer kolejny, osobny licznik dla każdego wydawcy, od 1 |
| 14 | `flags` | u8 | `0` (zarezerwowane) |
| 15 | `note_len` | u8 | 0..60 |
| 16 | `note` | bytes | dopisek UTF-8, dokładnie `note_len` bajtów, **maks. 60 BAJTÓW** |

Nagłówek ma 16 B, więc payload ma od 16 do 76 B.
Dla typu `KEY_REVOKE` (250) `note` to dokładnie 2 bajty: `u16` z ID odwoływanego wydawcy.

---

## 3. Ramka (to idzie w eter / do QR)

```
+-------+-----------+---------+-----------+------------------------------------+-------+
| MAGIC | frame_len | payload | sig_count | sig_count × [signer_id u16 | sig 64B] | crc16 |
| "SG"  |   u16     | 16..76B |    u8     |                                    |  u16  |
+-------+-----------+---------+-----------+------------------------------------+-------+
```

- `MAGIC` = bajty `0x53 0x47` („SG”).
- `frame_len` = liczba bajtów **po** polu `frame_len`, łącznie z CRC. Całkowita długość ramki to `4 + frame_len`.
- `sig_count` = 1 lub 2.
- Każdy podpis to `Ed25519(sign, payload)`: **podpisujemy wyłącznie bajty payloadu**. Wszyscy sygnatariusze podpisują te same bajty.
- Pierwszy `signer_id` **musi** być równy `payload.issuer_id`.
- `crc16` = **CRC-16/CCITT-FALSE** (poly `0x1021`, init `0xFFFF`, bez odwracania bitów, bez xorout), liczone po wszystkich bajtach od `MAGIC` do ostatniego bajtu ostatniego podpisu.
  Kontrola: `CRC("123456789") = 0x29B1`.
- CRC służy **tylko** do wykrywania błędów transmisji. Nie zapewnia bezpieczeństwa, bo atakujący może je przeliczyć. Bezpieczeństwo daje podpis.

Rozmiary: komunikat z 1 podpisem ma 89 + note B (z dopiskiem 29 B daje 118 B). Komunikat z 2 podpisami ma 155 + note B.

### Referencyjne CRC

```csharp
static ushort Crc16(ReadOnlySpan<byte> d) {
    ushort crc = 0xFFFF;
    foreach (var b in d) {
        crc ^= (ushort)(b << 8);
        for (int i = 0; i < 8; i++)
            crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
    }
    return crc;
}
```

```php
function crc16(string $d): int {
    $crc = 0xFFFF;
    for ($i = 0, $n = strlen($d); $i < $n; $i++) {
        $crc ^= ord($d[$i]) << 8;
        for ($b = 0; $b < 8; $b++)
            $crc = ($crc & 0x8000) ? (($crc << 1) ^ 0x1021) & 0xFFFF : ($crc << 1) & 0xFFFF;
    }
    return $crc;
}
```

### QR

Tekst kodu QR: `"SYG1:" + base64url(frame)` **bez paddingu** `=`. Korekcja błędów QR: poziom **M**.

---

## 4. Typy komunikatów

| ID | Kod | Nazwa PL | Krytyczny (2 podpisy) | Domyślna instrukcja w aplikacji |
|---|---|---|---|---|
| 1 | `AIR_RAID` | Alarm lotniczy | nie | Natychmiast udaj się do najbliższego schronu lub piwnicy. Odsuń się od okien. |
| 2 | `ALL_CLEAR` | Odwołanie alarmu | nie | Zagrożenie minęło. Zachowaj ostrożność i słuchaj kolejnych komunikatów. |
| 3 | `EVACUATION` | Ewakuacja | **TAK** | Opuść wskazany obszar wyznaczonym kierunkiem. Zabierz dokumenty, wodę, leki. |
| 4 | `WATER_CONTAMINATION` | Skażenie wody | nie | Nie pij wody z kranu. Używaj wody butelkowanej lub przegotowanej. |
| 5 | `POWER_OUTAGE` | Awaria zasilania | nie | Oszczędzaj baterię telefonu. Przygotuj latarkę i radio na baterie. |
| 6 | `CHEMICAL` | Zagrożenie chemiczne | nie | Zostań w budynku, zamknij okna i wentylację, uszczelnij drzwi. |
| 7 | `DISINFO_WARNING` | Ostrzeżenie przed dezinformacją | nie | Krążą fałszywe komunikaty. Ufaj tylko komunikatom zweryfikowanym w SYGNET. |
| 8 | `GENERAL` | Komunikat ogólny | nie | Zapoznaj się z treścią komunikatu. |
| 250 | `KEY_REVOKE` | Unieważnienie klucza | (tylko ROOT) | (systemowy, aplikacja unieważnia klucz wskazanego wydawcy) |

Polityka „krytyczny = 2 podpisy” jest **zaszyta w aplikacji** (lista typów), a nie w ramce, więc atakujący nie może jej wyłączyć.

---

## 5. Wydawcy i łańcuch zaufania

### 5.1 Hierarchia

```
ROOT (issuer_id 0) – klucz główny, offline. Jego klucz publiczny jest WBUDOWANY w aplikację (pinning).
 └─ podpisuje CERTYFIKATY wydawców (issuer_id ≥ 1) → plik trust_store.json w aplikacji
```

ROOT podpisuje tylko certyfikaty i komunikaty `KEY_REVOKE`. Komunikat od ROOT innego typu oznacza FORGED.

### 5.2 Wydawcy (zestaw na demo)

| ID | Nazwa (w certyfikacie) | Zakres (scopes) |
|---|---|---|
| 0 | ROOT – Klucz główny SYGNET | (wbudowany, nie w trust store) |
| 1 | Dowództwo Operacyjne RSZ | `[0]` cała Polska |
| 2 | Rządowe Centrum Bezpieczeństwa | `[0]` |
| 3 | Wojewoda Mazowiecki | `[14]` |
| 4 | Wojewoda Małopolski | `[12]` |
| 5 | Komendant Wojewódzki PSP Mazowsze | `[14]` |
| 6 | Prezydent m.st. Warszawy | `[1465]` |
| 7 | Prezydent Miasta Krakowa | `[1261]` |
| – | HAKER | klucz spoza trust store (tylko konsola, tryb ataku) |

### 5.3 Certyfikat wydawcy (bajty podpisywane przez ROOT)

| Offset | Pole | Typ |
|---|---|---|
| 0 | `cert_version` | u8 = 1 |
| 1 | `issuer_id` | u16 |
| 3 | `pubkey` | 32 B |
| 35 | `valid_from` | u32 (unix s) |
| 39 | `valid_until` | u32 (unix s) |
| 43 | `scope_count` | u8 |
| 44 | `scopes` | `scope_count × u16` |
| … | `name_len` | u8 |
| … | `name` | UTF-8 |

`root_sig = Ed25519(root, cert_bytes)`.

### 5.4 Plik `trust_store.json`

```json
{
  "version": 1,
  "root_fingerprint": "6A38-03D5-F059-902A",
  "issuers": [
    { "cert_b64": "<base64 standard certyfikatu>", "root_sig_b64": "<base64 standard podpisu>" }
  ]
}
```

Aplikacja przy starcie weryfikuje **każdy** certyfikat wbudowanym kluczem ROOT. Certyfikat z błędnym podpisem jest odrzucany i logowany. Nazwa i zakres wydawcy pochodzą **tylko z bajtów certyfikatu**, nigdy z innych pól JSON.

---

## 6. Obszary

Kody oparte na TERYT:

| Kod | Znaczenie |
|---|---|
| `0` | Cała Polska |
| `1..99` | Województwo (2 cyfry TERYT), np. `14` mazowieckie, `12` małopolskie |
| `100..9999` | Powiat (4 cyfry TERYT), np. `1465` Warszawa, `1261` Kraków |

**Reguła pokrycia** `covers(c, a)` (czy obszar `c` obejmuje obszar `a`):

```
covers(c, a) = (c == 0) || (c == a) || (c < 100 && a / 100 == c)     // dzielenie całkowite
```

- **Uprawnienie wydawcy:** podpis jest ważny dla `area_code` tylko, jeśli `covers(scope, area_code)` dla któregoś ze scopes sygnatariusza.
- **Trafność dla użytkownika:** komunikat dotyczy użytkownika, jeśli `covers(area_code, user_area)`.

Lista na demo: `0` Cała Polska, `14` woj. mazowieckie, `12` woj. małopolskie, `1465` Warszawa, `1261` Kraków.

---

## 7. Weryfikacja (aplikacja). Kolejność jest obowiązkowa

Wejście: bajty ramki, `now` (UTC), `user_area`, zbiór `revoked`, zbiór `seen` (pary `issuer_id, sequence`).

1. Parsowanie: MAGIC, długość i CRC. Błąd daje **MALFORMED** (ignoruj po cichu i słuchaj dalej).
2. `version != 1` daje **MALFORMED**.
3. Brak podpisów daje **FORGED** `NO_SIGNATURE`. Pierwszy `signer_id != issuer_id` daje **FORGED** `PRIMARY_SIGNER_MISMATCH`.
4. Dla każdego podpisu:
   - `signer_id == 0` → klucz ROOT (wbudowany), scopes `[0]`,
   - inaczej: brak w trust store → **FORGED** `UNKNOWN_ISSUER`,
   - wydawca w `revoked` → **FORGED** `REVOKED_ISSUER`,
   - `now` poza `[valid_from, valid_until]` certyfikatu → **FORGED** `CERT_EXPIRED`,
   - podpis niepoprawny → **FORGED** `BAD_SIGNATURE`,
   - żaden scope nie pokrywa `area_code` → **FORGED** `UNAUTHORIZED_AREA`.
5. `type == 250` i `issuer_id != 0` → **FORGED** `REVOKE_NOT_ROOT`. `issuer_id == 0` i `type != 250` → **FORGED** `ROOT_ONLY_REVOKE`.
6. Typ krytyczny i mniej niż 2 **różne** poprawne podpisy → **INCOMPLETE** `DUAL_SIGNATURE_REQUIRED`.
7. `timestamp > now + 300` → **FORGED** `TIMESTAMP_IN_FUTURE`.
8. `now > timestamp + valid_minutes*60` → **EXPIRED** (prawdziwy, ale nieaktualny, np. odtworzone nagranie).
9. `(issuer_id, sequence)` w `seen` → **DUPLICATE** (już otrzymany, pokaż istniejący wpis, bez alarmu).
10. `!covers(area_code, user_area)` → **VERIFIED_OTHER_AREA**.
11. W przeciwnym razie → **VERIFIED**.

Po **VERIFIED** lub **VERIFIED_OTHER_AREA**: dodaj do `seen`. Jeśli typ to `KEY_REVOKE`, dodaj `note` (u16) do `revoked` i zapisz trwale.

| Status | Kolor | Komunikat dla użytkownika |
|---|---|---|
| VERIFIED | zielony | ✅ ZWERYFIKOWANO, + nadawca, czas, treść, instrukcja |
| VERIFIED_OTHER_AREA | niebieski | ✅ Prawdziwy komunikat, ale dla innego obszaru |
| EXPIRED | bursztynowy | ⚠️ Prawdziwy, ale NIEAKTUALNY (wygasł …), możliwe odtworzone nagranie |
| INCOMPLETE | czerwony | 🟥 Komunikat krytyczny bez drugiego podpisu, NIE WYKONUJ |
| FORGED | czerwony | 🟥 FAŁSZYWKA, + powód po ludzku |
| DUPLICATE | – | brak alarmu |
| MALFORMED | – | ignoruj |

Powody FORGED po ludzku:

- `BAD_SIGNATURE`: „Podpis nie pasuje do {nazwa wydawcy}. Ktoś się podszywa lub zmienił treść.”
- `UNKNOWN_ISSUER`: „Nieznany nadawca.”
- `UNAUTHORIZED_AREA`: „{wydawca} nie ma uprawnień dla tego obszaru.”
- `REVOKED_ISSUER`: „Klucz tego nadawcy został unieważniony.”
- `TIMESTAMP_IN_FUTURE`: „Podejrzany czas wydania.”

---

## 8. Modem dźwiękowy (dual-tone 16-FSK)

### 8.1 Stałe (identyczne we wszystkich implementacjach)

| Stała | Wartość |
|---|---|
| Pasmo A (górne 4 bity) | `fA(n) = 1500 + 100·n` Hz, n = 0..15 (1500–3000 Hz) |
| Pasmo B (dolne 4 bity) | `fB(n) = 3200 + 100·n` Hz, n = 0..15 (3200–4700 Hz) |
| Symbol | 40 ms tonu (dwa tony naraz) + 10 ms ciszy = **1 bajt / 50 ms** |
| Amplituda tonów danych | 0.4 każdy (suma ≤ 0.8) |
| Preambuła | 200 ms tonu **1000 Hz** → 50 ms ciszy → 200 ms tonu **5200 Hz** → 50 ms ciszy |
| Amplituda preambuły / końca | 0.6 |
| Znacznik końca | 200 ms tonu 5200 Hz |
| Obwiednia | każdy ton: narastanie i opadanie **raised cosine 5 ms** (`w(i)=0.5−0.5·cos(π·i/fade)`) |
| Powtórzenia | nadawca wysyła ramkę `R` razy (1–3, domyślnie **2**) z 500 ms ciszy między nimi |
| Długość odcinka | `n = round(sr · ms / 1000)` próbek |

Kolejność: `PREAMBUŁA → bajty ramki (kolejno od MAGIC) → ZNACZNIK KOŃCA`, całość powtórzona R razy.

Bajt `b` → ton `fA(b >> 4)` + ton `fB(b & 0x0F)`.

Czas (1 powtórzenie): `0.5 s + 0.05 s × liczba_bajtów + 0.2 s`. Ramka 118 B trwa około 6,6 s.

Ograniczenie: radio AM obcina pasmo powyżej ~4,5 kHz. Pasmo dla radia FM, głośników i telefonów jest OK. Dla AM to rzecz do rozwiązania w kolejnej wersji (np. niższe pasma).

### 8.2 Enkoder (pseudokod)

```
samples = []
repeat R:
    tone([1000], 200ms, amp .6); silence(50ms)
    tone([5200], 200ms, amp .6); silence(50ms)
    for b in frame:
        tone([1500+100*(b>>4), 3200+100*(b&15)], 40ms, amp .4); silence(10ms)
    tone([5200], 200ms, amp .6)
    if not last: silence(500ms)

tone(freqs, ms, amp): n=round(sr*ms/1000); s[i]=Σ amp*sin(2π f i/sr); raised-cosine fade 5ms na obu końcach
```

### 8.3 Dekoder (zalecany algorytm, sprawdzony w implementacji referencyjnej)

**Moc znormalizowana** dla okna `x` i częstotliwości `f` (Goertzel lub DFT jednej częstotliwości):

```
P(f) = 2·|X(f)|² / (N · Σx²)       // czysty ton ≈ 1.0, niezależnie od głośności
```

1. **Detekcja preambuły:** okno 10 ms, krok 2,5 ms, liczysz `P(1000)` i `P(5200)`.
   - Szukaj ≥ 120 ms z `P(1000) > 0.5`, po którym w ciągu ≤ 120 ms zaczyna się ≥ 120 ms z `P(5200) > 0.5`.
   - **Synchronizacja od NARASTAJĄCEGO zbocza tonu 5200 Hz** (pogłos wydłuża tylko koniec tonu, więc opadające zbocze się nie nadaje):
     `onsetB = indeks_pierwszego_okna · hop + okno/2`, a `t0 = onsetB + 200 ms + 50 ms`.
2. **Dostrojenie t0:** sprawdź przesunięcia od −12 do +12 ms co 1 ms. Dla każdego zdekoduj 4 pierwsze symbole. Wybierz przesunięcie, dla którego pierwsze 2 bajty to `"SG"`, a suma pewności (`P` zwycięskich tonów) jest największa.
3. **Symbol k** zaczyna się w `t0 + k·50 ms`. Analizuj okno **[6 ms, 34 ms]** od początku symbolu. W paśmie A wybierz `argmax P` z 16 częstotliwości (górne 4 bity), w paśmie B analogicznie (dolne 4 bity).
4. Z symboli 2–3 odczytaj `frame_len` (odrzuć, jeśli > 400). Zdekoduj pozostałe `frame_len` symboli. Sprawdź CRC.
5. Ramki z poprawnym CRC przekaż do weryfikacji (§7). Powtórzenia odrzucaj po identycznych bajtach.

Na 48 kHz okno analizy (28 ms) ma 1344 próbki × 32 częstotliwości, czyli trywialny koszt, również na telefonie.

---

## 9. Wektory testowe (`testvectors/testvectors.json`)

Seedy testowe (**tylko do testów!**): ROOT = `0x02×32`, wydawca 1 = `0x01×32`, 3 = `0x03×32`, 5 = `0x05×32`, 6 = `0x06×32`, HAKER = `0x66×32`.
`now = timestamp + 600`, `user_area = 1465`. Certyfikaty ważne 2026-01-01 – 2028-01-01.

| Wektor | Opis | Oczekiwany status |
|---|---|---|
| TV1_air_raid_single | Alarm lotniczy, wyd. 1, Warszawa, dopisek z polskimi znakami | VERIFIED |
| TV2_evacuation_dual | Ewakuacja, podpisy 3 + 5 | VERIFIED |
| TV3_forged_hacker_as_1 | Haker podpisuje swoim kluczem, podaje się za wyd. 1 | FORGED `BAD_SIGNATURE:1` |
| TV4_evacuation_single | Ewakuacja z 1 podpisem | INCOMPLETE |
| TV5_unauthorized_area | Prezydent Warszawy wydaje komunikat dla Krakowa | FORGED `UNAUTHORIZED_AREA:6` |
| TV6_tampered_note | TV1 ze zmienionym 1 bajtem dopisku i przeliczonym CRC | FORGED `BAD_SIGNATURE:1` |

Przy `now = timestamp + 3 dni` TV1 i TV2 dają **EXPIRED**.

Kontrolnie TV1:

```
payload_hex = 0100010105b96ac1a7e000780001001d536368726f6e3a206d6574726f20c59a7769c499746f6b727a79736b61
root fingerprint = 6A38-03D5-F059-902A
CRC("123456789") = 29B1
```

Pełne `frame_hex`, `qr_text`, trust store i pliki WAV są w `testvectors/`.
`python3 tools/sygnet_ref.py decode plik.wav` dekoduje dowolne nagranie (np. WAV z konsoli) i je weryfikuje.
