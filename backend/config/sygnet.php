<?php

/*
 * SYGNET – konfiguracja konsoli. Wartości 1:1 z docs/PROTOCOL.md §4–§6, §8.
 * Zmiana czegokolwiek tutaj = zmiana w aplikacji Unity i w tools/sygnet_ref.py.
 */
return [

    // PROTOCOL.md §5.2 – nazwy trafiają do certyfikatów (UTF-8, ≤ 255 B)
    'issuers' => [
        0 => ['name' => 'ROOT – Klucz główny SYGNET', 'scopes' => [0]],
        1 => ['name' => 'Dowództwo Operacyjne RSZ', 'scopes' => [0]],
        2 => ['name' => 'Rządowe Centrum Bezpieczeństwa', 'scopes' => [0]],
        3 => ['name' => 'Wojewoda Mazowiecki', 'scopes' => [14]],
        4 => ['name' => 'Wojewoda Małopolski', 'scopes' => [12]],
        5 => ['name' => 'Komendant Wojewódzki PSP Mazowsze', 'scopes' => [14]],
        6 => ['name' => 'Prezydent m.st. Warszawy', 'scopes' => [1465]],
        7 => ['name' => 'Prezydent Miasta Krakowa', 'scopes' => [1261]],
    ],

    // PROTOCOL.md §4 – instrukcje jak w aplikacji (Sygnet.Core.AlertTypes)
    'alert_types' => [
        1 => ['code' => 'AIR_RAID', 'name' => 'Alarm lotniczy', 'icon' => 'plane', 'critical' => false,
            'instruction' => 'Natychmiast udaj się do najbliższego schronu lub piwnicy. Odsuń się od okien.'],
        2 => ['code' => 'ALL_CLEAR', 'name' => 'Odwołanie alarmu', 'icon' => 'check', 'critical' => false,
            'instruction' => 'Zagrożenie minęło. Zachowaj ostrożność i słuchaj kolejnych komunikatów.'],
        3 => ['code' => 'EVACUATION', 'name' => 'Ewakuacja', 'icon' => 'exit', 'critical' => true,
            'instruction' => 'Opuść wskazany obszar wyznaczonym kierunkiem. Zabierz dokumenty, wodę, leki.'],
        4 => ['code' => 'WATER_CONTAMINATION', 'name' => 'Skażenie wody', 'icon' => 'drop', 'critical' => false,
            'instruction' => 'Nie pij wody z kranu. Używaj wody butelkowanej lub przegotowanej.'],
        5 => ['code' => 'POWER_OUTAGE', 'name' => 'Awaria zasilania', 'icon' => 'bolt', 'critical' => false,
            'instruction' => 'Oszczędzaj baterię telefonu. Przygotuj latarkę i radio na baterie.'],
        6 => ['code' => 'CHEMICAL', 'name' => 'Zagrożenie chemiczne', 'icon' => 'flask', 'critical' => false,
            'instruction' => 'Zostań w budynku, zamknij okna i wentylację, uszczelnij drzwi.'],
        7 => ['code' => 'DISINFO_WARNING', 'name' => 'Ostrzeżenie przed dezinformacją', 'icon' => 'shield', 'critical' => false,
            'instruction' => 'Krążą fałszywe komunikaty. Ufaj tylko komunikatom zweryfikowanym w SYGNET.'],
        8 => ['code' => 'GENERAL', 'name' => 'Komunikat ogólny', 'icon' => 'info', 'critical' => false,
            'instruction' => 'Zapoznaj się z treścią komunikatu.'],
        250 => ['code' => 'KEY_REVOKE', 'name' => 'Unieważnienie klucza', 'icon' => 'key', 'critical' => false, 'root_only' => true,
            'instruction' => 'Klucz wskazanego nadawcy został unieważniony. Jego komunikaty nie będą już uznawane.'],
    ],

    // PROTOCOL.md §6 – lista na demo
    'areas' => [
        0 => 'Cała Polska',
        14 => 'woj. mazowieckie',
        12 => 'woj. małopolskie',
        1465 => 'Warszawa',
        1261 => 'Kraków',
    ],

    // obszar telefonu odbiorcy w podglądzie i w kontrolnej weryfikacji (demo: HackYeah w Krakowie)
    'demo_user_area' => (int) env('SYGNET_USER_AREA', 1261),

    // pierwszy numer sequence w nowej bazie (np. 1000 na serwerze, jeśli telefony widziały numery z laptopa)
    'sequence_start' => (int) env('SYGNET_SEQUENCE_START', 1),

    'validity_options' => [30, 120, 1440],

    'repeat_default' => 2,

    // hasło do całej konsoli (serwer publiczny bez dostępu do nginx); puste = bez logowania (lokalnie, offline)
    'console_password' => (string) env('SYGNET_CONSOLE_PASSWORD', ''),

    // ramka „Dla oceniających” z hasłem i PIN-em (na czas oceny HackYeah); false = wersja docelowa, bez podpowiedzi
    'jury_info' => (bool) env('SYGNET_JURY_INFO', true),

    // PIN „drugiego operatora” przy komunikacie krytycznym – tylko na demo, podpis i tak jest podwójny
    'second_operator_pin' => env('SYGNET_SECOND_PIN', '1234'),

    // ważność certyfikatów z sygnet:init (losowe klucze); seedy testowe mają daty z wektorów
    'cert_valid_years' => 2,

    'paths' => [
        'keys' => storage_path('app/keys'),
        'export' => storage_path('app/export'),
        'testvectors' => base_path('../testvectors/testvectors.json'),
    ],
];
