{{-- Duży kod QR (SYG1:base64url, korekcja M) do zeskanowania z ekranu/projektora. --}}
<div x-data x-cloak x-show="$store.qr.open" x-transition.opacity
     class="fixed inset-0 z-50 grid place-items-center bg-black/80 p-8" @keydown.escape.window="$store.qr.close()" @click.self="$store.qr.close()">
    <div class="flex max-w-[720px] flex-col items-center gap-5 rounded-3xl bg-panel p-8 shadow-2xl">
        <div class="flex w-full items-center justify-between">
            <div>
                <div class="panel-title">Zeskanuj w aplikacji SYGNET</div>
                <div class="mt-1 text-xl font-bold" x-text="$store.qr.b ? `${$store.qr.b.type_name} · ${$store.qr.b.area_name}` : ''"></div>
            </div>
            <div class="flex gap-2">
                <a class="btn btn-ghost !px-3 !py-2" :href="$store.qr.b ? `/poster/${$store.qr.b.id}` : '#'" target="_blank">Plakat do druku</a>
                <button type="button" class="btn btn-ghost !px-3 !py-2" @click="$store.qr.close()">Zamknij</button>
            </div>
        </div>
        <div class="rounded-2xl bg-white p-3"><canvas id="qr-canvas" class="block size-[600px]"></canvas></div>
        <div class="mono w-full truncate text-xs text-muted" x-text="$store.qr.b?.qr_text"></div>
    </div>
</div>
