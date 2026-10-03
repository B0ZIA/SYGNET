using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sygnet.Core;

namespace Sygnet.Tests
{
    /// <summary>Dekoder na żywo: porcje dowolnej długości, jak z mikrofonu (CLIENT_UNITY.md §4.2).</summary>
    public class StreamingTests
    {
        static IEnumerable<string> Vectors => TestData.VectorNames;
        static byte[] TV1 => TestData.Get("TV1_air_raid_single").Frame;
        static byte[] TV5 => TestData.Get("TV5_unauthorized_area").Frame;

        /// <summary>Podaje próbki porcjami o losowej długości (jak Microphone.GetData co klatkę).</summary>
        static List<byte[]> Feed(StreamingDecoder d, float[] x, int seed = 1, int minChunk = 64, int maxChunk = 4096,
            Action<StreamingDecoder> afterChunk = null)
        {
            var got = new List<byte[]>();
            d.FrameDecoded += f => got.Add(f);
            var rng = new Random(seed);
            for (int pos = 0; pos < x.Length;)
            {
                int n = Math.Min(x.Length - pos, rng.Next(minChunk, maxChunk));
                d.Push(x, pos, n);
                pos += n;
                afterChunk?.Invoke(d);
            }
            return got;
        }

        static float[] Silence(int sr, double s) => new float[(int)(sr * s)];

        [TestCaseSource(nameof(Vectors))]
        public void ReferenceWav_InChunks_GivesFrameOnce(string name)
        {
            var v = TestData.Get(name);
            var x = TestData.ReadWav(v, out int sr);
            var got = Feed(new StreamingDecoder(sr), x.Concat(Silence(sr, 1)).ToArray(), seed: name.Length);
            Assert.AreEqual(1, got.Count, "2 powtórzenia nadawcy → 1 ramka");
            CollectionAssert.AreEqual(v.Frame, got[0]);
        }

        [TestCase(1)]
        [TestCase(480)]
        [TestCase(48000)]
        public void ChunkSize_DoesNotMatter(int chunk)
        {
            const int sr = 48000;
            var x = Silence(sr, 0.7).Concat(ModemEncoder.Encode(TV1, sr, 1)).Concat(Silence(sr, 0.5)).ToArray();
            var got = Feed(new StreamingDecoder(sr), x, minChunk: chunk, maxChunk: chunk + 1);
            Assert.AreEqual(1, got.Count);
            CollectionAssert.AreEqual(TV1, got[0]);
        }

        /// <summary>Regresja: porcja dłuższa niż zakres skanu (np. zaległe próbki po przycięciu aplikacji).</summary>
        [Test]
        public void WholeRecordingInOnePush()
        {
            var v = TestData.Get("TV2_evacuation_dual");
            var x = TestData.ReadWav(v, out int sr);
            var d = new StreamingDecoder(sr);
            var got = new List<byte[]>();
            d.FrameDecoded += got.Add;
            d.Push(x);
            Assert.AreEqual(1, got.Count);
            CollectionAssert.AreEqual(v.Frame, got[0]);
        }

        [TestCase(44100)]
        [TestCase(24000)]
        [TestCase(16000)]
        public void OtherSampleRates(int sr)
        {
            var x = Silence(sr, 0.3).Concat(ModemEncoder.Encode(TV1, sr)).Concat(Silence(sr, 0.5)).ToArray();
            var got = Feed(new StreamingDecoder(sr), x);
            Assert.AreEqual(1, got.Count);
            CollectionAssert.AreEqual(TV1, got[0]);
        }

        [Test]
        public void TwoDifferentFrames_AreBothDelivered()
        {
            const int sr = 48000;
            var x = ModemEncoder.Encode(TV1, sr, 1).Concat(Silence(sr, 1)).Concat(ModemEncoder.Encode(TV5, sr, 1))
                .Concat(Silence(sr, 0.5)).ToArray();
            var got = Feed(new StreamingDecoder(sr), x);
            Assert.AreEqual(2, got.Count);
            CollectionAssert.AreEqual(TV1, got[0]);
            CollectionAssert.AreEqual(TV5, got[1]);
        }

        [Test]
        public void SameFrameWithin30s_IsSuppressed_After30s_IsDeliveredAgain()
        {
            const int sr = 16000;                                         // niski sr = szybki test długiego nagrania
            var one = ModemEncoder.Encode(TV5, sr, 1);
            var x = one.Concat(Silence(sr, 5)).Concat(one).Concat(Silence(sr, 32)).Concat(one).Concat(Silence(sr, 0.5)).ToArray();
            var got = Feed(new StreamingDecoder(sr), x, maxChunk: 8000);
            Assert.AreEqual(2, got.Count);
        }

        [Test]
        public void LongNoise_ThenFrame_BufferStaysBounded()
        {
            const int sr = 16000;
            var rng = new Random(3);
            var noise = new float[sr * 60];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(0.05 * (rng.NextDouble() * 2 - 1));
            var x = noise.Concat(ModemEncoder.Encode(TV1, sr, 1)).Concat(Silence(sr, 0.5)).ToArray();
            var d = new StreamingDecoder(sr);
            var got = Feed(d, x, maxChunk: 8000);
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(x.Length, d.TotalSamples);
        }

        [Test]
        public void Progress_GrowsWhileReceiving_AndResets()
        {
            const int sr = 24000;
            var x = Silence(sr, 0.3).Concat(ModemEncoder.Encode(TV1, sr, 1)).Concat(Silence(sr, 0.5)).ToArray();
            var progress = new List<double>();
            int bytes = -1;
            var got = Feed(new StreamingDecoder(sr), x, minChunk: 1200, maxChunk: 1201, afterChunk: d =>
            {
                progress.Add(d.Progress);
                if (d.ReceivingBytes > 0) bytes = d.ReceivingBytes;
            });
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(TV1.Length, bytes, "ReceivingBytes = 4 + frame_len");
            var receiving = progress.Where(p => p > 0).ToList();
            Assert.Greater(receiving.Count, 50);
            for (int i = 1; i < receiving.Count; i++) Assert.GreaterOrEqual(receiving[i], receiving[i - 1]);
            Assert.Greater(receiving.Last(), 0.95);
            Assert.AreEqual(-1, progress.Last(), "po odebraniu ramki brak odbioru");
        }

        [Test]
        public void Reset_DropsFrameInProgress()
        {
            const int sr = 24000;
            var x = ModemEncoder.Encode(TV1, sr, 1);
            var d = new StreamingDecoder(sr);
            var got = new List<byte[]>();
            d.FrameDecoded += got.Add;
            d.Push(x, 0, x.Length / 2);
            Assert.Greater(d.Progress, 0.1);
            d.Reset();
            d.Push(x, x.Length / 2, x.Length - x.Length / 2);
            d.Push(Silence(sr, 0.5));
            CollectionAssert.IsEmpty(got);
            Assert.AreEqual(-1, d.Progress);
        }

        [Test]
        public void CorruptedSymbol_RaisesFrameFailed_WithRecording()
        {
            const int sr = 24000;
            var x = ModemEncoder.Encode(TV1, sr, 1);
            int dataStart = ModemConstants.Samples(sr, 500), period = ModemConstants.Samples(sr, 50);
            var other = ModemEncoder.Encode(new[] { (byte)(TV1[40] ^ 0x33) }, sr, 1);
            Array.Copy(other, dataStart, x, dataStart + 40 * period, period);       // przekłamany symbol nr 40
            var d = new StreamingDecoder(sr);
            float[] dump = null;
            d.FrameFailed += s => dump = s;
            var got = Feed(d, x.Concat(Silence(sr, 0.5)).ToArray());
            CollectionAssert.IsEmpty(got);
            Assert.AreEqual(1, d.FramesFailed);
            Assert.IsNotNull(dump);
            Assert.Greater(dump.Length, x.Length * 0.9, "nagranie obejmuje całą ramkę z preambułą");
        }

        [Test]
        public void Combining_SecondRepetitionRescuesFrame()
        {
            const int sr = 24000;
            var once = ModemEncoder.Encode(TV1, sr, 1);
            int dataStart = ModemConstants.Samples(sr, 500), period = ModemConstants.Samples(sr, 50);
            float[] Corrupt(int index)
            {
                var c = (float[])once.Clone();
                var other = ModemEncoder.Encode(new[] { (byte)(TV1[index] ^ 0x5A) }, sr, 1);
                Array.Copy(other, dataStart, c, dataStart + index * period, period);
                return c;
            }
            var x = Corrupt(25).Concat(Silence(sr, 0.5)).Concat(Corrupt(90)).Concat(Silence(sr, 0.5)).ToArray();
            var d = new StreamingDecoder(sr);
            var got = Feed(d, x);
            Assert.AreEqual(1, got.Count);
            CollectionAssert.AreEqual(TV1, got[0]);
            Assert.AreEqual(1, d.FramesCombined);
        }

        /// <summary>Głośnik laptopa → pokój → mikrofon telefonu: 44,1 kHz, pogłos, dryf zegara, szum 10 dB, porcje.</summary>
        [Test]
        public void Noisy_Echo_Drift_InChunks()
        {
            var rng = new Random(11);
            var x = ModemEncoder.Encode(TestData.Get("TV2_evacuation_dual").Frame, 48000).Select(v => (double)v).ToArray();
            x = Resample(x, (int)(x.LongLength * 44100 / 48000 * 1.001));
            x = new double[44100 / 4].Concat(x).Concat(new double[44100 / 2]).ToArray();
            foreach (var (ms, g) in new[] { (15, 0.45), (40, 0.25) })
            {
                int dd = 44100 * ms / 1000;
                var src = (double[])x.Clone();
                for (int i = dd; i < x.Length; i++) x[i] += g * src[i - dd];
            }
            double sigP = x.Where(v => v != 0).Average(v => v * v);
            double sd = Math.Sqrt(sigP * 0.25 / Math.Pow(10, 1.0));
            var y = x.Select(v => (float)(v * 0.5 + sd * Gauss(rng))).ToArray();
            var got = Feed(new StreamingDecoder(44100), y, seed: 5);
            Assert.AreEqual(1, got.Count);
            CollectionAssert.AreEqual(TestData.Get("TV2_evacuation_dual").Frame, got[0]);
        }

        static double[] Resample(double[] x, int n)
        {
            var y = new double[n];
            double step = (double)(x.Length - 1) / (n - 1);
            for (int i = 0; i < n; i++)
            {
                double p = i * step;
                int k = Math.Min((int)p, x.Length - 2);
                y[i] = x[k] + (p - k) * (x[k + 1] - x[k]);
            }
            return y;
        }

        static double Gauss(Random rng) =>
            Math.Sqrt(-2.0 * Math.Log(1.0 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
    }
}
