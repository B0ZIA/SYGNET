using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sygnet.Core;

namespace Sygnet.Tests
{
    /// <summary>Modem dźwiękowy (PROTOCOL.md §8, CLIENT_UNITY.md §6): WAV-y referencyjne, pętla zwrotna, szum, pogłos.</summary>
    public class ModemTests
    {
        static IEnumerable<string> Vectors => TestData.VectorNames;
        static byte[] TV1 => TestData.Get("TV1_air_raid_single").Frame;
        static byte[] TV2 => TestData.Get("TV2_evacuation_dual").Frame;

        // ── WAV-y referencyjne (wygenerowane przez tools/sygnet_ref.py) ──

        [TestCaseSource(nameof(Vectors))]
        public void Decode_ReferenceWav_GivesExactFrame(string name)
        {
            var v = TestData.Get(name);
            var x = TestData.ReadWav(v, out int sr);
            Assert.AreEqual(48000, sr);
            var frames = ModemDecoder.Decode(x, sr);
            Assert.AreEqual(1, frames.Count, "powtórzenia ramki powinny zostać scalone");
            Assert.AreEqual(Bytes.ToHex(v.Frame), Bytes.ToHex(frames[0]));
        }

        [Test]
        public void Decode_ReferenceWav_FindsBothRepetitions()
        {
            var x = TestData.ReadWav(TestData.Get("TV1_air_raid_single"), out int sr);
            var dec = new ModemDecoder(sr);
            var starts = dec.FindPreambles(x, x.Length);
            Assert.AreEqual(2, starts.Count);
            foreach (var t0 in starts) CollectionAssert.AreEqual(TV1, dec.DecodeAt(x, x.Length, t0));
        }

        [TestCaseSource(nameof(Vectors))]
        public void Encoder_MatchesReferenceWav(string name)
        {
            var v = TestData.Get(name);
            var wav = TestData.ReadWav(v, out int sr);
            var y = ModemEncoder.Encode(v.Frame, sr);
            Assert.AreEqual(wav.Length, y.Length, "liczba próbek");
            int worst = 0;
            for (int i = 0; i < y.Length; i++)
            {
                int expected = (int)Math.Round(wav[i] * 32768.0);
                int actual = (short)(Math.Max(-1f, Math.Min(1f, y[i])) * 32767);
                worst = Math.Max(worst, Math.Abs(expected - actual));
            }
            Assert.LessOrEqual(worst, 1, "różnica w LSB 16-bit");
        }

        [Test]
        public void Duration_MatchesReference()
        {
            Assert.AreEqual(13.7, ModemConstants.DurationSeconds(118), 1e-9);
            Assert.AreEqual(ModemConstants.DurationSeconds(118) * 48000, ModemEncoder.Encode(TV1, 48000).Length, 1.0);
        }

        // ── pętla zwrotna ──

        [TestCase(48000)]
        [TestCase(44100)]
        [TestCase(24000)]
        [TestCase(22050)]
        [TestCase(16000)]
        public void Loopback_Clean(int sr)
        {
            var x = Pad(ModemEncoder.Encode(TV2, sr, repeat: 1), sr, leadMs: 500, tailMs: 500);
            var frames = ModemDecoder.Decode(x, sr);
            Assert.AreEqual(1, frames.Count);
            CollectionAssert.AreEqual(TV2, frames[0]);
        }

        [Test]
        public void Loopback_TwoDifferentFramesInOneRecording()
        {
            const int sr = 48000;
            var tv5 = TestData.Get("TV5_unauthorized_area").Frame;
            var x = ModemEncoder.Encode(TV1, sr, 1).Concat(new float[sr]).Concat(ModemEncoder.Encode(tv5, sr, 1)).ToArray();
            var frames = ModemDecoder.Decode(x, sr);
            Assert.AreEqual(2, frames.Count);
            CollectionAssert.AreEqual(TV1, frames[0]);
            CollectionAssert.AreEqual(tv5, frames[1]);
        }

        [Test]
        public void Decode_NoiseOnly_GivesNothing()
        {
            var rng = new Random(7);
            var x = new float[48000 * 3];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(0.3 * Gauss(rng));
            CollectionAssert.IsEmpty(ModemDecoder.Decode(x, 48000));
        }

        [Test]
        public void Decode_CorruptedSymbol_IsRejectedByCrc()
        {
            const int sr = 48000;
            var x = ModemEncoder.Encode(TV1, sr, 1);
            Assert.AreEqual(1, ModemDecoder.Decode(x, sr).Count);
            // Podmień symbol nr 30 na ton innego bajtu. Dane zaczynają się po preambule 200+50+200+50 ms.
            int dataStart = ModemConstants.Samples(sr, 2 * (ModemConstants.PreambleToneMs + ModemConstants.PreambleGapMs));
            int period = ModemConstants.Samples(sr, ModemConstants.SymbolPeriodMs);
            var other = ModemEncoder.Encode(new[] { (byte)(TV1[30] ^ 0x5A) }, sr, 1);
            Array.Copy(other, dataStart, x, dataStart + 30 * period, period);
            CollectionAssert.IsEmpty(ModemDecoder.Decode(x, sr));
        }

        // ── macierz selftest z sygnet_ref.py: szum, inny sample rate, przesunięcie, pogłos, dryf zegara ──

        static IEnumerable<TestCaseData> SelfTestMatrix()
        {
            foreach (var snr in new[] { 30, 15, 10, 6 })
            foreach (var sr in new[] { 48000, 44100 })
            foreach (var (lead, echo, drift) in new[] { (0, false, false), (37, false, false), (500, true, false), (120, true, true) })
                yield return new TestCaseData(snr, sr, lead, echo, drift)
                    .SetName($"SelfTest_SNR{snr}dB_{sr}Hz_lead{lead}ms{(echo ? "_echo" : "")}{(drift ? "_drift" : "")}");
        }

        [TestCaseSource(nameof(SelfTestMatrix))]
        public void SelfTest(int snrDb, int srDec, int leadMs, bool echo, bool drift)
        {
            var rng = new Random(snrDb * 1000 + srDec / 100 + leadMs);
            var x = ModemEncoder.Encode(TV2, 48000).Select(v => (double)v).ToArray();
            if (srDec != 48000) x = Resample(x, (int)(x.Length * (long)srDec / 48000));   // inny sample rate mikrofonu
            x = new double[srDec * leadMs / 1000].Concat(x).Concat(new double[srDec / 2]).ToArray();
            if (echo) Echo(x, srDec);                                                         // pogłos pomieszczenia
            if (drift) x = Resample(x, (int)(x.Length * 1.001));                              // zegary różnią się o 0,1%
            AddNoise(x, snrDb, rng);

            var frames = ModemDecoder.Decode(x.Select(v => (float)v).ToArray(), srDec);
            Assert.IsTrue(frames.Any(f => f.SequenceEqual(TV2)), "nie zdekodowano TV2 (ramek: " + frames.Count + ")");
        }

        /// <summary>„Przekaż dalej”: telefon A gra przy 24 kHz (wyjście Pixela), telefon B nagrywa 48 kHz, szum 10 dB, pogłos.</summary>
        [Test]
        public void Relay_24kPlayback_48kRecording_Noisy()
        {
            var rng = new Random(2024);
            var x = ModemEncoder.Encode(TV1, 24000, repeat: 1).Select(v => (double)v).ToArray();
            x = Resample(x, x.Length * 2);
            x = new double[48000 / 3].Concat(x).Concat(new double[24000]).ToArray();
            Echo(x, 48000);
            AddNoise(x, 10, rng);
            var frames = ModemDecoder.Decode(x.Select(v => (float)v).ToArray(), 48000);
            Assert.AreEqual(1, frames.Count);
            CollectionAssert.AreEqual(TV1, frames[0]);
        }

        // ── WAV ──

        [Test]
        public void Wav_WriteRead_RoundTrip()
        {
            var y = ModemEncoder.Encode(TestData.Get("TV5_unauthorized_area").Frame, 44100, 1);
            var back = Wav.Read(Wav.Write16(y, 44100), out int sr);
            Assert.AreEqual(44100, sr);
            Assert.AreEqual(y.Length, back.Length);
            for (int i = 0; i < y.Length; i++) Assert.AreEqual(y[i], back[i], 1.0 / 16000);
            CollectionAssert.AreEqual(TestData.Get("TV5_unauthorized_area").Frame, ModemDecoder.Decode(back, sr).Single());
        }

        [Test]
        public void Wav_RejectsGarbage()
        {
            Assert.Throws<FormatException>(() => Wav.Read(new byte[100], out _));
            Assert.Throws<FormatException>(() => Wav.Read(null, out _));
        }

        // ── pomocnicze (odpowiedniki numpy z sygnet_ref.py) ──

        static float[] Pad(float[] x, int sr, int leadMs, int tailMs) =>
            new float[sr * leadMs / 1000].Concat(x).Concat(new float[sr * tailMs / 1000]).ToArray();

        /// <summary>np.interp(np.linspace(0, len−1, n), np.arange(len), x)</summary>
        static double[] Resample(double[] x, int n)
        {
            var y = new double[n];
            double step = (double)(x.Length - 1) / (n - 1);
            for (int i = 0; i < n; i++)
            {
                double p = i * step;
                int k = (int)p;
                if (k >= x.Length - 1) { y[i] = x[x.Length - 1]; continue; }
                double f = p - k;
                y[i] = x[k] + f * (x[k + 1] - x[k]);
            }
            return y;
        }

        /// <summary>Odbicia po 15 ms (0,45) i 40 ms (0,25), stosowane kolejno.</summary>
        static void Echo(double[] x, int sr)
        {
            foreach (var (ms, g) in new[] { (15, 0.45), (40, 0.25) })
            {
                int d = sr * ms / 1000;
                var src = (double[])x.Clone();
                for (int i = d; i < x.Length; i++) x[i] += g * src[i - d];
            }
        }

        /// <summary>x·0,5 + szum gaussowski o mocy dobranej do SNR względem niezerowego sygnału.</summary>
        static void AddNoise(double[] x, int snrDb, Random rng)
        {
            var nz = x.Where(v => v != 0).ToArray();
            double sigP = nz.Average(v => v * v);
            double sd = Math.Sqrt(sigP * 0.25 / Math.Pow(10, snrDb / 10.0));
            for (int i = 0; i < x.Length; i++) x[i] = x[i] * 0.5 + sd * Gauss(rng);
        }

        static double Gauss(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
