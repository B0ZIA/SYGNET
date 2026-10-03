{{-- Ramka w hex, segmenty w kolorach: nagłówek | payload | dopisek | podpisy | CRC. $frame = wyrażenie Alpine z ramką. --}}
<div class="panel flex min-h-[150px] flex-col p-4">
    <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
        <div class="panel-title">Ramka hex</div>
        <div class="flex flex-wrap gap-x-3 gap-y-1 text-[11px] font-semibold">
            <span class="text-seg-head">■ SG + długość</span>
            <span class="text-seg-payload">■ payload</span>
            <span class="text-seg-note">■ dopisek</span>
            <span class="text-seg-sig">■ podpisy Ed25519</span>
            <span class="text-seg-crc">■ CRC</span>
        </div>
    </div>
    <div class="mono scroll-thin mt-3 min-h-0 flex-1 overflow-auto text-[13px] leading-6">
        <template x-if="{{ $frame }}">
            <div>
                <template x-for="(byte, i) in sygnet.segments({{ $frame }}.frame_hex)" :key="i">
                    <span class="inline-block" :class="[sygnet.SEGMENT_CLASS[byte.kind], (typeof hexDiff !== 'undefined' && hexDiff.has(i)) ? 'bg-alarm/40 rounded-sm' : '', i % 2 ? 'mr-2' : '']" x-text="byte.hex"></span>
                </template>
                <div class="mt-2 text-xs text-muted" x-text="`${ {{ $frame }}.bytes } B · tyle mieści się w kodzie QR i w ${ {{ $frame }}.audio_seconds } s dźwięku`"></div>
            </div>
        </template>
        <template x-if="!{{ $frame }}">
            <div class="text-sm text-dim">{{ $empty ?? 'Podpisz komunikat, żeby zobaczyć bajty.' }}</div>
        </template>
    </div>
</div>
