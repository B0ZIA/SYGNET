using System;
using System.Text;

namespace Sygnet.Core
{
    /// <summary>Big-endian, hex i base64url. Wszystkie liczby wielobajtowe w protokole są big-endian (PROTOCOL.md §1).</summary>
    public static class Bytes
    {
        public static int ReadU16(byte[] b, int offset) => (b[offset] << 8) | b[offset + 1];

        public static long ReadU32(byte[] b, int offset) =>
            ((long)b[offset] << 24) | ((long)b[offset + 1] << 16) | ((long)b[offset + 2] << 8) | b[offset + 3];

        public static void WriteU16(byte[] b, int offset, int v)
        {
            b[offset] = (byte)(v >> 8);
            b[offset + 1] = (byte)v;
        }

        public static void WriteU32(byte[] b, int offset, long v)
        {
            b[offset] = (byte)(v >> 24);
            b[offset + 1] = (byte)(v >> 16);
            b[offset + 2] = (byte)(v >> 8);
            b[offset + 3] = (byte)v;
        }

        public static string ToHex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (hex.Length % 2 != 0) throw new FormatException("Nieparzysta długość hex");
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>base64url bez paddingu (QR, PROTOCOL.md §3).</summary>
        public static string ToBase64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] FromBase64Url(string s)
        {
            var t = s.Replace('-', '+').Replace('_', '/');
            switch (t.Length % 4)
            {
                case 2: t += "=="; break;
                case 3: t += "="; break;
                case 1: throw new FormatException("Nieprawidłowa długość base64url");
            }
            return Convert.FromBase64String(t);
        }

        public static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
