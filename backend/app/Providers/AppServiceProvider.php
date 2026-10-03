<?php

namespace App\Providers;

use App\Sygnet\KeyStore;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\ServiceProvider;

class AppServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        // magazyn kluczy z config/sygnet.php; Broadcaster, TrustStoreExporter i AttackFactory składa kontener
        $this->app->bind(KeyStore::class, fn () => KeyStore::fromConfig());
    }

    public function boot(): void
    {
        Schema::defaultStringLength(191);     // indeksy utf8mb4 także na starszym MySQL/MariaDB
    }
}
