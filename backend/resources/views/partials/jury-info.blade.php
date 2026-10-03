{{-- Podpowiedź dla oceniających (SYGNET_JURY_INFO). $items = [etykieta => wartość]. W wersji docelowej jej nie ma. --}}
@if(config('sygnet.jury_info'))
    <div class="rounded-xl border border-dashed border-expired/60 bg-expired/10 px-4 py-3 text-left">
        <div class="text-[10px] font-bold tracking-[0.18em] text-expired uppercase">Dla oceniających · HackYeah 2026</div>
        @foreach($items as $label => $value)
            <div class="mt-1.5 flex flex-wrap items-baseline gap-x-2 text-sm">
                <span class="text-muted">{{ $label }}:</span>
                <span class="mono font-bold text-ink select-all">{{ $value }}</span>
            </div>
        @endforeach
        <div class="mt-1.5 text-xs text-muted">Podpowiedź tylko na czas oceny – w wersji docelowej jej nie ma.</div>
    </div>
@endif
