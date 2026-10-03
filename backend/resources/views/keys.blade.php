<x-layout :boot="$boot" title="Klucze" subtitle="Klucze i łańcuch zaufania">
<div x-data="keysPage" class="grid grid-cols-12 gap-4 tall:h-[calc(100vh-104px)]">

    <div class="col-span-12 flex min-h-0 flex-col gap-4 xl:col-span-8">
        {{-- ROOT --}}
        <section class="panel flex items-center gap-8 p-6">
            <div class="grid size-20 shrink-0 place-items-center rounded-2xl bg-panel2" x-html="sygnet.icon('key', 'size-10')"></div>
            <div class="min-w-0 flex-1">
                <div class="panel-title">Odcisk klucza głównego ROOT – wbudowany w aplikację</div>
                <div class="mono mt-2 text-5xl font-bold tracking-wider">{{ $boot['rootFingerprint'] ?? '––––-––––-––––-––––' }}</div>
                <div class="mt-2 text-sm text-muted">
                    Obywatel porównuje go z wydrukiem przy pierwszym uruchomieniu. ROOT podpisuje tylko certyfikaty wydawców i unieważnienia –
                    produkcyjnie trzymany offline w HSM.
                    @if($boot['testKeys'])<b class="text-expired">Teraz: klucze TESTOWE z PROTOCOL.md §9.</b>@endif
                </div>
            </div>
        </section>

        {{-- wydawcy --}}
        <section class="panel overflow-auto p-6 tall:min-h-0 tall:flex-1">
            <div class="panel-title">Wydawcy w trust store</div>
            <table class="mt-4 w-full text-left text-sm">
                <thead class="text-[11px] tracking-[0.14em] text-muted uppercase">
                    <tr class="border-b border-line">
                        <th class="py-2 pr-3 font-semibold">ID</th>
                        <th class="py-2 pr-3 font-semibold">Wydawca</th>
                        <th class="py-2 pr-3 font-semibold">Zakres</th>
                        <th class="py-2 pr-3 font-semibold">Odcisk klucza</th>
                        <th class="py-2 pr-3 font-semibold">Certyfikat</th>
                        <th class="py-2 pr-3 font-semibold">Status</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <template x-for="r in boot.issuerRows" :key="r.id">
                        <tr class="border-b border-line/60">
                            <td class="mono py-3 pr-3 text-muted" x-text="r.id"></td>
                            <td class="py-3 pr-3 font-semibold" x-text="r.name"></td>
                            <td class="py-3 pr-3 text-muted" x-text="r.scopes.join(', ')"></td>
                            <td class="mono py-3 pr-3" x-text="r.fingerprint ?? '–'"></td>
                            <td class="mono py-3 pr-3 text-xs text-muted"
                                x-text="r.in_trust_store ? `${new Date(r.cert_from * 1000).toLocaleDateString('pl-PL')} – ${new Date(r.cert_until * 1000).toLocaleDateString('pl-PL')}` : 'brak'"></td>
                            <td class="py-3 pr-3">
                                <span class="chip" :class="r.revoked ? '!border-danger/50 !text-alarm' : r.in_trust_store ? '!border-go/40 !text-go' : ''"
                                      x-text="r.revoked ? 'unieważniony' : r.in_trust_store ? 'aktywny' : 'brak klucza'"></span>
                            </td>
                            <td class="py-3 text-right">
                                <button type="button" class="btn btn-ghost !px-3 !py-1.5 !text-xs" x-show="r.in_trust_store && !r.revoked" @click="confirm = r">
                                    Unieważnij
                                </button>
                            </td>
                        </tr>
                    </template>
                </tbody>
            </table>
            <div class="mt-4 flex items-center gap-2 text-sm text-muted">
                <span x-html="sygnet.icon('spy', 'size-4')"></span>
                Klucz HAKERA (tylko laboratorium, poza trust store):
                <span class="mono text-ink">{{ $boot['hackerFingerprint'] ?? '–' }}</span>
            </div>
            @foreach($boot['rejected'] as $why)
                <div class="mt-2 text-sm text-alarm">Odrzucony certyfikat: {{ $why }}</div>
            @endforeach
        </section>

        <x-transmit>
            <button type="button" class="btn btn-danger h-full !px-6" :disabled="!result || $store.tx.playing" @click="act('play')">
                <span x-html="sygnet.icon('play', 'size-5')"></span> NADAJ UNIEWAŻNIENIE
            </button>
            <div class="flex flex-col gap-2">
                <button type="button" class="btn btn-ghost !py-2" :disabled="!result" @click="act('qr')"><span x-html="sygnet.icon('qr', 'size-4')"></span> QR</button>
                <button type="button" class="btn btn-ghost !py-2" :disabled="!result" @click="act('wav')"><span x-html="sygnet.icon('download', 'size-4')"></span> WAV</button>
            </div>
        </x-transmit>
    </div>

    <div class="col-span-12 flex min-h-0 flex-col gap-4 xl:col-span-4">
        {{-- eksport --}}
        <section class="panel p-6">
            <div class="panel-title">Eksport dla aplikacji (tylko klucze publiczne)</div>
            <div class="mt-4 grid gap-2">
                <a class="btn btn-ghost justify-start {{ $boot['exports']['trust_store'] ? '' : 'pointer-events-none opacity-40' }}"
                   href="{{ route('keys.export', 'sygnet_trust_store.json') }}">
                    <span x-html="sygnet.icon('download', 'size-4')"></span> sygnet_trust_store.json
                </a>
                <a class="btn btn-ghost justify-start {{ $boot['exports']['root_key'] ? '' : 'pointer-events-none opacity-40' }}"
                   href="{{ route('keys.export', 'RootKey.cs') }}">
                    <span x-html="sygnet.icon('download', 'size-4')"></span> RootKey.cs
                </a>
            </div>
            <ol class="mt-4 list-decimal space-y-1 pl-5 text-sm text-muted">
                <li><span class="mono text-ink">sygnet_trust_store.json</span> → <span class="mono">Assets/Resources/</span></li>
                <li><span class="mono text-ink">RootKey.cs</span> → <span class="mono">Assets/Sygnet/App/</span></li>
                <li>Zbuduj APK – nowy ROOT widać w onboardingu i na stronie „Klucze i nadawcy”.</li>
            </ol>
            <div class="mt-3 text-xs text-dim">Nowe klucze: <span class="mono">php artisan sygnet:init --force</span></div>
        </section>

        <section class="panel flex min-h-0 flex-1 flex-col items-center gap-3 overflow-hidden p-5">
            <div class="flex w-full items-center justify-between">
                <h2 class="panel-title">Unieważnienie na telefonie</h2>
                <span class="text-xs text-alarm" x-text="error"></span>
            </div>
            <div class="origin-top scale-[0.82]">
                @include('partials.phone', ['idle' => 'KEY_REVOKE od ROOT: telefon zapisuje unieważnienie na stałe i odrzuca kolejne podpisy tym kluczem.'])
            </div>
        </section>
    </div>

    {{-- potwierdzenie --}}
    <div x-cloak x-show="confirm" x-transition.opacity class="fixed inset-0 z-40 grid place-items-center bg-black/75" @keydown.escape.window="confirm = null">
        <div class="panel w-[560px] p-7">
            <div class="text-lg font-bold">Unieważnić klucz?</div>
            <p class="mt-3 text-sm text-muted">
                ROOT podpisze komunikat <span class="mono text-ink">KEY_REVOKE</span> dla
                <b class="text-ink" x-text="confirm?.name"></b>. Telefon, który go odbierze, na stałe przestanie ufać temu kluczowi
                (np. po kradzieży). Tego nie da się cofnąć w aplikacji – tylko nowym kluczem i certyfikatem.
            </p>
            <div class="mt-6 flex justify-end gap-3">
                <button type="button" class="btn btn-ghost" @click="confirm = null">Anuluj</button>
                <button type="button" class="btn btn-danger" :disabled="busy" @click="revoke()">Podpisz unieważnienie</button>
            </div>
        </div>
    </div>
</div>
</x-layout>
