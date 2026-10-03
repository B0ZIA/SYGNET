import Alpine from 'alpinejs';
import QRCode from 'qrcode';
import { Transmitter, encode, toWav, durationSeconds, WAV_SAMPLE_RATE } from './sygnet-modem';
import { draw } from './waveform';
import { phoneView, previewView } from './phone';
import { icon } from './icons';
import { segments, SEGMENT_CLASS, hexToBytes, utf8Length, frameSize, download, clock, dateTime } from './frame';

import.meta.glob(['../images/**'], { eager: true });             // logo przez Vite::asset()

const boot = JSON.parse(document.getElementById('boot')?.textContent ?? '{}');
const csrf = document.querySelector('meta[name="csrf-token"]')?.content;

async function api(method, url, body) {
    const res = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-CSRF-TOKEN': csrf },
        body: body ? JSON.stringify(body) : undefined,
    });
    if (res.status === 401) {
        window.location.href = '/login';             // hasło konsoli: sesja wygasła
    }
    const data = await res.json().catch(() => ({}));
    return { ok: res.ok, status: res.status, data };
}

// helpery dostępne w szablonach
const DOT = { VERIFIED: '#2EA043', VERIFIED_OTHER_AREA: '#3B82F6', EXPIRED: '#B7791F', DUPLICATE: '#5B6570' };
const TITLE = { VERIFIED: 'ZWERYFIKOWANO', VERIFIED_OTHER_AREA: 'INNY OBSZAR', EXPIRED: 'NIEAKTUALNY', INCOMPLETE: 'NIEPEŁNY PODPIS', FORGED: 'FAŁSZYWKA' };
window.sygnet = {
    icon, segments, SEGMENT_CLASS, clock, dateTime,
    dot: (status) => DOT[status] ?? '#E5484D',
    title: (status) => TITLE[status] ?? status,
    validity: (m) => (m < 60 ? `${m} min` : m <= 1440 ? `${m / 60} h` : `${m / 1440} dni`),
};

// ───────────── nadajnik (wspólny dla wszystkich stron) ─────────────
Alpine.store('tx', {
    tx: new Transmitter(),
    repeat: boot.repeat ?? 2,
    current: null,
    playing: false,
    progress: 0,
    elapsed: 0,
    duration: 0,
    canvas: null,

    attach(canvas) {
        this.canvas = canvas;
        new ResizeObserver(() => this.redraw()).observe(canvas);
        this.redraw();
    },

    select(b) {
        if (this.playing) return;
        this.current = b;
        this.progress = 0;
        this.duration = b ? durationSeconds(b.bytes, this.repeat) : 0;
        this.redraw();
    },

    redraw() {
        if (!this.canvas) return;
        draw(this.canvas, {
            frame: this.current ? hexToBytes(this.current.frame_hex) : null,
            repeat: this.repeat,
            progress: this.progress,
            analyser: this.tx.analyser,
            playing: this.playing,
        });
    },

    /** Wołać synchronicznie w kliknięciu, zanim cokolwiek poczeka na sieć. */
    unlock() {
        this.tx.unlock();
    },

    async play(b) {
        this.select(b);
        this.playing = true;
        try {
            await this.tx.play(hexToBytes(b.frame_hex), this.repeat, {
                onProgress: (p, t, d) => {
                    this.progress = p;
                    this.elapsed = t;
                    this.duration = d;
                    this.redraw();
                },
            });
        } finally {
            this.playing = false;
            this.redraw();
        }
    },

    stop() {
        this.tx.stop();
        this.playing = false;
        this.progress = 0;
        this.redraw();
    },

    wav(b) {
        const x = encode(hexToBytes(b.frame_hex), WAV_SAMPLE_RATE, this.repeat);
        const name = b.kind === 'attack' ? `sygnet_atak_${b.attack_type}_${b.id}` : `sygnet_${b.issuer_id}_${b.sequence}`;
        download(toWav(x, WAV_SAMPLE_RATE), `${name}.wav`);
    },
});

// ───────────── kod QR (poziom korekcji M, PROTOCOL.md §3) ─────────────
Alpine.store('qr', {
    open: false,
    b: null,
    async show(b) {
        this.b = b;
        this.open = true;
        await Alpine.nextTick();
        const canvas = document.getElementById('qr-canvas');
        await QRCode.toCanvas(canvas, b.qr_text, {
            errorCorrectionLevel: 'M', margin: 4, width: 600, color: { dark: '#000000', light: '#ffffff' },
        });
    },
    close() {
        this.open = false;
    },
});

// ───────────── /console ─────────────
Alpine.data('consoleApp', () => ({
    boot,
    form: {
        issuer_id: (boot.issuers.find((i) => i.id === 1 && i.has_key) ?? boot.issuers.find((i) => i.has_key) ?? boot.issuers[0])?.id,
        type: 1,
        area_code: boot.userArea,
        valid_minutes: 120,
        note: '',
        second_signer_id: null,
    },
    userArea: boot.userArea,
    signed: null,
    errors: {},
    busy: false,
    history: [],
    pinOpen: false,
    pin: '',
    pinError: '',
    pinOk: false,
    pinValue: '',
    pending: null,

    init() {
        this.fixArea();
        this.$watch('form', () => {
            this.signed = null;
            this.errors = {};
            this.$store.tx.select(null);
        }, { deep: true });
        this.$watch('form.issuer_id', () => this.fixArea());
        this.$watch('form.area_code', () => this.fixSecond());
        this.$watch('form.type', () => this.fixSecond());
        this.loadHistory();
    },

    get issuer() { return this.boot.issuers.find((i) => i.id === this.form.issuer_id); },
    get type() { return this.boot.types.find((t) => t.id === this.form.type); },
    get critical() { return !!this.type?.critical; },
    get allowedAreas() { return this.boot.areas.filter((a) => this.issuer?.areas.includes(a.code)); },
    get noteBytes() { return utf8Length(this.form.note); },
    get secondCandidates() {
        return this.boot.issuers.filter((i) => i.id !== this.form.issuer_id && i.has_key && !i.revoked
            && i.areas.includes(this.form.area_code));
    },
    get estimateBytes() { return frameSize(this.noteBytes, this.critical ? 2 : 1); },
    get estimateSeconds() { return durationSeconds(this.estimateBytes, this.$store.tx.repeat).toFixed(1); },
    get phone() {
        return this.signed
            ? phoneView(this.signed, this.userArea)
            : previewView(this.form, { types: this.boot.types, areas: this.boot.areas, issuers: this.boot.issuers, userArea: this.userArea });
    },
    get canSign() {
        return this.issuer?.has_key && !this.issuer?.revoked && this.noteBytes <= 60
            && (!this.critical || this.form.second_signer_id !== null);
    },

    fixArea() {
        if (!this.issuer?.areas.includes(this.form.area_code)) {
            this.form.area_code = this.issuer?.areas.includes(this.userArea) ? this.userArea : this.issuer?.areas[0];
        }
        this.fixSecond();
    },
    fixSecond() {
        if (!this.critical) {
            this.form.second_signer_id = null;
        } else if (!this.secondCandidates.some((i) => i.id === this.form.second_signer_id)) {
            this.form.second_signer_id = this.secondCandidates[0]?.id ?? null;
        }
    },

    async loadHistory() {
        const r = await api('GET', '/api/broadcasts?limit=30&kind=genuine');
        if (r.ok) this.history = r.data;
    },

    /** Podpisuje (raz na stan formularza). Typ krytyczny: najpierw zatwierdzenie drugiego operatora. */
    async sign(action) {
        if (this.signed) return this.signed;
        if (this.critical && !this.pinOk) {
            this.pending = action;
            this.pin = '';
            this.pinError = '';
            this.pinOpen = true;
            return null;
        }
        this.busy = true;
        const r = await api('POST', '/api/broadcast', { ...this.form, second_pin: this.critical ? this.pinValue : null });
        this.busy = false;
        this.pinOk = false;
        if (!r.ok) {
            this.errors = r.data.errors ?? { form: [r.data.message ?? 'Błąd serwera'] };
            return null;
        }
        // $watch na formularzu nie może skasować świeżo podpisanej ramki
        await this.$nextTick();
        this.signed = r.data;
        this.history.unshift(r.data);
        this.$store.tx.select(r.data);
        return r.data;
    },

    async confirmPin() {
        this.pinOk = true;
        this.pinValue = this.pin;
        this.pinOpen = false;
        const action = this.pending;
        this.pending = null;
        if (action === 'play') this.$store.tx.unlock();
        const b = await this.sign(action);
        if (!b) {
            if (this.errors.second_pin) {
                this.pinError = this.errors.second_pin[0];
                this.pending = action;
                this.pinOpen = true;
            }
            return;
        }
        this.run(action, b);
    },

    async act(action) {
        if (action === 'play') this.$store.tx.unlock();
        const b = await this.sign(action);
        if (b) this.run(action, b);
    },

    run(action, b) {
        if (action === 'play') this.$store.tx.play(b);
        if (action === 'qr') this.$store.qr.show(b);
        if (action === 'wav') this.$store.tx.wav(b);
        if (action === 'poster') window.open(`/poster/${b.id}`, '_blank');
    },

    /** Ten sam formularz, ale następne nadanie podpisze nowy komunikat (nowy numer) – telefon go nie pominie. */
    newNumber() {
        this.signed = null;
        this.$store.tx.select(null);
    },

    pick(b) {
        if (this.$store.tx.playing) return;
        this.signed = null;
        this.$nextTick(() => {
            this.signed = b;
            this.$store.tx.select(b);
        });
    },

    replay(b) {
        this.$store.tx.unlock();
        this.pick(b);
        this.$nextTick(() => this.$store.tx.play(b));
    },
}));

// ───────────── /attack ─────────────
// Dla oceniającego: lista ataków, każdy z własnym przyciskiem „Uruchom”, wynik telefonu i jedno zdanie „dlaczego”.
Alpine.data('attackLab', () => ({
    boot,
    attacks: boot.attacks,
    results: {},
    busy: null,
    selected: null,
    error: null,

    get order() { return Object.keys(this.attacks); },
    get done() { return Object.keys(this.results).length; },
    get blocked() { return Object.values(this.results).filter((r) => r.as_expected).length; },
    get phone() { return phoneView(this.selected); },

    async generate(code) {
        this.busy = code;
        this.error = null;
        const r = await api('POST', `/api/attack/${code}`, {});
        this.busy = null;
        if (!r.ok) {
            this.error = `${code}: ${r.data.message ?? 'błąd serwera'}`;
            return null;
        }
        this.results[code] = r.data;
        return r.data;
    },

    show(b) {
        this.selected = b;
        this.$store.tx.select(b);
    },

    /** Kliknięcie wiersza z wynikiem: pokaż go jeszcze raz na telefonie. */
    pick(code) {
        if (this.results[code]) this.show(this.results[code]);
    },

    /** „Uruchom”: nowa ramka ataku, od razu nadana dźwiękiem (telefon słucha) i wynik na podglądzie telefonu. */
    async launch(code) {
        if (this.$store.tx.playing) return;
        this.$store.tx.unlock();                 // w kliknięciu – przeglądarka wymaga gestu, zanim zagra dźwięk
        const b = await this.generate(code);
        if (!b) return;
        this.show(b);
        this.$store.tx.play(b);
    },

}));

// ───────────── /keys ─────────────
Alpine.data('keysPage', () => ({
    boot,
    confirm: null,
    busy: false,
    error: null,
    result: null,
    revokeId: boot.issuerRows?.find((r) => r.in_trust_store && !r.revoked)?.id ?? null,

    async revoke() {
        const issuer = this.confirm;
        this.busy = true;
        this.error = null;
        const r = await api('POST', '/api/revoke', { issuer_id: issuer.id });
        this.busy = false;
        this.confirm = null;
        if (!r.ok) {
            this.error = r.data.message ?? 'Błąd';
            return;
        }
        this.result = r.data;
        this.$store.tx.select(r.data);
        const row = this.boot.issuerRows.find((x) => x.id === issuer.id);
        if (row) row.revoked = true;
        this.revokeId = this.boot.issuerRows.find((r) => r.in_trust_store && !r.revoked)?.id ?? null;
    },

    act(action) {
        if (!this.result) return;
        if (action === 'play') {
            this.$store.tx.unlock();
            this.$store.tx.play(this.result);
        }
        if (action === 'qr') this.$store.qr.show(this.result);
        if (action === 'wav') this.$store.tx.wav(this.result);
    },
}));

window.Alpine = Alpine;
Alpine.start();
