# SYGNET

**Podpisane komunikaty kryzysowe, które sprawdzisz bez internetu.**
HackYeah 2026 · kategoria Defence

**Wypróbuj:**

- konsola nadawcza: **https://api.hackathon.copentro.com/console** (hasło dla oceniających jest na stronie logowania),
- aplikacja na Androida: **https://api.hackathon.copentro.com/sygnet.apk**.

## 1. Problem

W czasie ataku internet i systemy państwowe mogą przestać działać, a ludzie dostają komunikaty z radia, telewizji, plakatów i od sąsiadów. **Żadnego z tych kanałów obywatel nie może zweryfikować.** Przeciwnik to wykorzystuje:

- **maj 2024:** atak na PAP i fałszywa depesza o mobilizacji 200 tys. Polaków,
- **sierpień 2023:** nieautoryzowany sygnał „Radio-Stop” zatrzymał ok. 20 pociągów PKP, bo system nie sprawdzał, kto nadaje,
- **2023:** zhakowane stacje radiowe i TV w Rosji nadały fałszywe alarmy lotnicze.

## 2. Rozwiązanie

Każdy oficjalny komunikat dostaje **podpis cyfrowy Ed25519** („pieczątkę”). Komunikat i podpis to ~120 bajtów, które da się przesłać **dowolnym kanałem**:

- 🔊 **dźwiękiem:** kilkusekundowy „ćwierk” po komunikacie w radiu, telewizji, z megafonu albo głośnika telefonu,
- ▦ **kodem QR:** plakat, ekran TV, drzwi urzędu.

Telefon obywatela ma wbudowany klucz główny (ROOT) i **sprawdza podpis offline**:

- ✅ **ZWERYFIKOWANO**: kto wydał, kiedy, dla jakiego obszaru i co robić,
- ⚠️ **NIEAKTUALNY**: prawdziwy, ale wygasły (np. odtworzone nagranie),
- 🟥 **FAŁSZYWKA** / **NIEPEŁNY PODPIS**: z wyjaśnieniem powodu po ludzku.

**Nasłuch w tle:** aplikacja słucha także wtedy, gdy telefon leży w kieszeni z wygaszonym ekranem. Gdy po komunikacie w radiu albo telewizji zabrzmi sygnał SYGNET, na ekranie blokady pojawia się powiadomienie z wynikiem.

**Przekaż dalej:** telefon odtwarza zweryfikowany komunikat dźwiękiem sąsiadowi. Informacja rozchodzi się od człowieka do człowieka bez sieci, a nadal nie da się jej podrobić.

## 3. Co działa

**Aplikacja obywatela** (`SYGNET_Unity/`, Unity 6000.3.8f1, Android):

- weryfikacja Ed25519 w telefonie (BouncyCastle); aplikacja **nie ma uprawnienia INTERNET** i działa w trybie samolotowym,
- odbiór dźwiękiem: modem 2 × 16-FSK, dekodowanie strumieniowe z mikrofonu, łączenie powtórzeń ramki; dekoduje przy SNR 6 dB i z pogłosem,
- nasłuch w tle (usługa pierwszoplanowa Androida) i powiadomienie z wynikiem na ekranie blokady,
- skaner QR (ZXing.Net),
- ekran wyniku: ZWERYFIKOWANO / INNY OBSZAR / NIEAKTUALNY / NIEPEŁNY PODPIS / FAŁSZYWKA, „co robić”, nadawca i odcisk jego klucza, podpisujący, czasy,
- kolejka komunikatów: nowy komunikat nie zabiera ekranu w trakcie czytania – pojawia się pasek „Nowy komunikat · Pokaż”; powtórzony komunikat daje informację „Już w skrzynce”,
- skrzynka, „Przekaż dalej”, onboarding (obszar, odcisk ROOT do porównania, zgoda na mikrofon), ekran „Klucze i nadawcy”,
- okrąg nasłuchu reaguje na dźwięk otoczenia (poziom liczony względem szumu tła), w trakcie odbioru pokazuje postęp,
- ukryty panel diagnostyczny: 5 × dotknięcie logo.

**Konsola nadawcza** (`backend/`, Laravel 13, PHP 8.4):

- formularz: nadawca, typ, obszar w zakresie nadawcy, ważność, dopisek; podgląd telefonu na żywo i kontrolna weryfikacja tą samą logiką co w aplikacji,
- nadanie dźwiękiem (modem w JS, Web Audio) z wizualizacją tonów, kod QR, plik WAV, plakat A4 z kodem QR (ważność do 7 dni), historia,
- ewakuacja wymaga dwóch podpisów: drugi operator zatwierdza ją PIN-em,
- **laboratorium ataków** (`/attack`): siedem ataków A1–A7, przycisk „Uruchom” od razu nadaje dźwiękiem prawdziwą fałszywkę, a obok widać, co pokaże telefon,
- **klucze** (`/keys`): odcisk ROOT, certyfikaty urzędów, unieważnienie klucza (KEY_REVOKE od ROOT), eksport trust store dla aplikacji,
- hasło do konsoli (`SYGNET_CONSOLE_PASSWORD`) i podpowiedź dla oceniających na stronie logowania (`SYGNET_JURY_INFO`),
- wdrożona na **https://api.hackathon.copentro.com**.

## 4. Bezpieczeństwo w skrócie

| Zagrożenie | Ochrona |
|---|---|
| Podrobienie komunikatu | podpis Ed25519, bez klucza prywatnego niewykonalne |
| Zmiana treści | zmiana 1 bajtu unieważnia podpis |
| Odtworzenie starego komunikatu | `timestamp` + `valid_minutes`, potem status NIEAKTUALNY |
| Wydawca poza swoim terenem | certyfikat wydawcy z zakresem obszarów |
| Kradzież jednego klucza | komunikaty krytyczne (ewakuacja) wymagają **2 podpisów**, a klucze można **unieważnić** komunikatem ROOT |
| Podmiana klucza publicznego | klucz ROOT wbudowany w aplikację (pinning) + odcisk do sprawdzenia przez człowieka |
| Złośliwy QR z linkiem | aplikacja nigdy nie otwiera URL-i |
| Brak internetu / serwerów | weryfikacja w 100% lokalna, zero zależności sieciowych |

Hierarchia kluczy: **ROOT (offline)** → certyfikaty wydawców (Dowództwo Operacyjne, RCB, wojewodowie, PSP, prezydenci miast) → komunikaty. Odcisk klucza głównego wersji demo: **`82B7-E5B3-7129-166D`**.

Wszystkie ataki z tabeli można uruchomić w laboratorium ataków:

| | Atak | Wynik w telefonie |
|---|---|---|
| A1 | Podszycie się pod wojsko (własny klucz, nazwa Dowództwa Operacyjnego) | FAŁSZYWKA |
| A2 | Podmiana treści prawdziwego komunikatu | FAŁSZYWKA |
| A3 | Stare nagranie (prawdziwy alarm sprzed 3 dni) | NIEAKTUALNY |
| A4 | Cudzy teren (Prezydent Warszawy ogłasza alarm w Krakowie) | FAŁSZYWKA |
| A5 | Ewakuacja z jednym podpisem | NIEPEŁNY PODPIS |
| A6 | Zmyślony urząd spoza listy zaufanych | FAŁSZYWKA |
| A7 | Skradziony, unieważniony klucz urzędu | FAŁSZYWKA |

## 5. Architektura

```
┌─────────────────────────────┐   🔊 dźwięk (radio / głośnik)   ┌──────────────────────────────┐
│ KONSOLA NADAWCZA (Laravel)  │ ──────────────────────────────▶ │ APLIKACJA OBYWATELA (Unity)  │
│ serwer albo laptop offline  │   ▦ QR (plakat / ekran)        │ Android, tryb samolotowy     │
│ • formularz + podgląd       │ ──────────────────────────────▶ │ • nasłuch mikrofonu, w tle   │
│ • podpis Ed25519 (sodium)   │                                 │ • skaner QR                  │
│ • modem JS (Web Audio)      │                                 │ • weryfikacja Ed25519 offline│
│ • laboratorium ataków       │                                 │ • ✅ / ⚠️ / 🟥 + „co robić”   │
│ • klucze, trust store       │ ── trust_store.json + RootKey ─▶│ • 🔊 przekaż dalej ──▶ 📱 B  │
└─────────────────────────────┘    (raz, w czasie pokoju)       └──────────────────────────────┘
```

Wspólny kontrakt: `docs/PROTOCOL.md` + `testvectors/`. Klucz prywatny nigdy nie trafia do przeglądarki – podpisuje serwer konsoli, przeglądarka tylko gra dźwięk albo pokazuje QR.

## 6. Przetestuj sam (ok. 3 minuty)

1. **Telefon z Androidem:** pobierz i zainstaluj https://api.hackathon.copentro.com/sygnet.apk (zezwól na instalację z nieznanych źródeł). Przy pierwszym uruchomieniu wybierz obszar **Kraków** i zezwól na mikrofon. Możesz włączyć tryb samolotowy.
2. **Komputer z głośnikiem:** otwórz https://api.hackathon.copentro.com/console – hasło dla oceniających jest na stronie logowania.
3. Kliknij **„Podpisz i nadaj dźwiękiem”** i trzymaj telefon 0,5–2 m od głośnika (głośność ok. 70%). Po kilku sekundach telefon pokaże **ZWERYFIKOWANO**.
4. Zakładka **„Laboratorium ataków”**: kliknij „Uruchom” przy dowolnym ataku – telefon pokaże FAŁSZYWKA, NIEAKTUALNY albo NIEPEŁNY PODPIS z powodem.
5. Gdy jest za głośno: w konsoli **„Kod QR”**, w aplikacji **„Skanuj kod QR”**.

## 7. Uruchomienie lokalne

**Konsola** (`backend/`) – PHP 8.3+ z rozszerzeniem `sodium`, Composer, Node 20+:

```bash
cd backend
composer install
cp .env.example .env
php artisan key:generate
touch database/database.sqlite
php artisan migrate
npm install
npm run build
php artisan sygnet:init --force      # nowe klucze: ROOT, wydawcy, „haker” do laboratorium
php artisan serve                    # http://127.0.0.1:8000/console
```

Szczegóły (Windows, klucze testowe, historia do ataku A3, API): [`backend/README.md`](backend/README.md). Na serwerze **nie** uruchamiaj `sygnet:init` – patrz [`backend/DEPLOY.md`](backend/DEPLOY.md).

**Aplikacja** (`SYGNET_Unity/`) – Unity 6000.3.8f1 z modułem Android:

1. Po `sygnet:init` skopiuj `backend/storage/app/export/sygnet_trust_store.json` do `Assets/Resources/` i `RootKey.cs` do `Assets/Sygnet/App/`.
2. File → Build Profiles → Android → Build.

APK z linku ufa wyłącznie kluczom serwera demo – przy własnych kluczach trzeba zbudować aplikację od nowa. Klucze prywatne (`backend/storage/app/keys/`) nie są w repozytorium.

**Testy:**

| Co | Polecenie | Wynik |
|---|---|---|
| Aplikacja: protokół, weryfikator, modem (do SNR 6 dB), dekoder strumieniowy, QR | Unity → Window → General → Test Runner → EditMode | 151 testów |
| Konsola: ramki, podpisy, API, ataki | `cd backend && php artisan test` | 37 testów |
| Modem JS (zgodność próbek z referencją) | `cd backend && npm test` | 8 testów |
| Wektory TV1–TV6 bajt w bajt | `cd backend && php artisan sygnet:testvectors` | ALL OK |
| Implementacja referencyjna | `python3 tools/sygnet_ref.py selftest` (numpy, cryptography) | |

## 8. Dalszy rozwój

- **mObywatel:** nasłuch sygnału i skaner QR wbudowane w aplikację, którą ludzie już mają; klucz główny w HSM, klucze urzędów na kartach kryptograficznych.
- **Podpisany Alert RCB:** ten sam podpis w SMS-ach RCB.
- **Sieć sąsiedzka:** telefony same przekazują zweryfikowany alarm dalej (Bluetooth), bez sieci.
- **Standard sojuszników:** kilka kluczy głównych w jednej aplikacji – wspólny system ostrzegania wschodniej flanki NATO.
- **Podpisy postkwantowe:** protokół ma pole `version`, więc zmiana algorytmu nie psuje zgodności.

## 9. Przejrzystość (wymóg HackYeah)

- **AI:** koncepcja, specyfikacja protokołu, implementacja referencyjna (`tools/sygnet_ref.py`) i dokumentacja powstały z pomocą Claude (Anthropic). Kod aplikacji i konsoli z pomocą Claude Code. Zespół rozumie i potrafi obronić każdy element.
- **Biblioteki:** BouncyCastle (MIT), ZXing.Net (Apache 2.0), Laravel (MIT), ext-sodium/libsodium (ISC), Alpine.js (MIT), Tailwind CSS (MIT), Vite (MIT), qrcode (MIT), numpy, cryptography (Python). Fonty Inter i JetBrains Mono (SIL Open Font License 1.1).
- **Inspiracja:** idea transmisji danych dźwiękiem (np. projekt ggwave). Modem SYGNET to własna, prostsza implementacja.
- Wszystko powstało podczas HackYeah 2026.
