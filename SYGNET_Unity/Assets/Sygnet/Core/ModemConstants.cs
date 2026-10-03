using System;

namespace Sygnet.Core
{
    /// <summary>
    /// WSZYSTKIE stałe modemu dual-tone 16-FSK (PROTOCOL.md §8.1 + parametry dekodera z tools/sygnet_ref.py).
    /// Nie zmieniać bez uzgodnienia z konsolą – te same wartości są w modemie JS i w implementacji referencyjnej.
    /// </summary>
    public static class ModemConstants
    {
        // ── nadajnik ──
        public const double BandABase = 1500.0;   // górne 4 bity: 1500 + 100·n Hz
        public const double BandBBase = 3200.0;   // dolne 4 bity: 3200 + 100·n Hz
        public const double ToneStep = 100.0;
        public const int SymbolMs = 40;           // ton
        public const int GapMs = 10;              // cisza → okres symbolu 50 ms = 1 bajt
        public const int SymbolPeriodMs = SymbolMs + GapMs;
        public const double PreambleAHz = 1000.0;
        public const double PreambleBHz = 5200.0; // także znacznik końca
        public const int PreambleToneMs = 200;
        public const int PreambleGapMs = 50;
        public const int EndToneMs = 200;
        public const int DefaultRepeat = 2;
        public const int RepeatGapMs = 500;
        public const double ToneAmp = 0.4;        // każdy z 2 tonów danych
        public const double MarkerAmp = 0.6;      // preambuła i koniec
        public const int FadeMs = 5;              // raised cosine na obu końcach tonu

        // ── odbiornik ──
        public const double DetWindowMs = 10.0;
        public const double DetHopMs = 2.5;
        public const double DetRatio = 0.5;       // próg znormalizowanej mocy P(f)
        public const int DetMinMs = 120;          // min. długość tonu preambuły i maks. odstęp A→B
        public const int AnalyzeFromMs = 6;       // okno analizy symbolu [6, 34] ms od jego początku
        public const int AnalyzeToMs = 34;
        public const int SyncSearchMs = 12;       // dostrojenie t0: −12..+12 ms
        public const int SyncStepMs = 1;
        public const int MaxFrameLen = 400;       // pole frame_len większe niż to → odrzuć

        public static readonly double[] BandA = Band(BandABase);
        public static readonly double[] BandB = Band(BandBBase);

        /// <summary>Długość odcinka w próbkach: round(sr·ms/1000), zaokrąglenie jak Python (do parzystej).</summary>
        public static int Samples(int sampleRate, double ms) => (int)Math.Round(sampleRate * ms / 1000.0);

        /// <summary>Jak int(sr·ms/1000) w dekoderze referencyjnym – obcięcie w stronę zera.</summary>
        public static int SamplesTrunc(int sampleRate, double ms) => (int)(sampleRate * ms / 1000.0);

        /// <summary>Czas nadawania ramki: R·(0,5 s + 0,05 s·bajty + 0,2 s) + (R−1)·0,5 s.</summary>
        public static double DurationSeconds(int frameBytes, int repeat = DefaultRepeat)
        {
            double once = (2 * PreambleToneMs + 2 * PreambleGapMs + frameBytes * SymbolPeriodMs + EndToneMs) / 1000.0;
            return repeat * once + (repeat - 1) * RepeatGapMs / 1000.0;
        }

        static double[] Band(double baseHz)
        {
            var f = new double[16];
            for (int i = 0; i < 16; i++) f[i] = baseHz + i * ToneStep;
            return f;
        }
    }
}
