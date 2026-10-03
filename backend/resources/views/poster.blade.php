{{-- Plakat A4 z kodem QR komunikatu. Drukuj z przeglądarki (Ctrl+P, A4, bez marginesów) albo zapisz jako PDF. --}}
@php
    $time = fn (int $unix) => \Illuminate\Support\Carbon::createFromTimestamp($unix, 'Europe/Warsaw')->format('d.m.Y, H:i');
    $days = intdiv($b['valid_minutes'], 1440);
@endphp
<!DOCTYPE html>
<html lang="pl" class="bg-white">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Plakat · {{ $b['type_name'] }} · {{ $b['area_name'] }} · SYGNET</title>
    <link rel="icon" type="image/png" href="/favicon.png">
    @vite(['resources/css/app.css', 'resources/js/poster.js'])
    <style>
        @page { size: A4 portrait; margin: 0; }
        html, body { background: #fff !important; color: #0A0C0F; }
        .sheet { width: 210mm; height: 297mm; padding: 14mm 16mm 12mm; box-sizing: border-box; display: flex; flex-direction: column; }
        @media screen { body { padding: 24px 0; background: #2A313B !important; } .sheet { margin: 0 auto; background: #fff; box-shadow: 0 10px 40px #0008; } }
        @media print { .no-print { display: none !important; } body { padding: 0; } }
        #poster-qr svg { width: 100%; height: 100%; display: block; }
    </style>
</head>
<body>
<div class="no-print mx-auto mb-4 flex w-[210mm] items-center justify-between text-sm text-white">
    <span>Podgląd plakatu A4 · drukuj bez marginesów („Marginesy: brak”), skala 100%</span>
    <button type="button" onclick="window.print()" class="rounded-lg bg-white px-4 py-2 font-bold text-black">Drukuj / zapisz PDF</button>
</div>

<main class="sheet">
    {{-- nagłówek --}}
    <div class="flex items-center justify-between border-b-2 border-black pb-4">
        <div class="flex items-center gap-3">
            <img src="{{ Vite::asset('resources/images/sygnet_mark_black.png') }}" alt="" style="height: 13mm">
            <span class="text-[22pt] font-extrabold tracking-[0.3em]">SYGNET</span>
        </div>
        <div class="text-right text-[9.5pt] leading-tight font-semibold tracking-[0.12em] uppercase">
            Komunikat<br>podpisany cyfrowo
        </div>
    </div>

    {{-- treść --}}
    <div class="mt-6">
        <div class="text-[11pt] font-bold tracking-[0.16em] text-[#5B6570] uppercase">{{ $b['issuer_name'] }} · {{ $b['area_name'] }}</div>
        <h1 class="mt-1 text-[34pt] leading-[1.05] font-extrabold tracking-tight">{{ $b['type_name'] }}</h1>
        @if($b['note'] !== '' && $b['type'] !== 250)
            <p class="mt-3 text-[19pt] leading-snug font-semibold">„{{ $b['note'] }}”</p>
        @endif
        <div class="mt-4 rounded-[4mm] border-2 border-black px-5 py-3">
            <div class="text-[9pt] font-bold tracking-[0.18em] text-[#5B6570] uppercase">Co robić</div>
            <div class="mt-1 text-[14pt] leading-snug font-semibold">{{ $b['instruction'] }}</div>
        </div>
    </div>

    {{-- kod QR --}}
    <div class="mt-6 flex flex-1 items-center gap-8">
        <div id="poster-qr" data-qr="{{ $b['qr_text'] }}" style="width: 104mm; height: 104mm; margin-left: -6mm" class="shrink-0"></div>
        <div class="flex flex-col gap-4">
            <div class="text-[20pt] leading-tight font-extrabold">Sprawdź, czy to prawda</div>
            <div class="text-[12.5pt] leading-snug">
                Zeskanuj kod w aplikacji <b>SYGNET</b>. Telefon sprawdzi podpis cyfrowy
                <b>bez internetu</b> – kluczem wbudowanym w aplikację.
            </div>
            <div class="flex flex-col gap-2 text-[12pt] leading-snug">
                <div class="flex items-start gap-2"><span class="mt-[1.2mm] inline-block size-[4mm] shrink-0 rounded-full bg-[#0E7A3E]"></span><span><b>ZWERYFIKOWANO</b> – komunikat prawdziwy, postępuj według instrukcji.</span></div>
                <div class="flex items-start gap-2"><span class="mt-[1.2mm] inline-block size-[4mm] shrink-0 rounded-full bg-[#B42318]"></span><span><b>FAŁSZYWKA</b> lub inny wynik – <b>nie wykonuj</b> poleceń.</span></div>
            </div>
        </div>
    </div>

    {{-- szczegóły podpisu --}}
    <div class="mt-6 grid grid-cols-2 gap-x-8 gap-y-1.5 border-t-2 border-black pt-4 text-[9.5pt]">
        <div><span class="text-[#5B6570]">Wydano:</span> <b>{{ $time($b['timestamp']) }}</b></div>
        <div><span class="text-[#5B6570]">Ważny do:</span> <b>{{ $time($b['valid_until']) }}</b>@if($days >= 1) ({{ $days }} {{ $days === 1 ? 'doba' : 'dni' }})@endif</div>
        <div><span class="text-[#5B6570]">Podpisali:</span> <b>{{ implode(' + ', $signerNames) }}</b></div>
        <div><span class="text-[#5B6570]">Nr komunikatu:</span> <b class="mono">{{ $b['issuer_id'] }}/{{ $b['sequence'] }}</b> · <span class="mono">{{ $b['bytes'] }} B</span></div>
        @if($issuerFingerprint)
            <div><span class="text-[#5B6570]">Klucz nadawcy:</span> <b class="mono">{{ $issuerFingerprint }}</b></div>
        @endif
        @if($rootFingerprint)
            <div><span class="text-[#5B6570]">Klucz główny ROOT:</span> <b class="mono">{{ $rootFingerprint }}</b></div>
        @endif
    </div>
    <div class="mt-3 text-[8pt] text-[#5B6570]">
        Ed25519 · {{ \App\Sygnet\FrameBuilder::QR_PREFIX }}… · Odcisk ROOT porównaj z ekranem „Klucze i nadawcy” w aplikacji.
        Kod nie zawiera linku – aplikacja SYGNET nigdy nie otwiera adresów z kodów QR.
    </div>
</main>
</body>
</html>
