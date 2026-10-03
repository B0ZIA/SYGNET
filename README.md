# SYGNET

**Podpisane komunikaty kryzysowe, które sprawdzisz bez internetu.**
HackYeah 2026 · kategoria Defence · zespół 2-osobowy

> Dla Claude Code: to jest przegląd projektu. Szczegóły:
> - `docs/PROTOCOL.md`: **specyfikacja protokołu (źródło prawdy)**
> - `docs/CLIENT_UNITY.md`: aplikacja obywatela (Unity, Android)
> - `docs/CONSOLE_LARAVEL.md`: konsola nadawcza + laboratorium ataków (Laravel)
> - `tools/sygnet_ref.py`: implementacja referencyjna protokołu i modemu (Python, przetestowana)
> - `testvectors/`: wektory testowe (JSON + WAV), które obie implementacje muszą przechodzić

---

## 1. Problem

W czasie ataku internet i systemy państwowe mogą przestać działać, a ludzie dostają komunikaty z radia, plakatów, SMS-ów i od sąsiadów. **Żadnego z tych kanałów obywatel nie może zweryfikować.** Przeciwnik to wykorzystuje:

- **maj 2024:** atak na PAP i fałszywa depesza o mobilizacji 200 tys. Polaków,
- **sierpień 2023:** nieautoryzowany sygnał „Radio-Stop” zatrzymał ok. 20 pociągów PKP, bo system nie sprawdzał, kto nadaje,
- **2023:** zhakowane stacje radiowe i TV w Rosji nadały fałszywe alarmy lotnicze.

## 2. Rozwiązanie

Każdy oficjalny komunikat dostaje **podpis cyfrowy Ed25519** („pieczątkę”). Komunikat i podpis to ~120 bajtów, które da się przesłać **dowolnym kanałem**:

- 🔊 **dźwiękiem:** kilkusekundowy „ćwierk” w radiu, z megafonu, z głośnika telefonu,
- ▦ **kodem QR:** plakat, ekran TV, drzwi urzędu.

Telefon obywatela ma wbudowany klucz publiczny i **sprawdza podpis offline**:

- ✅ **ZWERYFIKOWANO**: kto wydał, kiedy, dla jakiego obszaru i co robić,
- ⚠️ **NIEAKTUALNY**: prawdziwy, ale wygasły (np. odtworzone nagranie),
- 🟥 **FAŁSZYWKA**: z wyjaśnieniem powodu.

**Przekaż dalej:** telefon odtwarza zweryfikowany komunikat dźwiękiem sąsiadowi. Informacja rozchodzi się od człowieka do człowieka bez sieci, a nadal nie da się jej podrobić.

## 3. Bezpieczeństwo w skrócie

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

Hierarchia kluczy: **ROOT (offline)** → certyfikaty wydawców (Dowództwo Operacyjne, RCB, wojewodowie, PSP, prezydenci miast) → komunikaty.

## 4. Architektura

```
┌─────────────────────────────┐   🔊 dźwięk (radio / głośnik)   ┌──────────────────────────────┐
│ KONSOLA NADAWCZA (Laravel)  │ ──────────────────────────────▶ │ APLIKACJA OBYWATELA (Unity)  │
│ laptop, offline             │   ▦ QR (plakat / ekran)        │ Android, tryb samolotowy     │
│ • formularz + podgląd       │ ──────────────────────────────▶ │ • nasłuch mikrofonu (FSK)    │
│ • podpis Ed25519 (sodium)   │                                 │ • skaner QR                  │
│ • modem JS (Web Audio)      │                                 │ • weryfikacja Ed25519 offline│
│ • laboratorium ataków       │                                 │ • ✅ / ⚠️ / 🟥 + „co robić”   │
│ • klucze, trust store       │ ── trust_store.json + RootKey ─▶│ • 🔊 przekaż dalej ──▶ 📱 B  │
└─────────────────────────────┘    (raz, w czasie pokoju)       └──────────────────────────────┘
```

Wspólny kontrakt: `docs/PROTOCOL.md` + `testvectors/`. Implementacja referencyjna dekoduje ramki przy SNR 6 dB, z pogłosem, przy 44,1 i 48 kHz (`python3 tools/sygnet_ref.py selftest`).

## 5. Podział pracy

| Osoba | Zakres | Dokument |
|---|---|---|
| **Bozia (Unity)** | aplikacja Android: rdzeń protokołu w C#, dekoder audio, QR, UI, przekaż dalej | `docs/CLIENT_UNITY.md` |
| **Osoba 2 (Laravel)** | konsola, podpisywanie, modem JS, laboratorium ataków, klucze; prezentacja PDF | `docs/CONSOLE_LARAVEL.md` |

## 6. Harmonogram 24h i punkty synchronizacji

| Godz. | Unity | Laravel | 🔁 Sync |
|---|---|---|---|
| 0–3 | U1–U2: projekt, rdzeń, testy TV1–TV6 | L1–L2: projekt, FrameBuilder, `sygnet:testvectors` | **S1: obie strony przechodzą wektory testowe** |
| 3–7 | U3–U4: dekoder WAV, QR → wynik | L3–L4: `sygnet:init`, broadcast, QR | **S2: QR z konsoli daje ✅ na telefonie** |
| 7–12 | U5: mikrofon na żywo | L5: modem JS, WAV, wizualizacja | **S3: dźwięk z laptopa daje ✅ na telefonie** |
| 12–16 | U6–U7: prawdziwe klucze, przekaż dalej, skrzynka | L6: laboratorium ataków | **S4: wszystkie ataki dają oczekiwany wynik** |
| 16–19 | U7–U8: design, onboarding, debug | L7: podwójny podpis, /keys, UI | **Feature freeze o 19:00** |
| 19–22 | testy na sali (hałas!), nagranie demo | prezentacja PDF, zrzuty | **2 pełne próby demo** |
| 22–24 | bufor, poprawki krytyczne | wysłanie zgłoszenia | **Wysłać ≥ 1h przed terminem** |

**Zasada:** po S1 nikt nie zmienia protokołu bez zgody drugiej osoby.

## 7. Scenariusz demo (3 min)

1. **Problem (20 s):** „W maju 2024 Rosjanie wrzucili przez PAP fałszywkę o mobilizacji. Jak obywatel ma odróżnić prawdę, gdy nie ma internetu?”
2. **Tryb samolotowy:** pokazujemy na telefonach, że nie ma sieci.
3. **Prawdziwy komunikat:** konsola, Wojewoda Mazowiecki, Alarm lotniczy, Warszawa, ▶. Ćwierk, telefony pokazują **✅ ZWERYFIKOWANO** + instrukcję.
4. **Atak:** laboratorium, A1 „Podszycie pod Dowództwo Operacyjne”, ▶. Telefony pokazują **🟥 FAŁSZYWKA: podpis nie pasuje**.
5. **Powtórka:** A3, stare prawdziwe nagranie. Telefony pokazują **⚠️ NIEAKTUALNY**.
6. **Ewakuacja:** z jednym podpisem **🟥 NIEPEŁNY PODPIS**, z dwoma **✅**.
7. **Sąsiad ostrzega sąsiada:** telefon A, „Przekaż dalej”, telefon B pokazuje **✅**.
8. **QR:** plakat z kodem, skan, **✅**.
9. **Puenta:** „Wróg może wyłączyć internet i podrobić komunikat, ale nie podrobi pieczątki.”

Plan B: jeśli dźwięk zawodzi w hałasie, pokazujemy QR i odtwarzamy WAV bezpośrednio przy mikrofonie telefonu.

## 8. Prezentacja (maks. 10 slajdów)

1. **SYGNET**: tytuł + jedno zdanie
2. **Problem**: PAP 2024, Radio-Stop 2023, fałszywe alarmy radiowe
3. **Kto ma problem**: obywatele bez internetu, służby, które muszą dotrzeć z prawdziwym komunikatem
4. **Rozwiązanie**: pieczątka cyfrowa + dowolny kanał (dźwięk / QR) + weryfikacja offline
5. **Jak to działa**: schemat nadawca → ramka 120 B → telefon
6. **Bezpieczeństwo**: tabela zagrożeń i ochrony, hierarchia kluczy, 2 podpisy
7. **Demo**: zrzuty: konsola, ✅, 🟥, laboratorium ataków
8. **Odporność**: działa bez internetu, prądu sieciowego (radio na baterie), serwerów; przekaż dalej
9. **Wdrożenie**: niski koszt (bez nowej infrastruktury), integracja z RCB/mObywatel w czasie pokoju, rozwój: kanał SMS, niższe pasmo dla radia AM, podpisy postkwantowe (pole `version`)
10. **Zespół + podsumowanie**

## 9. Pytania jury i odpowiedzi

- **Czym to się różni od Alertu RCB?** Alert RCB wymaga sieci komórkowej i nie daje się zweryfikować. SYGNET działa przez dowolny kanał, także radio na baterie, i każdy może sprawdzić autentyczność.
- **Kradzież klucza?** Hierarchia z zakresem obszarów, unieważnianie komunikatem ROOT, 2 podpisy dla komunikatów krytycznych. Produkcyjnie: HSM / karty kryptograficzne.
- **Da się wyliczyć klucz z podsłuchanych komunikatów?** Nie. Ed25519 to ten sam standard, który chroni SSH i Signala, a podpisy są deterministyczne (odporne na błąd, przez który złamano PS3).
- **Komputery kwantowe?** Pole `version` w protokole pozwala przejść na podpisy postkwantowe (np. ML-DSA), w pierwszej kolejności przez kanał QR, który zmieści więcej danych.
- **Hałas, zasięg?** 2 tony na symbol, CRC, powtórzenie ramki. Implementacja referencyjna działa przy SNR 6 dB i z pogłosem. Zawsze jest QR jako drugi kanał.
- **Zegar telefonu offline?** Telefon trzyma czas z zegara RTC. Tolerancja ±5 min na czas „z przyszłości”.

## 10. Przejrzystość (wymóg HackYeah)

W zgłoszeniu ujawniamy:

- **AI:** koncepcja, specyfikacja protokołu, implementacja referencyjna (`tools/sygnet_ref.py`) i dokumentacja powstały z pomocą Claude (Anthropic). Kod aplikacji z pomocą Claude Code. Zespół rozumie i potrafi obronić każdy element.
- **Biblioteki:** BouncyCastle (MIT), ZXing.Net (Apache 2.0), Laravel (MIT), ext-sodium/libsodium (ISC), qrcode (MIT), numpy, cryptography (Python).
- **Inspiracja:** idea transmisji danych dźwiękiem (np. projekt ggwave). Modem SYGNET to własna, prostsza implementacja.
- Wszystko powstało podczas HackYeah 2026.
