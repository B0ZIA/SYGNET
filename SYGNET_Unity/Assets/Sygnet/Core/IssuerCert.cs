using System;
using System.Text;

namespace Sygnet.Core
{
    /// <summary>Certyfikat wydawcy podpisany przez ROOT (PROTOCOL.md §5.3).</summary>
    public sealed class IssuerCert
    {
        public const int CurrentVersion = 1;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public int CertVersion;
        public int IssuerId;
        public byte[] PublicKey;
        public long ValidFrom;
        public long ValidUntil;
        public int[] Scopes;
        public string Name;
        public byte[] Raw;

        public string Fingerprint => Ed25519.Fingerprint(PublicKey);

        public bool IsValidAt(long now) => ValidFrom <= now && now <= ValidUntil;

        public bool CoversArea(int area)
        {
            foreach (var s in Scopes)
                if (Areas.Covers(s, area)) return true;
            return false;
        }

        /// <summary>Rzuca <see cref="FormatException"/> dla obciętych bajtów, nadmiarowych bajtów lub złego UTF-8.</summary>
        public static IssuerCert Parse(byte[] c)
        {
            if (c == null || c.Length < 45) throw new FormatException("Certyfikat za krótki");
            var pub = new byte[Ed25519.PublicKeySize];
            Buffer.BlockCopy(c, 3, pub, 0, pub.Length);
            int n = c[43];
            int p = 44;
            if (c.Length < p + 2 * n + 1) throw new FormatException("Certyfikat za krótki");
            var scopes = new int[n];
            for (int i = 0; i < n; i++) scopes[i] = Bytes.ReadU16(c, p + 2 * i);
            p += 2 * n;
            int nameLen = c[p++];
            if (c.Length != p + nameLen) throw new FormatException("Zła długość certyfikatu");
            return new IssuerCert
            {
                CertVersion = c[0],
                IssuerId = Bytes.ReadU16(c, 1),
                PublicKey = pub,
                ValidFrom = Bytes.ReadU32(c, 35),
                ValidUntil = Bytes.ReadU32(c, 39),
                Scopes = scopes,
                Name = StrictUtf8.GetString(c, p, nameLen),
                Raw = c,
            };
        }

        /// <summary>Do testów i narzędzi. Format jak build_cert w sygnet_ref.py.</summary>
        public static byte[] Build(int issuerId, byte[] pub, long validFrom, long validUntil, int[] scopes, string name)
        {
            var nb = Encoding.UTF8.GetBytes(name);
            var c = new byte[44 + 2 * scopes.Length + 1 + nb.Length];
            c[0] = CurrentVersion;
            Bytes.WriteU16(c, 1, issuerId);
            Buffer.BlockCopy(pub, 0, c, 3, Ed25519.PublicKeySize);
            Bytes.WriteU32(c, 35, validFrom);
            Bytes.WriteU32(c, 39, validUntil);
            c[43] = (byte)scopes.Length;
            int p = 44;
            foreach (var s in scopes)
            {
                Bytes.WriteU16(c, p, s);
                p += 2;
            }
            c[p++] = (byte)nb.Length;
            Buffer.BlockCopy(nb, 0, c, p, nb.Length);
            return c;
        }
    }
}
