<?php

use App\Http\Controllers\BroadcastController;
use App\Http\Controllers\PageController;
use Illuminate\Support\Facades\Route;

Route::redirect('/', '/console');

Route::get('/console', [PageController::class, 'console'])->name('console');
Route::get('/attack', [PageController::class, 'attack'])->name('attack');
Route::get('/keys', [PageController::class, 'keys'])->name('keys');
Route::get('/keys/export/{file}', [PageController::class, 'export'])->name('keys.export');

// JSON w grupie „web”: sesja + CSRF (CONSOLE_LARAVEL.md §5.3), bez logowania – konsola działa lokalnie, offline
Route::prefix('api')->group(function () {
    Route::post('/broadcast', [BroadcastController::class, 'store']);
    Route::get('/broadcasts', [BroadcastController::class, 'index']);
    Route::post('/revoke', [BroadcastController::class, 'revoke']);
    Route::post('/attack/{type}', [BroadcastController::class, 'attack'])->where('type', '[aA][1-7]');
});
