<x-layout :boot="$boot" title="Konsola nadawcza">
<div x-data="consoleApp" class="mx-auto grid max-w-[1720px] grid-cols-12 gap-6 tall:h-[calc(100vh-104px)]">

    {{-- ───────── komunikat ───────── --}}
    <section class="scroll-thin col-span-12 flex min-h-0 flex-col gap-5 xl:col-span-7 tall:overflow-y-auto tall:pr-1">
        <div class="max-w-[720px]">
            <h1 class="text-[34px] leading-tight font-extrabold">Nadaj komunikat</h1>
            <p class="mt-2 text-[15px] leading-snug text-muted">
                Wybierz, kto nadaje, co i gdzie. Serwer podpisuje komunikat kluczem urzędu, a przeglądarka nadaje go
                dźwiękiem albo kodem QR. Po prawej widać, co zobaczy telefon obywatela.
            </p>
        </div>

        <div class="panel p-6">
            <div class="grid grid-cols-1 gap-x-6 gap-y-5 lg:grid-cols-2">
                <label class="flex flex-col gap-2">
                    <span class="field-label">Nadawca</span>
                    <select class="input" x-model.number="form.issuer_id">
                        <template x-for="i in boot.issuers" :key="i.id">
                            <option :value="i.id" :disabled="!i.has_key || i.revoked" :selected="i.id === form.issuer_id"
                                    x-text="i.name + (i.revoked ? ' (klucz unieważniony)' : !i.has_key ? ' (brak klucza)' : '')"></option>
                        </template>
                    </select>
                </label>

                <label class="flex flex-col gap-2">
                    <span class="field-label">Typ</span>
                    <select class="input" x-model.number="form.type">
                        <template x-for="t in boot.types" :key="t.id">
                            <option :value="t.id" :selected="t.id === form.type" x-text="t.name + (t.critical ? '  · wymaga 2 podpisów' : '')"></option>
                        </template>
                    </select>
                </label>

                <div class="flex flex-col gap-2">
                    <span class="field-label">Obszar <span class="normal-case tracking-normal text-dim">– tylko w zakresie nadawcy</span></span>
                    <div class="seg">
                        <template x-for="a in allowedAreas" :key="a.code">
                            <button type="button" :aria-pressed="form.area_code === a.code" @click="form.area_code = a.code" x-text="a.name"></button>
                        </template>
                    </div>
                </div>

                <div class="flex flex-col gap-2">
                    <span class="field-label">Ważność</span>
                    <div class="seg">
                        <template x-for="m in boot.validity" :key="m">
                            <button type="button" :aria-pressed="form.valid_minutes === m" @click="form.valid_minutes = m"
                                    x-text="m < 60 ? m + ' min' : m <= 1440 ? (m / 60) + ' h' : (m / 1440) + ' dni'"></button>
                        </template>
                    </div>
                </div>

                <label class="flex flex-col gap-2 lg:col-span-2">
                    <span class="flex items-baseline justify-between">
                        <span class="field-label">Dopisek <span class="normal-case tracking-normal text-dim">– opcjonalnie</span></span>
                        <span class="mono text-xs" :class="noteBytes > 60 ? 'text-alarm font-bold' : 'text-muted'" x-text="`${noteBytes}/60 B`"></span>
                    </span>
                    <input class="input" x-model="form.note" maxlength="120" placeholder="np. Schron: piwnice i przejścia podziemne"
                           :class="noteBytes > 60 && '!border-alarm'" @keydown.enter.prevent>
                </label>

                <template x-if="critical">
                    <div class="flex flex-wrap items-center gap-4 rounded-xl border border-expired/40 bg-expired/10 p-4 lg:col-span-2">
                        <span class="text-expired" x-html="sygnet.icon('lock', 'size-6')"></span>
                        <div class="min-w-[220px] flex-1 text-sm">
                            <div class="font-bold">Ewakuacja wymaga drugiego podpisu</div>
                            <div class="text-muted">Drugi urząd podpisuje własnym kluczem, operator zatwierdza PIN-em.</div>
                        </div>
                        <select class="input !w-80" x-model.number="form.second_signer_id">
                            <template x-for="i in secondCandidates" :key="i.id">
                                <option :value="i.id" :selected="i.id === form.second_signer_id" x-text="i.name"></option>
                            </template>
                            <template x-if="!secondCandidates.length"><option value="">brak drugiego klucza dla obszaru</option></template>
                        </select>
                    </div>
                </template>
            </div>

            {{-- akcje --}}
            <div class="mt-6 flex flex-wrap items-center gap-3 border-t border-line pt-5">
                <button type="button" class="btn btn-go !px-7 !py-3.5 !text-base" :disabled="!canSign || busy || $store.tx.playing" @click="act('play')">
                    <span x-html="sygnet.icon('play', 'size-5')"></span>
                    <span x-text="$store.tx.playing ? `Nadaję… ${Math.round($store.tx.progress * 100)}%` : signed ? 'Nadaj jeszcze raz' : 'Podpisz i nadaj dźwiękiem'"></span>
                </button>
                <button type="button" class="btn btn-ghost !px-4 !py-3" :disabled="!canSign || busy" @click="act('qr')">
                    <span x-html="sygnet.icon('qr', 'size-4')"></span> Kod QR
                </button>
                <button type="button" class="btn btn-ghost !px-4 !py-3" :disabled="!canSign || busy" @click="act('wav')" title="Plik dźwiękowy do odtworzenia w radiu">
                    <span x-html="sygnet.icon('download', 'size-4')"></span> WAV
                </button>
                <button type="button" class="btn btn-ghost !px-4 !py-3" :disabled="!canSign || busy" @click="act('poster')" title="Plakat A4 z kodem QR do druku">
                    <span x-html="sygnet.icon('print', 'size-4')"></span> Plakat
                </button>
            </div>

            <div class="mt-4 flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
                <template x-if="signed">
                    <span class="flex flex-wrap items-center gap-x-3 gap-y-2">
                        <span class="font-semibold text-[#3FD068]" x-text="`✓ Podpisano: ${signed.issuer_name}${signed.signer_ids.length > 1 ? ' + drugi urząd' : ''} · nr ${signed.sequence}`"></span>
                        <span class="text-xs text-muted">ponowne nadanie to ten sam komunikat – telefon, który go ma, pokaże „Już w skrzynce”</span>
                        <button type="button" class="btn btn-ghost !px-3 !py-1.5 !text-xs" :disabled="busy || $store.tx.playing" @click="newNumber()">Nowy numer</button>
                    </span>
                </template>
                <template x-if="!signed">
                    <span class="mono text-xs text-muted" x-text="`${estimateBytes} B · dźwięk ok. ${estimateSeconds.replace('.', ',')} s · klucz prywatny nie opuszcza serwera`"></span>
                </template>
                <template x-for="(msgs, field) in errors" :key="field">
                    <span class="font-semibold text-alarm" x-text="msgs[0]"></span>
                </template>
            </div>
        </div>

        {{-- zapis tonów: widać, co gra głośnik --}}
        <div class="flex items-center gap-4 rounded-2xl border border-line bg-panel px-4 py-3">
            <canvas x-data x-init="$store.tx.attach($el)" class="block h-[72px] min-w-0 flex-1 rounded-lg bg-bg"></canvas>
            <div class="w-24 shrink-0 text-right">
                <div class="mono text-2xl font-bold" x-text="Math.round($store.tx.progress * 100) + '%'"></div>
                <div class="text-xs text-muted" x-text="$store.tx.current ? `${$store.tx.current.bytes} B · 2×` : 'gotowe'"></div>
            </div>
        </div>

        {{-- szczegóły dla dociekliwych --}}
        <details class="panel group overflow-hidden">
            <summary class="flex cursor-pointer list-none items-center justify-between gap-3 px-5 py-3.5">
                <span class="panel-title">Co idzie w eter</span>
                <span class="text-xs text-muted" x-text="signed ? `${signed.bytes} bajtów – tyle mieści się w kodzie QR` : 'po podpisaniu'"></span>
            </summary>
            <div class="mono px-5 pb-5 text-[13px] leading-6">
                <template x-if="signed">
                    <div>
                        <template x-for="(byte, i) in sygnet.segments(signed.frame_hex)" :key="i">
                            <span class="inline-block" :class="[sygnet.SEGMENT_CLASS[byte.kind], i % 2 ? 'mr-2' : '']" x-text="byte.hex"></span>
                        </template>
                        <div class="mt-2 flex flex-wrap gap-x-3 gap-y-1 font-sans text-[11px] font-semibold">
                            <span class="text-seg-head">■ SG + długość</span>
                            <span class="text-seg-payload">■ nadawca, typ, obszar, czas</span>
                            <span class="text-seg-note">■ dopisek</span>
                            <span class="text-seg-sig">■ podpis Ed25519</span>
                            <span class="text-seg-crc">■ CRC</span>
                        </div>
                    </div>
                </template>
                <template x-if="!signed"><div class="font-sans text-sm text-dim">Podpisz komunikat, żeby zobaczyć bajty.</div></template>
            </div>
        </details>

        <details class="panel group overflow-hidden">
            <summary class="flex cursor-pointer list-none items-center justify-between gap-3 px-5 py-3.5">
                <span class="panel-title">Historia</span>
                <span class="text-xs text-muted" x-text="`${history.length} nadanych`"></span>
            </summary>
            <div class="scroll-thin max-h-[360px] space-y-1 overflow-auto px-3 pb-3">
                <template x-for="b in history" :key="b.id">
                    <div class="group flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2 hover:bg-panel2"
                         :class="signed?.id === b.id && 'bg-panel2 ring-1 ring-line'" @click="pick(b)">
                        <span class="mono w-12 shrink-0 text-xs text-dim" x-text="'nr ' + b.sequence"></span>
                        <span class="mono shrink-0 text-xs text-muted" x-text="sygnet.clock(b.timestamp)"></span>
                        <span class="shrink-0" x-html="sygnet.icon(b.type_icon, 'size-4')"></span>
                        <span class="flex min-w-0 flex-1 flex-col text-sm leading-tight">
                            <span class="truncate font-semibold" x-text="b.type_name"></span>
                            <span class="truncate text-xs text-muted" x-text="b.area_name + ' · ' + b.issuer_name"></span>
                        </span>
                        <span class="size-2 shrink-0 rounded-full" :style="`background:${sygnet.dot(b.check?.status)}`" :title="b.check?.status"></span>
                        <span class="flex shrink-0 gap-1 opacity-60 group-hover:opacity-100">
                            <button type="button" class="rounded-lg p-1.5 hover:bg-line" title="Nadaj ponownie" @click.stop="replay(b)" x-html="sygnet.icon('play', 'size-3.5')"></button>
                            <button type="button" class="rounded-lg p-1.5 hover:bg-line" title="Kod QR" @click.stop="$store.qr.show(b)" x-html="sygnet.icon('qr', 'size-3.5')"></button>
                            <a class="rounded-lg p-1.5 hover:bg-line" title="Plakat do druku" :href="`/poster/${b.id}`" target="_blank" @click.stop x-html="sygnet.icon('print', 'size-3.5')"></a>
                        </span>
                    </div>
                </template>
                <div x-show="!history.length" class="px-3 py-2 text-sm text-dim">Brak nadanych komunikatów.</div>
            </div>
        </details>
    </section>

    {{-- ───────── telefon ───────── --}}
    <section class="panel col-span-12 flex min-h-0 flex-col items-center gap-4 p-5 xl:col-span-5">
        <div class="flex w-full flex-wrap items-center justify-between gap-2">
            <h2 class="panel-title">Telefon obywatela</h2>
            <label class="flex items-center gap-2 text-xs text-muted" title="Gdzie jest telefon odbiorcy">
                <span x-html="sygnet.icon('pin', 'size-4')"></span>
                <select class="input !w-auto !py-1 !text-xs" x-model.number="userArea">
                    <template x-for="a in boot.areas" :key="a.code">
                        <option :value="a.code" :selected="a.code === userArea" x-text="a.name"></option>
                    </template>
                </select>
            </label>
        </div>

        @include('partials.phone')

        <div class="w-full rounded-xl bg-bg px-4 py-3 text-sm">
            <template x-if="signed?.check">
                <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span class="size-2.5 shrink-0 rounded-full" :style="`background:${phone?.color ?? '#5B6570'}`"></span>
                    <span class="text-muted">Telefon sprawdził podpis offline:</span>
                    <span class="font-bold" x-text="sygnet.title(phone?.status ?? signed.check.status)"></span>
                </div>
            </template>
            <template x-if="!signed">
                <div class="text-muted">Podgląd na żywo – zmienia się razem z formularzem.</div>
            </template>
        </div>
    </section>

    {{-- zatwierdzenie drugiego operatora --}}
    <div x-cloak x-show="pinOpen" x-transition.opacity class="fixed inset-0 z-40 grid place-items-center bg-black/75" @keydown.escape.window="pinOpen = false">
        <form class="panel w-[520px] max-w-[calc(100vw-32px)] p-7" @submit.prevent="confirmPin()">
            <div class="flex items-center gap-3">
                <span class="grid size-11 place-items-center rounded-full bg-expired/20 text-expired" x-html="sygnet.icon('lock', 'size-6')"></span>
                <div>
                    <div class="text-lg font-bold">Zatwierdzenie drugiego operatora</div>
                    <div class="text-sm text-muted" x-text="`${type?.name} · ${boot.areas.find(a => a.code === form.area_code)?.name}`"></div>
                </div>
            </div>
            <p class="mt-5 text-sm text-muted">
                Drugi podpis złoży <b class="text-ink" x-text="boot.issuers.find(i => i.id === form.second_signer_id)?.name"></b>
                własnym kluczem. Zatwierdza PIN-em drugiego operatora – podpis i tak jest naprawdę podwójny (dwa klucze).
            </p>
            <div class="mt-4">
                @include('partials.jury-info', ['items' => ['PIN drugiego operatora' => config('sygnet.second_operator_pin')]])
            </div>
            <input type="password" inputmode="numeric" class="input mono mt-4 text-center !text-2xl tracking-[0.5em]" x-model="pin"
                   placeholder="PIN" x-effect="pinOpen && $nextTick(() => $el.focus())">
            <div class="mt-2 h-5 text-sm font-semibold text-alarm" x-text="pinError"></div>
            <div class="mt-3 flex justify-end gap-3">
                <button type="button" class="btn btn-ghost" @click="pinOpen = false">Anuluj</button>
                <button type="submit" class="btn btn-go" :disabled="!pin">Zatwierdź i podpisz</button>
            </div>
        </form>
    </div>
</div>
</x-layout>
