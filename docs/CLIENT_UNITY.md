# SYGNET – Aplikacja obywatela (Unity → Android)

> **Instrukcja dla Claude Code.** Budujesz aplikację mobilną SYGNET w Unity (C#) na Androida.
> Zanim napiszesz jakikolwiek kod, przeczytaj **`docs/PROTOCOL.md`**. To jedyne źródło prawdy o formacie bajtów, kryptografii, weryfikacji i modemie dźwiękowym.
> Implementacja referencyjna w Pythonie: `tools/sygnet_ref.py`. Gdy masz wątpliwość, jak coś ma działać, ona rozstrzyga.
> Wektory testowe: `testvectors/testvectors.json` + `testvectors/*.wav`. **Wszystkie muszą przechodzić**, zanim zajmiemy się UI.

## 1. Cel

Telefon obywatela **bez internetu** (tryb samolotowy):

1. **Nasłuchuje mikrofonem** komunikatów nadawanych dźwiękiem (z radia, z głośnika konsoli, z innego telefonu).
2. **Skanuje kody QR** z komunikatami (plakat, ekran TV).
3. **Weryfikuje podpis offline** i pokazuje pełnoekranowy wynik: ✅ ZWERYFIKOWANO / ⚠️ NIEAKTUALNY / 🟥 FAŁSZYWKA.
4. **Przekazuje dalej**: odtwarza dźwiękiem dokładnie tę samą ramkę, żeby usłyszał ją telefon sąsiada.
5. Trzyma **skrzynkę** zweryfikowanych komunikatów i instrukcje „co robić”, wszystko offline.

Aplikacja **nie wysyła niczego do sieci** i nie wymaga uprawnienia INTERNET.

## 2. Technologia

| Element | Wybór |
|---|---|
| Unity | 2022.3 LTS lub Unity 6 (to, co jest zainstalowane) |
| Platforma | Android, **IL2CPP, ARM64**, minSdk 26 |
| UI | uGUI + TextMeshPro (szybciej niż UI Toolkit na hackathonie) |
| Kryptografia | **BouncyCastle.Cryptography** (DLL z NuGet, `lib/netstandard2.0`) → `Assets/Plugins/` |
| QR | **ZXing.Net** (`zxing.unity.dll`) + `WebCamTexture` |
| Uprawnienia | `RECORD_AUDIO`, `CAMERA` (przez `UnityEngine.Android.Permission`) |

**Ważne:** dodaj `Assets/link.xml`, żeby IL2CPP nie wyciął BouncyCastle:

```xml
<linker>
  <assembly fullname="BouncyCastle.Cryptography" preserve="all"/>
  <assembly fullname="zxing.unity" preserve="all"/>
</linker>
```

## 3. Architektura

Dwie warstwy. **Rdzeń to czysty C# bez `UnityEngine`**, testowalny w EditMode.

```
Assets/
├── Sygnet/
│   ├── Core/                         ← asmdef: Sygnet.Core (noEngineReferences: true)
│   │   ├── Crc16.cs
│   │   ├── Payload.cs                ← struktura + ToBytes()/Parse()
│   │   ├── Frame.cs                  ← Build()/Parse(), sygnatury, QR text ↔ bytes
│   │   ├── Ed25519.cs                ← Sign/Verify (BouncyCastle), Fingerprint()
│   │   ├── IssuerCert.cs             ← parsowanie certyfikatu
│   │   ├── TrustStore.cs             ← ładowanie JSON, weryfikacja certów kluczem ROOT
│   │   ├── Areas.cs                  ← Covers(c,a), nazwy obszarów
│   │   ├── AlertTypes.cs             ← typy, nazwy, instrukcje, CRITICAL set
│   │   ├── Verifier.cs               ← §7 protokołu → VerificationResult
│   │   ├── ModemConstants.cs         ← WSZYSTKIE stałe z §8.1
│   │   ├── ModemEncoder.cs           ← byte[] → float[] (dla „Przekaż dalej”)
│   │   ├── ModemDecoder.cs           ← float[] (cały bufor) → List<byte[]> ramek
│   │   └── StreamingDecoder.cs       ← Push(float[]) na żywo z mikrofonu → event OnFrame
│   ├── App/                          ← asmdef: Sygnet.App (Unity)
│   │   ├── RootKey.cs                ← WBUDOWANY klucz publiczny ROOT (pinning)
│   │   ├── SygnetApp.cs              ← bootstrap, stan, nawigacja ekranów
│   │   ├── MicListener.cs            ← mikrofon → StreamingDecoder (Android: usługa w tle + wątek nasłuchu)
│   │   ├── AlertNotification.cs      ← powiadomienie o wyniku weryfikacji odebranej w tle
│   │   ├── QrScanner.cs              ← WebCamTexture + ZXing
│   │   ├── RelayPlayer.cs            ← ModemEncoder → AudioClip → AudioSource
│   │   ├── Storage.cs                ← inbox, seen, revoked, user_area (JSON w persistentDataPath)
│   │   └── UI/                       ← ekrany (sekcja 5)
│   └── Tests/EditMode/               ← asmdef testów, NUnit
│       ├── ProtocolTests.cs
│       └── ModemTests.cs
├── Resources/
│   └── sygnet_trust_store.json       ← z konsoli (php artisan sygnet:init)
├── StreamingAssets/testvectors/      ← *.wav do testów na urządzeniu (panel debug)
├── Plugins/Android/SygnetListen.androidlib/   ← SygnetListenService.java: nasłuch w tle, powiadomienia
└── link.xml
```

### Kluczowe API rdzenia

```csharp
public enum VerifyStatus { Verified, VerifiedOtherArea, Expired, Incomplete, Forged, Duplicate, Malformed }

public sealed class VerificationResult {
    public VerifyStatus Status;
    public string ReasonCode;        // np. "BAD_SIGNATURE:1" – jak w sygnet_ref.py
    public Payload Payload;          // null przy Malformed
    public string IssuerName;        // z certyfikatu
    public string[] SignerNames;
    public byte[] RawFrame;          // do „Przekaż dalej”
}

public static class Verifier {
    public static VerificationResult Verify(byte[] frame, TrustStore trust, long nowUnix,
        int userArea, ISet<int> revoked, ISet<(int issuer, int seq)> seen);
}
```

`ReasonCode` musi mieć **ten sam format co w `sygnet_ref.py`** (`BAD_SIGNATURE:1`, `UNAUTHORIZED_AREA:6`, `DUAL_SIGNATURE_REQUIRED`…), bo testy porównują go z `testvectors.json`.

## 4. Szczegóły implementacyjne

### 4.1 Kryptografia (BouncyCastle)

```csharp
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

public static bool Verify(byte[] pub32, byte[] msg, byte[] sig64) {
    var v = new Ed25519Signer();
    v.Init(false, new Ed25519PublicKeyParameters(pub32, 0));
    v.BlockUpdate(msg, 0, msg.Length);
    return v.VerifySignature(sig64);
}
public static byte[] Sign(byte[] seed32, byte[] msg) {            // tylko w testach
    var s = new Ed25519Signer();
    s.Init(true, new Ed25519PrivateKeyParameters(seed32, 0));
    s.BlockUpdate(msg, 0, msg.Length);
    return s.GenerateSignature();
}
```

`RootKey.cs` to stała `byte[]` z kluczem publicznym ROOT (base64 z `php artisan sygnet:init`) oraz `Fingerprint` do pokazania na ekranie „O aplikacji”. **W aplikacji nie ma żadnego klucza prywatnego.**
Na start, zanim konsola będzie gotowa, użyj ROOT z wektorów testowych (`root_pub_b64` w `testvectors.json`) i trust store z `testvectors.json → trust_store`.

### 4.2 Mikrofon → dekoder strumieniowy

- `Microphone.Start(null, loop: true, lengthSec: 10, frequency: sr)`, gdzie `sr` = 48000, jeśli `GetDeviceCaps` pozwala, inaczej maks. dostępny. Dekoder działa dla dowolnego sample rate, bo liczy po częstotliwościach.
- W `Update()`: odczytaj nowe próbki od ostatniej pozycji (`Microphone.GetPosition`, uwaga na zawinięcie bufora kołowego) i wywołaj `StreamingDecoder.Push(samples)`.
- `StreamingDecoder`:
  - trzyma bufor ostatnich ~30 s,
  - detekcja preambuły liczona **przyrostowo** (okna 10 ms, krok 2,5 ms, `P(1000)`, `P(5200)`) wg §8.3,
  - po wykryciu `t0` czeka, aż w buforze będzie nagłówek (4 symbole), odczytuje `frame_len`, czeka na resztę i dekoduje (z dostrojeniem ±12 ms),
  - poprawne CRC wywołuje `OnFrame(byte[])`. Odrzucaj identyczne ramki z ostatnich 30 s (nadawca powtarza 2×),
  - dekodowanie może iść w wątku tła, a wynik wraca na główny wątek przez kolejkę (`ConcurrentQueue`).
- **Podczas „Przekaż dalej” wstrzymaj nasłuch**, żeby telefon nie dekodował sam siebie.
- Wskaźnik poziomu sygnału (RMS) i „widmo” z 32 mocy Goertzela dają ładną animację na ekranie nasłuchu.

#### Nasłuch w tle (Android)

Scenariusz: telewizor nadaje komunikat, na końcu sygnał SYGNET; telefon leży w kieszeni z wygaszonym ekranem
albo jest w nim otwarta inna aplikacja – i i tak pokazuje powiadomienie, czy komunikat jest prawdziwy.

- `SygnetListenService` (Java, `Plugins/Android/SygnetListen.androidlib`) to usługa pierwszoplanowa typu `microphone`
  ze stałym powiadomieniem „SYGNET nasłuchuje komunikatów”. `AudioRecord` 48 kHz mono, źródło `UNPROCESSED`
  (jeśli telefon je ma; bez AGC i tłumienia szumów, które psują tony), inaczej `VOICE_RECOGNITION`; partial wakelock;
  bufor kołowy 4 s. Usługa tylko nagrywa, nic nie dekoduje i nic nie wysyła (aplikacja nadal nie ma `INTERNET`).
- Usługę startujemy, gdy aplikacja jest na ekranie (Android 14+ nie pozwala uruchomić mikrofonu z tła), potem działa dalej.
  Ten sam tor służy na pierwszym planie, więc nie ma przełączania mikrofonu przy wyjściu z aplikacji.
- W C# osobny wątek (`AndroidJNI.AttachCurrentThread`) co ok. 40 ms woła `drain()` i karmi `StreamingDecoder`
  – pętla Unity w tle stoi, ale wątki C# działają. Ramka na pierwszym planie idzie kolejką do wątku głównego
  (ekran wyniku jak dotąd), w tle od razu do `SygnetApp.HandleFrameInBackground`: ta sama weryfikacja
  (Verify → Commit → skrzynka, pod blokadą) i `AlertNotification.Post` → `SygnetListenService.notifyResult`.
- Powiadomienie (kanał „Komunikaty SYGNET”, wysoka ważność, wibracja, widoczne na ekranie blokady):
  `✅ ZWERYFIKOWANO · typ` z dopiskiem, nadawcą, obszarem i „Co robić”; `ℹ️ INNY OBSZAR`; `⚠️ NIEAKTUALNY`;
  `⛔ NIEPEŁNY PODPIS`; `⛔ FAŁSZYWKA · podaje się za: typ` z powodem (bez treści atakującego).
  DUPLICATE (np. powtórka w TV) i MALFORMED – bez powiadomienia. Dotknięcie otwiera aplikację na ekranie tego wyniku.
- Uprawnienia: `RECORD_AUDIO` i `POST_NOTIFICATIONS` (pytamy razem po onboardingu), `FOREGROUND_SERVICE(_MICROPHONE)`,
  `WAKE_LOCK`. Koszt: ok. 6% jednego rdzenia przy wygaszonym ekranie (Pixel).
- W edytorze zostaje `Microphone` Unity, tylko gdy aplikacja jest aktywna.

### 4.3 QR

- `WebCamTexture` (tylna kamera), co ~200 ms `BarcodeReader.Decode(pixels, w, h)` z `PossibleFormats = QR_CODE`.
- Tekst musi zaczynać się od `SYG1:`. Inny QR (np. z linkiem) daje komunikat „To nie jest komunikat SYGNET” i **nigdy nie otwieramy URL-i**.

### 4.4 Przekaż dalej

`ModemEncoder.Encode(rawFrame, AudioSettings.outputSampleRate, repeat: 2)`, potem `AudioClip.Create`, `SetData`, `AudioSource.Play`. Dwa powtórzenia (ok. 14 s zamiast 7 s): głośnik telefonu potrafi przekłamać pojedyncze symbole, a odbiornik łączy powtórzenia (sumuje moce tonów kopii, które osobno nie przeszły CRC – zmiana tylko po stronie odbiornika, protokół bez zmian). Przekazujemy **oryginalne bajty** (podpis zostaje nienaruszony). Przycisk dostępny tylko dla VERIFIED / VERIFIED_OTHER_AREA.

### 4.5 Stan (Storage)

- `user_area` (wybierany przy pierwszym uruchomieniu),
- `inbox`: lista zweryfikowanych komunikatów (raw frame hex + status + czas odbioru),
- `seen`: `(issuer, seq)`,
- `revoked`: unieważnieni wydawcy (`KEY_REVOKE`).

Zapis jako JSON w `Application.persistentDataPath`. Wszystko działa offline.

### 4.6 Alarm

- VERIFIED alarmu lotniczego, ewakuacji lub zagrożenia chemicznego: `Handheld.Vibrate()` w pętli 3× i pełnoekranowy kolor.
- FORGED: krótka wibracja i czerwony ekran z wyjaśnieniem.
- Nie odtwarzaj dźwięku alarmu przez głośnik podczas nasłuchu (zakłóca dekodowanie). Wibracja wystarczy.

## 5. Ekrany i design

**Styl:** ciemny, wysoki kontrast, duże fonty (min. 18 sp, nagłówki statusu 40+ sp), jak systemowa aplikacja ratunkowa: spokojna, czytelna w stresie, obsługa jedną ręką. Kolory statusów:

| Status | Tło | Ikona/tekst |
|---|---|---|
| VERIFIED | `#0E7A3E` | ✅ ZWERYFIKOWANO |
| VERIFIED_OTHER_AREA | `#1F4E8C` | ✅ INNY OBSZAR |
| EXPIRED | `#B7791F` | ⚠️ NIEAKTUALNY |
| INCOMPLETE / FORGED | `#B42318` | 🟥 FAŁSZYWKA / NIEPEŁNY PODPIS |
| tło aplikacji | `#0B0F14`, karty `#151B23`, tekst `#E6EDF3` | |

1. **Onboarding** (pierwsze uruchomienie): wybór obszaru (Warszawa, Kraków…), prośba o mikrofon i kamerę, ekran „Klucz główny: 6A38-03D5-F059-902A, porównaj z wydrukiem w urzędzie”.
2. **Nasłuch** (ekran główny): duży pulsujący okrąg „Nasłuchuję komunikatów…”, animowane słupki widma, ikona ✈️ „działa bez internetu”, przyciski: `▦ Skanuj QR` i `📥 Skrzynka`. Po wykryciu preambuły pokaż „Odbieram… 34%” (postęp wg `frame_len`). To świetnie wygląda na demo.
3. **Wynik** (pełny ekran w kolorze statusu): status, nadawca (z certyfikatu) + odcisk, typ z ikoną, obszar, czas wydania i „ważne do”, dopisek, **instrukcja „Co robić”** (z `AlertTypes`), przy dwóch podpisach „Podpisali: Wojewoda Mazowiecki + Komendant PSP”. Przyciski: `🔊 Przekaż dalej`, `OK`. Dla FORGED duże wyjaśnienie powodu po ludzku (`PROTOCOL.md` §7).
4. **Skrzynka:** lista otrzymanych komunikatów z kolorowym paskiem statusu, tap otwiera ekran wyniku.
5. **O aplikacji / zaufani nadawcy:** lista wydawców z trust store (nazwa, zakres, odcisk, ważność, unieważniony?).
6. **Panel debug** (ukryty, 5× tap w logo): sample rate, RMS, `P(1000)`/`P(5200)` na żywo, ostatnia surowa ramka hex, przycisk „Dekoduj testowe WAV” (pliki ze StreamingAssets, czytane przez `UnityWebRequest`). Ratuje skórę, gdy coś nie działa na scenie.

## 6. Testy (EditMode, NUnit), robione PRZED UI

`testvectors.json` i WAV skopiuj do `Assets/Sygnet/Tests/EditMode/Data/`.

- `Crc16("123456789") == 0x29B1`
- dla każdego wektora: `Payload.Parse(payload_hex).ToBytes()` daje identyczne bajty,
- podpis z seeda testowego daje dokładnie `frame_hex` (Ed25519 jest deterministyczny),
- `Verifier.Verify(frame, now_for_tests, user_area 1465)` daje `expected_status` + `reason`,
- `Verifier.Verify(..., now_expired)` daje `expected_status_3_days_later`,
- `TrustStore` weryfikuje wszystkie certy z `trust_store` kluczem `root_pub_b64`; zmieniony bajt certu powoduje jego odrzucenie,
- `ModemDecoder.Decode(wav)` zwraca dokładnie `frame_hex` dla każdego WAV,
- `ModemEncoder.Encode(frame) → ModemDecoder.Decode` przechodzi pętlę zwrotną, także z dodanym szumem i 500 ms ciszy na początku.

## 7. Kolejność pracy (kamienie milowe)

| # | Cel | Kryterium „gotowe” |
|---|---|---|
| U1 | Projekt Unity, Android build, BouncyCastle + link.xml | pusta aplikacja odpala się na telefonie |
| U2 | `Sygnet.Core`: CRC, Payload, Frame, Ed25519, TrustStore, Verifier | testy protokołu zielone (TV1–TV6) |
| U3 | `ModemDecoder` + `ModemEncoder` | testy WAV zielone |
| U4 | **Szkielet przepływu:** QR → Verify → ekran wyniku | skan QR z `qr_text` TV1 na ekranie laptopa daje ✅ |
| U5 | `MicListener` + `StreamingDecoder` | WAV TV1 odtworzony z laptopa daje ✅ na telefonie |
| U6 | Integracja z konsolą: prawdziwy trust store i ROOT z `sygnet:init` | komunikat z konsoli daje ✅, tryb hakera daje 🟥 |
| U7 | Przekaż dalej, skrzynka, onboarding, design | telefon A → telefon B daje ✅ |
| U8 | Panel debug, dopieszczenie, nagranie wideo demo | próba generalna demo bez błędów |

**Priorytety, jeśli zabraknie czasu:** U1–U6 obowiązkowe. Przekaż dalej (U7) jest bardzo ważne dla pitchu. Skrzynka, onboarding i panel debug mogą być uproszczone.

## 8. Czego NIE robić

- Nie dodawaj sieci, Firebase, analityki, uprawnienia INTERNET.
- Nie zmieniaj stałych modemu ani formatu bajtów (patrz `PROTOCOL.md`). Jeśli trzeba, uzgadniamy to z konsolą i aktualizujemy `sygnet_ref.py`.
- Nie pisz własnej kryptografii, tylko BouncyCastle.
- Nie otwieraj linków z QR.
