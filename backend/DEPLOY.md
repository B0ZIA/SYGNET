# Wdrożenie konsoli SYGNET na VPS

Instrukcja dla Ubuntu 22.04 / 24.04 (nginx + PHP-FPM + MySQL). Konsola to zwykła aplikacja Laravel –
**dźwięk gra przeglądarka komputera, na którym otwierasz stronę**, więc serwer tylko serwuje stronę i podpisuje ramki.

## 0. Zanim zaczniesz – 3 rzeczy, które łatwo zepsuć

1. **Klucze.** Aplikacja na telefonach ma wbudowany ROOT `82B7-E5B3-7129-166D`. Pasujące klucze prywatne są
   **tylko** na laptopie, w `backend/storage/app/keys/` (nie ma ich w gicie). Trzeba je **skopiować** na serwer (krok 5).
   **Nie uruchamiaj na serwerze `php artisan sygnet:init`** – wygeneruje nowe klucze i telefony zaczną odrzucać wszystko
   jako FAŁSZYWKĘ (trzeba by budować nowe APK).
2. **Konsola nie ma logowania.** Na publicznym serwerze każdy, kto zna adres, mógłby podpisać „prawdziwy” komunikat.
   Dlatego w nginx **obowiązkowo** hasło (HTTP Basic Auth, krok 8) i HTTPS.
3. **Zegar serwera.** Telefon odrzuca komunikat z czasem z przyszłości (> 5 min) i oznacza stary jako NIEAKTUALNY.
   Sprawdź: `timedatectl` → `System clock synchronized: yes`.
4. **Numeracja komunikatów.** Każdy wydawca ma licznik (`sequence`), a telefon pamięta, które numery już dostał.
   Nowa baza na serwerze zaczęłaby od 1 – telefony testowe, które słyszały konsolę z laptopa, uznałyby pierwsze
   komunikaty za „już otrzymane” i nic by nie pokazały. Dlatego w `.env` jest `SYGNET_SEQUENCE_START=1000` (krok 4).

## 1. Pakiety

```bash
sudo apt update
sudo apt install -y nginx git unzip curl apache2-utils \
    php8.3-fpm php8.3-cli php8.3-mysql php8.3-mbstring php8.3-xml php8.3-curl php8.3-zip php8.3-intl
php -m | grep -E 'sodium|pdo_mysql'       # oba muszą być (sodium jest w php8.3-common)
```

Na Ubuntu 22.04 nie ma PHP 8.3 w standardowych repozytoriach – najpierw `sudo add-apt-repository ppa:ondrej/php`.

Composer i Node 22 (Vite 8 wymaga Node ≥ 20.19):

```bash
curl -sS https://getcomposer.org/installer | php && sudo mv composer.phar /usr/local/bin/composer
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - && sudo apt install -y nodejs
```

## 2. Kod

Klonujemy całe repo (komenda `sygnet:testvectors` korzysta z `testvectors/` w katalogu głównym):

```bash
sudo mkdir -p /var/www && cd /var/www
sudo git clone https://github.com/B0ZIA/SYGNET.git sygnet
sudo chown -R $USER:www-data /var/www/sygnet
cd /var/www/sygnet/backend
```

## 3. Zależności i build frontu

```bash
composer install --no-dev --optimize-autoloader
npm ci
npm run build              # → public/build (fonty, JS, CSS – bez CDN)
```

## 4. Baza MySQL i `.env`

Baza i użytkownik (MySQL ≥ 5.7.8 albo MariaDB ≥ 10.2.7 – potrzebny typ JSON):

```bash
sudo mysql
```

```sql
CREATE DATABASE sygnet CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'sygnet'@'localhost' IDENTIFIED BY 'TU_SILNE_HASLO';
GRANT ALL PRIVILEGES ON sygnet.* TO 'sygnet'@'localhost';
FLUSH PRIVILEGES;
EXIT;
```

`utf8mb4` jest konieczne – dopiski i nazwy wydawców mają polskie znaki.

```bash
cp .env.example .env
php artisan key:generate
nano .env
```

Zmień:

```dotenv
APP_NAME=SYGNET
APP_ENV=production
APP_DEBUG=false
APP_URL=https://sygnet.twojadomena.pl
APP_LOCALE=pl

DB_CONNECTION=mysql
DB_HOST=127.0.0.1
DB_PORT=3306
DB_DATABASE=sygnet
DB_USERNAME=sygnet
DB_PASSWORD=TU_SILNE_HASLO

SESSION_SECURE_COOKIE=true

SYGNET_USER_AREA=1261          # obszar telefonu w podglądzie (1261 = Kraków)
SYGNET_SECOND_PIN=1234         # PIN drugiego operatora przy ewakuacji – na serwerze ustaw inny
SYGNET_SEQUENCE_START=1000     # numery komunikatów od 1000 – patrz punkt 0.4
```

W `.env.example` linie `DB_HOST` … `DB_PASSWORD` są zakomentowane (`#`) – usuń `#` albo dopisz je jak wyżej.
Jeśli MySQL jest na innym serwerze, podaj jego adres w `DB_HOST` i nadaj uprawnienia `'sygnet'@'ADRES_VPS'`.

Tabele:

```bash
php artisan migrate --force
```

## 5. Klucze z laptopa

Na **laptopie** (PowerShell, w katalogu repo):

```powershell
scp -r backend\storage\app\keys backend\storage\app\export uzytkownik@ADRES_VPS:/tmp/
```

Na **serwerze**:

```bash
cd /var/www/sygnet/backend
mkdir -p storage/app
mv /tmp/keys /tmp/export storage/app/
sudo chown -R www-data:www-data storage/app/keys storage/app/export
sudo chmod 700 storage/app/keys && sudo chmod 600 storage/app/keys/*.seed
```

Powinno być 9 plików: `0.seed` … `7.seed` i `hacker.seed`. Klucze przesyłaj tylko przez `scp`/`rsync` –
nie przez maila, komunikator ani repo.

## 6. Uprawnienia i cache

```bash
sudo chown -R www-data:www-data storage bootstrap/cache
sudo chmod -R ug+rwX storage bootstrap/cache
php artisan optimize          # cache konfiguracji, tras i widoków
```

Kontrola (nie rusza kluczy konsoli, działa na kluczach testowych z `testvectors/`):

```bash
php artisan sygnet:testvectors     # musi wypisać ALL OK
```

## 7. nginx

`sudo nano /etc/nginx/sites-available/sygnet`:

```nginx
server {
    listen 80;
    server_name sygnet.twojadomena.pl;
    root /var/www/sygnet/backend/public;
    index index.php;

    # konsola podpisuje prawdziwymi kluczami – bez hasła ani rusz
    auth_basic "SYGNET";
    auth_basic_user_file /etc/nginx/sygnet.htpasswd;

    add_header X-Frame-Options "SAMEORIGIN";
    add_header X-Content-Type-Options "nosniff";
    charset utf-8;

    location / {
        try_files $uri $uri/ /index.php?$query_string;
    }

    location ~ \.php$ {
        fastcgi_pass unix:/run/php/php8.3-fpm.sock;
        fastcgi_param SCRIPT_FILENAME $realpath_root$fastcgi_script_name;
        include fastcgi_params;
        fastcgi_hide_header X-Powered-By;
    }

    location ~ /\.(?!well-known).* {
        deny all;
    }
}
```

## 8. Hasło i HTTPS

```bash
sudo htpasswd -c /etc/nginx/sygnet.htpasswd operator     # poda o hasło
sudo ln -s /etc/nginx/sites-available/sygnet /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx

sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d sygnet.twojadomena.pl
```

Kolejni operatorzy: `sudo htpasswd /etc/nginx/sygnet.htpasswd druga_osoba` (bez `-c`, bo nadpisze plik).

## 9. Sprawdzenie

1. Otwórz `https://sygnet.twojadomena.pl` → prosi o hasło → `/console`.
2. W nagłówku i na stronie **Klucze** musi być ROOT **`82B7-E5B3-7129-166D`** (bez dopisku „test”).
   Inny odcisk = złe klucze → wróć do kroku 5.
3. Podpisz komunikat i pokaż QR – zeskanuj telefonem z aplikacją SYGNET → ✅ ZWERYFIKOWANO.
4. „Nadaj dźwiękiem” gra z głośnika komputera, na którym jest otwarta strona – telefon 20–50 cm od głośnika.

## 10. Aktualizacja

```bash
cd /var/www/sygnet && git pull
cd backend
composer install --no-dev --optimize-autoloader
npm ci && npm run build
php artisan migrate --force
php artisan optimize
sudo systemctl reload php8.3-fpm
```

Klucze w `storage/app/keys` są poza gitem, a historia nadań w MySQL – `git pull` ich nie rusza.
Kopia historii: `mysqldump -u sygnet -p sygnet > sygnet_$(date +%F).sql`. Kopię kluczy trzymaj poza serwerem (np. na pendrivie).

## 11. Gdy coś nie działa

| Objaw | Co sprawdzić |
|---|---|
| Błąd 500 | `tail -50 storage/logs/laravel.log`; uprawnienia z kroku 6 |
| `SQLSTATE[HY000] [1045] Access denied` | `DB_USERNAME` / `DB_PASSWORD` w `.env`, uprawnienia `GRANT` z kroku 4 |
| `SQLSTATE[HY000] [2002]` | MySQL nie działa (`systemctl status mysql`) albo zły `DB_HOST` / `DB_PORT` |
| `could not find driver` | brak `php8.3-mysql` → `sudo apt install php8.3-mysql && sudo systemctl reload php8.3-fpm` |
| Telefon nie reaguje na nowe komunikaty z serwera (bez błędu) | telefon ma te numery już za sobą – ustaw `SYGNET_SEQUENCE_START` wyżej (np. 2000) i `php artisan optimize`; albo w aplikacji: 5× dotknij logo → „Wyczyść skrzynkę i pamięć odbioru” |
| W nagłówku „ROOT brak”, przy podpisie „Konsola nie ma klucza tego nadawcy” | klucze nie zostały skopiowane albo www-data nie może ich czytać (krok 5) – **nie** uruchamiaj `sygnet:init` |
| Telefon pokazuje FAŁSZYWKA dla każdego komunikatu | inny ROOT niż w aplikacji – porównaj odcisk na stronie Klucze |
| Telefon pokazuje „Podejrzany czas wydania” | zegar serwera (`timedatectl`) |
| Brak stylów / pusta strona | `npm run build` nie przeszło albo brak `public/build` |
| 419 przy podpisywaniu | wygasła sesja – odśwież stronę; przy HTTPS `SESSION_SECURE_COOKIE=true` |
| Po zmianie `.env` nic się nie zmienia | `php artisan optimize:clear && php artisan optimize` |
