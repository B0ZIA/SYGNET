<?php

namespace App\Console\Commands;

use App\Sygnet\AttackFactory;
use App\Sygnet\Broadcaster;
use App\Sygnet\IssuerRegistry;
use Illuminate\Console\Command;

/**
 * Prawdziwe komunikaty sprzed 3 dni – materiał do ataku A3 „powtórka” (CONSOLE_LARAVEL.md §5.1).
 */
class SygnetSeedHistory extends Command
{
    protected $signature = 'sygnet:seed-history';

    protected $description = 'Dodaje do historii kilka prawdziwych komunikatów sprzed 3 dni (do ataku „powtórka”)';

    public function handle(): int
    {
        $created = (new AttackFactory(Broadcaster::fromConfig(), new IssuerRegistry))->seedHistory();

        if ($created === []) {
            $this->error('Brak kluczy. Uruchom najpierw: php artisan sygnet:init');

            return self::FAILURE;
        }

        foreach ($created as $b) {
            $this->line(sprintf('#%d  %s  %s  seq %d  %d B', $b->id, gmdate('Y-m-d H:i', $b->timestamp).' UTC',
                $b->summary(), $b->sequence, strlen($b->frame())));
        }
        $this->info(count($created).' komunikatów sprzed 3 dni w historii.');

        return self::SUCCESS;
    }
}
