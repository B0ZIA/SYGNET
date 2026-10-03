@props(['boot', 'title' => 'Konsola', 'subtitle' => 'Konsola nadawcza', 'theme' => ''])
<!DOCTYPE html>
<html lang="pl">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="csrf-token" content="{{ csrf_token() }}">
    <title>{{ $title ?? 'Konsola' }} · SYGNET</title>
    <link rel="icon" type="image/png" href="/favicon.png">
    @vite(['resources/css/app.css', 'resources/js/app.js'])
</head>
<body class="min-h-screen {{ $theme ?? '' }}">
<script type="application/json" id="boot">@json($boot)</script>

<div class="flex min-h-screen flex-col">
    <header class="flex min-h-16 shrink-0 flex-wrap items-center gap-x-6 gap-y-2 border-b border-line px-4 py-2.5 sm:px-6 {{ ($theme ?? '') === 'attack-theme' ? 'bg-danger/15' : 'bg-panel/60' }}">
        <a href="{{ route('console') }}" class="flex shrink-0 items-center gap-3">
            <img src="{{ Vite::asset('resources/images/sygnet_mark_white.png') }}" alt="" class="h-8 w-auto">
            <span class="text-[17px] font-extrabold tracking-[0.32em]">SYGNET</span>
            <span class="hidden text-[11px] font-semibold uppercase tracking-[0.2em] text-muted xl:inline">· {{ $subtitle ?? 'Konsola nadawcza' }}</span>
        </a>

        <nav class="flex items-center gap-1 whitespace-nowrap lg:ml-4">
            <a href="{{ route('console') }}" class="nav-link" @if(request()->routeIs('console')) aria-current="page" @endif>Konsola</a>
            <a href="{{ route('attack') }}" class="nav-link" @if(request()->routeIs('attack')) aria-current="page" @endif>Laboratorium ataków</a>
            <a href="{{ route('keys') }}" class="nav-link" @if(request()->routeIs('keys')) aria-current="page" @endif>Klucze</a>
        </nav>

        <div class="ml-auto flex flex-wrap items-center justify-end gap-2">
            {{ $headerRight ?? '' }}
            <span class="chip mono hidden md:inline-flex" title="Odcisk klucza głównego – ten sam widzi obywatel w aplikacji">
                <span x-data x-html="sygnet.icon('key', 'size-3.5')"></span>
                ROOT {{ $boot['rootFingerprint'] ?? 'brak – php artisan sygnet:init' }}
                @if($boot['testKeys'] ?? false)<span class="text-expired">· test</span>@endif
            </span>
            <span class="chip border-go/40 text-go" title="Konsola działa bez internetu i CDN">
                <span class="size-2 rounded-full bg-go blink"></span> OFFLINE
            </span>
            @if(\App\Http\Middleware\ConsolePassword::enabled())
                <form method="POST" action="{{ route('logout') }}">
                    @csrf
                    <button type="submit" class="chip hover:text-ink">Wyloguj</button>
                </form>
            @endif
        </div>
    </header>

    @if(! empty($boot['keyProblems']))
        <div class="border-b border-danger/50 bg-danger/20 px-6 py-3 text-sm">
            <div class="font-bold text-alarm">Klucze konsoli i trust store nie pasują do siebie – telefony mogą odrzucać komunikaty.</div>
            @foreach($boot['keyProblems'] as $problem)
                <div class="mt-1 text-ink">{{ $problem }}</div>
            @endforeach
            <div class="mt-1 text-muted">Diagnostyka na serwerze: <span class="mono">php artisan sygnet:check</span></div>
        </div>
    @endif

    <main class="flex-1 p-3 sm:p-5">
        {{ $slot }}
    </main>
</div>

@include('partials.qr-modal')
</body>
</html>
