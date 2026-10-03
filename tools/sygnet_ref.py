#!/usr/bin/env python3
"""
SYGNET v1 - implementacja referencyjna (Python).

Cel: jedno, sprawdzone źródło prawdy dla protokołu i modemu.
Laravel (PHP/JS) i Unity (C#) muszą dawać IDENTYCZNE bajty i dekodować te same pliki WAV.

Użycie:
  python3 sygnet_ref.py selftest          # test modemu: szum, przesunięcia, inny sample rate
  python3 sygnet_ref.py vectors OUT_DIR   # generuje testvectors.json + pliki WAV
  python3 sygnet_ref.py decode plik.wav   # dekoduje WAV (np. nagrany z konsoli) i weryfikuje

Wymaga: numpy, cryptography
"""
import base64, hashlib, json, struct, sys, wave
from dataclasses import dataclass, field
import numpy as np
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives import serialization
from cryptography.exceptions import InvalidSignature

# ───────────────────────── STAŁE PROTOKOŁU ─────────────────────────
VERSION = 1
MAGIC = b"SG"
PAYLOAD_HEADER_LEN = 16
NOTE_MAX = 60
SIG_LEN = 64
CLOCK_SKEW_S = 300

# ───────────────────────── STAŁE MODEMU ─────────────────────────
BAND_A_BASE, BAND_B_BASE, TONE_STEP = 1500.0, 3200.0, 100.0
SYMBOL_MS, GAP_MS = 40, 10                  # okres symbolu = 50 ms (1 bajt)
PRE_A_HZ, PRE_B_HZ = 1000.0, 5200.0
PRE_TONE_MS, PRE_GAP_MS = 200, 50
END_TONE_MS = 200
REPEAT, REPEAT_GAP_MS = 2, 500
TONE_AMP = 0.4                               # amplituda każdego z 2 tonów danych
MARKER_AMP = 0.6                             # amplituda tonów preambuły/końca
FADE_MS = 5
# dekoder
DET_WIN_MS, DET_HOP_MS = 10.0, 2.5
DET_RATIO = 0.5
DET_MIN_MS = 120
ANALYZE_FROM_MS, ANALYZE_TO_MS = 6, 34
SYNC_SEARCH_MS, SYNC_STEP_MS = 12, 1


def crc16_ccitt(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


# ───────────────────────── KLUCZE ─────────────────────────
def key_from_seed(seed: bytes) -> Ed25519PrivateKey:
    return Ed25519PrivateKey.from_private_bytes(seed)

def pub_bytes(priv: Ed25519PrivateKey) -> bytes:
    return priv.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)

def fingerprint(pub: bytes) -> str:
    h = hashlib.sha256(pub).hexdigest().upper()[:16]
    return "-".join(h[i:i + 4] for i in range(0, 16, 4))

def verify(pub: bytes, msg: bytes, sig: bytes) -> bool:
    try:
        Ed25519PublicKey.from_public_bytes(pub).verify(sig, msg)
        return True
    except InvalidSignature:
        return False


# ───────────────────────── PAYLOAD / RAMKA ─────────────────────────
@dataclass
class Payload:
    issuer_id: int
    type: int
    area_code: int
    timestamp: int
    valid_minutes: int
    sequence: int
    note: bytes = b""
    flags: int = 0
    version: int = VERSION

    def to_bytes(self) -> bytes:
        if len(self.note) > NOTE_MAX:
            raise ValueError("note > 60 B")
        return struct.pack(">BHBHIHHBB", self.version, self.issuer_id, self.type, self.area_code,
                           self.timestamp, self.valid_minutes, self.sequence, self.flags,
                           len(self.note)) + self.note

    @staticmethod
    def parse(b: bytes) -> "Payload":
        v, iss, t, area, ts, vm, seq, fl, nl = struct.unpack(">BHBHIHHBB", b[:16])
        return Payload(iss, t, area, ts, vm, seq, b[16:16 + nl], fl, v)


def build_frame(payload: bytes, signatures: list[tuple[int, bytes]]) -> bytes:
    body = payload + bytes([len(signatures)]) + b"".join(struct.pack(">H", sid) + s for sid, s in signatures)
    frame_len = len(body) + 2                       # + CRC
    head = MAGIC + struct.pack(">H", frame_len) + body
    return head + struct.pack(">H", crc16_ccitt(head))


def sign_payload(payload: bytes, signers: list[tuple[int, Ed25519PrivateKey]]) -> bytes:
    return build_frame(payload, [(sid, k.sign(payload)) for sid, k in signers])


@dataclass
class ParsedFrame:
    payload_bytes: bytes
    payload: Payload
    signatures: list = field(default_factory=list)


def parse_frame(frame: bytes) -> ParsedFrame:
    if frame[:2] != MAGIC:
        raise ValueError("BAD_MAGIC")
    (flen,) = struct.unpack(">H", frame[2:4])
    if len(frame) != 4 + flen:
        raise ValueError("BAD_LENGTH")
    if crc16_ccitt(frame[:-2]) != struct.unpack(">H", frame[-2:])[0]:
        raise ValueError("BAD_CRC")
    note_len = frame[4 + 15]
    plen = PAYLOAD_HEADER_LEN + note_len
    payload_bytes = frame[4:4 + plen]
    p = 4 + plen
    count = frame[p]; p += 1
    sigs = []
    for _ in range(count):
        (sid,) = struct.unpack(">H", frame[p:p + 2])
        sigs.append((sid, frame[p + 2:p + 2 + SIG_LEN])); p += 2 + SIG_LEN
    if p != len(frame) - 2:
        raise ValueError("BAD_LENGTH")
    return ParsedFrame(payload_bytes, Payload.parse(payload_bytes), sigs)


def to_qr_text(frame: bytes) -> str:
    return "SYG1:" + base64.urlsafe_b64encode(frame).decode().rstrip("=")

def from_qr_text(s: str) -> bytes:
    b = s[5:]
    return base64.urlsafe_b64decode(b + "=" * (-len(b) % 4))


# ───────────────────────── CERTYFIKATY ─────────────────────────
def build_cert(issuer_id, pub, valid_from, valid_until, scopes, name: str) -> bytes:
    nb = name.encode("utf-8")
    return (struct.pack(">BH", 1, issuer_id) + pub + struct.pack(">IIB", valid_from, valid_until, len(scopes))
            + b"".join(struct.pack(">H", s) for s in scopes) + bytes([len(nb)]) + nb)

def parse_cert(c: bytes) -> dict:
    v, iss = struct.unpack(">BH", c[:3]); pub = c[3:35]
    vf, vu, n = struct.unpack(">IIB", c[35:44]); p = 44
    scopes = [struct.unpack(">H", c[p + 2 * i:p + 2 * i + 2])[0] for i in range(n)]; p += 2 * n
    nl = c[p]; name = c[p + 1:p + 1 + nl].decode("utf-8")
    return dict(version=v, issuer_id=iss, pub=pub, valid_from=vf, valid_until=vu, scopes=scopes, name=name)

def scope_covers(scope: int, area: int) -> bool:
    return scope == 0 or scope == area or (scope < 100 and area // 100 == scope)


# ───────────────────────── WERYFIKACJA ─────────────────────────
CRITICAL_TYPES = {3}           # EVACUATION wymaga 2 podpisów
KEY_REVOKE = 250
ROOT_ID = 0

def verify_frame(frame: bytes, root_pub: bytes, trust: dict, now: int, user_area: int,
                 revoked: set = frozenset(), seen: set = frozenset()) -> tuple[str, str]:
    """Zwraca (STATUS, powód). Kolejność sprawdzeń = kolejność w PROTOCOL.md §7."""
    try:
        f = parse_frame(frame)
    except Exception as e:
        return "MALFORMED", str(e)
    p = f.payload
    if p.version != VERSION:
        return "MALFORMED", "BAD_VERSION"
    if not f.signatures:
        return "FORGED", "NO_SIGNATURE"
    if f.signatures[0][0] != p.issuer_id:
        return "FORGED", "PRIMARY_SIGNER_MISMATCH"
    valid_signers = []
    for sid, sig in f.signatures:
        if sid == ROOT_ID:
            pub, scopes = root_pub, [0]
        else:
            c = trust.get(sid)
            if c is None:
                return "FORGED", f"UNKNOWN_ISSUER:{sid}"
            if sid in revoked:
                return "FORGED", f"REVOKED_ISSUER:{sid}"
            if not (c["valid_from"] <= now <= c["valid_until"]):
                return "FORGED", f"CERT_EXPIRED:{sid}"
            pub, scopes = c["pub"], c["scopes"]
        if not verify(pub, f.payload_bytes, sig):
            return "FORGED", f"BAD_SIGNATURE:{sid}"
        if not any(scope_covers(s, p.area_code) for s in scopes):
            return "FORGED", f"UNAUTHORIZED_AREA:{sid}"
        valid_signers.append(sid)
    if p.type == KEY_REVOKE and p.issuer_id != ROOT_ID:
        return "FORGED", "REVOKE_NOT_ROOT"
    if p.issuer_id == ROOT_ID and p.type != KEY_REVOKE:
        return "FORGED", "ROOT_ONLY_REVOKE"
    if p.type in CRITICAL_TYPES and len(set(valid_signers)) < 2:
        return "INCOMPLETE", "DUAL_SIGNATURE_REQUIRED"
    if p.timestamp > now + CLOCK_SKEW_S:
        return "FORGED", "TIMESTAMP_IN_FUTURE"
    if now > p.timestamp + p.valid_minutes * 60:
        return "EXPIRED", "EXPIRED"
    if (p.issuer_id, p.sequence) in seen:
        return "DUPLICATE", "ALREADY_RECEIVED"
    if not scope_covers(p.area_code, user_area):
        return "VERIFIED_OTHER_AREA", "OTHER_AREA"
    return "VERIFIED", "OK"


# ───────────────────────── MODEM: ENKODER ─────────────────────────
def _tone(freqs, ms, sr, amp):
    n = int(round(sr * ms / 1000.0))
    t = np.arange(n) / sr
    s = sum(amp * np.sin(2 * np.pi * f * t) for f in freqs)
    fade = int(round(sr * FADE_MS / 1000.0))
    if fade > 0 and n > 2 * fade:
        w = 0.5 - 0.5 * np.cos(np.pi * np.arange(fade) / fade)   # raised cosine 0→1
        s[:fade] *= w; s[-fade:] *= w[::-1]
    return s

def _silence(ms, sr):
    return np.zeros(int(round(sr * ms / 1000.0)))

def encode_frame_once(frame: bytes, sr: int) -> np.ndarray:
    parts = [_tone([PRE_A_HZ], PRE_TONE_MS, sr, MARKER_AMP), _silence(PRE_GAP_MS, sr),
             _tone([PRE_B_HZ], PRE_TONE_MS, sr, MARKER_AMP), _silence(PRE_GAP_MS, sr)]
    for b in frame:
        fa = BAND_A_BASE + (b >> 4) * TONE_STEP
        fb = BAND_B_BASE + (b & 0x0F) * TONE_STEP
        parts += [_tone([fa, fb], SYMBOL_MS, sr, TONE_AMP), _silence(GAP_MS, sr)]
    parts.append(_tone([PRE_B_HZ], END_TONE_MS, sr, MARKER_AMP))
    return np.concatenate(parts)

def encode(frame: bytes, sr: int = 48000) -> np.ndarray:
    one = encode_frame_once(frame, sr)
    out = [one]
    for _ in range(REPEAT - 1):
        out += [_silence(REPEAT_GAP_MS, sr), one]
    return np.concatenate(out).astype(np.float32)

def duration_s(frame_len_total: int) -> float:
    once = (2 * PRE_TONE_MS + 2 * PRE_GAP_MS + frame_len_total * (SYMBOL_MS + GAP_MS) + END_TONE_MS) / 1000
    return REPEAT * once + (REPEAT - 1) * REPEAT_GAP_MS / 1000


# ───────────────────────── MODEM: DEKODER ─────────────────────────
def goertzel_power(x: np.ndarray, f: float, sr: int) -> float:
    """Znormalizowana moc: czysty ton o dowolnej amplitudzie ≈ 1.0 względem energii okna."""
    n = len(x)
    coeff = 2 * np.cos(2 * np.pi * f / sr)
    s1 = s2 = 0.0
    for v in x:
        s0 = v + coeff * s1 - s2
        s2, s1 = s1, s0
    p = s1 * s1 + s2 * s2 - coeff * s1 * s2
    e = float(np.dot(x, x))
    return 0.0 if e <= 1e-12 else 2.0 * p / (n * e)

def _gpow_fast(x, freqs, sr):
    # wektorowo (to samo co goertzel_power, tylko przez DFT pojedynczych częstotliwości)
    n = len(x); t = np.arange(n) / sr
    e = float(np.dot(x, x))
    if e <= 1e-12:
        return np.zeros(len(freqs))
    res = []
    for f in freqs:
        c = np.dot(x, np.cos(2 * np.pi * f * t)); s = np.dot(x, np.sin(2 * np.pi * f * t))
        res.append(2.0 * (c * c + s * s) / (n * e))
    return np.array(res)

A_FREQS = [BAND_A_BASE + i * TONE_STEP for i in range(16)]
B_FREQS = [BAND_B_BASE + i * TONE_STEP for i in range(16)]

def _symbol(x, start, sr):
    a0 = start + int(sr * ANALYZE_FROM_MS / 1000); a1 = start + int(sr * ANALYZE_TO_MS / 1000)
    if a0 < 0 or a1 > len(x):
        return None, 0.0
    w = x[a0:a1]
    pa = _gpow_fast(w, A_FREQS, sr); pb = _gpow_fast(w, B_FREQS, sr)
    ia, ib = int(np.argmax(pa)), int(np.argmax(pb))
    return (ia << 4) | ib, float(pa[ia] + pb[ib])

def find_preambles(x: np.ndarray, sr: int) -> list[int]:
    """Zwraca indeksy próbek t0 (początek pierwszego symbolu danych) dla każdej znalezionej preambuły."""
    win = int(sr * DET_WIN_MS / 1000); hop = int(sr * DET_HOP_MS / 1000)
    n = (len(x) - win) // hop
    ra = np.zeros(n); rb = np.zeros(n)
    for k in range(n):
        seg = x[k * hop:k * hop + win]
        ra[k], rb[k] = _gpow_fast(seg, [PRE_A_HZ, PRE_B_HZ], sr)
    need = int(DET_MIN_MS / DET_HOP_MS)
    starts, k = [], 0
    while k < n:
        if ra[k] > DET_RATIO:
            j = k
            while j < n and ra[j] > DET_RATIO: j += 1
            if j - k >= need:
                # szukaj tonu B w ciągu 120 ms
                m = j
                while m < n and m < j + need and rb[m] <= DET_RATIO: m += 1
                if m < n and rb[m] > DET_RATIO:
                    e = m
                    while e < n and rb[e] > DET_RATIO: e += 1
                    if e - m >= need:
                        # Synchronizacja od NARASTAJĄCEGO zbocza tonu B (pogłos wydłuża tylko koniec tonu)
                        b_onset = m * hop + win // 2
                        starts.append(b_onset + int(sr * (PRE_TONE_MS + PRE_GAP_MS) / 1000))
                        k = e; continue
            k = j
        k += 1
    return starts

def _decode_at(x, t0, sr):
    period = int(round(sr * (SYMBOL_MS + GAP_MS) / 1000))
    # dostrojenie t0: szukamy przesunięcia maksymalizującego pewność 4 pierwszych symboli + poprawny MAGIC
    best = (None, -1.0)
    for off_ms in np.arange(-SYNC_SEARCH_MS, SYNC_SEARCH_MS + 0.001, SYNC_STEP_MS):
        t = t0 + int(sr * off_ms / 1000)
        conf, bs = 0.0, []
        for k in range(4):
            b, c = _symbol(x, t + k * period, sr)
            if b is None: break
            bs.append(b); conf += c
        if len(bs) == 4 and bytes(bs[:2]) == MAGIC and conf > best[1]:
            best = (t, conf)
    if best[0] is None:
        return None
    t = best[0]
    head = bytes(_symbol(x, t + k * period, sr)[0] for k in range(4))
    (flen,) = struct.unpack(">H", head[2:4])
    if flen > 400:
        return None
    rest = []
    for k in range(4, 4 + flen):
        b, _ = _symbol(x, t + k * period, sr)
        if b is None: return None
        rest.append(b)
    frame = head + bytes(rest)
    return frame if crc16_ccitt(frame[:-2]) == struct.unpack(">H", frame[-2:])[0] else None

def decode(x: np.ndarray, sr: int) -> list[bytes]:
    out = []
    for t0 in find_preambles(x, sr):
        f = _decode_at(x, t0, sr)
        if f and f not in out:
            out.append(f)
    return out


# ───────────────────────── WAV I/O ─────────────────────────
def write_wav(path, x, sr):
    pcm = np.clip(x, -1, 1)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((pcm * 32767).astype("<i2").tobytes())

def read_wav(path):
    with wave.open(path, "rb") as w:
        sr, ch, sw = w.getframerate(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(w.getnframes())
    assert sw == 2, "tylko 16-bit PCM"
    x = np.frombuffer(raw, "<i2").astype(np.float64) / 32768
    if ch > 1: x = x.reshape(-1, ch).mean(axis=1)
    return x, sr


# ───────────────────────── DANE TESTOWE ─────────────────────────
TS = 1791076320            # 2026-10-04 01:12:00 UTC (03:12 czasu PL)
SEEDS = {0: bytes([0x02]) * 32, 1: bytes([0x01]) * 32, 3: bytes([0x03]) * 32,
         5: bytes([0x05]) * 32, 6: bytes([0x06]) * 32, "hacker": bytes([0x66]) * 32}
ISSUERS = {  # id: (nazwa, scopes)
    1: ("Dowództwo Operacyjne RSZ", [0]),
    3: ("Wojewoda Mazowiecki", [14]),
    5: ("Komendant Wojewódzki PSP Mazowsze", [14]),
    6: ("Prezydent m.st. Warszawy", [1465]),
}
CERT_FROM, CERT_UNTIL = 1767225600, 1830297600   # 2026-01-01 .. 2028-01-01

def test_world():
    keys = {k: key_from_seed(s) for k, s in SEEDS.items()}
    root_pub = pub_bytes(keys[0])
    trust, certs = {}, []
    for iid, (name, scopes) in ISSUERS.items():
        cert = build_cert(iid, pub_bytes(keys[iid]), CERT_FROM, CERT_UNTIL, scopes, name)
        sig = keys[0].sign(cert)
        assert verify(root_pub, cert, sig)
        trust[iid] = parse_cert(cert)
        certs.append(dict(cert_b64=base64.b64encode(cert).decode(), root_sig_b64=base64.b64encode(sig).decode()))
    return keys, root_pub, trust, certs

def make_vectors():
    keys, root_pub, trust, certs = test_world()
    note1 = "Schron: metro Świętokrzyska".encode("utf-8")
    p1 = Payload(1, 1, 1465, TS, 120, 1, note1).to_bytes()
    v = {}
    v["TV1_air_raid_single"] = (p1, sign_payload(p1, [(1, keys[1])]), "VERIFIED")
    p2 = Payload(3, 3, 1465, TS, 240, 7, "Kierunek: Grodzisk Maz.".encode()).to_bytes()
    v["TV2_evacuation_dual"] = (p2, sign_payload(p2, [(3, keys[3]), (5, keys[5])]), "VERIFIED")
    v["TV3_forged_hacker_as_1"] = (p1, sign_payload(p1, [(1, keys["hacker"])]), "FORGED")
    v["TV4_evacuation_single"] = (p2, sign_payload(p2, [(3, keys[3])]), "INCOMPLETE")
    p5 = Payload(6, 1, 1261, TS, 120, 1, b"").to_bytes()          # Warszawa podpisuje dla Krakowa
    v["TV5_unauthorized_area"] = (p5, sign_payload(p5, [(6, keys[6])]), "FORGED")
    tampered = bytearray(v["TV1_air_raid_single"][1]); tampered[4 + 16] ^= 0x01   # zmiana 1. bajtu notatki
    tampered[-2:] = struct.pack(">H", crc16_ccitt(bytes(tampered[:-2])))
    v["TV6_tampered_note"] = (bytes(tampered[4:4 + len(p1)]), bytes(tampered), "FORGED")
    return keys, root_pub, trust, certs, v


def cmd_vectors(out_dir):
    import os
    os.makedirs(out_dir, exist_ok=True)
    keys, root_pub, trust, certs, v = make_vectors()
    now_ok, now_expired = TS + 600, TS + 3 * 86400
    out = {
        "_info": "SYGNET v1 test vectors. Klucze to seedy testowe - NIE używać produkcyjnie.",
        "now_for_tests": now_ok, "now_expired": now_expired, "user_area": 1465,
        "seeds_hex": {str(k): s.hex() for k, s in SEEDS.items()},
        "root_pub_hex": root_pub.hex(), "root_pub_b64": base64.b64encode(root_pub).decode(),
        "root_fingerprint": fingerprint(root_pub),
        "issuer_pub_hex": {str(k): pub_bytes(keys[k]).hex() for k in ISSUERS},
        "trust_store": {"version": 1, "root_fingerprint": fingerprint(root_pub), "issuers": certs},
        "crc16_check": {"input_ascii": "123456789", "crc_hex": f"{crc16_ccitt(b'123456789'):04X}"},
        "vectors": {},
    }
    for name, (payload, frame, expect) in v.items():
        st, why = verify_frame(frame, root_pub, trust, now_ok, 1465)
        assert st == expect, (name, st, why)
        st2, _ = verify_frame(frame, root_pub, trust, now_expired, 1465)
        x = encode(frame, 48000)
        wav = f"{name}.wav"; write_wav(os.path.join(out_dir, wav), x, 48000)
        out["vectors"][name] = dict(payload_hex=payload.hex(), frame_hex=frame.hex(), frame_len=len(frame),
                                    qr_text=to_qr_text(frame), expected_status=expect, reason=why,
                                    expected_status_3_days_later=st2, wav=wav,
                                    audio_seconds=round(duration_s(len(frame)), 2))
    with open(os.path.join(out_dir, "testvectors.json"), "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=2, ensure_ascii=False)
    print(json.dumps({k: (d["expected_status"], d["reason"], d["frame_len"], d["audio_seconds"])
                      for k, d in out["vectors"].items()}, indent=1, ensure_ascii=False))


def cmd_selftest():
    rng = np.random.default_rng(42)
    keys, root_pub, trust, certs, v = make_vectors()
    frame = v["TV2_evacuation_dual"][1]
    ok = total = 0
    for snr_db in [30, 15, 10, 6]:
        for sr_dec in [48000, 44100]:
            for lead_ms, echo, drift in [(0, False, False), (37, False, False), (500, True, False), (120, True, True)]:
                x = encode(frame, 48000)
                if sr_dec != 48000:   # symulacja innego sample rate mikrofonu (resampling liniowy)
                    n = int(len(x) * sr_dec / 48000)
                    x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)
                x = np.concatenate([np.zeros(int(sr_dec * lead_ms / 1000)), x, np.zeros(sr_dec // 2)])
                if echo:   # pogłos pomieszczenia: odbicia po 15 ms i 40 ms
                    for d_ms, g in [(15, 0.45), (40, 0.25)]:
                        d = int(sr_dec * d_ms / 1000); x[d:] = x[d:] + g * x[:-d].copy()
                if drift:  # różnica zegarów głośnik/mikrofon 0.1%
                    n = int(len(x) * 1.001)
                    x = np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x)
                sig_p = np.mean(x[x != 0] ** 2)
                x = x * 0.5 + rng.normal(0, np.sqrt(sig_p * 0.25 / 10 ** (snr_db / 10)), len(x))
                res = decode(x, sr_dec)
                good = frame in res
                total += 1; ok += good
                print(f"SNR {snr_db:>2} dB  sr {sr_dec}  lead {lead_ms:>3} ms  echo={echo:d} drift={drift:d} -> {'OK ' if good else 'FAIL'}")
    print(f"\n{ok}/{total} OK")
    # crc sanity
    assert crc16_ccitt(b"123456789") == 0x29B1
    return ok == total


def cmd_decode(path):
    keys, root_pub, trust, certs, v = make_vectors()
    x, sr = read_wav(path)
    for f in decode(x, sr):
        p = parse_frame(f).payload
        print(f.hex(), p, verify_frame(f, root_pub, trust, TS + 600, 1465))


if __name__ == "__main__":
    c = sys.argv[1] if len(sys.argv) > 1 else "selftest"
    if c == "selftest": sys.exit(0 if cmd_selftest() else 1)
    if c == "vectors": cmd_vectors(sys.argv[2] if len(sys.argv) > 2 else "testvectors")
    if c == "decode": cmd_decode(sys.argv[2])
