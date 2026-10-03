using System;
using System.Text;

namespace Sygnet.Core
{
    /// <summary>Treść komunikatu, czyli dokładnie to, co jest podpisywane (PROTOCOL.md §2).</summary>
    public sealed class Payload
    {
        public const int HeaderLength = 16;
        public const int NoteMax = 60;
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;   // u8
        public int IssuerId;                   // u16
        public int Type;                       // u8
        public int AreaCode;                   // u16
        public long Timestamp;                 // u32, unix s UTC
        public int ValidMinutes;               // u16
        public int Sequence;                   // u16
        public int Flags;                      // u8, zarezerwowane = 0
        public byte[] Note = Array.Empty<byte>();

        /// <summary>Dopisek jako tekst. Niepoprawne sekwencje UTF-8 zastępowane znakiem �.</summary>
        public string NoteText => Encoding.UTF8.GetString(Note);

        /// <summary>Koniec ważności: timestamp + valid_minutes·60.</summary>
        public long ValidUntil => Timestamp + ValidMinutes * 60L;

        public int Length => HeaderLength + Note.Length;

        public byte[] ToBytes()
        {
            if (Note.Length > NoteMax) throw new ArgumentException("note > 60 B");
            Check(Version, 0xFF, nameof(Version));
            Check(IssuerId, 0xFFFF, nameof(IssuerId));
            Check(Type, 0xFF, nameof(Type));
            Check(AreaCode, 0xFFFF, nameof(AreaCode));
            if (Timestamp < 0 || Timestamp > 0xFFFFFFFFL) throw new ArgumentOutOfRangeException(nameof(Timestamp));
            Check(ValidMinutes, 0xFFFF, nameof(ValidMinutes));
            Check(Sequence, 0xFFFF, nameof(Sequence));
            Check(Flags, 0xFF, nameof(Flags));

            var b = new byte[HeaderLength + Note.Length];
            b[0] = (byte)Version;
            Bytes.WriteU16(b, 1, IssuerId);
            b[3] = (byte)Type;
            Bytes.WriteU16(b, 4, AreaCode);
            Bytes.WriteU32(b, 6, Timestamp);
            Bytes.WriteU16(b, 10, ValidMinutes);
            Bytes.WriteU16(b, 12, Sequence);
            b[14] = (byte)Flags;
            b[15] = (byte)Note.Length;
            Buffer.BlockCopy(Note, 0, b, HeaderLength, Note.Length);
            return b;
        }

        /// <summary>Parsuje payload z bufora. Rzuca <see cref="FormatException"/>, gdy brakuje bajtów.</summary>
        public static Payload Parse(byte[] b) => Parse(b, 0, b.Length);

        public static Payload Parse(byte[] b, int offset, int count)
        {
            if (count < HeaderLength) throw new FormatException("BAD_LENGTH");
            int noteLen = b[offset + 15];
            if (count < HeaderLength + noteLen) throw new FormatException("BAD_LENGTH");
            var note = new byte[noteLen];
            Buffer.BlockCopy(b, offset + HeaderLength, note, 0, noteLen);
            return new Payload
            {
                Version = b[offset],
                IssuerId = Bytes.ReadU16(b, offset + 1),
                Type = b[offset + 3],
                AreaCode = Bytes.ReadU16(b, offset + 4),
                Timestamp = Bytes.ReadU32(b, offset + 6),
                ValidMinutes = Bytes.ReadU16(b, offset + 10),
                Sequence = Bytes.ReadU16(b, offset + 12),
                Flags = b[offset + 14],
                Note = note,
            };
        }

        static void Check(int v, int max, string name)
        {
            if (v < 0 || v > max) throw new ArgumentOutOfRangeException(name);
        }
    }
}
