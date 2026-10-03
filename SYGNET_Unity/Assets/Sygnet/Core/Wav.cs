using System;
using System.IO;
using System.Text;

namespace Sygnet.Core
{
    /// <summary>
    /// Minimalny WAV (RIFF): odczyt PCM 16-bit i float 32-bit (kanały uśredniane do mono), zapis PCM 16-bit mono.
    /// Skala jak w sygnet_ref.py: odczyt /32768, zapis ·32767 z obcięciem.
    /// </summary>
    public static class Wav
    {
        public static float[] Read(byte[] data, out int sampleRate)
        {
            if (data == null || data.Length < 12 || Ascii(data, 0) != "RIFF" || Ascii(data, 8) != "WAVE")
                throw new FormatException("To nie jest plik WAV");

            int format = 0, channels = 0, bits = 0;
            sampleRate = 0;
            int p = 12;
            while (p + 8 <= data.Length)
            {
                string id = Ascii(data, p);
                int size = BitConverter.ToInt32(data, p + 4);
                int body = p + 8;
                if (size < 0 || body + size > data.Length) size = data.Length - body; // ucięty plik / size 0xFFFFFFFF
                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(data, body);
                    channels = BitConverter.ToUInt16(data, body + 2);
                    sampleRate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToUInt16(data, body + 14);
                    if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(data, body + 24); // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    if (channels <= 0 || sampleRate <= 0) throw new FormatException("WAV: brak nagłówka fmt przed data");
                    return Decode(data, body, size, format, channels, bits);
                }
                p = body + size + (size & 1);
            }
            throw new FormatException("WAV: brak danych");
        }

        static float[] Decode(byte[] d, int offset, int size, int format, int channels, int bits)
        {
            int bytesPerSample;
            if (format == 1 && bits == 16) bytesPerSample = 2;
            else if (format == 3 && bits == 32) bytesPerSample = 4;
            else throw new FormatException("WAV: obsługiwane tylko PCM 16-bit i float 32-bit (jest format " + format + ", " + bits + " bit)");

            int frames = size / (bytesPerSample * channels);
            var x = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = offset + (i * channels + c) * bytesPerSample;
                    sum += bytesPerSample == 2 ? BitConverter.ToInt16(d, at) / 32768.0 : BitConverter.ToSingle(d, at);
                }
                x[i] = (float)(sum / channels);
            }
            return x;
        }

        public static byte[] Write16(float[] samples, int sampleRate)
        {
            using (var ms = new MemoryStream(44 + samples.Length * 2))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + samples.Length * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);              // PCM
                w.Write((short)1);              // mono
                w.Write(sampleRate);
                w.Write(sampleRate * 2);        // byte rate
                w.Write((short)2);              // block align
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(samples.Length * 2);
                foreach (var s in samples)
                    w.Write((short)(Math.Max(-1f, Math.Min(1f, s)) * 32767));
                return ms.ToArray();
            }
        }

        static string Ascii(byte[] d, int at) => Encoding.ASCII.GetString(d, at, 4);
    }
}
