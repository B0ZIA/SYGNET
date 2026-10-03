namespace Sygnet.Core
{
    /// <summary>
    /// CRC-16/CCITT-FALSE: poly 0x1021, init 0xFFFF, bez odwracania bitów, bez xorout. Kontrola: "123456789" → 0x29B1.
    /// Tylko wykrywanie błędów transmisji; bezpieczeństwo daje podpis (PROTOCOL.md §3).
    /// </summary>
    public static class Crc16
    {
        public static ushort Compute(byte[] d) => Compute(d, 0, d.Length);

        public static ushort Compute(byte[] d, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int k = offset; k < offset + count; k++)
            {
                crc ^= (ushort)(d[k] << 8);
                for (int i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }
    }
}
