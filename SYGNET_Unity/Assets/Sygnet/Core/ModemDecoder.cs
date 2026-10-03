using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>
    /// Dekoder modemu (PROTOCOL.md §8.3), odwzorowanie find_preambles/_decode_at/decode z tools/sygnet_ref.py.
    /// Działa dla dowolnego sample rate, bo liczy po częstotliwościach. Instancja trzyma tablice dla jednego
    /// sample rate i nie jest bezpieczna wątkowo (bufory robocze).
    /// </summary>
    public sealed class ModemDecoder
    {
        public readonly int SampleRate;
        public readonly int DetWindow;        // 10 ms
        public readonly int DetHop;           // 2,5 ms
        public readonly int DetMinWindows;    // 120 ms w krokach detekcji
        public readonly int SymbolPeriod;     // 50 ms
        readonly int preambleToData;          // od narastającego zbocza 5200 Hz do pierwszego symbolu: 200 + 50 ms
        readonly int analyzeFrom, analyzeTo;  // [6, 34] ms od początku symbolu

        readonly ToneBank detBank;            // 1000 Hz, 5200 Hz
        readonly ToneBank bankA, bankB;       // 16 + 16 tonów danych
        readonly double[] detPow = new double[2];
        readonly double[] powA = new double[16];
        readonly double[] powB = new double[16];

        public ModemDecoder(int sampleRate)
        {
            if (sampleRate < 11025) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Za niski sample rate dla 5200 Hz");
            SampleRate = sampleRate;
            DetWindow = ModemConstants.SamplesTrunc(sampleRate, ModemConstants.DetWindowMs);
            DetHop = ModemConstants.SamplesTrunc(sampleRate, ModemConstants.DetHopMs);
            DetMinWindows = (int)(ModemConstants.DetMinMs / ModemConstants.DetHopMs);
            SymbolPeriod = ModemConstants.Samples(sampleRate, ModemConstants.SymbolPeriodMs);
            preambleToData = ModemConstants.SamplesTrunc(sampleRate, ModemConstants.PreambleToneMs + ModemConstants.PreambleGapMs);
            analyzeFrom = ModemConstants.SamplesTrunc(sampleRate, ModemConstants.AnalyzeFromMs);
            analyzeTo = ModemConstants.SamplesTrunc(sampleRate, ModemConstants.AnalyzeToMs);

            detBank = new ToneBank(sampleRate, DetWindow, ModemConstants.PreambleAHz, ModemConstants.PreambleBHz);
            bankA = new ToneBank(sampleRate, analyzeTo - analyzeFrom, ModemConstants.BandA);
            bankB = new ToneBank(sampleRate, analyzeTo - analyzeFrom, ModemConstants.BandB);
        }

        /// <summary>Dekoduje cały bufor. Zwraca unikalne ramki z poprawnym CRC (powtórzenia nadawcy są scalane).</summary>
        public static List<byte[]> Decode(float[] samples, int sampleRate) => new ModemDecoder(sampleRate).DecodeAll(samples, samples.Length);

        /// <summary>Jak <see cref="Decode"/>, dla pierwszych <paramref name="length"/> próbek bufora.</summary>
        public List<byte[]> DecodeAll(float[] x, int length)
        {
            var output = new List<byte[]>();
            foreach (var t0 in FindPreambles(x, length))
            {
                var f = DecodeAt(x, length, t0);
                if (f == null) continue;
                bool dup = false;
                foreach (var o in output)
                    if (Bytes.SequenceEqual(o, f)) { dup = true; break; }
                if (!dup) output.Add(f);
            }
            return output;
        }

        // ───────────── preambuła ─────────────

        /// <summary>Liczba okien detekcji dla bufora o długości <paramref name="length"/>: (len − win) / hop.</summary>
        public int DetectionWindowCount(int length) => length < DetWindow ? 0 : (length - DetWindow) / DetHop;

        /// <summary>P(1000 Hz) i P(5200 Hz) dla okna detekcji o indeksie <paramref name="k"/> (start = k·hop).</summary>
        public void DetectionPowers(float[] x, int k, out double pA, out double pB)
        {
            detBank.Powers(x, k * DetHop, detPow);
            pA = detPow[0];
            pB = detPow[1];
        }

        /// <summary>Indeksy próbek t0 (początek pierwszego symbolu danych) dla każdej znalezionej preambuły.</summary>
        public List<int> FindPreambles(float[] x, int length)
        {
            int n = DetectionWindowCount(length);
            var ra = new double[n];
            var rb = new double[n];
            for (int k = 0; k < n; k++) DetectionPowers(x, k, out ra[k], out rb[k]);
            var starts = new List<int>();
            foreach (var m in ScanPreambles(ra, rb, 0, n, DetMinWindows)) starts.Add(StartFromOnsetWindow(m));
            return starts;
        }

        /// <summary>t0 z indeksu okna, w którym zaczyna się ton 5200 Hz (synchronizacja od NARASTAJĄCEGO zbocza).</summary>
        public int StartFromOnsetWindow(int m) => m * DetHop + DetWindow / 2 + preambleToData;

        /// <summary>
        /// Szuka ≥ need okien z P(1000) &gt; 0,5, po których w ciągu ≤ need okien zaczyna się ≥ need okien z P(5200) &gt; 0,5.
        /// Zwraca indeksy okien narastającego zbocza 5200 Hz. Logika 1:1 z find_preambles.
        /// </summary>
        public static List<int> ScanPreambles(double[] ra, double[] rb, int from, int n, int need)
        {
            const double r = ModemConstants.DetRatio;
            var onsets = new List<int>();
            int k = from;
            while (k < n)
            {
                if (ra[k] > r)
                {
                    int j = k;
                    while (j < n && ra[j] > r) j++;
                    if (j - k >= need)
                    {
                        int m = j;
                        while (m < n && m < j + need && rb[m] <= r) m++;
                        if (m < n && rb[m] > r)
                        {
                            int e = m;
                            while (e < n && rb[e] > r) e++;
                            if (e - m >= need)
                            {
                                onsets.Add(m);
                                k = e;
                                continue;
                            }
                        }
                    }
                    k = j;
                }
                k++;
            }
            return onsets;
        }

        // ───────────── symbole ─────────────

        /// <summary>Symbol zaczynający się w próbce <paramref name="start"/>; false, gdy okno analizy wychodzi poza bufor.</summary>
        public bool TrySymbol(float[] x, int length, int start, out byte value, out double confidence)
        {
            int a0 = start + analyzeFrom;
            int a1 = start + analyzeTo;
            if (a0 < 0 || a1 > length)
            {
                value = 0;
                confidence = 0;
                return false;
            }
            bankA.Powers(x, a0, powA);
            bankB.Powers(x, a0, powB);
            int ia = ArgMax(powA), ib = ArgMax(powB);
            value = (byte)((ia << 4) | ib);
            confidence = powA[ia] + powB[ib];
            return true;
        }

        /// <summary>
        /// Dostrojenie t0 o −12..+12 ms: wybiera przesunięcie, dla którego 2 pierwsze bajty to "SG", a suma pewności
        /// 4 pierwszych symboli jest największa. Zwraca dostrojony początek albo -1.
        /// </summary>
        public int RefineStart(float[] x, int length, int t0)
        {
            int best = -1;
            double bestConf = -1;
            for (int offMs = -ModemConstants.SyncSearchMs; offMs <= ModemConstants.SyncSearchMs; offMs += ModemConstants.SyncStepMs)
            {
                int t = t0 + (int)(SampleRate * (double)offMs / 1000.0);
                double conf = 0;
                byte b0 = 0, b1 = 0;
                bool complete = true;
                for (int k = 0; k < 4; k++)
                {
                    if (!TrySymbol(x, length, t + k * SymbolPeriod, out var b, out var c))
                    {
                        complete = false;
                        break;
                    }
                    if (k == 0) b0 = b;
                    if (k == 1) b1 = b;
                    conf += c;
                }
                if (complete && b0 == Frame.Magic0 && b1 == Frame.Magic1 && conf > bestConf)
                {
                    best = t;
                    bestConf = conf;
                }
            }
            return best;
        }

        /// <summary>Pole frame_len z symboli 2–3 (po dostrojeniu); -1, gdy brak danych albo &gt; 400.</summary>
        public int ReadFrameLen(float[] x, int length, int t)
        {
            if (!TrySymbol(x, length, t + 2 * SymbolPeriod, out var hi, out _)) return -1;
            if (!TrySymbol(x, length, t + 3 * SymbolPeriod, out var lo, out _)) return -1;
            int flen = (hi << 8) | lo;
            return flen > ModemConstants.MaxFrameLen ? -1 : flen;
        }

        /// <summary>Liczba próbek od dostrojonego t potrzebna do zdekodowania całej ramki o danym frame_len.</summary>
        public int SamplesNeeded(int frameLen) => (4 + frameLen - 1) * SymbolPeriod + analyzeTo;

        /// <summary>Dekoduje ramkę od t0 (z dostrojeniem). Zwraca bajty z poprawnym CRC albo null.</summary>
        public byte[] DecodeAt(float[] x, int length, int t0)
        {
            int t = RefineStart(x, length, t0);
            if (t < 0) return null;
            int flen = ReadFrameLen(x, length, t);
            if (flen < 0) return null;
            var frame = new byte[4 + flen];
            for (int k = 0; k < frame.Length; k++)
            {
                if (!TrySymbol(x, length, t + k * SymbolPeriod, out frame[k], out _)) return null;
            }
            if (frame.Length < 6) return null;
            int crcPos = frame.Length - 2;
            return Crc16.Compute(frame, 0, crcPos) == Bytes.ReadU16(frame, crcPos) ? frame : null;
        }

        static int ArgMax(double[] a)
        {
            int best = 0;
            for (int i = 1; i < a.Length; i++)
                if (a[i] > a[best]) best = i;
            return best;
        }
    }
}
