// Pomocnicze funkcje ramki po stronie przeglądarki (tylko prezentacja – podpisuje wyłącznie backend).

export const hexToBytes = (hex) => Uint8Array.from((hex.match(/../g) ?? []).map((b) => parseInt(b, 16)));

export const utf8Length = (s) => new TextEncoder().encode(s ?? '').length;

/** Rozmiar ramki: 4 (SG + len) + 16 (nagłówek payloadu) + dopisek + 1 + podpisy × 66 + 2 (CRC). */
export const frameSize = (noteBytes, signatures) => 4 + 16 + noteBytes + 1 + 66 * signatures + 2;

/**
 * Segmenty ramki do kolorowego podglądu hex: nagłówek | payload | dopisek | podpisy | CRC (PROTOCOL.md §3).
 * Zwraca listę bajtów z rodzajem segmentu.
 */
export function segments(hex) {
    const bytes = hexToBytes(hex);
    if (bytes.length < 23) return [];
    const noteLen = bytes[4 + 15];
    const payloadEnd = 4 + 16;
    const noteEnd = payloadEnd + noteLen;
    return [...bytes].map((b, i) => ({
        hex: b.toString(16).padStart(2, '0'),
        kind: i < 4 ? 'head' : i < payloadEnd ? 'payload' : i < noteEnd ? 'note' : i < bytes.length - 2 ? 'sig' : 'crc',
    }));
}

export const SEGMENT_CLASS = {
    head: 'text-seg-head',
    payload: 'text-seg-payload',
    note: 'text-seg-note',
    sig: 'text-seg-sig',
    crc: 'text-seg-crc',
};

/** Indeksy bajtów różniących się między dwiema ramkami (atak A2: podmieniony dopisek, przeliczone CRC). */
export function diffIndices(hexA, hexB) {
    const a = hexToBytes(hexA);
    const b = hexToBytes(hexB);
    const out = new Set();
    for (let i = 0; i < Math.max(a.length, b.length); i++) if (a[i] !== b[i]) out.add(i);
    return out;
}

export function download(blob, filename) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
}

export const clock = (unix) => new Date(unix * 1000).toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' });

export const dateTime = (unix) => new Date(unix * 1000).toLocaleString('pl-PL', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
});
