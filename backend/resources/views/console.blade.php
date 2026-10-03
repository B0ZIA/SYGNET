<x-layout :boot="$boot" title="Konsola nadawcza">
<div x-data="consoleApp" class="grid grid-cols-12 gap-4 tall:h-[calc(100vh-104px)]">

    {{-- ───────── lewa kolumna ───────── --}}
    <div class="col-span-12 flex min-h-0 flex-col gap-4 xl:col-span-8">

        {{-- formularz --}}
        <section class="panel p-6">
            <div class="flex flex-wrap items-center justify-between gap-2">
                <h2 class="panel-title">Nowy komunikat</h2>
                <span class="chip mono max-w-full overflow-hidden" x-show="issuer?.fingerprint">
                    <span x-html="sygnet.icon('lock', 'size-3.5')"></span>
                    <span class="truncate" x-text="`podpisuje: ${issuer?.name} · ${issuer?.fingerprint}`"></span>
                </span>
            </div>

            <div class="mt-5 grid grid-cols-1 gap-x-6 gap-y-5 lg:grid-cols-2">
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
                        <span class="field-label">Dopisek</span>
                        <span class="mono text-xs" :class="noteBytes > 60 ? 'text-alarm font-bold' : 'text-muted'" x-text="`${noteBytes}/60 B`"></span>
                    </span>
                    <input class="input" x-model="form.note" maxlength="120" placeholder="np. Schron: piwnice i przejścia podziemne"
                           :class="noteBytes > 60 && '!border-alarm'" @keydown.enter.prevent>
                </label>

                <template x-if="critical">
                    <div class="flex flex-wrap items-center gap-4 rounded-xl border border-expired/40 bg-expired/10 p-4 lg:col-span-2">
                        <span class="text-expired" x-html="sygnet.icon('lock', 'size-6')"></span>
                        <div class="flex-1 text-sm">
                            <div class="font-bold">Komunikat krytyczny – drugi, niezależny podpis</div>
                            <div class="text-muted">Telefon odrzuci ewakuację z jednym podpisem. Drugi operator zatwierdza PIN-em.</div>
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

            <div class="mt-5 flex flex-wrap items-center gap-x-5 gap-y-2 border-t border-line pt-4 text-sm text-muted">
                <span class="mono" x-text="`📦 ${estimateBytes} B`"></span>
                <span class="mono" x-text="`🔊 ~${estimateSeconds.replace('.', ',')} s (${$store.tx.repeat}×)`"></span>
                <span x-show="signed" class="mono text-go" x-text="signed ? `podpisano · seq ${signed.sequence}` : ''"></span>
                <template x-for="(msgs, field) in errors" :key="field">
                    <span class="font-semibold text-alarm" x-text="msgs[0]"></span>
                </template>
            </div>
        </section>

        {{-- nadawanie --}}
        <x-transmit>
            <button type="button" class="btn btn-go h-full !px-7 !text-base" :disabled="!canSign || busy || $store.tx.playing" @click="act('play')">
                <span x-html="sygnet.icon('play', 'size-5')"></span> NADAJ DŹWIĘKIEM
            </button>
            <div class="flex flex-col gap-2">
                <button type="button" class="btn btn-ghost !py-2" :disabled="!canSign || busy" @click="act('qr')">
                    <span x-html="sygnet.icon('qr', 'size-4')"></span> POKAŻ QR
                </button>
                <div class="flex gap-2">
                    <button type="button" class="btn btn-ghost flex-1 !px-3 !py-2" :disabled="!canSign || busy" @click="act('wav')">
                        <span x-html="sygnet.icon('download', 'size-4')"></span> WAV
                    </button>
                    <button type="button" class="btn btn-ghost flex-1 !px-3 !py-2" :disabled="!canSign || busy" @click="act('poster')"
                            title="Plakat A4 z kodem QR do druku">
                        <span x-html="sygnet.icon('print', 'size-4')"></span> PLAKAT
                    </button>
                </div>
            </div>
        </x-transmit>

        <div class="grid min-h-[300px] flex-1 grid-cols-1 gap-4 lg:grid-cols-2 tall:min-h-0">
            @include('partials.hex', ['frame' => 'signed'])

            {{-- historia --}}
            <section class="panel flex max-h-[480px] min-h-0 flex-col p-4 tall:max-h-none">
                <div class="panel-title">Historia</div>
                <div class="scroll-thin mt-3 min-h-0 flex-1 space-y-1.5 overflow-auto pr-1">
                    <template x-for="b in history" :key="b.id">
                        <div class="group flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2 hover:bg-panel2"
                             :class="signed?.id === b.id && 'bg-panel2 ring-1 ring-line'" @click="pick(b)">
                            <span class="mono w-10 text-xs text-dim" x-text="'#' + b.sequence"></span>
                            <span class="mono text-xs text-muted" x-text="sygnet.clock(b.timestamp)"></span>
                            <span class="shrink-0" x-html="sygnet.icon(b.type_icon, 'size-4')"></span>
                            <span class="flex min-w-0 flex-1 flex-col text-sm leading-tight">
                                <span class="truncate font-semibold" x-text="b.type_name"></span>
                                <span class="truncate text-xs text-muted" x-text="b.area_name + (b.signer_ids.length > 1 ? ' · 2 podpisy' : '')"></span>
                            </span>
                            <span class="size-2 shrink-0 rounded-full" :style="`background:${sygnet.dot(b.check?.status)}`" :title="b.check?.status"></span>
                            <span class="flex shrink-0 gap-1 opacity-60 group-hover:opacity-100">
                                <button type="button" class="rounded-lg p-1.5 hover:bg-line" title="Nadaj ponownie" @click.stop="replay(b)" x-html="sygnet.icon('play', 'size-3.5')"></button>
                                <button type="button" class="rounded-lg p-1.5 hover:bg-line" title="QR" @click.stop="$store.qr.show(b)" x-html="sygnet.icon('qr', 'size-3.5')"></button>
                                <button type="button" class="rounded-lg p-1.5 hover:bg-line" title="WAV" @click.stop="$store.tx.wav(b)" x-html="sygnet.icon('download', 'size-3.5')"></button>
                                <a class="rounded-lg p-1.5 hover:bg-line" title="Plakat do druku" :href="`/poster/${b.id}`" target="_blank" @click.stop x-html="sygnet.icon('print', 'size-3.5')"></a>
                            </span>
                        </div>
                    </template>
                    <div x-show="!history.length" class="px-3 text-sm text-dim">Brak nadanych komunikatów.</div>
                </div>
            </section>
        </div>
    </div>

    {{-- ───────── podgląd na telefonie ───────── --}}
    <section class="panel col-span-12 flex min-h-0 flex-col items-center gap-4 p-5 xl:col-span-4">
        <div class="flex w-full flex-wrap items-center justify-between gap-2">
            <h2 class="panel-title">Podgląd na telefonie</h2>
            <label class="flex items-center gap-2 text-xs text-muted">
                <span x-html="sygnet.icon('pin', 'size-4')"></span>
                <select class="input !w-auto !py-1 !text-xs" x-model.number="userArea">
                    <template x-for="a in boot.areas" :key="a.code">
                        <option :value="a.code" :selected="a.code === userArea" x-text="'telefon: ' + a.name"></option>
                    </template>
                </select>
            </label>
        </div>

        @include('partials.phone')

        <div class="w-full rounded-xl bg-bg px-4 py-3 text-xs">
            <template x-if="signed?.check">
                <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span class="size-2 shrink-0 rounded-full" :style="`background:${phone?.color ?? '#5B6570'}`"></span>
                    <span class="text-muted">Kontrolna weryfikacja (jak w aplikacji):</span>
                    <span class="mono font-bold break-all" x-text="`${phone?.status ?? signed.check.status} ${signed.check.reason}`"></span>
                </div>
            </template>
            <template x-if="!signed">
                <div class="text-muted">Podgląd na żywo – komunikat nie jest jeszcze podpisany. Klucz prywatny nie opuszcza serwera.</div>
            </template>
        </div>
    </section>

    {{-- zatwierdzenie drugiego operatora --}}
    <div x-cloak x-show="pinOpen" x-transition.opacity class="fixed inset-0 z-40 grid place-items-center bg-black/75" @keydown.escape.window="pinOpen = false">
        <form class="panel w-[520px] p-7" @submit.prevent="confirmPin()">
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
