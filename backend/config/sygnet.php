<?php


return [

    'issuers' => [
        0 => [
            'name' => 'ROOT',
            'scopes' => [0],
        ],
        1 => [
            'name' => 'Dowództwo Operacyjne RSZ',
            'scopes' => [0],
        ],
        2 => [
            'name' => 'RCB',
            'scopes' => [0],
        ],
        3 => [
            'name' => 'Wojewoda Mazowiecki',
            'scopes' => [14],
        ],
        4 => [
            'name' => 'Wojewoda Małopolski',
            'scopes' => [12],
        ],
        5 => [
            'name' => 'Komendant Woj. PSP Mazowsze',
            'scopes' => [14],
        ],
        6 => [
            'name' => 'Prezydent m.st. Warszawy',
            'scopes' => [1465],
        ],
        7 => [
            'name' => 'Prezydent Miasta Krakowa',
            'scopes' => [1261],
        ],
    ],

    'alert_types' => [
        1 => [
            'code' => 'INFO',
            'name' => 'Informacja',
            'icon' => 'ℹ️',
            'critical' => false,
        ],
        2 => [
            'code' => 'ALARM',
            'name' => 'Alarm',
            'icon' => '⚠️',
            'critical' => false,
        ],
        3 => [
            'code' => 'EVACUATION',
            'name' => 'Ewakuacja',
            'icon' => '🚨',
            'critical' => true,
        ],
        250 => [
            'code' => 'KEY_REVOKE',
            'name' => 'Unieważnienie klucza',
            'icon' => '🔐',
            'critical' => false,
            'root_only' => true,
        ],
    ],

    'areas' => [
        0 => 'Cała Polska',
        14 => 'woj. mazowieckie',
        12 => 'woj. małopolskie',
        1465 => 'Warszawa',
        1261 => 'Kraków',
    ],

    'validity_options' => [
        30,
        120,
        1440,
    ],

    'repeat_default' => 2,

];
