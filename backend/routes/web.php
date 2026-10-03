<?php

use App\Http\Controllers\BroadcastController;
use App\Http\Controllers\LoginController;
use App\Http\Controllers\PageController;
use App\Http\Middleware\ConsolePassword;
use Illuminate\Support\Facades\Route;

// hasło operatora tylko, gdy SYGNET_CONSOLE_PASSWORD jest ustawione (serwer publiczny); lokalnie bez logowania
Route::get('/login', [LoginController::class, 'show'])->name('login');
Route::post('/login', [LoginController::class, 'login'])->middleware('throttle:10,1');
Route::post('/logout', [LoginController::class, 'logout'])->name('logout');

Route::middleware(ConsolePassword::class)->group(function () {
    Route::redirect('/', '/console');

    Route::get('/console', [PageController::class, 'console'])->name('console');
    Route::get('/attack', [PageController::class, 'attack'])->name('attack');
    Route::get('/keys', [PageController::class, 'keys'])->name('keys');
    Route::get('/keys/export/{file}', [PageController::class, 'export'])->name('keys.export');
    Route::get('/poster/{broadcast}', [PageController::class, 'poster'])->name('poster');

    // JSON w grupie „web”: sesja + CSRF (CONSOLE_LARAVEL.md §5.3) – konsola działa lokalnie albo za hasłem
    Route::prefix('api')->group(function () {
        Route::post('/broadcast', [BroadcastController::class, 'store']);
        Route::get('/broadcasts', [BroadcastController::class, 'index']);
        Route::post('/revoke', [BroadcastController::class, 'revoke']);
        Route::post('/attack/{type}', [BroadcastController::class, 'attack'])->where('type', '[aA][1-7]');
    });
});
