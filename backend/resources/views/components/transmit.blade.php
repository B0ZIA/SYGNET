{{-- Pasek nadawania: zapis tonów ramki, kursor, widmo na żywo, postęp. Przyciski akcji podaje strona. --}}
<div class="panel flex flex-wrap items-stretch gap-4 p-4">
    <div class="flex shrink-0 items-center gap-3">
        {{ $slot }}
    </div>

    <div class="relative min-w-[260px] flex-1">
        <canvas x-data x-init="$store.tx.attach($el)" class="block h-[104px] w-full rounded-xl bg-bg"></canvas>
    </div>

    <div class="flex w-40 shrink-0 flex-col justify-center gap-2">
        <div class="flex items-baseline justify-between">
            <span class="mono text-3xl font-bold" x-text="Math.round($store.tx.progress * 100) + '%'"></span>
            <button type="button" class="text-xs font-semibold text-muted hover:text-ink" x-show="$store.tx.playing" @click="$store.tx.stop()">STOP</button>
        </div>
        <div class="h-1.5 overflow-hidden rounded-full bg-line">
            <div class="h-full rounded-full bg-go transition-[width] duration-100" :style="`width:${$store.tx.progress * 100}%`"></div>
        </div>
        <div class="mono text-xs text-muted">
            <template x-if="$store.tx.current">
                <span x-text="`${$store.tx.current.bytes} B · ${$store.tx.repeat}× · ${$store.tx.duration.toFixed(1)} s`"></span>
            </template>
            <template x-if="!$store.tx.current"><span>brak ramki</span></template>
        </div>
        <div class="seg !p-0.5 text-xs">
            <template x-for="r in [1, 2, 3]" :key="r">
                <button type="button" class="!py-1 !text-xs" :aria-pressed="$store.tx.repeat === r" :disabled="$store.tx.playing"
                        @click="$store.tx.repeat = r; $store.tx.select($store.tx.current)" x-text="r + '×'"></button>
            </template>
        </div>
    </div>
</div>
