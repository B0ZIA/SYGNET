{{-- Podgląd ekranu telefonu – model z resources/js/phone.js (te same teksty co ResultScreen w aplikacji). --}}
<div class="phone mx-auto">
    <div class="phone-screen" :style="phone ? `background:${phone.color}` : 'background:#0A0C0F'">
        <div class="phone-notch"></div>

        {{-- telefon nasłuchuje / ignoruje --}}
        <template x-if="!phone">
            <div class="flex h-full flex-col items-center justify-center gap-6 px-8 text-center">
                <div class="relative grid size-36 place-items-center">
                    <div class="pulse-ring absolute inset-0 rounded-full border-2 border-ink/30"></div>
                    <div class="pulse-ring delay absolute inset-0 rounded-full border-2 border-ink/30"></div>
                    <div class="grid size-28 place-items-center rounded-full bg-[#1D232B]" x-html="sygnet.icon('signal', 'size-12 text-ink')"></div>
                </div>
                <div>
                    <div class="text-lg font-bold">Nasłuchuję komunikatów</div>
                    <div class="mt-1 text-sm text-muted">{{ $idle ?? 'Ramka bez poprawnego CRC jest ignorowana.' }}</div>
                </div>
            </div>
        </template>

        <template x-if="phone">
            <div class="flex h-full flex-col gap-2.5 overflow-hidden px-4 pt-11 pb-4 text-white">
                <div class="flex items-center justify-between text-[10px] font-bold tracking-[0.2em] text-white/70">
                    <span class="flex items-center gap-1.5">
                        <img src="{{ Vite::asset('resources/images/sygnet_mark_white.png') }}" class="h-3.5 opacity-70" alt="">SYGNET
                    </span>
                    <span class="mono tracking-normal" x-text="phone.time"></span>
                </div>

                <div class="flex flex-col items-center gap-1.5 pt-1 text-center">
                    <div class="grid size-12 place-items-center rounded-full bg-white/15" x-html="sygnet.icon(phone.icon, 'size-7')"></div>
                    <div class="text-[25px] leading-tight font-extrabold tracking-tight" x-text="phone.title"></div>
                    <div class="text-[12px] leading-snug text-white/85" x-text="phone.line"></div>
                </div>

                <template x-if="phone.reason">
                    <div class="rounded-xl bg-black/30 px-3.5 py-2.5 text-[13px] leading-snug font-bold" x-text="phone.reason"></div>
                </template>

                <div class="rounded-xl bg-black/20 px-3.5 py-2.5">
                    <div class="text-[9px] font-bold tracking-[0.18em] text-white/65 uppercase" x-text="phone.label"></div>
                    <div class="mt-1 flex items-center gap-2 text-[16px] font-bold">
                        <span x-html="sygnet.icon(phone.typeIcon, 'size-5 shrink-0')"></span>
                        <span x-text="phone.typeName"></span>
                    </div>
                    <template x-if="phone.note">
                        <div class="mt-1 text-[13px] leading-snug font-semibold" x-text="phone.note"></div>
                    </template>
                </div>

                <div class="rounded-xl bg-white px-3.5 py-2.5 text-[#0A0C0F]">
                    <div class="text-[9px] font-bold tracking-[0.18em] text-[#5B6570] uppercase">Co robić</div>
                    <div class="mt-0.5 text-[13px] leading-snug font-bold" x-text="phone.instruction"></div>
                </div>

                <div class="rounded-xl bg-black/20 px-3.5 py-2 text-[11px]">
                    <template x-for="d in phone.details" :key="d[0]">
                        <div class="flex gap-2 py-0.5">
                            <span class="w-16 shrink-0 text-white/65" x-text="d[0]"></span>
                            <span class="font-semibold" :class="d[2] && 'mono'" x-text="d[1]"></span>
                        </div>
                    </template>
                </div>
            </div>
        </template>
    </div>
</div>
