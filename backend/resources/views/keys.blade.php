<x-layout :boot="$boot" title="Klucze" subtitle="Klucze i łańcuch zaufania">
<div x-data="keysPage" class="mx-auto grid max-w-[1720px] grid-cols-12 gap-6 tall:h-[calc(100vh-104px)]">

    {{-- ───────── komu ufa telefon ───────── --}}
    <section class="scroll-thin col-span-12 flex min-h-0 flex-col gap-5 xl:col-span-7 tall:overflow-y-auto tall:pr-1">
        <div class="max-w-[720px]">
            <h1 class="text-[34px] leading-tight font-extrabold">Komu ufa telefon</h1>
            <p class="mt-2 text-[15px] leading-snug text-muted">
                Telefon ma wbudowany tylko jeden klucz – główny. Nim są podpisane certyfikaty urzędów. Każdy urząd może
                nadawać wyłącznie na swoim terenie, a ewakuację muszą podpisać dwa urzędy.
            </p>
        </div>

        <div class="panel flex items-center gap-6 p-6">
            <div class="grid size-16 shrink-0 place-items-center rounded-2xl bg-panel2" x-html="sygnet.icon('key', 'size-8')"></div>
            <div class="min-w-0">
                <div class="panel-title">Klucz główny (ROOT) – wbudowany w aplikację</div>
                <div class="mono mt-1 text-[clamp(28px,3.2vw,44px)] leading-tight font-bold tracking-wider">{{ $boot['rootFingerprint'] ?? '––––-––––-––––-––––' }}</div>
                <div class="mt-1 text-sm text-muted">
                    Trzymany offline. Obywatel porównuje ten odcisk z ekranem „Klucze i nadawcy” w aplikacji.
                    @if($boot['testKeys'])<b class="text-expired">Teraz klucze TESTOWE.</b>@endif
                </div>
            </div>
        </div>

        <div class="flex flex-col gap-2.5">
            <div class="flex items-baseline justify-between px-1">
                <span class="panel-title">Urzędy z certyfikatem</span>
                <span class="text-xs text-muted" x-text="`${boot.issuerRows.filter(r => r.in_trust_store).length} w aplikacji`"></span>
            </div>
            <template x-for="r in boot.issuerRows" :key="r.id">
                <div class="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-2xl border border-line bg-panel px-5 py-3.5">
                    <div class="min-w-[200px] flex-1">
                        <div class="text-[16px] font-bold" x-text="r.name"></div>
                        <div class="mono text-xs text-muted" x-text="r.fingerprint ? 'klucz ' + r.fingerprint : 'brak klucza w konsoli'"></div>
                    </div>
                    <span class="chip"><span x-html="sygnet.icon('pin', 'size-3.5')"></span><span x-text="r.scopes.join(', ')"></span></span>
                    <span class="chip w-28 justify-center" :class="r.revoked ? '!border-danger/50 !text-alarm' : r.in_trust_store ? '!border-go/40 !text-go' : ''"
                          x-text="r.revoked ? 'unieważniony' : r.in_trust_store ? 'aktywny' : 'brak'"></span>
                </div>
            </template>
            @foreach($boot['rejected'] as $why)
                <div class="text-sm text-alarm">Odrzucony certyfikat: {{ $why }}</div>
            @endforeach
        </div>
    </section>

    {{-- ───────── jak to działa ───────── --}}
    <section class="col-span-12 flex min-h-0 flex-col gap-5 xl:col-span-5">
        <div class="panel p-6">
            <div class="panel-title">Jak telefon sprawdza komunikat</div>
            <ol class="mt-4 space-y-3">
                <li class="flex gap-3">
                    <span class="mono grid size-8 shrink-0 place-items-center rounded-full bg-go/20 text-sm font-bold text-[#3FD068]">1</span>
                    <span class="text-[15px] leading-snug"><b>Certyfikat urzędu</b> jest podpisany kluczem głównym – nikt nie dopisze sobie urzędu.</span>
                </li>
                <li class="flex gap-3">
                    <span class="mono grid size-8 shrink-0 place-items-center rounded-full bg-go/20 text-sm font-bold text-[#3FD068]">2</span>
                    <span class="text-[15px] leading-snug"><b>Teren komunikatu</b> mieści się w zakresie urzędu – Warszawa nie ogłosi alarmu w Krakowie.</span>
                </li>
                <li class="flex gap-3">
                    <span class="mono grid size-8 shrink-0 place-items-center rounded-full bg-go/20 text-sm font-bold text-[#3FD068]">3</span>
                    <span class="text-[15px] leading-snug"><b>Podpis komunikatu</b> pasuje do klucza urzędu – zmiana jednej litery go psuje.</span>
                </li>
            </ol>
            <div class="mt-4 rounded-xl bg-bg px-4 py-3 text-sm text-muted">Wszystko w telefonie, offline – bez internetu i serwerów.</div>
        </div>

        <div class="panel p-6">
            <div class="panel-title">Skradziony klucz?</div>
            <p class="mt-3 text-[15px] leading-snug">
                Klucz główny unieważnia klucz urzędu specjalnym komunikatem. Telefon, który go odbierze, na stałe przestaje
                ufać temu kluczowi.
            </p>
            <a href="{{ route('attack') }}" class="btn btn-ghost mt-4 !px-4 !py-2.5 !text-sm">Zobacz w laboratorium ataków (A7) →</a>
        </div>

        {{-- dla operatora – prawdziwe operacje, schowane --}}
        <details class="panel group overflow-hidden">
            <summary class="flex cursor-pointer list-none items-center justify-between gap-3 px-6 py-4">
                <span class="panel-title">Dla operatora: unieważnienie i pliki dla aplikacji</span>
                <span class="text-xs text-muted">rozwiń</span>
            </summary>
            <div class="space-y-5 px-6 pb-6">
                <div>
                    <div class="text-sm font-semibold">Unieważnij klucz urzędu</div>
                    <p class="mt-1 text-xs text-muted">Prawdziwa operacja: telefony, które odbiorą unieważnienie, odrzucą każdy kolejny komunikat tego urzędu.</p>
                    <div class="mt-3 flex flex-wrap gap-2">
                        <select class="input !w-auto !py-2 !text-sm" x-model.number="revokeId">
                            <template x-for="r in boot.issuerRows.filter(r => r.in_trust_store && !r.revoked)" :key="r.id">
                                <option :value="r.id" :selected="r.id === revokeId" x-text="r.name"></option>
                            </template>
                        </select>
                        <button type="button" class="btn btn-danger !px-4 !py-2 !text-sm" :disabled="!revokeId || busy"
                                @click="confirm = boot.issuerRows.find(r => r.id === revokeId)">Podpisz unieważnienie</button>
                    </div>
                    <div class="mt-2 text-sm text-alarm" x-show="error" x-text="error"></div>
                    <template x-if="result">
                        <div class="mt-3 flex flex-wrap items-center gap-2 rounded-xl bg-bg px-4 py-3">
                            <span class="text-sm font-semibold text-[#3FD068]" x-text="`✓ Unieważnienie podpisane (${result.bytes} B)`"></span>
                            <button type="button" class="btn btn-ghost !px-3 !py-1.5 !text-xs" :disabled="$store.tx.playing" @click="act('play')"
                                    x-text="$store.tx.playing ? `Nadaję… ${Math.round($store.tx.progress * 100)}%` : 'Nadaj dźwiękiem'"></button>
                            <button type="button" class="btn btn-ghost !px-3 !py-1.5 !text-xs" @click="act('qr')">Kod QR</button>
                        </div>
                    </template>
                </div>

                <div>
                    <div class="text-sm font-semibold">Pliki dla aplikacji (tylko klucze publiczne)</div>
                    <div class="mt-2 flex flex-wrap gap-2">
                        <a class="btn btn-ghost !px-3 !py-2 !text-sm {{ $boot['exports']['trust_store'] ? '' : 'pointer-events-none opacity-40' }}"
                           href="{{ route('keys.export', 'sygnet_trust_store.json') }}"><span x-html="sygnet.icon('download', 'size-4')"></span> sygnet_trust_store.json</a>
                        <a class="btn btn-ghost !px-3 !py-2 !text-sm {{ $boot['exports']['root_key'] ? '' : 'pointer-events-none opacity-40' }}"
                           href="{{ route('keys.export', 'RootKey.cs') }}"><span x-html="sygnet.icon('download', 'size-4')"></span> RootKey.cs</a>
                    </div>
                    <p class="mt-2 text-xs text-muted">Do Unity: Assets/Resources/ i Assets/Sygnet/App/, potem nowe APK.</p>
                </div>

                <div class="text-xs text-muted">
                    Klucz „hakera” do laboratorium (poza listą zaufanych): <span class="mono text-ink">{{ $boot['hackerFingerprint'] ?? '–' }}</span>
                </div>
            </div>
        </details>
    </section>

    {{-- potwierdzenie --}}
    <div x-cloak x-show="confirm" x-transition.opacity class="fixed inset-0 z-40 grid place-items-center bg-black/75 p-4" @keydown.escape.window="confirm = null">
        <div class="panel w-[560px] max-w-full p-7">
            <div class="text-lg font-bold">Unieważnić klucz?</div>
            <p class="mt-3 text-sm text-muted">
                Klucz główny podpisze unieważnienie dla <b class="text-ink" x-text="confirm?.name"></b>. Telefon, który je odbierze,
                na stałe przestanie ufać temu kluczowi, a konsola nie będzie już nim podpisywać. Nie da się tego cofnąć –
                tylko nowym kluczem i certyfikatem.
            </p>
            <div class="mt-6 flex justify-end gap-3">
                <button type="button" class="btn btn-ghost" @click="confirm = null">Anuluj</button>
                <button type="button" class="btn btn-danger" :disabled="busy" @click="revoke()">Podpisz unieważnienie</button>
            </div>
        </div>
    </div>
</div>
</x-layout>
