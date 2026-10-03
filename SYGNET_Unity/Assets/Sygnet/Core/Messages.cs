namespace Sygnet.Core
{
    /// <summary>Teksty dla użytkownika wg tabeli i „powodów po ludzku” z PROTOCOL.md §7.</summary>
    public static class Messages
    {
        public static string Title(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified: return "ZWERYFIKOWANO";
                case VerifyStatus.VerifiedOtherArea: return "INNY OBSZAR";
                case VerifyStatus.Expired: return "NIEAKTUALNY";
                case VerifyStatus.Incomplete: return "NIEPEŁNY PODPIS";
                case VerifyStatus.Forged: return "FAŁSZYWKA";
                case VerifyStatus.Duplicate: return "JUŻ OTRZYMANY";
                default: return "";
            }
        }

        public static string Summary(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified: return "Komunikat prawdziwy, podpis sprawdzony.";
                case VerifyStatus.VerifiedOtherArea: return "Prawdziwy komunikat, ale dla innego obszaru.";
                case VerifyStatus.Expired: return "Prawdziwy, ale nieaktualny. Możliwe odtworzone nagranie.";
                case VerifyStatus.Incomplete: return "Komunikat krytyczny bez drugiego podpisu. NIE WYKONUJ.";
                case VerifyStatus.Forged: return "Ten komunikat jest fałszywy.";
                case VerifyStatus.Duplicate: return "Ten komunikat już jest w skrzynce.";
                default: return "";
            }
        }

        /// <summary>Powód FORGED / INCOMPLETE po ludzku; null, gdy nie ma czego wyjaśniać.</summary>
        public static string Reason(VerificationResult r, TrustStore trust)
        {
            if (r.ReasonCode == null) return null;
            var code = r.ReasonCode;
            int signer = -1;
            int colon = code.IndexOf(':');
            if (colon > 0)
            {
                int.TryParse(code.Substring(colon + 1), out signer);
                code = code.Substring(0, colon);
            }
            string name = signer >= 0 ? trust.NameOf(signer) ?? ("nadawca " + signer) : r.IssuerName ?? "nadawca";

            switch (code)
            {
                case "BAD_SIGNATURE": return "Podpis nie pasuje do: " + name + ". Ktoś się podszywa lub zmienił treść.";
                case "UNKNOWN_ISSUER": return "Nieznany nadawca.";
                case "UNAUTHORIZED_AREA": return name + " nie ma uprawnień dla tego obszaru.";
                case "REVOKED_ISSUER": return "Klucz tego nadawcy został unieważniony.";
                case "CERT_EXPIRED": return "Certyfikat nadawcy (" + name + ") jest nieważny.";
                case "TIMESTAMP_IN_FUTURE": return "Podejrzany czas wydania.";
                case "NO_SIGNATURE": return "Komunikat nie ma podpisu.";
                case "PRIMARY_SIGNER_MISMATCH": return "Podpis nie należy do nadawcy wskazanego w komunikacie.";
                case "REVOKE_NOT_ROOT": return "Tylko klucz główny może unieważniać klucze.";
                case "ROOT_ONLY_REVOKE": return "Klucz główny nie wydaje komunikatów tego typu.";
                case "DUAL_SIGNATURE_REQUIRED": return "Komunikat krytyczny wymaga dwóch podpisów. NIE WYKONUJ.";
                default: return null;
            }
        }
    }
}
