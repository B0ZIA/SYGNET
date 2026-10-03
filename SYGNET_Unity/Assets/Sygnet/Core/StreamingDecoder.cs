using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>
    /// Dekoder na żywo (CLIENT_UNITY.md §4.2): <see cref="Push"/> dostaje kolejne próbki z mikrofonu, a poprawne ramki
    /// wychodzą przez <see cref="FrameDecoded"/>. Detekcja preambuły jest przyrostowa (moce liczone raz na okno) i używa
    /// tej samej logiki co dekoder wsadowy (<see cref="ModemDecoder.ScanPreambles"/>), więc wyniki są identyczne.
    /// Nie jest bezpieczny wątkowo – wołać z jednego wątku.
    /// </summary>
    public sealed class StreamingDecoder
    {
        public const double BufferSeconds = 25;   // > najdłuższa ramka (400 B ≈ 20,7 s)
        public const double DedupSeconds = 30;    // nadawca powtarza ramkę – identyczne bajty w tym oknie pomijamy
        const double ScanSeconds = 1.5;           // ile ostatnich okien detekcji skanujemy po każdej porcji
        const double MinPreambleGapSeconds = 0.6; // dwie preambuły nie mogą być bliżej (ta sama wykryta ponownie)

        public readonly ModemDecoder Decoder;
        public int SampleRate => Decoder.SampleRate;

        /// <summary>Poprawna ramka (CRC OK), jeszcze bez weryfikacji podpisu.</summary>
        public event Action<byte[]> FrameDecoded;

        /// <summary>
        /// Diagnostyka: preambuła i nagłówek odebrane, ale CRC całej ramki się nie zgadza. Dostaje kopię nagrania
        /// od ~0,6 s przed preambułą do teraz (do analizy offline).
        /// </summary>
        public event Action<float[]> FrameFailed;
        public int FramesFailed { get; private set; }

        /// <summary>Ramki uratowane łączeniem powtórzeń (żadna kopia osobno nie przeszła CRC).</summary>
        public int FramesCombined { get; private set; }

        readonly RepetitionCombiner combiner;

        // ── bufor próbek: buf[0] to próbka o numerze bufStart od początku nasłuchu ──
        readonly float[] buf;
        long bufStart;
        int bufLen;
        public long TotalSamples => bufStart + bufLen;

        // ── moce detekcji: ra[i], rb[i] dla okna o numerze wBase + i ──
        readonly double[] ra, rb;
        long wBase, nextWindow;
        int wCount;
        readonly int scanWindows, minGapWindows;
        long lastOnset = long.MinValue;

        sealed class Job
        {
            public long T0;            // przybliżony początek pierwszego symbolu (z preambuły)
            public long Start = -1;    // po dostrojeniu
            public int FrameLen = -1;  // pole frame_len
        }

        readonly List<Job> jobs = new List<Job>();
        readonly List<(byte[] frame, long at)> recent = new List<(byte[] frame, long at)>();

        // ── wskaźniki dla UI / panelu debug ──
        public float Rms { get; private set; }
        public double PreambleA { get; private set; }     // P(1000 Hz) ostatniego okna
        public double PreambleB { get; private set; }     // P(5200 Hz) ostatniego okna
        public readonly double[] Spectrum;                // znormalizowane moce pasm do animacji
        readonly ToneBank spectrumBank;

        /// <summary>Postęp odbieranej ramki 0..1; -1, gdy nic nie odbieramy.</summary>
        public double Progress { get; private set; } = -1;

        /// <summary>Długość odbieranej ramki w bajtach (4 + frame_len) albo -1.</summary>
        public int ReceivingBytes { get; private set; } = -1;

        public int FramesDecoded { get; private set; }
        public int PreamblesDetected { get; private set; }

        public StreamingDecoder(int sampleRate)
        {
            Decoder = new ModemDecoder(sampleRate);
            combiner = new RepetitionCombiner((long)(DedupSeconds * sampleRate));
            buf = new float[(int)(BufferSeconds * sampleRate)];
            scanWindows = (int)(ScanSeconds * sampleRate / Decoder.DetHop);
            minGapWindows = (int)(MinPreambleGapSeconds * sampleRate / Decoder.DetHop);
            ra = new double[scanWindows * 4];
            rb = new double[scanWindows * 4];

            var freqs = new double[24];
            for (int i = 0; i < freqs.Length; i++) freqs[i] = 800 + i * 200;   // 800..5400 Hz: preambuła i dane
            spectrumBank = new ToneBank(sampleRate, Math.Min(1024, sampleRate / 40), freqs);
            Spectrum = new double[freqs.Length];
        }

        public void Push(float[] samples) => Push(samples, 0, samples.Length);

        public void Push(float[] samples, int offset, int count)
        {
            // Długie porcje (np. zaległe próbki po przycięciu aplikacji) dzielimy na kawałki ≤ 0,5 s,
            // żeby każde nowe okno detekcji trafiło do skanu razem z ≥ 1 s historii (pełna preambuła).
            int maxChunk = SampleRate / 2;
            while (count > 0)
            {
                int n = Math.Min(count, maxChunk);
                PushChunk(samples, offset, n);
                offset += n;
                count -= n;
            }
        }

        void PushChunk(float[] samples, int offset, int count)
        {
            Append(samples, offset, count);
            UpdateLevels(samples, offset, count);
            if (ComputeWindows()) Scan();
            ProcessJobs();
        }

        /// <summary>Porzuca odbierane ramki i stan detekcji (np. po własnym „Przekaż dalej”). Zachowuje pamięć ostatnich ramek.</summary>
        public void Reset()
        {
            jobs.Clear();
            wCount = 0;
            wBase = nextWindow = (TotalSamples + Decoder.DetHop - 1) / Decoder.DetHop;
            lastOnset = long.MinValue;
            Progress = -1;
            ReceivingBytes = -1;
        }

        // ───────────── bufor ─────────────

        void Append(float[] samples, int offset, int count)
        {
            if (count >= buf.Length)
            {
                // porcja dłuższa niż bufor – zostaw tylko końcówkę
                bufStart += bufLen + count - buf.Length;
                Array.Copy(samples, offset + count - buf.Length, buf, 0, buf.Length);
                bufLen = buf.Length;
                return;
            }
            if (bufLen + count > buf.Length)
            {
                // usuń najstarsze próbki z zapasem 2 s, żeby nie przesuwać bufora przy każdej porcji
                int drop = Math.Min(bufLen, bufLen + count - buf.Length + 2 * SampleRate);
                Array.Copy(buf, drop, buf, 0, bufLen - drop);
                bufLen -= drop;
                bufStart += drop;
            }
            Array.Copy(samples, offset, buf, bufLen, count);
            bufLen += count;
        }

        void UpdateLevels(float[] s, int offset, int count)
        {
            double e = 0;
            for (int i = offset; i < offset + count; i++) e += s[i] * s[i];
            Rms = (float)Math.Sqrt(e / count);
            // widmo tylko do animacji – najwyżej co pół okna, a nie przy każdej (nawet 1-próbkowej) porcji
            int n = spectrumBank.WindowLength;
            if (bufLen >= n && TotalSamples - lastSpectrumAt >= n / 2)
            {
                spectrumBank.Powers(buf, bufLen - n, Spectrum);
                lastSpectrumAt = TotalSamples;
            }
        }

        long lastSpectrumAt;

        // ───────────── preambuła ─────────────

        /// <summary>Liczy moce dla nowych okien detekcji; true, jeśli przybyło choć jedno.</summary>
        bool ComputeWindows()
        {
            long before = nextWindow;
            int hop = Decoder.DetHop, win = Decoder.DetWindow;
            long first = (bufStart + hop - 1) / hop;                   // okna, które mieszczą się w buforze
            if (nextWindow < first)
            {
                nextWindow = first;
                wBase = first;
                wCount = 0;
            }
            while (nextWindow * hop + win <= TotalSamples)
            {
                if (wCount == ra.Length)
                {
                    // zachowaj ostatnie 2× zakres skanu
                    int keep = 2 * scanWindows;
                    Array.Copy(ra, wCount - keep, ra, 0, keep);
                    Array.Copy(rb, wCount - keep, rb, 0, keep);
                    wBase += wCount - keep;
                    wCount = keep;
                }
                int local = (int)(nextWindow * hop - bufStart);
                Decoder.DetectionPowersAt(buf, local, out ra[wCount], out rb[wCount]);
                PreambleA = ra[wCount];
                PreambleB = rb[wCount];
                wCount++;
                nextWindow++;
            }
            return nextWindow != before;
        }

        void Scan()
        {
            int from = Math.Max(0, wCount - scanWindows);
            foreach (var m in ModemDecoder.ScanPreambles(ra, rb, from, wCount, Decoder.DetMinWindows))
            {
                long onset = wBase + m;
                if (onset < lastOnset + minGapWindows) continue;        // ta sama preambuła z poprzedniego skanu
                lastOnset = onset;
                PreamblesDetected++;
                jobs.Add(new Job { T0 = onset * Decoder.DetHop + Decoder.DetWindow / 2 + Decoder.PreambleToData });
            }
        }

        // ───────────── dekodowanie ramek ─────────────

        void ProcessJobs()
        {
            double progress = -1;
            int receiving = -1;
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                if (j.T0 - Decoder.SyncMargin < bufStart)
                {
                    jobs.RemoveAt(i);                                   // wypadło z bufora
                    continue;
                }
                if (j.FrameLen < 0)
                {
                    // nagłówek (4 symbole) + zakres dostrajania
                    if (TotalSamples < j.T0 + Decoder.SyncMargin + Decoder.SamplesNeeded(0)) { progress = Math.Max(progress, 0); continue; }
                    int t = Decoder.RefineStart(buf, bufLen, (int)(j.T0 - bufStart));
                    int flen = t < 0 ? -1 : Decoder.ReadFrameLen(buf, bufLen, t);
                    if (flen < 0)
                    {
                        jobs.RemoveAt(i);                               // fałszywa preambuła albo zakłócony nagłówek
                        continue;
                    }
                    j.Start = bufStart + t;
                    j.FrameLen = flen;
                }

                long needed = Decoder.SamplesNeeded(j.FrameLen);
                long have = TotalSamples - j.Start;
                if (have < needed)
                {
                    double p = Math.Max(0, (double)have / needed);
                    if (p > progress)
                    {
                        progress = p;
                        receiving = 4 + j.FrameLen;
                    }
                    continue;
                }

                jobs.RemoveAt(i);
                int symbols = 4 + j.FrameLen;
                var powers = Decoder.FramePowers(buf, bufLen, (int)(j.Start - bufStart), symbols);
                var frame = powers == null ? null : ModemDecoder.Decide(powers, symbols);
                if (ModemDecoder.CrcOk(frame))
                {
                    Emit(frame, j.Start);
                }
                else if (powers != null && (frame = combiner.AddFailed(powers, symbols, j.Start)) != null)
                {
                    FramesFailed++;
                    FramesCombined++;
                    Emit(frame, j.Start);                               // uratowana z sumy powtórzeń
                }
                else
                {
                    FramesFailed++;
                    if (FrameFailed != null)
                    {
                        int from = (int)Math.Max(0, j.Start - bufStart - Decoder.PreambleToData - SampleRate * 6 / 10);
                        var snap = new float[bufLen - from];
                        Array.Copy(buf, from, snap, 0, snap.Length);
                        FrameFailed(snap);
                    }
                }
            }
            Progress = progress;
            ReceivingBytes = receiving;
        }

        void Emit(byte[] frame, long at)
        {
            long horizon = at - (long)(DedupSeconds * SampleRate);
            recent.RemoveAll(r => r.at < horizon);
            foreach (var r in recent)
                if (Bytes.SequenceEqual(r.frame, frame)) return;
            recent.Add((frame, at));
            FramesDecoded++;
            FrameDecoded?.Invoke(frame);
        }
    }
}
