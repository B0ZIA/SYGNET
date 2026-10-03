<!DOCTYPE html>
<html lang="pl">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Logowanie · SYGNET</title>
    <link rel="icon" type="image/png" href="/favicon.png">
    @vite(['resources/css/app.css'])
</head>
<body class="grid min-h-screen place-items-center p-4">
    <form method="POST" action="{{ url('/login') }}" class="panel w-full max-w-[420px] p-8">
        @csrf
        <div class="flex items-center gap-3">
            <img src="{{ Vite::asset('resources/images/sygnet_mark_white.png') }}" alt="" class="h-9 w-auto">
            <span class="text-lg font-extrabold tracking-[0.32em]">SYGNET</span>
        </div>
        <div class="mt-6 text-xl font-bold">Konsola nadawcza</div>
        <p class="mt-1 text-sm text-muted">Konsola podpisuje komunikaty prawdziwymi kluczami – dostęp tylko dla operatorów.</p>

        <label class="mt-6 flex flex-col gap-2">
            <span class="field-label">Hasło operatora</span>
            <input type="password" name="password" class="input" autocomplete="current-password" autofocus required>
        </label>
        @error('password')
            <div class="mt-2 text-sm font-semibold text-alarm">{{ $message }}</div>
        @enderror

        <button type="submit" class="btn btn-go mt-6 w-full">Zaloguj</button>
    </form>
</body>
</html>
