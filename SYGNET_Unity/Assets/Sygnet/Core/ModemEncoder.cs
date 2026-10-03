using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>
    /// byte[] ramki → próbki audio (PROTOCOL.md §8.2). Kolejność: PREAMBUŁA → bajty od MAGIC → ZNACZNIK KOŃCA,
    /// całość R razy z 500 ms ciszy pomiędzy. Próbki są identyczne z encode() z tools/sygnet_ref.py.
    /// </summary>
    public static class ModemEncoder
    {
        public static float[] Encode(byte[] frame, int sampleRate, int repeat = ModemConstants.DefaultRepeat)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (repeat < 1) throw new ArgumentOutOfRangeException(nameof(repeat));

            var once = EncodeOnce(frame, sampleRate);
            int gap = ModemConstants.Samples(sampleRate, ModemConstants.RepeatGapMs);
            var output = new float[repeat * once.Length + (repeat - 1) * gap];
            for (int r = 0; r < repeat; r++)
                Array.Copy(once, 0, output, r * (once.Length + gap), once.Length);
            return output;
        }

        static float[] EncodeOnce(byte[] frame, int sr)
        {
            var parts = new List<float[]>(frame.Length * 2 + 5)
            {
                Tone(sr, ModemConstants.PreambleToneMs, ModemConstants.MarkerAmp, ModemConstants.PreambleAHz),
                Silence(sr, ModemConstants.PreambleGapMs),
                Tone(sr, ModemConstants.PreambleToneMs, ModemConstants.MarkerAmp, ModemConstants.PreambleBHz),
                Silence(sr, ModemConstants.PreambleGapMs),
            };
            var symbols = new Dictionary<byte, float[]>();
            var gap = Silence(sr, ModemConstants.GapMs);
            foreach (var b in frame)
            {
                if (!symbols.TryGetValue(b, out var tone))
                {
                    tone = Tone(sr, ModemConstants.SymbolMs, ModemConstants.ToneAmp,
                        ModemConstants.BandA[b >> 4], ModemConstants.BandB[b & 0x0F]);
                    symbols[b] = tone;
                }
                parts.Add(tone);
                parts.Add(gap);
            }
            parts.Add(Tone(sr, ModemConstants.EndToneMs, ModemConstants.MarkerAmp, ModemConstants.PreambleBHz));

            int total = 0;
            foreach (var p in parts) total += p.Length;
            var x = new float[total];
            int pos = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, x, pos, p.Length);
                pos += p.Length;
            }
            return x;
        }

        /// <summary>s[i] = Σ amp·sin(2π f i / sr), z obwiednią raised cosine 5 ms na obu końcach.</summary>
        static float[] Tone(int sr, int ms, double amp, params double[] freqs)
        {
            int n = ModemConstants.Samples(sr, ms);
            var s = new double[n];
            foreach (var f in freqs)
            {
                double w = 2 * Math.PI * f / sr;
                for (int i = 0; i < n; i++) s[i] += amp * Math.Sin(w * i);
            }
            int fade = ModemConstants.Samples(sr, ModemConstants.FadeMs);
            if (fade > 0 && n > 2 * fade)
            {
                for (int i = 0; i < fade; i++)
                {
                    double g = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
                    s[i] *= g;
                    s[n - 1 - i] *= g;
                }
            }
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = (float)s[i];
            return o;
        }

        static float[] Silence(int sr, int ms) => new float[ModemConstants.Samples(sr, ms)];
    }
}
