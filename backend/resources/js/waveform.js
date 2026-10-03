// Wizualizacja nadawania: „partytura” tonów ramki (czas × częstotliwość, jak spektrogram) z kursorem odtwarzania
// i pasek widma na żywo z AnalyserNode po prawej, na tej samej osi częstotliwości.
import { MODEM, durationSeconds } from './sygnet-modem';

const F_MIN = 700;
const F_MAX = 5500;
const LIVE_W = 64;

function plan(frame, repeat) {
    const tones = [];
    let t = 0;
    const s = (ms) => ms / 1000;
    for (let r = 0; r < repeat; r++) {
        if (r > 0) t += s(MODEM.REPEAT_GAP_MS);
        tones.push({ t, d: s(MODEM.PRE_TONE_MS), f: MODEM.PRE_A, kind: 'marker' });
        t += s(MODEM.PRE_TONE_MS + MODEM.PRE_GAP_MS);
        tones.push({ t, d: s(MODEM.PRE_TONE_MS), f: MODEM.PRE_B, kind: 'marker' });
        t += s(MODEM.PRE_TONE_MS + MODEM.PRE_GAP_MS);
        for (const b of frame) {
            tones.push({ t, d: s(MODEM.SYMBOL_MS), f: MODEM.A0 + (b >> 4) * MODEM.STEP, kind: 'a' });
            tones.push({ t, d: s(MODEM.SYMBOL_MS), f: MODEM.B0 + (b & 15) * MODEM.STEP, kind: 'b' });
            t += s(MODEM.SYMBOL_MS + MODEM.GAP_MS);
        }
        tones.push({ t, d: s(MODEM.END_TONE_MS), f: MODEM.PRE_B, kind: 'marker' });
        t += s(MODEM.END_TONE_MS);
    }
    return { tones, total: t };
}

const COLORS = {
    marker: ['#F5B86066', '#F5B860'],
    a: ['#2EA04355', '#3FD068'],
    b: ['#5AA9FF44', '#7CC0FF'],
};

/**
 * Rysuje stan nadajnika na canvasie.
 * @param {HTMLCanvasElement} canvas
 * @param {{frame?: Uint8Array, repeat: number, progress: number, analyser?: AnalyserNode, playing: boolean}} st
 */
export function draw(canvas, st) {
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
        canvas.width = Math.round(w * dpr);
        canvas.height = Math.round(h * dpr);
    }
    const g = canvas.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);

    const plotW = w - LIVE_W - 12;
    const y = (f) => h - 8 - ((f - F_MIN) / (F_MAX - F_MIN)) * (h - 16);

    // pasma A i B
    g.fillStyle = '#ffffff06';
    g.fillRect(0, y(3000 + 50), plotW, y(1500 - 50) - y(3000 + 50));
    g.fillRect(0, y(4700 + 50), plotW, y(3200 - 50) - y(4700 + 50));
    g.font = '600 10px "JetBrains Mono", monospace';
    g.fillStyle = '#5B6570';
    g.fillText('A 1500–3000 Hz', 6, y(3000) - 4);
    g.fillText('B 3200–4700 Hz', 6, y(4700) - 4);

    if (st.frame?.length) {
        const { tones, total } = plan(st.frame, st.repeat);
        const now = st.progress * total;
        const x = (t) => (t / total) * plotW;
        for (const tn of tones) {
            const done = tn.t <= now;
            g.fillStyle = COLORS[tn.kind][done && st.playing ? 1 : 0];
            const tw = Math.max(1, x(tn.d) - (tn.kind === 'marker' ? 0 : 0.4));
            g.fillRect(x(tn.t), y(tn.f) - 2, tw, tn.kind === 'marker' ? 5 : 3);
        }
        if (st.playing || (st.progress > 0 && st.progress < 1)) {
            const px = x(now);
            g.fillStyle = '#ECF0F4';
            g.fillRect(px - 1, 0, 2, h);
            const grad = g.createLinearGradient(px - 40, 0, px, 0);
            grad.addColorStop(0, '#ECF0F400');
            grad.addColorStop(1, '#ECF0F418');
            g.fillStyle = grad;
            g.fillRect(px - 40, 0, 40, h);
        }
    } else {
        g.fillStyle = '#5B6570';
        g.font = '500 13px Inter, sans-serif';
        g.fillText('Tu pojawi się zapis tonów ramki – 1 bajt = 2 tony przez 40 ms', 12, h / 2 + 4);
    }

    // widmo na żywo (prawa krawędź)
    const lx = w - LIVE_W;
    g.fillStyle = '#ffffff08';
    g.fillRect(lx, 0, LIVE_W, h);
    if (st.analyser && st.playing) {
        const bins = new Uint8Array(st.analyser.frequencyBinCount);
        st.analyser.getByteFrequencyData(bins);
        const hz = st.analyser.context.sampleRate / st.analyser.fftSize;
        for (let i = Math.floor(F_MIN / hz); i < Math.min(bins.length, F_MAX / hz); i++) {
            const v = bins[i] / 255;
            if (v < 0.25) continue;
            g.fillStyle = `rgba(236,240,244,${0.25 + 0.75 * v})`;
            g.fillRect(lx + LIVE_W * (1 - v), y(i * hz) - 1, LIVE_W * v, 2);
        }
    }
    g.fillStyle = '#5B6570';
    g.font = '600 9px "JetBrains Mono", monospace';
    g.fillText('NA ŻYWO', lx + 8, 12);
}

export { durationSeconds };
