# Wdrożenie konsoli SYGNET na serwer (bez sudo)

Na serwerze działa już backend z tego repo (baza MySQL `hackathon_api`, serwer WWW ustawiony). Aktualizacja do
konsoli **nie wymaga sudo** – wszystko robi się z własnego konta w katalogu projektu.
**Dźwięk gra przeglądarka komputera, na którym otwierasz konsolę** – serwer tylko serwuje stronę i podpisuje ramki.

## 0. Najpierw przeczytaj – 4 rzeczy, które łatwo zepsuć

1. **Klucze.** Aplikacja na telefonach ma wbudowany ROOT `82B7-E5B3-7129-166D`. Pasujące klucze prywatne są
   **tylko** na laptopie Bozi, w `backend/storage/app/keys/` (nie ma ich w gicie) – trzeba je skopiować (krok 7).
   **Nie uruchamiaj na serwerze `php artisan sygnet:init`** – wygeneruje nowe klucze i telefony zaczną odrzucać
   wszystko jako FAŁSZYWKĘ (trzeba by budować nowe APK).
2. **Hasło.** Konsola podpisuje prawdziwymi kluczami, a na serwerze jest publiczna. Bez sudo nie ustawimy hasła
   w nginx, więc jest hasło w samej aplikacji: `SYGNET_CONSOLE_PASSWORD` w `.env` (krok 5) – **obowiązkowo**.
3. **Numeracja.** Telefon pamięta numery komunikatów, które już dostał. Baza na serwerze zaczęłaby od 1, a telefony
   słyszały już konsolę z laptopa – uznałyby pierwsze komunikaty za „już otrzymane”. Stąd `SYGNET_SEQUENCE_START=1000`.
4. **Zegar.** Telefon odrzuca komunikat z czasem z przyszłości (> 5 min). Porównaj `date -u` na serwerze z prawdziwym
   czasem UTC – jeśli się różni, to jedyna rzecz, którą musi poprawić administrator.

## 1. Sprawdź serwer

```bash
cd ~/ŚCIEŻKA/DO/SYGNET/backend      # katalog, w którym leży plik artisan
php -v                              # wymagane ≥ 8.3 (tak było już dla poprzedniej wersji)
php -m | grep -iE 'sodium|pdo_mysql'
composer -V                         # albo: php composer.phar -V
node -v                             # opcjonalnie – bez Node front zbudujesz na laptopie (krok 4B)
date -u
```

Jeśli brakuje `sodium` albo `pdo_mysql` – tylko to wymaga administratora (`sodium` jest zwykle wbudowane w PHP).

**Sprawdź, czy klucze nie będą widoczne z internetu.** Strona musi wskazywać na `backend/public`, a nie na katalog repo:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://TWOJ-ADRES/artisan
curl -s -o /dev/null -w "%{http_code}\n" https://TWOJ-ADRES/backend/artisan
```

Oba muszą dać **404** (albo 403). Jeśli któryś daje **200**, serwer udostępnia pliki projektu – **nie kopiuj kluczy**,
dopóki administrator nie ustawi katalogu strony na `backend/public`.

## 2. Pobierz zmiany

```bash
cd ~/ŚCIEŻKA/DO/SYGNET
git pull
cd backend
```

## 3. Zależności PHP

```bash
composer install --no-dev --optimize-autoloader
```

Bez Composera (nie trzeba sudo):

```bash
php -r "copy('https://getcomposer.org/installer', 'composer-setup.php');"
php composer-setup.php && rm composer-setup.php
php composer.phar install --no-dev --optimize-autoloader
```

Przy błędzie pamięci: `COMPOSER_MEMORY_LIMIT=-1 composer install --no-dev --optimize-autoloader`.

## 4. Front (`public/build`) – bez tego strona daje błąd „Vite manifest not found”

**A) Jest Node na serwerze (≥ 20.19):**

```bash
npm ci
npm run build
```

**B) Nie ma Node** – budujesz na laptopie i wysyłasz gotowe pliki (PowerShell, w katalogu repo):

```powershell
cd backend
npm ci
npm run build
scp -r public\build uzytkownik@ADRES_VPS:~/ŚCIEŻKA/DO/SYGNET/backend/public/
```

## 5. `.env` – dopisz / zmień (bazy `DB_*` nie ruszaj, działa jak dotąd)

```bash
nano .env
```

```dotenv
APP_ENV=production
APP_DEBUG=false

SYGNET_CONSOLE_PASSWORD=TU_DLUGIE_HASLO   # hasło do konsoli – OBOWIĄZKOWO
SYGNET_SECOND_PIN=1234                    # PIN drugiego operatora przy ewakuacji – można zmienić tutaj
SYGNET_SEQUENCE_START=1000                # numery komunikatów od 1000 – patrz punkt 0.3
SYGNET_USER_AREA=1261                     # obszar telefonu w podglądzie (1261 = Kraków)
```

Jeśli strona działa po **HTTPS**, dopisz też `SESSION_SECURE_COOKIE=true`. Po samym HTTP **nie** dopisuj –
logowanie by nie działało.

## 6. Nowa tabela

```bash
php artisan migrate --force
```

Doda tylko tabelę `broadcasts` (historia nadań). Pozostałe tabele w `hackathon_api` już są i nie są zmieniane.

## 7. Klucze z laptopa

Na **laptopie** (PowerShell, w katalogu repo):

```powershell
scp -r backend\storage\app\keys backend\storage\app\export uzytkownik@ADRES_VPS:~/ŚCIEŻKA/DO/SYGNET/backend/storage/app/
```

Na **serwerze**:

```bash
chmod 700 storage/app/keys
chmod 600 storage/app/keys/*.seed
ls storage/app/keys          # 0.seed … 7.seed + hacker.seed (9 plików)
```

Klucze przesyłaj tylko przez `scp`/`rsync` – nie przez maila, komunikator ani repo.

## 8. Cache Laravela

```bash
php artisan optimize:clear
php artisan optimize
```

## 9. Sprawdzenie

1. `https://TWOJ-ADRES/` → strona logowania → hasło z `SYGNET_CONSOLE_PASSWORD` → konsola.
2. W nagłówku i na stronie **Klucze**: ROOT **`82B7-E5B3-7129-166D`**, bez dopisku „test”, wszyscy wydawcy „aktywny”.
3. Podpisz komunikat, „Pokaż QR”, zeskanuj telefonem z aplikacją SYGNET → ✅ ZWERYFIKOWANO.
4. „Nadaj dźwiękiem” gra z głośnika komputera, na którym jest otwarta konsola – telefon 20–50 cm od głośnika.
5. Kontrola implementacji (nie rusza kluczy konsoli): `php artisan sygnet:testvectors` → `ALL OK`.

## 10. Kolejne aktualizacje

```bash
cd ~/ŚCIEŻKA/DO/SYGNET && git pull && cd backend
composer install --no-dev --optimize-autoloader
npm ci && npm run build          # albo krok 4B z laptopa
php artisan migrate --force
php artisan optimize:clear && php artisan optimize
```

Klucze (`storage/app/keys`) i baza nie są w gicie – `git pull` ich nie rusza.

## 11. Gdy coś nie działa

| Objaw | Co zrobić |
|---|---|
| Błąd 500 | `tail -50 storage/logs/laravel.log` |
| `Vite manifest not found` | krok 4 (brak `public/build`) |
| `Table 'hackathon_api.broadcasts' doesn't exist` | krok 6 |
| `Permission denied` w `storage/` | `chmod -R u+rwX storage bootstrap/cache` |
| W nagłówku „ROOT brak”, przy podpisie „Konsola nie ma klucza tego nadawcy” | klucze nie skopiowane (krok 7) albo PHP strony działa jako inny użytkownik i nie czyta plików `600` – wtedy `chmod 755 storage/app/keys && chmod 644 storage/app/keys/*.seed`, ale **tylko jeśli** sprawdzenie z kroku 1 (curl) dało 404; **nie** uruchamiaj `sygnet:init` |
| Logowanie wraca do formularza | po HTTP usuń `SESSION_SECURE_COOKIE=true`; potem `php artisan optimize` |
| 419 przy podpisywaniu | wygasła sesja – odśwież stronę |
| Telefon pokazuje FAŁSZYWKA dla każdego komunikatu | inny ROOT niż w aplikacji – porównaj odcisk na stronie Klucze |
| Telefon pokazuje „Podejrzany czas wydania” | zegar serwera (`date -u`) – do administratora |
| Telefon nie reaguje na komunikaty z serwera (bez błędu) | telefon zna te numery – podnieś `SYGNET_SEQUENCE_START` (np. 2000) i `php artisan optimize`; albo w aplikacji: 5× dotknij logo → „Wyczyść skrzynkę i pamięć odbioru” |
| Zmiany w `.env` nie działają | `php artisan optimize:clear && php artisan optimize` |

## Dodatek: własny serwer z sudo

Root strony nginx/Apache na `backend/public`, PHP-FPM ≥ 8.3 z `sodium` i `pdo_mysql`, HTTPS (certbot), dalej kroki 2–9.
Hasło można wtedy dać dodatkowo w nginx (`auth_basic`), ale `SYGNET_CONSOLE_PASSWORD` wystarcza.
