<x-layout :boot="$boot" title="Laboratorium ataków" subtitle="Laboratorium ataków" theme="attack-theme">
<div x-data="attackLab" class="mx-auto grid max-w-[1720px] grid-cols-12 gap-6 tall:h-[calc(100vh-104px)]">

    {{-- ───────── ataki ───────── --}}
    <section class="col-span-12 flex min-h-0 flex-col gap-5 xl:col-span-7">
        <div class="flex flex-wrap items-end justify-between gap-4">
            <div class="max-w-[640px]">
                <h1 class="text-[34px] leading-tight font-extrabold">Spróbuj oszukać telefon</h1>
                <p class="mt-2 text-[15px] leading-snug text-muted">
                    Kliknij „Uruchom” przy dowolnym ataku. Konsola nada dźwiękiem prawdziwą ramkę, jaką wysłałby przeciwnik –
                    telefon z aplikacją SYGNET ją usłyszy. Po prawej widać, co pokaże obywatelowi.
                </p>
            </div>
            <div class="text-right" x-show="done" x-cloak>
                <div class="text-[11px] font-semibold tracking-[0.16em] text-muted uppercase">Odparte ataki</div>
                <div class="mono text-3xl font-bold" :class="blocked === done ? 'text-[#3FD068]' : 'text-expired'"
                     x-text="`${blocked} z ${done}`"></div>
            </div>
        </div>

        <div class="scroll-thin flex flex-col gap-2.5 tall:min-h-0 tall:flex-1 tall:overflow-y-auto tall:pr-1">
            <template x-for="code in order" :key="code">
                <div @click="results[code] && pick(code)"
                     class="flex w-full items-center gap-4 rounded-2xl border px-5 py-4 transition"
                     :class="[selected?.attack_type === code ? 'border-alarm/70 bg-panel2' : 'border-line bg-panel',
                              results[code] ? 'cursor-pointer hover:bg-panel2' : '']">
                    <span class="mono w-11 shrink-0 rounded-md bg-danger/20 py-1 text-center text-sm font-bold text-alarm" x-text="code"></span>
                    <span class="min-w-0 flex-1">
                        <span class="block text-[16px] font-bold" x-text="attacks[code].title"></span>
                        <span class="block text-[14px] leading-snug text-muted" x-text="attacks[code].what"></span>
                    </span>
                    <button type="button" class="btn btn-danger w-40 shrink-0 !px-4 !py-2.5 !text-sm" :disabled="busy !== null || $store.tx.playing"
                            @click.stop="launch(code)">
                        <span x-html="sygnet.icon('play', 'size-4')"></span>
                        <span x-text="$store.tx.playing && selected?.attack_type === code ? `Nadaję… ${Math.round($store.tx.progress * 100)}%`
                            : busy === code ? 'Przygotowuję…' : results[code] ? 'Jeszcze raz' : 'Uruchom'"></span>
                    </button>
                </div>
            </template>
            <div class="text-sm font-semibold text-alarm" x-show="error" x-text="error"></div>
        </div>
    </section>

    {{-- ───────── telefon ───────── --}}
    <section class="panel col-span-12 flex min-h-0 flex-col items-center gap-4 p-5 xl:col-span-5">
        <div class="flex w-full items-center justify-between">
            <h2 class="panel-title">Telefon obywatela</h2>
            <span class="chip"><span x-html="sygnet.icon('pin', 'size-3.5')"></span><span x-text="boot.areas.find(a => a.code === boot.userArea)?.name"></span></span>
        </div>

        @include('partials.phone', ['idle' => 'Uruchom dowolny atak z listy.'])

    </section>
</div>
</x-layout>
