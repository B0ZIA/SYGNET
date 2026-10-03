// Modem dźwiękowy SYGNET: dual-tone 16-FSK, dokładnie PROTOCOL.md §8.1–8.2
// (odpowiednik _tone / encode_frame_once / encode z tools/sygnet_ref.py i ModemEncoder.cs w Unity).

export const MODEM = {
    A0: 1500, B0: 3200, STEP: 100, SYMBOL_MS: 40, GAP_MS: 10,
    PRE_A: 1000, PRE_B: 5200, PRE_TONE_MS: 200, PRE_GAP_MS: 50, END_TONE_MS: 200,
    REPEAT_GAP_MS: 500, TONE_AMP: 0.4, MARKER_AMP: 0.6, FADE_MS: 5,
};

export const WAV_SAMPLE_RATE = 48000;

// round() z Pythona/C# zaokrągla połówki do parzystej (44,1 kHz × 5 ms = 220,5 → 220), Math.round – w górę
function roundHalfEven(x) {
    const f = Math.floor(x);
    const d = x - f;
    if (d > 0.5) return f + 1;
    if (d < 0.5) return f;
    return f % 2 === 0 ? f : f + 1;
}

const samplesFor = (sr, ms) => roundHalfEven((sr * ms) / 1000);

function tone(out, pos, freqs, ms, sr, amp) {
    const n = samplesFor(sr, ms);
    const fade = samplesFor(sr, MODEM.FADE_MS);
    for (let i = 0; i < n; i++) {
        const t = i / sr;
        let s = 0;
        for (const f of freqs) s += amp * Math.sin(2 * Math.PI * f * t);
        out[pos + i] = s;
    }
    if (fade > 0 && n > 2 * fade) {
        for (let i = 0; i < fade; i++) {
            const w = 0.5 - 0.5 * Math.cos((Math.PI * i) / fade);     // raised cosine 0 → 1
            out[pos + i] *= w;
            out[pos + n - 1 - i] *= w;
        }
    }
    return pos + n;
}

const silence = (pos, ms, sr) => pos + samplesFor(sr, ms);

function onceLength(frameLength, sr) {
    return 2 * samplesFor(sr, MODEM.PRE_TONE_MS) + 2 * samplesFor(sr, MODEM.PRE_GAP_MS)
        + frameLength * (samplesFor(sr, MODEM.SYMBOL_MS) + samplesFor(sr, MODEM.GAP_MS))
        + samplesFor(sr, MODEM.END_TONE_MS);
}

/** Ramka (Uint8Array) → próbki Float32Array: R × (preambuła, bajty, znacznik końca) z 500 ms ciszy pomiędzy. */
export function encode(frame, sr, repeat = 2) {
    const once = onceLength(frame.length, sr);
    const gap = samplesFor(sr, MODEM.REPEAT_GAP_MS);
    const out = new Float32Array(repeat * once + (repeat - 1) * gap);
    let pos = 0;
    for (let r = 0; r < repeat; r++) {
        if (r > 0) pos = silence(pos, MODEM.REPEAT_GAP_MS, sr);
        pos = tone(out, pos, [MODEM.PRE_A], MODEM.PRE_TONE_MS, sr, MODEM.MARKER_AMP);
        pos = silence(pos, MODEM.PRE_GAP_MS, sr);
        pos = tone(out, pos, [MODEM.PRE_B], MODEM.PRE_TONE_MS, sr, MODEM.MARKER_AMP);
        pos = silence(pos, MODEM.PRE_GAP_MS, sr);
        for (const b of frame) {
            const fa = MODEM.A0 + (b >> 4) * MODEM.STEP;
            const fb = MODEM.B0 + (b & 0x0f) * MODEM.STEP;
            pos = tone(out, pos, [fa, fb], MODEM.SYMBOL_MS, sr, MODEM.TONE_AMP);
            pos = silence(pos, MODEM.GAP_MS, sr);
        }
        pos = tone(out, pos, [MODEM.PRE_B], MODEM.END_TONE_MS, sr, MODEM.MARKER_AMP);
    }
    return out;
}

/** Czas nadawania w sekundach (PROTOCOL.md §8.1). */
export const durationSeconds = (frameLength, repeat = 2) =>
    repeat * (0.7 + frameLength * 0.05) + (repeat - 1) * 0.5;

/** WAV 16-bit PCM mono (jak write_wav w sygnet_ref.py: obcięcie do ±1, × 32767, ucięcie do całości). */
export function toWav(samples, sr = WAV_SAMPLE_RATE) {
    const buffer = new ArrayBuffer(44 + samples.length * 2);
    const v = new DataView(buffer);
    const str = (o, s) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
    str(0, 'RIFF');
    v.setUint32(4, 36 + samples.length * 2, true);
    str(8, 'WAVE');
    str(12, 'fmt ');
    v.setUint32(16, 16, true);
    v.setUint16(20, 1, true);                 // PCM
    v.setUint16(22, 1, true);                 // mono
    v.setUint32(24, sr, true);
    v.setUint32(28, sr * 2, true);
    v.setUint16(32, 2, true);
    v.setUint16(34, 16, true);
    str(36, 'data');
    v.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) {
        const x = Math.max(-1, Math.min(1, samples[i]));
        v.setInt16(44 + i * 2, Math.trunc(x * 32767), true);
    }
    return new Blob([buffer], { type: 'audio/wav' });
}

/**
 * Nadawanie przez głośnik: AudioContext w częstotliwości urządzenia, AnalyserNode do wizualizacji.
 * play() zwraca obietnicę rozwiązaną po końcu (albo stop()).
 */
export class Transmitter {
    constructor() {
        this.ctx = null;
        this.source = null;
        this.analyser = null;
        this.timer = 0;
    }

    /** Wołać synchronicznie w obsłudze kliknięcia – przeglądarki wymagają gestu użytkownika. */
    unlock() {
        this.ctx ??= new (window.AudioContext || window.webkitAudioContext)();
        if (this.ctx.state === 'suspended') this.ctx.resume();
        if (!this.analyser) {
            this.analyser = this.ctx.createAnalyser();
            this.analyser.fftSize = 2048;
            this.analyser.smoothingTimeConstant = 0.6;
            this.analyser.connect(this.ctx.destination);
        }
        return this.ctx;
    }

    async play(frame, repeat = 2, { onProgress } = {}) {
        this.stop();
        const ctx = this.unlock();
        await ctx.resume();
        const sr = ctx.sampleRate;
        const samples = encode(frame, sr, repeat);
        const buffer = ctx.createBuffer(1, samples.length, sr);
        buffer.copyToChannel(samples, 0);

        const source = ctx.createBufferSource();
        source.buffer = buffer;
        source.connect(this.analyser);
        this.source = source;

        const duration = samples.length / sr;
        const started = ctx.currentTime + 0.05;
        // timer, nie requestAnimationFrame – postęp idzie też, gdy karta jest w tle
        const tick = () => {
            const t = Math.min(duration, Math.max(0, ctx.currentTime - started));
            onProgress?.(t / duration, t, duration);
        };

        return new Promise((resolve) => {
            source.onended = () => {
                if (this.source === source) this.source = null;
                clearInterval(this.timer);
                onProgress?.(1, duration, duration);
                resolve();
            };
            source.start(started);
            this.timer = setInterval(tick, 40);
        });
    }

    stop() {
        if (this.source) {
            const s = this.source;
            this.source = null;
            try { s.stop(); } catch { /* już zatrzymane */ }
        }
        clearInterval(this.timer);
    }
}
