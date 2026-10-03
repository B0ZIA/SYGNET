<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

/**
 * Hasło do konsoli (SYGNET_CONSOLE_PASSWORD). Konsola podpisuje prawdziwymi kluczami, więc na serwerze
 * publicznym nie może być otwarta dla każdego. Bez ustawionego hasła (lokalnie, offline) – przepuszcza wszystko.
 */
class ConsolePassword
{
    public const SESSION_KEY = 'sygnet_console';

    public static function enabled(): bool
    {
        return (string) config('sygnet.console_password') !== '';
    }

    /** Odcisk hasła w sesji – zmiana hasła w .env wylogowuje wszystkich. */
    public static function token(): string
    {
        return hash_hmac('sha256', (string) config('sygnet.console_password'), (string) config('app.key'));
    }

    public function handle(Request $request, Closure $next): Response
    {
        if (! self::enabled() || hash_equals(self::token(), (string) $request->session()->get(self::SESSION_KEY))) {
            return $next($request);
        }

        if ($request->is('api/*') || $request->expectsJson()) {
            return response()->json(['message' => 'Sesja wygasła – zaloguj się ponownie.'], 401);
        }

        return redirect()->guest(route('login'));
    }
}
