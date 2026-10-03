// Zgodność modemu JS z implementacją referencyjną: próbki encode() = pliki testvectors/*.wav (±1 LSB).
// Uruchom: npm test
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { encode, toWav, durationSeconds } from '../../resources/js/sygnet-modem.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '../../../testvectors');
const vectors = JSON.parse(readFileSync(join(root, 'testvectors.json'), 'utf8')).vectors;
const hex = (h) => Uint8Array.from(h.match(/../g).map((b) => parseInt(b, 16)));

function readWav(buf) {
    const v = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    let p = 12;
    let sr = 0;
    while (p < buf.length) {
        const id = buf.toString('ascii', p, p + 4);
        const size = v.getUint32(p + 4, true);
        if (id === 'fmt ') sr = v.getUint32(p + 12, true);
        if (id === 'data') {
            const out = new Int16Array(size / 2);
            for (let i = 0; i < out.length; i++) out[i] = v.getInt16(p + 8 + i * 2, true);
            return { sr, pcm: out };
        }
        p += 8 + size;
    }
    throw new Error('brak danych WAV');
}

for (const [name, tv] of Object.entries(vectors)) {
    test(`${name}: encode() = ${tv.wav}`, () => {
        const { sr, pcm } = readWav(readFileSync(join(root, tv.wav)));
        const x = encode(hex(tv.frame_hex), sr, 2);
        assert.equal(x.length, pcm.length, 'liczba próbek');
        let worst = 0;
        for (let i = 0; i < x.length; i++) {
            worst = Math.max(worst, Math.abs(Math.trunc(Math.max(-1, Math.min(1, x[i])) * 32767) - pcm[i]));
        }
        assert.ok(worst <= 1, `największa różnica ${worst} LSB`);
        assert.ok(Math.abs(durationSeconds(tv.frame_len, 2) - tv.audio_seconds) < 0.01, 'czas nadawania');
    });
}

test('toWav: nagłówek RIFF 16-bit mono 48 kHz', async () => {
    const blob = toWav(new Float32Array([0, 0.5, -1, 2]), 48000);
    const b = Buffer.from(await blob.arrayBuffer());
    assert.equal(b.toString('ascii', 0, 4), 'RIFF');
    const { sr, pcm } = readWav(b);
    assert.equal(sr, 48000);
    assert.deepEqual([...pcm], [0, 16383, -32767, 32767]);
});

test('44,1 kHz: zaokrąglanie długości jak w Pythonie (połówki do parzystej)', () => {
    // 1 bajt: 2×200 ms + 2×50 ms + (40 + 10) ms + 200 ms; 5 ms fade = 220,5 → 220 nie zmienia długości
    assert.equal(encode(new Uint8Array([0x53]), 44100, 1).length, 8820 * 2 + 2205 * 2 + 1764 + 441 + 8820);
});
