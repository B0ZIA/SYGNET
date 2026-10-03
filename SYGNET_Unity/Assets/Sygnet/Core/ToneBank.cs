using System;

namespace Sygnet.Core
{
    /// <summary>
    /// Znormalizowana moc kilku częstotliwości w oknie stałej długości (DFT pojedynczych częstotliwości,
    /// równoważne Goertzelowi): P(f) = 2·|X(f)|² / (N·Σx²). Czysty ton ≈ 1,0 niezależnie od głośności.
    /// Tablice sin/cos liczone raz, więc analiza okna to tylko mnożenia i dodawania.
    /// </summary>
    public sealed class ToneBank
    {
        public readonly int WindowLength;
        public readonly double[] Frequencies;
        readonly float[][] cos;
        readonly float[][] sin;

        public ToneBank(int sampleRate, int windowLength, params double[] frequencies)
        {
            if (windowLength <= 0) throw new ArgumentOutOfRangeException(nameof(windowLength));
            WindowLength = windowLength;
            Frequencies = frequencies;
            cos = new float[frequencies.Length][];
            sin = new float[frequencies.Length][];
            for (int k = 0; k < frequencies.Length; k++)
            {
                cos[k] = new float[windowLength];
                sin[k] = new float[windowLength];
                double w = 2 * Math.PI * frequencies[k] / sampleRate;
                for (int i = 0; i < windowLength; i++)
                {
                    cos[k][i] = (float)Math.Cos(w * i);
                    sin[k][i] = (float)Math.Sin(w * i);
                }
            }
        }

        /// <summary>Wypełnia <paramref name="powers"/> mocami dla okna x[start .. start+WindowLength). Cisza → zera.</summary>
        public void Powers(float[] x, int start, double[] powers)
        {
            int n = WindowLength;
            double e = 0;
            for (int i = 0; i < n; i++)
            {
                double v = x[start + i];
                e += v * v;
            }
            if (e <= 1e-12)
            {
                Array.Clear(powers, 0, Frequencies.Length);
                return;
            }
            double norm = 2.0 / (n * e);
            for (int k = 0; k < Frequencies.Length; k++)
            {
                var ck = cos[k];
                var sk = sin[k];
                double c = 0, s = 0;
                for (int i = 0; i < n; i++)
                {
                    double v = x[start + i];
                    c += v * ck[i];
                    s += v * sk[i];
                }
                powers[k] = (c * c + s * s) * norm;
            }
        }
    }
}
