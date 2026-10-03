<?php

namespace App\Http\Controllers;

use App\Http\Middleware\ConsolePassword;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

/**
 * Logowanie jednym hasłem operatora (SYGNET_CONSOLE_PASSWORD) – dla serwera, na którym nie da się ustawić hasła w nginx.
 */
class LoginController extends Controller
{
    public function show(): View|RedirectResponse
    {
        return ConsolePassword::enabled() ? view('login') : redirect()->route('console');
    }

    public function login(Request $request): RedirectResponse
    {
        $request->validate(['password' => ['required', 'string']]);

        if (! ConsolePassword::enabled()
            || ! hash_equals((string) config('sygnet.console_password'), (string) $request->input('password'))) {
            return back()->withErrors(['password' => 'Złe hasło.']);
        }

        $request->session()->regenerate();
        $request->session()->put(ConsolePassword::SESSION_KEY, ConsolePassword::token());

        return redirect()->intended(route('console'));
    }

    public function logout(Request $request): RedirectResponse
    {
        $request->session()->invalidate();
        $request->session()->regenerateToken();

        return redirect()->route('login');
    }
}
