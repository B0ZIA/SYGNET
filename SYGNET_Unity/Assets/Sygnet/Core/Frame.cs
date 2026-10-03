using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    public readonly struct SignatureEntry
    {
        public readonly int SignerId;      // u16
        public readonly byte[] Signature;  // 64 B

        public SignatureEntry(int signerId, byte[] signature)
        {
            SignerId = signerId;
            Signature = signature;
        }
    }

    public sealed class ParsedFrame
    {
        public byte[] Raw;
        public byte[] PayloadBytes;
        public Payload Payload;
        public IReadOnlyList<SignatureEntry> Signatures;
    }

    /// <summary>
    /// Ramka: MAGIC "SG" | frame_len u16 | payload | sig_count u8 | sig_count × [signer_id u16 | sig 64 B] | crc16 u16.
    /// PROTOCOL.md §3. Parsowanie odwzorowuje parse_frame z tools/sygnet_ref.py.
    /// </summary>
    public static class Frame
    {
        public const byte Magic0 = 0x53; // 'S'
        public const byte Magic1 = 0x47; // 'G'
        public const int SignatureEntryLength = 2 + Ed25519.SignatureSize;
        public const string QrPrefix = "SYG1:";

        /// <summary>Najkrótsza możliwa ramka: nagłówek 4 B + payload 16 B + sig_count 1 B + CRC 2 B.</summary>
        public const int MinLength = 4 + Payload.HeaderLength + 1 + 2;

        public static byte[] Build(byte[] payload, IList<SignatureEntry> signatures)
        {
            int bodyLen = payload.Length + 1 + signatures.Count * SignatureEntryLength;
            int frameLen = bodyLen + 2;
            var f = new byte[4 + frameLen];
            f[0] = Magic0;
            f[1] = Magic1;
            Bytes.WriteU16(f, 2, frameLen);
            Buffer.BlockCopy(payload, 0, f, 4, payload.Length);
            int p = 4 + payload.Length;
            f[p++] = (byte)signatures.Count;
            foreach (var s in signatures)
            {
                if (s.Signature == null || s.Signature.Length != Ed25519.SignatureSize)
                    throw new ArgumentException("Podpis musi mieć 64 B");
                Bytes.WriteU16(f, p, s.SignerId);
                Buffer.BlockCopy(s.Signature, 0, f, p + 2, Ed25519.SignatureSize);
                p += SignatureEntryLength;
            }
            Bytes.WriteU16(f, p, Crc16.Compute(f, 0, p));
            return f;
        }

        /// <summary>Parsuje i sprawdza MAGIC, długość i CRC. Kod błędu: BAD_MAGIC, BAD_LENGTH, BAD_CRC.</summary>
        public static bool TryParse(byte[] frame, out ParsedFrame parsed, out string error)
        {
            parsed = null;
            error = null;
            if (frame == null || frame.Length < 2 || frame[0] != Magic0 || frame[1] != Magic1)
            {
                error = "BAD_MAGIC";
                return false;
            }
            if (frame.Length < 6 || frame.Length != 4 + Bytes.ReadU16(frame, 2))
            {
                error = "BAD_LENGTH";
                return false;
            }
            int crcPos = frame.Length - 2;
            if (Crc16.Compute(frame, 0, crcPos) != Bytes.ReadU16(frame, crcPos))
            {
                error = "BAD_CRC";
                return false;
            }
            if (frame.Length < MinLength)
            {
                error = "BAD_LENGTH";
                return false;
            }

            int payloadLen = Payload.HeaderLength + frame[4 + 15];
            int p = 4 + payloadLen;
            if (p + 1 > crcPos)
            {
                error = "BAD_LENGTH";
                return false;
            }
            int count = frame[p++];
            if (p + count * SignatureEntryLength != crcPos)
            {
                error = "BAD_LENGTH";
                return false;
            }

            var payloadBytes = new byte[payloadLen];
            Buffer.BlockCopy(frame, 4, payloadBytes, 0, payloadLen);
            var sigs = new List<SignatureEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var sig = new byte[Ed25519.SignatureSize];
                Buffer.BlockCopy(frame, p + 2, sig, 0, sig.Length);
                sigs.Add(new SignatureEntry(Bytes.ReadU16(frame, p), sig));
                p += SignatureEntryLength;
            }

            parsed = new ParsedFrame
            {
                Raw = frame,
                PayloadBytes = payloadBytes,
                Payload = Payload.Parse(payloadBytes),
                Signatures = sigs,
            };
            return true;
        }

        public static ParsedFrame Parse(byte[] frame)
        {
            if (!TryParse(frame, out var parsed, out var error)) throw new FormatException(error);
            return parsed;
        }

        /// <summary>"SYG1:" + base64url(frame) bez paddingu.</summary>
        public static string ToQrText(byte[] frame) => QrPrefix + Bytes.ToBase64Url(frame);

        /// <summary>
        /// Zwraca false dla każdego tekstu, który nie jest komunikatem SYGNET (np. link).
        /// Nie sprawdza CRC ani podpisu – to robi <see cref="Verifier"/>.
        /// </summary>
        public static bool TryFromQrText(string text, out byte[] frame)
        {
            frame = null;
            if (text == null) return false;
            text = text.Trim();
            if (!text.StartsWith(QrPrefix, StringComparison.Ordinal)) return false;
            try
            {
                frame = Bytes.FromBase64Url(text.Substring(QrPrefix.Length));
                return frame.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
