<x-layout :boot="$boot" title="Laboratorium ataków" subtitle="Laboratorium ataków" theme="attack-theme">
<div x-data="attackLab" class="grid grid-cols-12 gap-4 tall:h-[calc(100vh-104px)]">

    <div class="col-span-12 flex min-h-0 flex-col gap-4 xl:col-span-8">
        {{-- nagłówek trybu --}}
        <div class="stripes flex items-center gap-4 rounded-2xl border border-danger/50 bg-danger/10 px-5 py-3.5">
            <span class="grid size-10 place-items-center rounded-full bg-danger/25 text-alarm" x-html="sygnet.icon('spy', 'size-6')"></span>
            <div>
                <div class="text-[15px] font-extrabold tracking-[0.14em] text-alarm">TRYB ATAKUJĄCEGO · KLUCZE NIEAUTORYZOWANE</div>
                <div class="text-sm text-muted">Każdy atak to prawdziwa ramka – nadaj ją dźwiękiem albo pokaż QR. Telefon z SYGNET ma ją odrzucić albo oznaczyć.</div>
            </div>
        </div>

        {{-- karty ataków --}}
        <div class="grid grid-cols-1 gap-4 md:grid-cols-2 2xl:grid-cols-3 tall:min-h-0 tall:flex-1">
            <template x-for="(a, code) in attacks" :key="code">
                <section class="panel flex min-h-0 flex-col gap-3 p-4" :class="selected?.attack_type === code && '!border-alarm/60 ring-1 ring-alarm/30'">
                    <div class="flex items-start gap-3">
                        <span class="mono rounded-md bg-danger/20 px-2 py-0.5 text-sm font-bold text-alarm" x-text="code"></span>
                        <div class="text-[15px] leading-snug font-bold" x-text="a.title"></div>
                    </div>
                    <p class="text-[13px] leading-snug text-muted" x-text="a.what"></p>

                    {{-- parametry --}}
                    <template x-if="code === 'A1'">
                        <div class="grid grid-cols-2 gap-2">
                            <select class="input !py-1.5 !text-sm" x-model.number="inputs.A1.victim_id" title="Pod kogo się podszywa">
                                <template x-for="i in boot.issuers" :key="i.id">
                                    <option :value="i.id" :selected="i.id === inputs.A1.victim_id" x-text="'jako: ' + i.name"></option>
                                </template>
                            </select>
                            <select class="input !py-1.5 !text-sm" x-model.number="inputs.A1.type">
                                <template x-for="t in boot.types" :key="t.id">
                                    <option :value="t.id" :selected="t.id === inputs.A1.type" x-text="t.name"></option>
                                </template>
                            </select>
                            <input class="input col-span-2 !py-1.5 !text-sm" x-model="inputs.A1.note" maxlength="60">
                        </div>
                    </template>
                    <template x-if="code === 'A2'">
                        <div class="grid gap-2">
                            <select class="input !py-1.5 !text-sm" x-model.number="inputs.A2.broadcast_id">
                                <option :value="null">ostatni prawdziwy komunikat</option>
                                <template x-for="g in genuine" :key="g.id">
                                    <option :value="g.id" x-text="`#${g.sequence} ${g.type_name} · ${g.area_name}`"></option>
                                </template>
                            </select>
                            <input class="input !py-1.5 !text-sm" x-model="inputs.A2.note" maxlength="60" title="Nowy dopisek">
                        </div>
                    </template>
                    <template x-if="code === 'A3'">
                        <select class="input !py-1.5 !text-sm" x-model.number="inputs.A3.broadcast_id">
                            <option :value="null">ostatni wygasły (sprzed 3 dni)</option>
                            <template x-for="g in expired" :key="g.id">
                                <option :value="g.id" x-text="`${sygnet.dateTime(g.timestamp)} ${g.type_name} · ${g.area_name}`"></option>
                            </template>
                        </select>
                    </template>
                    <template x-if="code === 'A7'">
                        <select class="input !py-1.5 !text-sm" x-model.number="inputs.A7.issuer_id">
                            <option :value="null" x-text="revoked.length ? 'pierwszy unieważniony klucz' : 'Prezydent m.st. Warszawy (najpierw unieważnij na /keys)'"></option>
                            <template x-for="i in boot.issuers.filter(i => i.has_key)" :key="i.id">
                                <option :value="i.id" x-text="i.name + (i.revoked ? ' · unieważniony' : '')"></option>
                            </template>
                        </select>
                    </template>

                    <div class="mt-auto space-y-2.5">
                        <div class="flex items-center gap-2 text-xs">
                            <span class="text-muted">Telefon:</span>
                            <span class="mono rounded-md px-1.5 py-0.5 font-bold" :style="`background:${sygnet.dot(a.expect)}26;color:${sygnet.dot(a.expect)}`"
                                  x-text="`${a.expect} ${a.reason}`"></span>
                            <template x-if="results[code]?.check">
                                <span class="mono ml-auto font-bold" :class="results[code].as_expected ? 'text-go' : 'text-expired'"
                                      x-text="results[code].as_expected ? '✓ zgodnie' : results[code].check.status"></span>
                            </template>
                        </div>
                        <div class="flex gap-2">
                            <button type="button" class="btn btn-danger flex-1 !py-2 !text-sm" :disabled="busy || $store.tx.playing" @click="run(code, 'play')">
                                <span x-html="sygnet.icon('play', 'size-4')"></span> Nadaj
                            </button>
                            <button type="button" class="btn btn-ghost !px-3 !py-2" :disabled="busy" title="QR" @click="run(code, 'qr')" x-html="sygnet.icon('qr', 'size-4')"></button>
                            <button type="button" class="btn btn-ghost !px-3 !py-2" :disabled="busy" title="WAV" @click="run(code, 'wav')" x-html="sygnet.icon('download', 'size-4')"></button>
                        </div>
                        <div class="text-xs font-semibold text-alarm" x-show="errors[code]" x-text="errors[code]"></div>
                    </div>
                </section>
            </template>

            <section class="panel flex flex-col justify-center gap-3 p-5 text-sm text-muted">
                <div class="panel-title !text-ink">Dlaczego to nie działa</div>
                <p>Podpis Ed25519 obejmuje każdy bajt treści. Bez klucza wydawcy nie da się go podrobić, a zmiana choćby jednej litery go unieważnia.</p>
                <p>Zakres, ważność i wymóg dwóch podpisów sprawdza telefon <b class="text-ink">offline</b>, kluczem ROOT wbudowanym w aplikację.</p>
            </section>
        </div>

        <x-transmit>
            <div class="flex flex-col gap-1 pr-2">
                <span class="panel-title">Nadawanie</span>
                <span class="max-w-40 text-sm font-semibold" x-text="selected ? `${selected.attack_type}: ${attacks[selected.attack_type]?.title}` : 'wybierz atak'"></span>
            </div>
        </x-transmit>
    </div>

    <div class="col-span-12 flex min-h-0 flex-col gap-4 xl:col-span-4">
        <section class="panel flex flex-col items-center gap-3 p-5">
            <div class="flex w-full items-center justify-between">
                <h2 class="panel-title">Telefon obywatela</h2>
                <span class="chip"><span x-html="sygnet.icon('pin', 'size-3.5')"></span><span x-text="boot.areas.find(a => a.code === boot.userArea)?.name"></span></span>
            </div>
            @include('partials.phone', ['idle' => 'Wybierz atak i nadaj go – zobaczysz, co pokaże telefon.'])
            <div class="w-full rounded-xl bg-bg px-4 py-2.5 text-xs" x-show="selected?.check">
                <span class="text-muted">Kontrolna weryfikacja:</span>
                <span class="mono font-bold" x-text="selected?.check ? `${selected.check.status} ${selected.check.reason}` : ''"></span>
            </div>
        </section>
        @include('partials.hex', ['frame' => 'selected', 'empty' => 'Bajty ataku pojawią się tutaj. W A2 na czerwono bajty zmienione względem oryginału.'])
    </div>
</div>
</x-layout>
