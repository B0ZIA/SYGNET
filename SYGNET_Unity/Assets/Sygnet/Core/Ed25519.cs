using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Sygnet.Core
{
    /// <summary>Ed25519 (RFC 8032) przez BouncyCastle. PROTOCOL.md §1.</summary>
    public static class Ed25519
    {
        public const int PublicKeySize = 32;
        public const int SignatureSize = 64;
        public const int SeedSize = 32;

        public static bool Verify(byte[] pub32, byte[] msg, byte[] sig64)
        {
            if (pub32 == null || pub32.Length != PublicKeySize) return false;
            if (sig64 == null || sig64.Length != SignatureSize) return false;
            if (msg == null) return false;
            try
            {
                var v = new Ed25519Signer();
                v.Init(false, new Ed25519PublicKeyParameters(pub32, 0));
                v.BlockUpdate(msg, 0, msg.Length);
                return v.VerifySignature(sig64);
            }
            catch (Exception)
            {
                // Nieprawidłowe kodowanie punktu klucza publicznego itp. – traktujemy jak zły podpis.
                return false;
            }
        }

        /// <summary>Tylko w testach. W aplikacji nie ma żadnego klucza prywatnego.</summary>
        public static byte[] Sign(byte[] seed32, byte[] msg)
        {
            var s = new Ed25519Signer();
            s.Init(true, new Ed25519PrivateKeyParameters(seed32, 0));
            s.BlockUpdate(msg, 0, msg.Length);
            return s.GenerateSignature();
        }

        /// <summary>Tylko w testach.</summary>
        public static byte[] PublicKeyFromSeed(byte[] seed32)
        {
            return new Ed25519PrivateKeyParameters(seed32, 0).GeneratePublicKey().GetEncoded();
        }

        /// <summary>Pierwsze 8 bajtów SHA-256(pubkey), hex WIELKIMI literami w grupach po 4: 6A38-03D5-F059-902A.</summary>
        public static string Fingerprint(byte[] pub32)
        {
            byte[] h;
            using (var sha = SHA256.Create()) h = sha.ComputeHash(pub32);
            var hex = BitConverter.ToString(h, 0, 8).Replace("-", "");
            return hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4);
        }
    }
}
