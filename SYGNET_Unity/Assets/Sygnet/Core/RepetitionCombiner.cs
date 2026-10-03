using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>
    /// Łączenie powtórzeń (soft combining) – zmiana tylko po stronie odbiornika, protokół bez zmian.
    /// Nadawca powtarza ramkę R razy. Gdy żadna kopia osobno nie przechodzi CRC, sumujemy znormalizowane moce
    /// tonów kopii o tej samej długości i dopiero wtedy wybieramy symbole – przypadkowe błędy rzadko trafiają
    /// w ten sam symbol w każdej kopii. Wynik nadal musi przejść CRC, a potem weryfikację podpisu.
    /// </summary>
    public sealed class RepetitionCombiner
    {
        /// <summary>Kopie muszą się zgadzać w co najmniej tylu bajtach – nie łączymy dwóch różnych ramek tej samej długości.</summary>
        const double MinAgreement = 0.75;

        readonly long horizon;
        readonly List<(double[] powers, byte[] decided, int symbols, long at)> failed =
            new List<(double[] powers, byte[] decided, int symbols, long at)>();

        /// <param name="horizonSamples">Jak długo pamiętamy nieudane kopie (w próbkach).</param>
        public RepetitionCombiner(long horizonSamples)
        {
            horizon = horizonSamples;
        }

        /// <summary>
        /// Dodaje kopię, która osobno nie przeszła CRC. Zwraca ramkę z poprawnym CRC uzyskaną z sumy z wcześniejszymi
        /// podobnymi kopiami albo null (kopia zostaje zapamiętana do kolejnych prób).
        /// </summary>
        public byte[] AddFailed(double[] powers, int symbols, long at)
        {
            failed.RemoveAll(f => at - f.at > horizon);
            var decided = ModemDecoder.Decide(powers, symbols);
            var sum = (double[])powers.Clone();
            int copies = 1;
            foreach (var f in failed)
            {
                if (f.symbols != symbols || Agreement(f.decided, decided) < MinAgreement) continue;
                for (int i = 0; i < sum.Length; i++) sum[i] += f.powers[i];
                copies++;
            }
            if (copies > 1)
            {
                var frame = ModemDecoder.Decide(sum, symbols);
                if (!ModemDecoder.CrcOk(frame)) frame = ResolveDisagreements(frame, decided, symbols);
                if (frame != null)
                {
                    failed.RemoveAll(f => f.symbols == symbols && Agreement(f.decided, frame) >= MinAgreement);
                    return frame;
                }
            }
            failed.Add((powers, decided, symbols, at));
            return null;
        }

        /// <summary>Maks. liczba pozycji, w których kopie się różnią, przy rozstrzyganiu przez CRC (2^5 = 32 próby).</summary>
        const int MaxDisagreements = 5;

        /// <summary>
        /// Suma mocy nie rozstrzyga remisów (np. dwie kopie, w każdej inny symbol podmieniony w całości). Wtedy w pozycjach,
        /// gdzie kopie się różnią, próbujemy wartości z kopii i sprawdzamy CRC. Liczba prób jest ograniczona
        /// (≤ 32 → szansa przypadkowo zgodnego CRC ≈ 0,05%; i tak zatrzyma ją weryfikacja podpisu).
        /// </summary>
        byte[] ResolveDisagreements(byte[] summed, byte[] latest, int symbols)
        {
            var copies = new List<byte[]> { latest };
            foreach (var f in failed)
                if (f.symbols == symbols && Agreement(f.decided, latest) >= MinAgreement) copies.Add(f.decided);

            // Tylko wartości, które faktycznie wystąpiły w kopiach: przy remisie suma rozstrzyga oba pasma niezależnie
            // i może dać bajt, którego nie ma w żadnej kopii.
            var trial = (byte[])summed.Clone();
            var positions = new List<int>();
            var options = new List<byte[]>();
            for (int k = 0; k < symbols; k++)
            {
                var values = new List<byte>();
                foreach (var c in copies)
                    if (!values.Contains(c[k])) values.Add(c[k]);
                if (values.Count == 1)
                {
                    trial[k] = values[0];
                    continue;
                }
                if (positions.Count == MaxDisagreements || values.Count > 2) return null;   // za dużo niepewności
                positions.Add(k);
                options.Add(values.ToArray());
            }
            if (positions.Count == 0) return null;

            for (int mask = 0; mask < 1 << positions.Count; mask++)
            {
                for (int p = 0; p < positions.Count; p++) trial[positions[p]] = options[p][(mask >> p) & 1];
                if (ModemDecoder.CrcOk(trial)) return trial;
            }
            return null;
        }

        static double Agreement(byte[] a, byte[] b)
        {
            int same = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i] == b[i]) same++;
            return (double)same / a.Length;
        }
    }
}
